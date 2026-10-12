using System;
using System.Collections.Generic;
using TimboJimbo.UINext.Text.Bridge;
using UnityEngine;

namespace TimboJimbo.UINext.Text
{
    // The questions a layout answers about itself once it is made: the rectangles a range covers, links under a point,
    // and carets, lines and the positions nearest a point, in source indices and layout pixels.
    public sealed partial class TextLayout
    {
        // What the caret and hit-test queries find of the last layout, as they first need it: each line's extent, and
        // for each index whether a character is drawn with a box of its own there (0 not yet known, 1 yes, 2 no), the
        // direction it reads in (0 not yet known, 1 left to right, 2 right to left) and the engine's caret (NaN not yet
        // known).
        private readonly List<LineSpan> _lineSpans = new();
        private byte[] _drawn = Array.Empty<byte>();
        private byte[] _direction = Array.Empty<byte>();
        private float[] _caretX = Array.Empty<float>();
        // Each character's leading and trailing edges across (NaN not yet known), where selection handles stand, and the
        // rectangles of the last source range asked for (a text field asks for its selection's every frame).
        private float[] _leadingX = Array.Empty<float>();
        private float[] _trailingX = Array.Empty<float>();
        private readonly List<Rect> _rangeRects = new();
        private int _rangeStart = -1, _rangeEnd = -1;

        /// <summary>
        /// Rectangles covering the source characters in [start, end), in layout pixels: one per stretch of a line they
        /// cover, a line whose text reads both ways having one per stretch that reads one way. A line's trailing spaces
        /// and its line break are covered after its text, where its carets stand (see <see cref="GetCaretRect"/>). A
        /// text with nothing to draw (only spaces or line breaks) is laid out all the same and answers too.
        /// </summary>
        public void GetSourceRangeRects(int sourceStart, int sourceEnd, List<Rect> results)
        {
            // The engine makes a new array of rectangles each time it is asked, so the last range's are kept for the
            // layout: a text field draws its selection from them every frame.
            if (sourceStart != _rangeStart || sourceEnd != _rangeEnd)
            {
                _rangeStart = sourceStart;
                _rangeEnd = sourceEnd;
                FindSourceRangeRects(sourceStart, sourceEnd, _rangeRects);
            }
            results.Clear();
            results.AddRange(_rangeRects);
        }

        private void FindSourceRangeRects(int sourceStart, int sourceEnd, List<Rect> results)
        {
            results.Clear();
            if (!_handle.IsLaidOut)
                return;
            int length = _layoutText.Length;
            int start = Mathf.Clamp(_layoutText.ToLayout(sourceStart), 0, length);
            int end = Mathf.Clamp(_layoutText.ToLayout(sourceEnd), 0, length);
            if (end <= start)
                return;
            int last = _handle.LineOf(CodePointStart(end - 1));
            for (int line = _handle.LineOf(CodePointStart(start)); line <= last; line++)
            {
                var span = LineSpanOf(line);
                int from = Mathf.Max(start, span.First), to = Mathf.Min(end, span.Next);
                int textTo = Mathf.Min(to, span.Content);
                int lineFrom = results.Count;
                if (textTo > from)
                {
                    _handle.GetRangeRects(from, textTo, _rects);
                    results.AddRange(_rects);
                }
                int spacesFrom = Mathf.Max(from, span.Content);
                if (to > spacesFrom)
                {
                    float a = CaretX(in span, spacesFrom);
                    float b = CaretX(in span, Mathf.Min(to, span.Stop));
                    // The line break, past the spaces, as wide as the engine draws it.
                    if (to > span.Stop)
                        b += (_rightToLeft ? -1f : 1f) * Width(span.Stop, span.Next);
                    if (Mathf.Abs(b - a) > 0.01f)
                        results.Add(Rect.MinMaxRect(Mathf.Min(a, b), span.Top, Mathf.Max(a, b), span.Bottom));
                }
                JoinTouching(results, lineFrom);
            }
        }

        // Joins the rectangles of one line from an index on that touch or overlap across into one, as one stretch.
        private static void JoinTouching(List<Rect> rects, int from)
        {
            for (int i = from; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    var a = rects[i];
                    var b = rects[j];
                    if (b.xMin > a.xMax + 0.01f || a.xMin > b.xMax + 0.01f)
                        continue;
                    rects[i] = Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
                    rects.RemoveAt(j);
                    j = i;
                }
            }
        }

        /// <summary>The index into <see cref="TextDocument.Links"/> of the link under a layout point, or -1.</summary>
        public int LinkAt(Vector2 pointPx)
        {
            if (!_handle.IsLaidOut)
                return -1;
            int link = _handle.LinkAt(pointPx);
            return link >= 0 && link < _document.Links.Count ? link : -1;
        }

        // ---- Carets and lines ----
        // Source indices and layout pixels (y down from the layout's top-left corner), for the last layout. Plain text
        // maps 1:1; with rich text a source index inside hidden markup is taken at the next shown character, as the
        // range rectangles take it. A text with nothing to draw (spaces, line breaks) is laid out all the same and
        // answers; an empty text answers from its font, as a single empty line placed where its alignment puts a line.
        // An index where a line wraps is on two lines at once: it ends the line before and starts the line after. Taken
        // upstream it is the end of the line before, otherwise the start of the line after, as a caret's affinity
        // decides in UIKit (UITextStorageDirection) and Flutter (TextAffinity); the caller keeps the affinity with its
        // caret, upstream after End or a tap past a wrapped line's end.
        //
        // Where text reads both ways, carets follow Core Text's primary caret, as UIKit's text views draw it (and
        // Android's getPrimaryHorizontal). A caret between two characters stands at the edge of the one whose run reads
        // nearer the paragraph's way (two in one run share that edge): where a character of the paragraph's direction
        // typed there would go. A line's start and end count as the paragraph's direction, so a caret at the start of a
        // line stands at the side the text starts from (the right of a right-to-left line), and one at its end (before
        // its line break, at a wrap taken upstream, at the text's end) at the side it ends at, past the spaces there,
        // which stand at the paragraph's level after the line's text, as Unicode's bidi algorithm has trailing
        // whitespace (UAX #9, L1).
        //
        // The engine places carets inside a line so, and those are taken from it. At a line's ends it does not: in a
        // right-to-left text over several lines it puts the text's start and end on other lines, the place before a line
        // break at its line's start and a wrapped line's trailing space before its box, and finds points past a line's
        // ends on other lines. So each line's ends are found here, from where its characters reach (see LineSpan), and a
        // point is matched to the caret nearest it on the line it is level with.

        /// <summary>
        /// The caret at a source index, in layout pixels: zero wide, at the insertion point, as tall as the line the
        /// index is on. An index where a line wraps is drawn at the end of the line before when
        /// <paramref name="upstream"/>, otherwise at the start of the line after. Where the text reads both ways, at
        /// the edge of the character that reads nearer the text's own direction, and at a line's start or end, at the
        /// side the line starts from or ends at as the text reads (the end of a right-to-left line at its left). In an
        /// empty text it sits at the side of the box the text is aligned to, as tall as the font's line height.
        /// </summary>
        public Rect GetCaretRect(int sourceIndex, bool upstream = false)
        {
            if (!_handle.IsLaidOut)
                return EmptyTextCaret();
            int index = LayoutIndex(sourceIndex);
            if (index == _layoutText.Length && EndsWithBreak())
                return TrailingLineCaret();
            var span = LineSpanOf(LineOfCaret(index, upstream));
            return new Rect(CaretX(in span, index), span.Top, 0f, span.Height);
        }

        /// <summary>
        /// The source index of the caret position nearest a point in layout pixels: on the line the point is level
        /// with (above the first line, the first; below the last, the last), the position whose caret is nearest across,
        /// so past either end of a line, that end. Positions inside what the engine draws as one (a letter and its vowel
        /// marks, a ligature, an emoji sequence) are not matched.
        /// </summary>
        public int GetIndexAt(Vector2 pointPx) => GetIndexAt(pointPx, out _);

        /// <summary>
        /// The source index of the caret position nearest a point in layout pixels, as <see cref="GetIndexAt(Vector2)"/>,
        /// and whether to take it upstream: true when the point is on a wrapped line past its end, whose nearest position
        /// is the index the next line starts at.
        /// </summary>
        public int GetIndexAt(Vector2 pointPx, out bool upstream)
        {
            upstream = false;
            if (!_handle.IsLaidOut)
                return 0;
            if (!TryLineAt(pointPx.y, out int line))
                return _layoutText.ToSource(_layoutText.Length);
            var span = LineSpanOf(line);
            int nearest = span.First;
            float distance = Mathf.Abs(pointPx.x - span.Start);
            for (int index = span.First + 1; index <= span.Stop; index++)
            {
                if (!IsCaretStop(in span, index))
                    continue;
                float d = Mathf.Abs(pointPx.x - CaretX(in span, index));
                if (d < distance)
                {
                    nearest = index;
                    distance = d;
                }
            }
            return LineEndToSource(in span, line, nearest, out upstream);
        }

        /// <summary>
        /// Whether the character at a source index reads right to left: a letter by its script, a number never, and a
        /// space or punctuation as the text around it has it laid out (the paragraph's way at a line's end).
        /// </summary>
        public bool IsCharacterRightToLeft(int sourceIndex)
        {
            if (!_handle.IsLaidOut)
                return _rightToLeft;
            int index = LayoutIndex(sourceIndex);
            return index < _layoutText.Length ? CharacterIsRightToLeft(index) : _rightToLeft;
        }

        /// <summary>
        /// A caret-shaped rect (zero wide, as tall as the line) at the leading or trailing edge of the character at a
        /// source index, in that character's own direction: its left, or for <paramref name="leading"/> false its right,
        /// in a left-to-right run, and the other way about in a right-to-left one. Where a selection's handles stand, as
        /// Android stands them and as iOS's selection rects place them: the start handle at the leading edge of the first
        /// character selected and the end handle at the trailing edge of the last, beside them however the text around
        /// them reads. The text's end, or a line break, at the end of its line.
        /// </summary>
        public Rect GetCharacterEdgeRect(int sourceIndex, bool leading)
        {
            if (!_handle.IsLaidOut)
                return EmptyTextCaret();
            int index = LayoutIndex(sourceIndex);
            if (index >= _layoutText.Length)
                return GetCaretRect(sourceIndex);
            var span = LineSpanOf(_handle.LineOf(index));
            return new Rect(EdgeX(in span, index, leading), span.Top, 0f, span.Height);
        }

        /// <summary>
        /// The source index whose character edge (see <see cref="GetCharacterEdgeRect"/>) is nearest a point in layout
        /// pixels, on the line the point is level with: for <paramref name="leading"/>, the index of a character whose
        /// leading edge is nearest (where a selection's start handle is dragged to); otherwise the index after a
        /// character whose trailing edge is nearest (where its end handle is dragged to), taken upstream when that is the
        /// end of a wrapped line.
        /// </summary>
        public int GetIndexAtEdge(Vector2 pointPx, bool leading, out bool upstream)
        {
            upstream = false;
            if (!_handle.IsLaidOut)
                return 0;
            if (!TryLineAt(pointPx.y, out int line))
                return _layoutText.ToSource(_layoutText.Length);
            var span = LineSpanOf(line);
            int nearest = leading ? span.First : span.Stop;
            float distance = float.PositiveInfinity;
            for (int index = span.First; index < span.Next; index++)
            {
                if (index < span.Stop && !StartsDrawnCharacter(index))
                    continue;
                float d = Mathf.Abs(pointPx.x - EdgeX(in span, index, leading));
                if (d < distance)
                {
                    nearest = leading ? index : Mathf.Min(NextCharacter(index, span.Next), span.Stop);
                    distance = d;
                }
            }
            if (leading)
                return _layoutText.ToSource(nearest);
            return LineEndToSource(in span, line, nearest, out upstream);
        }

        /// <summary>
        /// The caret position beside the caret at a source index on screen, to its right or its left, as the arrow keys
        /// move a caret in iOS, macOS and Android text views: across the line by where its carets stand, which in text
        /// that reads both ways is not the order of the indices. Past the end of a line, as the text reads, it goes on
        /// to the start of the next line, and past its start to the end of the line before (taken upstream where that
        /// line wraps, as <paramref name="besideUpstream"/> says); where a line wraps, the end of one line and the start
        /// of the next are one place in the text, so it goes on a place along the line it comes to, and each move goes
        /// through the text. At the text's first or last place, the same index.
        /// </summary>
        public int GetVisualNeighbour(int sourceIndex, bool upstream, bool toRight, out bool besideUpstream)
        {
            besideUpstream = false;
            if (!_handle.IsLaidOut)
                return sourceIndex;
            int length = _layoutText.Length;
            int index = LayoutIndex(sourceIndex);
            int last = LastLine();
            // Forward in reading order: to the right in left-to-right text, to the left in right-to-left text.
            bool forward = toRight != _rightToLeft;
            if (index == length && EndsWithBreak())
            {
                // On the empty line the text ends with: only back, to the end of the line before it.
                if (forward)
                    return sourceIndex;
                return _layoutText.ToSourceEnd(LineSpanOf(last).Stop);
            }

            int line = LineOfCaret(index, upstream);
            var span = LineSpanOf(line);
            int found = StopBeside(in span, index, CaretX(in span, index), toRight);
            if (found >= 0)
                return LineEndToSource(in span, line, found, out besideUpstream);
            // Past the line's end, on to the next line's start, and past its start back to the end of the line before.
            // Where a line wraps those are one place in the text, the end of one line and the start of the next: the
            // move goes on a place along the line it comes to, so each press moves the caret through the text, as
            // Android's getOffsetToLeftOf and macOS's moveLeft: do.
            if (forward)
            {
                if (line == last)
                    // Past the last line with characters on it: the empty line a final line break ends the text with.
                    return EndsWithBreak() ? _layoutText.ToSource(length) : sourceIndex;
                var after = LineSpanOf(line + 1);
                if (after.First == index && (found = StopBeside(in after, index, after.Start, toRight)) >= 0)
                    return LineEndToSource(in after, line + 1, found, out besideUpstream);
                return _layoutText.ToSource(after.First);
            }
            if (line == 0)
                return sourceIndex;
            var before = LineSpanOf(line - 1);
            if (before.Stop == index && (found = StopBeside(in before, index, before.End, toRight)) >= 0)
                return LineEndToSource(in before, line - 1, found, out besideUpstream);
            return LineEndToSource(in before, line - 1, before.Stop, out besideUpstream);
        }

        // The caret stop on a line nearest a place across it, to its right or its left, other than a layout index (the
        // caret's own); -1 with none that way.
        private int StopBeside(in LineSpan span, int index, float from, bool toRight)
        {
            int found = -1;
            float distance = float.PositiveInfinity;
            for (int stop = span.First; stop <= span.Stop; stop++)
            {
                if (stop == index || !IsCaretStop(in span, stop))
                    continue;
                float d = CaretX(in span, stop) - from;
                if (!toRight)
                    d = -d;
                if (d > 0.01f && d < distance)
                {
                    found = stop;
                    distance = d;
                }
            }
            return found;
        }

        /// <summary>How many lines the text has: at least 1, and a text ending in a line break ends with an empty line.</summary>
        public int LineCount
        {
            get
            {
                if (!_handle.IsLaidOut)
                    return 1;
                int last = _handle.LineOf(CodePointStart(_layoutText.Length - 1));
                return EndsWithBreak() ? last + 2 : last + 1;
            }
        }

        /// <summary>
        /// The line a source index is on, counted from 0. An index where a line wraps is on the line before when
        /// <paramref name="upstream"/>, otherwise on the line after.
        /// </summary>
        public int GetLineAt(int sourceIndex, bool upstream = false)
        {
            if (!_handle.IsLaidOut)
                return 0;
            int length = _layoutText.Length;
            int index = LayoutIndex(sourceIndex);
            if (index < length)
                return _handle.LineOf(upstream && WrapsAt(index) ? CodePointStart(index - 1) : index);
            // The text's end: on the empty line after a final line break, otherwise on the last character's line.
            int last = _handle.LineOf(CodePointStart(length - 1));
            return EndsWithBreak() ? last + 1 : last;
        }

        /// <summary>The source index of a line's first character. A line past the last starts at the text's end.</summary>
        public int GetLineStart(int line) => _layoutText.ToSource(LayoutLineStart(line));

        /// <summary>
        /// The source index at the end of a line's text: before its line break, if it ends in one. A line that wraps
        /// ends at the index the next one starts at; take it upstream (<see cref="GetCaretRect"/>,
        /// <see cref="GetLineAt"/>) to keep it on this line.
        /// </summary>
        public int GetLineEnd(int line)
        {
            int start = LayoutLineStart(line);
            int end = LayoutLineStart(line + 1);
            if (end > start && _layoutText.Text[end - 1] == '\n')
                end--;
            return _layoutText.ToSourceEnd(end);
        }

        /// <summary>
        /// The line height (the distance between baselines) of a font stack's primary font at a size, in layout pixels:
        /// the font a text with that stack is laid out in. 0 when there is no font to lay out in.
        /// </summary>
        public static float LineHeightOf(FontStack font, float fontSizePx)
        {
            var primary = EffectiveStack(font).Primary;
            if (primary == null)
                primary = AtgFallbackSet.DefaultFont;
            return primary != null ? AtgFontAssets.LineHeightPx(primary, fontSizePx) : 0f;
        }

        // A source index as a layout index the engine takes: within the text and at a code point boundary.
        private int LayoutIndex(int sourceIndex) => CodePointStart(Mathf.Clamp(_layoutText.ToLayout(sourceIndex), 0, _layoutText.Length));

        // A layout index between the halves of a surrogate pair moved back to the pair's start: the engine works in code
        // points and takes indices at their boundaries (see AtgTextHandle's queries).
        private int CodePointStart(int index)
        {
            var text = _layoutText.Text;
            return index > 0 && index < text.Length && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]) ? index - 1 : index;
        }

        private bool EndsWithBreak()
        {
            var text = _layoutText.Text;
            return text.Length > 0 && text[text.Length - 1] == '\n';
        }

        // Whether a line wraps at a layout index: a line starts there that no line break began, so the index also ends
        // the line before it.
        private bool WrapsAt(int index)
        {
            var text = _layoutText.Text;
            if (index <= 0 || index >= text.Length || text[index - 1] == '\n')
                return false;
            return _handle.LineOf(index) != _handle.LineOf(CodePointStart(index - 1));
        }

        // A line of the last layout: the layout indices it runs over, where its text stops, and where it starts and
        // ends across and down. Start and End are its sides as the text reads (a left-to-right line starts at its
        // left, a right-to-left one at its right); TextEnd is where its text reaches before the spaces at its end.
        private struct LineSpan
        {
            public bool Known;
            // Its first layout index, and the next line's (the text's length after the last line).
            public int First, Next;
            // Where its text ends before its line break (Next, without one), and before the spaces at its end. A line
            // of nothing but spaces has no spaces at its end: they are its text.
            public int Stop, Content;
            public float Start, TextEnd, End, Top, Height;

            public float Bottom => Top + Height;
        }

        // A line's span, found the first time it is asked for after a layout. Its start is the side its text starts from
        // and its text's end the side its text reaches, from the engine's rectangles for its text, which in a line that
        // reads both ways are several. The spaces at its end (Unicode's whitespace and segment separators, not a
        // no-break space, which belongs to the text around it) then stand after its text, as wide as they are drawn: the
        // engine puts a wrapped line's trailing space before the start of the layout's box in right-to-left text. A line
        // of nothing but its line break starts and ends at the side of the box the text is aligned to.
        private LineSpan LineSpanOf(int line)
        {
            while (_lineSpans.Count <= line)
                _lineSpans.Add(default);
            var span = _lineSpans[line];
            if (span.Known)
                return span;

            var text = _layoutText.Text;
            span.Known = true;
            span.First = LayoutLineStart(line);
            span.Next = LayoutLineStart(line + 1);
            span.Stop = span.Next > span.First && text[span.Next - 1] == '\n' ? span.Next - 1 : span.Next;
            span.Content = span.Stop;
            while (span.Content > span.First && Bidi.KindOf(text[span.Content - 1]) == Bidi.Kind.Space)
                span.Content--;
            if (span.Content == span.First)
                span.Content = span.Stop;

            float top, bottom;
            if (span.Stop > span.First && Extent(span.First, span.Content, out float left, out float right, out top, out bottom))
            {
                span.Start = _rightToLeft ? right : left;
                span.TextEnd = _rightToLeft ? left : right;
                float room = Width(span.Content, span.Stop);
                span.End = _rightToLeft ? span.TextEnd - room : span.TextEnd + room;
            }
            else
            {
                // Only a line break: its box gives the line's place down, not across.
                if (!Extent(span.First, span.Next, out _, out _, out top, out bottom))
                {
                    var caret = _handle.CaretRectPx(span.First);
                    top = caret.yMin;
                    bottom = caret.yMax;
                }
                span.Start = span.TextEnd = span.End = AlignedX();
            }
            span.Top = top;
            span.Height = bottom - top;
            _lineSpans[line] = span;
            return span;
        }

        // The caret's place across at a layout index on a line: the line's start, the engine's caret inside its text, past
        // its text by the room of the spaces before the index at its end, and the line's end.
        private float CaretX(in LineSpan span, int index)
        {
            if (index <= span.First)
                return span.Start;
            if (index >= span.Stop)
                return span.End;
            if (index < span.Content)
                return InteriorCaretX(index);
            float room = Width(span.Content, index);
            return _rightToLeft ? span.TextEnd - room : span.TextEnd + room;
        }

        // The engine's caret at a layout index inside a line's text, asked once per index after a layout.
        private float InteriorCaretX(int index)
        {
            EnsureCaches(index);
            if (float.IsNaN(_caretX[index]))
                _caretX[index] = _handle.CursorPositionPx(index).x;
            return _caretX[index];
        }

        // Where the leading or the trailing edge of the character at a layout index on a line stands across, in that
        // character's own direction. The spaces at the line's end and its line break stand after its text, as the
        // paragraph reads; a character drawn as part of the one before it (a mark, the rest of a ligature) has no box of
        // its own, so its edges are the carets on either side of it.
        private float EdgeX(in LineSpan span, int index, bool leading)
        {
            if (index >= span.Stop)
                return span.End;
            if (index >= span.Content)
                return CaretX(in span, leading ? index : NextCharacter(index, span.Stop));
            // Found once per character and edge after a layout: the handles of a selection stand there every frame, and
            // dragging one goes through every edge on its line.
            EnsureCaches(index);
            float x = leading ? _leadingX[index] : _trailingX[index];
            if (!float.IsNaN(x))
                return x;
            int next = NextCharacter(index, span.Content);
            x = !Extent(index, next, out float left, out float right, out _, out _) || right - left < 0.01f
                ? CaretX(in span, leading ? index : next)
                : leading != CharacterIsRightToLeft(index) ? left : right;
            if (leading)
                _leadingX[index] = x;
            else
                _trailingX[index] = x;
            return x;
        }

        // Whether the character at a layout index reads right to left. A letter reads its script's way and a number left
        // to right, as Unicode has them (UAX #9: strong characters and numbers keep their direction whatever surrounds
        // them, short of an explicit override). A space or punctuation takes its direction from the text around it, so it
        // reads as the run the engine laid it out in: the caret before it stands at the edge it starts from when the
        // character before it shares its run, and the caret after it at the edge it ends at when the one after does. At
        // a line's end, or alone between runs, it reads the paragraph's way, as Unicode has trailing whitespace and the
        // neutrals between runs of either direction. Found once per character after a layout.
        private bool CharacterIsRightToLeft(int index)
        {
            EnsureCaches(index);
            if (_direction[index] != 0)
                return _direction[index] == 2;
            bool rightToLeft;
            switch (Bidi.KindAt(_layoutText.Text, index))
            {
                case Bidi.Kind.RightToLeft:
                    rightToLeft = true;
                    break;
                case Bidi.Kind.LeftToRight:
                case Bidi.Kind.Number:
                    rightToLeft = false;
                    break;
                default:
                    rightToLeft = NeutralIsRightToLeft(index);
                    break;
            }
            _direction[index] = rightToLeft ? (byte)2 : (byte)1;
            return rightToLeft;
        }

        private bool NeutralIsRightToLeft(int index)
        {
            var span = LineSpanOf(_handle.LineOf(index));
            if (index >= span.Content)
                return _rightToLeft;
            int next = NextCharacter(index, span.Content);
            if (!Extent(index, next, out float left, out float right, out _, out _) || right - left < 0.01f)
                return _rightToLeft;
            if (index > span.First)
            {
                float before = InteriorCaretX(index);
                if (Mathf.Abs(before - left) < 0.01f)
                    return false;
                if (Mathf.Abs(before - right) < 0.01f)
                    return true;
            }
            if (next < span.Content)
            {
                float after = InteriorCaretX(next);
                if (Mathf.Abs(after - right) < 0.01f)
                    return false;
                if (Mathf.Abs(after - left) < 0.01f)
                    return true;
            }
            return _rightToLeft;
        }

        // The layout index after the character at one, past what is drawn as part of it (its vowel marks, the rest of a
        // ligature or an emoji sequence, the second half of a surrogate pair), at most a limit.
        private int NextCharacter(int index, int limit)
        {
            int next = index + 1;
            while (next < limit && !StartsDrawnCharacter(next))
                next++;
            return next;
        }

        // Whether a caret stops at a layout index on a line: at its start and end, and before each character drawn with a
        // box of its own between them.
        private bool IsCaretStop(in LineSpan span, int index) =>
            index == span.First || index == span.Stop || (index > span.First && index < span.Stop && StartsDrawnCharacter(index));

        // The line a caret at a layout index stands on: at a wrap taken upstream, the line before; at the text's end, the
        // last line with characters on it.
        private int LineOfCaret(int index, bool upstream)
        {
            if (upstream && WrapsAt(index))
                return _handle.LineOf(CodePointStart(index - 1));
            int length = _layoutText.Length;
            return _handle.LineOf(index < length ? index : CodePointStart(length - 1));
        }

        // The last line with characters on it: the text's last line, or the one before the empty line a final line break
        // ends it with.
        private int LastLine() => _handle.LineOf(CodePointStart(_layoutText.Length - 1));

        // The line with characters on it a point is level with, split midway between lines: above the first, the first,
        // and below the last, the last. False for a point on the empty line a final line break ends the text with.
        private bool TryLineAt(float y, out int line)
        {
            int last = LastLine();
            line = last;
            if (EndsWithBreak() && y >= TrailingLineCaret().yMin)
                return false;
            line = 0;
            while (line < last && y >= (LineSpanOf(line).Bottom + LineSpanOf(line + 1).Top) * 0.5f)
                line++;
            return true;
        }

        // A layout index on a line as a source index, and whether a caret there is taken upstream. At the line's end it is
        // the place after the line's last shown character (before any markup closing it), taken upstream where the line
        // wraps, as the line's end is also where the next line starts.
        private int LineEndToSource(in LineSpan span, int line, int index, out bool upstream)
        {
            upstream = index == span.Stop && span.Stop == span.Next && line < LastLine();
            return index == span.Stop && index < _layoutText.Length ? _layoutText.ToSourceEnd(index) : _layoutText.ToSource(index);
        }

        // Whether the character at a layout index is drawn with a box of its own, so a caret can stand before it: not the
        // second half of a surrogate pair, a vowel or accent mark drawn on the letter before it, or a letter drawn into a
        // ligature with the one before it. Asked of the engine once per character after a layout.
        private bool StartsDrawnCharacter(int index)
        {
            EnsureCaches(index);
            if (_drawn[index] == 0)
            {
                _handle.GetRangeRects(index, index + 1, _rects);
                bool drawn = false;
                for (int i = 0; i < _rects.Count && !drawn; i++)
                    drawn = _rects[i].width > 0f;
                _drawn[index] = drawn ? (byte)1 : (byte)2;
            }
            return _drawn[index] == 1;
        }

        // Room for what is found once per index after a layout, up to an index (and the text's length).
        private void EnsureCaches(int index)
        {
            if (_drawn.Length > index)
                return;
            int size = Mathf.Max(index + 1, _layoutText.Length + 1);
            int was = _caretX.Length;
            Array.Resize(ref _drawn, size);
            Array.Resize(ref _direction, size);
            Array.Resize(ref _caretX, size);
            Array.Resize(ref _leadingX, size);
            Array.Resize(ref _trailingX, size);
            for (int i = was; i < size; i++)
                _caretX[i] = _leadingX[i] = _trailingX[i] = float.NaN;
        }

        // Forgets what was found for the last layout.
        private void ClearQueries()
        {
            _lineSpans.Clear();
            Array.Clear(_drawn, 0, _drawn.Length);
            Array.Clear(_direction, 0, _direction.Length);
            for (int i = 0; i < _caretX.Length; i++)
                _caretX[i] = _leadingX[i] = _trailingX[i] = float.NaN;
            _rangeStart = _rangeEnd = -1;
        }

        // How far across and down the engine's rectangles for the layout indices in [start, end) reach: one per run, a
        // line whose text reads both ways having several. False when it gives none.
        private bool Extent(int start, int end, out float left, out float right, out float top, out float bottom)
        {
            _handle.GetRangeRects(start, end, _rects);
            left = top = float.PositiveInfinity;
            right = bottom = float.NegativeInfinity;
            for (int i = 0; i < _rects.Count; i++)
            {
                var r = _rects[i];
                left = Mathf.Min(left, r.xMin);
                right = Mathf.Max(right, r.xMax);
                top = Mathf.Min(top, r.yMin);
                bottom = Mathf.Max(bottom, r.yMax);
            }
            return _rects.Count > 0;
        }

        // How wide the engine draws the layout indices in [start, end), wherever it puts them.
        private float Width(int start, int end)
        {
            if (end <= start)
                return 0f;
            _handle.GetRangeRects(start, end, _rects);
            float width = 0f;
            for (int i = 0; i < _rects.Count; i++)
                width += _rects[i].width;
            return width;
        }

        // The first layout index on a line; the text's length past the last line with characters on it. Lines are runs
        // of the text in order, so it is found by bisecting the engine's line numbers. Each probe asks for the line of
        // the code point it lands in: both halves of a surrogate pair are on one line, so the bisection still finds the
        // pair's start.
        private int LayoutLineStart(int line)
        {
            if (!_handle.IsLaidOut || line <= 0)
                return 0;
            int low = 0, high = _layoutText.Length;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (_handle.LineOf(CodePointStart(mid)) < line)
                    low = mid + 1;
                else
                    high = mid;
            }
            return low;
        }

        // A text ending in a line break ends with an empty line, which holds no character for the engine to place a
        // caret at: it is the line below the last one with characters on it (a paragraph's spacing further down), one
        // line of the text's font tall, and starts at the side of the box the text is aligned to.
        private Rect TrailingLineCaret()
        {
            var span = LineSpanOf(LastLine());
            return new Rect(AlignedX(), span.Bottom + _paragraphSpacingPx, 0f, BaseLineHeightPx);
        }

        // An empty text is one empty line of its font, placed in its box as the alignment places a line.
        private Rect EmptyTextCaret()
        {
            float height = BaseLineHeightPx;
            float box = _heightPx >= 0f ? _heightPx : height;
            float top = ((int)_alignment / 3) switch { 0 => 0f, 1 => (box - height) * 0.5f, _ => box - height };
            return new Rect(AlignedX(), top, 0f, height);
        }

        // Where a line with nothing on it starts across: the side of the box the text is aligned to.
        private float AlignedX()
        {
            float width = _widthPx >= 0f ? _widthPx : _handle.SizePx.x;
            return ((int)_alignment % 3) switch { 0 => 0f, 1 => width * 0.5f, _ => width };
        }

        private float BaseLineHeightPx => _baseFont != null ? AtgFontAssets.LineHeightPx(_baseFont, _fontSizePx) : 0f;
    }
}
