using System.Collections.Generic;
using System.Text;
using TimboJimbo.UINext.Text.Bridge;

namespace TimboJimbo.UINext.Text
{
    /// <summary>What a run of the layout text is.</summary>
    internal enum LayoutRunKind : byte
    {
        /// <summary>Source text shown as written, in the style of the span it is in.</summary>
        Text,
        /// <summary>No-break spaces keeping a padded span's room either side of its text.</summary>
        Pad,
        /// <summary>The placeholder an element becomes, as big as what shows it.</summary>
        Element,
        /// <summary>The placeholder a block becomes, alone on its line, as big as what shows it.</summary>
        Block,
        /// <summary>The line break that ends a block's line.</summary>
        BlockEnd,
    }

    /// <summary>A range of the layout text with one kind and one match: the innermost span its text is in, or its object.</summary>
    internal struct LayoutRun
    {
        public int Start;
        public int Length;
        public LayoutRunKind Kind;
        public int Match;

        public int End => Start + Length;
    }

    /// <summary>
    /// The text the generator lays out, built from a <see cref="TextDocument"/>: the source it shows, copied, with what
    /// layout adds put in here and nowhere else: line breaks between lines, no-break spaces as a padded span's room
    /// either side, and one placeholder character for each element and block, a block's alone on its line. Keeps the
    /// mapping between source and layout indices, so a caret, a selection or a hit test can move between the two, and
    /// where each match landed.
    /// </summary>
    public sealed class LayoutText
    {
        /// <summary>
        /// A span's padding is a run of no-break spaces either side of its text, sized by the span builder. The no-break
        /// space breaks as padding should: a line may break at an ordinary space before it, never between it and the text
        /// it pads.
        /// </summary>
        public const char Pad = ' ';

        /// <summary>No-break spaces per pad run; two let one be sized up to twice a space of the span's font.</summary>
        public const int PadCount = 2;

        private static readonly string s_pads = new(Pad, PadCount);

        private struct Segment
        {
            public int LayoutStart;
            public int Length;
            // The source index of its first character, or -1 for text layout put in.
            public int SourceStart;
        }

        private readonly StringBuilder _text = new();
        private readonly List<Segment> _segments = new();
        private readonly List<int> _matchStart = new();
        private readonly List<int> _matchEnd = new();
        private readonly List<int> _objectIndex = new();
        private string _string = "";
        private bool _stringDirty;
        private int _sourceLength;
        // The source it was built from, and whether it is that source as it is (plain text, or text no rule matched in),
        // whose string it then is rather than a copy; and the document, at which reading, it was last built from.
        private string _source = "";
        private bool _verbatim;
        private TextDocument _builtFrom;
        private int _builtVersion;

        internal readonly List<LayoutRun> Runs = new();

        /// <summary>The layout text, cached until the next build.</summary>
        public string Text
        {
            get
            {
                if (_stringDirty)
                {
                    _string = _verbatim ? _source : _text.ToString();
                    _stringDirty = false;
                }
                return _string;
            }
        }

        public int Length => _text.Length;

        /// <summary>How many elements and blocks it holds.</summary>
        public int ObjectCount { get; private set; }

        /// <summary>The source as it is: one copied segment and no runs. For plain text.</summary>
        public void BuildPlain(string source)
        {
            Clear();
            _source = source ?? "";
            _sourceLength = source?.Length ?? 0;
            if (_sourceLength > 0)
            {
                _text.Append(source);
                _segments.Add(new Segment { LayoutStart = 0, Length = _sourceLength, SourceStart = 0 });
            }
            _stringDirty = true;
        }

        /// <summary>Builds the layout text for a parsed document, unless it is built from it as it is read already.</summary>
        public void Build(TextDocument document)
        {
            if (document == _builtFrom && document.Version == _builtVersion) return;
            Clear();
            _builtFrom = document;
            _builtVersion = document.Version;
            var source = _source = document.Text;
            var matches = document.Matches;
            _sourceLength = source.Length;
            for (int m = 0; m < matches.Count; m++)
            {
                _matchStart.Add(-1);
                _matchEnd.Add(-1);
                _objectIndex.Add(-1);
            }

            // A block whose line has not ended yet: the line break after it is its own, and anything else starts a new line.
            int block = -1;
            foreach (var piece in document.Pieces)
            {
                if (block >= 0 && piece.Kind != PieceKind.Break)
                {
                    Add("\n", -1, LayoutRunKind.BlockEnd, block, matches, extend: false);
                    block = -1;
                }
                switch (piece.Kind)
                {
                    case PieceKind.Text:
                        Add(source, piece.Start, piece.Length, piece.Start, LayoutRunKind.Text, piece.Match, matches);
                        break;
                    case PieceKind.PadStart:
                    case PieceKind.PadEnd:
                        Add(s_pads, -1, LayoutRunKind.Pad, piece.Match, matches);
                        break;
                    case PieceKind.Break:
                        if (block >= 0)
                        {
                            Add("\n", piece.Start, LayoutRunKind.BlockEnd, block, matches, extend: false);
                            block = -1;
                        }
                        else
                        {
                            AddPlain("\n", piece.Start);
                        }
                        break;
                    case PieceKind.Object:
                        bool isBlock = matches[piece.Match].Role == TextRole.Block;
                        // A block starts a line of its own.
                        if (isBlock && _text.Length > 0 && _text[_text.Length - 1] != '\n')
                            AddPlain("\n", -1);
                        int index = ObjectCount++;
                        _objectIndex[piece.Match] = index;
                        Add(AtgSpriteAssets.PlaceholderCode(index).ToString(), -1, isBlock ? LayoutRunKind.Block : LayoutRunKind.Element, piece.Match, matches);
                        if (isBlock)
                            block = piece.Match;
                        break;
                }
            }
            _verbatim &= _text.Length == _sourceLength;
            _stringDirty = true;
        }

        /// <summary>
        /// Where a match landed in the layout text: [start, end), its text and padding (and what is inside it), or its
        /// placeholder. False for a match that shows nothing.
        /// </summary>
        public bool TryGetRange(int match, out int start, out int end)
        {
            start = match >= 0 && match < _matchStart.Count ? _matchStart[match] : -1;
            end = start >= 0 ? _matchEnd[match] : -1;
            return start >= 0;
        }

        /// <summary>An element's or block's place among the objects (which placeholder it is), or -1 for another match.</summary>
        public int ObjectIndex(int match) => match >= 0 && match < _objectIndex.Count ? _objectIndex[match] : -1;

        /// <summary>The layout index of a source index. Inside skipped markup it is the next shown character; past the end, the layout length.</summary>
        public int ToLayout(int sourceIndex)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                var s = _segments[i];
                if (s.SourceStart < 0)
                    continue;
                if (sourceIndex < s.SourceStart)
                    return s.LayoutStart;
                if (sourceIndex < s.SourceStart + s.Length)
                    return s.LayoutStart + (sourceIndex - s.SourceStart);
            }
            return _text.Length;
        }

        /// <summary>The source index of a layout index. Inside inserted text it is the next shown source character; past the end, the source length.</summary>
        public int ToSource(int layoutIndex)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                var s = _segments[i];
                if (layoutIndex >= s.LayoutStart + s.Length)
                    continue;
                if (s.SourceStart >= 0)
                    return s.SourceStart + (layoutIndex - s.LayoutStart);
                for (int j = i + 1; j < _segments.Count; j++)
                    if (_segments[j].SourceStart >= 0)
                        return _segments[j].SourceStart;
                return _sourceLength;
            }
            return _sourceLength;
        }

        /// <summary>
        /// The source index just after the last shown source character before a layout index: where a position that
        /// ends a run of text belongs, such as the end of a line, rather than at the next shown character past text the
        /// layout inserted or markup it hides. With no source shown before it, as <see cref="ToSource"/>.
        /// </summary>
        public int ToSourceEnd(int layoutIndex)
        {
            for (int i = _segments.Count - 1; i >= 0; i--)
            {
                var s = _segments[i];
                if (s.SourceStart < 0 || s.LayoutStart >= layoutIndex)
                    continue;
                return s.SourceStart + System.Math.Min(layoutIndex - s.LayoutStart, s.Length);
            }
            return ToSource(layoutIndex);
        }

        private void Clear()
        {
            _text.Clear();
            _segments.Clear();
            _matchStart.Clear();
            _matchEnd.Clear();
            _objectIndex.Clear();
            Runs.Clear();
            ObjectCount = 0;
            _string = "";
            _stringDirty = false;
            _source = "";
            _verbatim = true;
            _builtFrom = null;
        }

        // Text with no run of its own (a line break), from a source index or put in (-1).
        private void AddPlain(string text, int sourceStart) => AddPlain(text, 0, text.Length, sourceStart);

        private void AddPlain(string text, int from, int length, int sourceStart)
        {
            // The source as it is so far: each piece copied from where it is in it, to where it is in it.
            if (_verbatim && (sourceStart != _text.Length || sourceStart + length > _source.Length
                || string.CompareOrdinal(text, from, _source, sourceStart, length) != 0))
                _verbatim = false;
            _segments.Add(new Segment { LayoutStart = _text.Length, Length = length, SourceStart = sourceStart });
            _text.Append(text, from, length);
        }

        private void Add(string text, int sourceStart, LayoutRunKind kind, int match, IReadOnlyList<TextMatch> matches, bool extend = true) =>
            Add(text, 0, text.Length, sourceStart, kind, match, matches, extend);

        // Text with a run: [from, from + length) of `text`, copied from a source index or put in (-1). The run's match, and
        // every span it is inside, reach over it, unless it only ends a block's line.
        private void Add(string text, int from, int length, int sourceStart, LayoutRunKind kind, int match, IReadOnlyList<TextMatch> matches,
            bool extend = true)
        {
            int start = _text.Length;
            AddPlain(text, from, length, sourceStart);
            Runs.Add(new LayoutRun { Start = start, Length = length, Kind = kind, Match = match });
            if (!extend) return;
            for (int m = match; m >= 0; m = matches[m].Parent)
            {
                if (_matchStart[m] < 0)
                    _matchStart[m] = start;
                _matchEnd[m] = _text.Length;
            }
        }
    }
}
