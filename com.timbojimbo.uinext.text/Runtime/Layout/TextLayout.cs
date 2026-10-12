using System;
using System.Collections.Generic;
using TimboJimbo.UINext.Text.Bridge;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UINext.Text
{
    /// <summary>Everything a layout needs. Lengths are layout pixels: the unit the text is laid out in, points for a Label.</summary>
    public struct TextLayoutInput
    {
        public string Text;

        /// <summary>The rules the text is read with (see <see cref="TextRule"/>); null for plain text.</summary>
        public TextRuleSet Markup;

        /// <summary>
        /// <see cref="Text"/> already read (<see cref="TextDocument.Parse"/>), to lay out instead of reading it again with
        /// <see cref="Markup"/>; null to read it. It must be the same text.
        /// </summary>
        public TextDocument Document;

        /// <summary>
        /// The size of each element and block, by match index into the document (any other entry is not read): what draws
        /// the text shows each as something of that size. An element missing here is a square as big as the text, and a
        /// block nothing.
        /// </summary>
        public IReadOnlyList<Vector2> ObjectSizes;

        /// <summary>The stack to draw with; null uses the project default, then the OS default font.</summary>
        public FontStack Font;
        public float FontSizePx;
        /// <summary>The box to lay out in. A negative width or height is unconstrained.</summary>
        public float WidthPx;
        public float HeightPx;
        /// <summary>Room around every glyph quad for anti-aliasing, in layout pixels: one device pixel's worth.</summary>
        public float AntiAliasMarginPx;
        public bool WordWrap;
        public TextAnchor Alignment;
        /// <summary>
        /// Whether <see cref="Alignment"/>'s left and right are the start and end of the text, as UIKit's natural
        /// alignment and CSS's text-align: start and end are: a text read right to left aligned left sits at the right.
        /// </summary>
        public bool NaturalAlignment;
        public TextOverflow Overflow;
        public int MaxLines;
        public TextDirection Direction;
        public float CharacterSpacingPx;
        public float WordSpacingPx;
        public float ParagraphSpacingPx;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strikethrough;

        public static TextLayoutInput Default => new()
        {
            Text = "",
            FontSizePx = 24f,
            WidthPx = -1f,
            HeightPx = -1f,
            AntiAliasMarginPx = 1f,
            WordWrap = true,
            Alignment = TextAnchor.UpperLeft,
        };
    }

    /// <summary>A solid rectangle drawn under the glyphs: an underline or strikethrough segment. Layout pixels, y down.</summary>
    public struct DecorationQuad
    {
        public Rect Px;
        /// <summary>A span's own colour, or <see cref="TextLayout.TextColor"/> for the text's.</summary>
        public Color32 Color;
    }

    /// <summary>
    /// Lays out one text with the generator and answers questions about the result. Owns the document it reads, the
    /// layout text, the generator handle and the decorations, and knows nothing about GameObjects or meshes. What draws
    /// the text (UI Next's Label) feeds it a rect and the sizes of the elements and blocks it shows, and draws the result.
    /// </summary>
    public sealed partial class TextLayout : IDisposable
    {
        /// <summary>
        /// The colour of glyphs and decorations in the text's own colour, which whatever draws the text colours as it
        /// likes (tints) without laying it out again; any other is a span's own (<see cref="TextStyle.Color"/>), kept.
        /// </summary>
        public static readonly Color32 TextColor = new(255, 255, 255, 255);

        private readonly AtgTextHandle _handle = new();
        private readonly TextDocument _document = new();
        private readonly LayoutText _layoutText = new();
        private readonly SpanBuffer _spans = new();
        private readonly List<DecorationRun> _decorationRuns = new();
        private readonly List<DecorationQuad> _decorations = new();
        private readonly List<(int Code, Rect Box)> _objectQuads = new();
        private readonly List<Vector2> _objectSizes = new();
        private readonly List<Rect> _rects = new();
        private TextDocument _laidOutDocument;
        private FontAsset _baseFont;
        private float _fontSizePx;
        private float _widthPx;
        private float _heightPx;
        private float _topPx;
        private TextAnchor _alignment;
        private float _paragraphSpacingPx;
        private bool _rightToLeft;
        private bool _generated;

        private static readonly List<TextLayout> s_measureScopes = new();
        private static readonly List<TextRule> s_rules = new();
        private static int s_measureDepth;
        private static AtgTextHandle s_overflowHandle;

        /// <summary>The text as read: what its rules matched, in source indices.</summary>
        public TextDocument Document => _laidOutDocument ?? _document;

        /// <summary>The text the generator laid out, with its mapping to the source.</summary>
        public LayoutText LayoutText => _layoutText;

        /// <summary>The generator's result as quad groups per atlas, for building geometry.</summary>
        internal AtgTextHandle Handle => _handle;

        /// <summary>True after a generation that produced glyphs.</summary>
        public bool HasContent => _generated;

        public Vector2 SizePx => _handle.SizePx;

        /// <summary>
        /// How far below the top of the box it was given the layout's own box starts, in layout pixels; everything it
        /// gives in layout pixels (its quads, decorations, rects and query answers) is measured from there. 0 unless
        /// <see cref="TextLayoutInput.MaxLines"/> made its box shorter than the one given, which it then sits in as its
        /// alignment says: in the middle, or at the bottom.
        /// </summary>
        public float TopPx => _topPx;

        /// <summary>True when the last layout cut the text short.</summary>
        public bool IsElided => _handle.IsElided;

        /// <summary>
        /// Whether the last layout read right to left: its <see cref="TextLayoutInput.Direction"/>, or for
        /// <see cref="TextDirection.Auto"/>, that of its first strong character.
        /// </summary>
        public bool IsRightToLeft => _rightToLeft;

        /// <summary>
        /// Whether a plain text (no markup) laid out in a direction reads right to left, as <see cref="IsRightToLeft"/>
        /// says once it is: for <see cref="TextDirection.Auto"/> and <see cref="TextDirection.AutoRightToLeft"/>,
        /// by its first strong character.
        /// </summary>
        public static bool ReadsRightToLeft(string text, TextDirection direction) => direction switch
        {
            TextDirection.RightToLeft => true,
            TextDirection.Auto => Bidi.FirstStrongIsRightToLeft(text, false),
            TextDirection.AutoRightToLeft => Bidi.FirstStrongIsRightToLeft(text, true),
            _ => false,
        };

        /// <summary>
        /// The alignment the last layout was made with: its input's, turned across for
        /// <see cref="TextLayoutInput.NaturalAlignment"/> when the text reads right to left.
        /// </summary>
        public TextAnchor Alignment => _alignment;

        /// <summary>Underline and strikethrough segments, in their span's colour or <see cref="TextColor"/>.</summary>
        public IReadOnlyList<DecorationQuad> Decorations => _decorations;

        /// <summary>Lays the text out. False when there was nothing to draw.</summary>
        public bool Generate(in TextLayoutInput input)
        {
            _generated = false;
            _topPx = 0f;
            ClearQueries();
            _decorations.Clear();
            _decorationRuns.Clear();
            _objectQuads.Clear();
            _objectSizes.Clear();

            AtgEngine.Initialize();
            var settings = Settings(in input, _decorationRuns);
            if (string.IsNullOrEmpty(settings.Text))
            {
                // Nothing to lay out: the handle lets go of the last text, so neither its size nor its queries answer for it.
                _handle.Clear();
                return false;
            }

            if (input.Underline || input.Strikethrough)
            {
                _decorationRuns.Add(new DecorationRun
                {
                    Start = 0,
                    End = settings.Text.Length,
                    Underline = input.Underline,
                    Strikethrough = input.Strikethrough,
                    Color = TextColor,
                    Font = _baseFont,
                    FontSizePx = input.FontSizePx,
                });
            }

            ApplyOverflow(ref settings, in input);
            // Room around every glyph for anti-aliasing, capped at what the atlas can represent.
            settings.VertexPaddingPx = _baseFont != null ? Mathf.Min(input.AntiAliasMarginPx, AtgFontAssets.MaxPaddingPx(_baseFont, input.FontSizePx)) : input.AntiAliasMarginPx;

            // The sizes its objects were laid out at, kept for where they go.
            if (input.ObjectSizes != null && _layoutText.ObjectCount > 0)
                for (int i = 0; i < input.ObjectSizes.Count; i++)
                    _objectSizes.Add(input.ObjectSizes[i]);

            _generated = _handle.Generate(ref settings);
            if (!_generated)
                return false;

            CollectObjectQuads();
            CollectDecorations();
            return true;
        }

        /// <summary>Builds the generator settings and, for a text with rules, the document, layout text and spans.</summary>
        private AtgTextSettings Settings(in TextLayoutInput input, List<DecorationRun> decorations)
        {
            var settings = AtgTextSettings.Default;
            var stack = EffectiveStack(input.Font);
            settings.Font = stack.Primary;
            settings.Fallbacks = stack.Fallbacks;
            _baseFont = settings.Font != null ? settings.Font : AtgFallbackSet.DefaultFont;
            _fontSizePx = input.FontSizePx;
            _widthPx = input.WidthPx;
            _heightPx = input.HeightPx;
            _paragraphSpacingPx = input.ParagraphSpacingPx;

            settings.FontSizePx = input.FontSizePx;
            settings.WidthPx = input.WidthPx;
            settings.HeightPx = -1f;
            settings.WordWrap = input.WordWrap;
            settings.VerticalAlignment = ((int)input.Alignment / 3) switch { 0 => AtgVerticalAlignment.Top, 1 => AtgVerticalAlignment.Middle, _ => AtgVerticalAlignment.Bottom };
            // Colour is applied when the mesh is built, so a tint change never re-shapes the text.
            settings.Color = TextColor;
            settings.Weight = input.Bold ? AtgWeight.Bold : AtgWeight.Regular;
            settings.Style = input.Italic ? AtgStyle.Italic : AtgStyle.None;
            settings.CharacterSpacingPx = input.CharacterSpacingPx;
            settings.WordSpacingPx = input.WordSpacingPx;
            settings.ParagraphSpacingPx = input.ParagraphSpacingPx;

            _laidOutDocument = input.Document;
            if (_laidOutDocument == null && input.Markup != null)
            {
                s_rules.Clear();
                input.Markup.GetRules(s_rules);
                _document.Parse(input.Text, s_rules);
                s_rules.Clear();
                _laidOutDocument = _document;
            }
            if (_laidOutDocument != null)
            {
                _layoutText.Build(_laidOutDocument);
                SpanBuilder.Build(_laidOutDocument, _layoutText, _baseFont, input.FontSizePx, input.Bold, input.ObjectSizes, decorations, _spans);
                settings.Spans = _spans.Items;
                settings.SpanCount = _spans.Count;
            }
            else
            {
                _document.SetPlain(input.Text);
                _layoutText.BuildPlain(input.Text);
            }
            settings.Text = _layoutText.Text;
            // Auto reads the text as shown, markup taken out.
            _rightToLeft = ReadsRightToLeft(settings.Text, input.Direction);
            settings.Direction = _rightToLeft ? AtgDirection.RightToLeft : AtgDirection.LeftToRight;
            _alignment = input.NaturalAlignment && _rightToLeft ? Across(input.Alignment) : input.Alignment;
            settings.HorizontalAlignment = ((int)_alignment % 3) switch { 0 => AtgHorizontalAlignment.Left, 1 => AtgHorizontalAlignment.Center, _ => AtgHorizontalAlignment.Right };
            return settings;
        }

        // An alignment turned across, left for right: TextAnchor runs upper-left to lower-right, a row of three at a time.
        internal static TextAnchor Across(TextAnchor alignment) => (TextAnchor)((int)alignment / 3 * 3 + 2 - (int)alignment % 3);

        private void ApplyOverflow(ref AtgTextSettings settings, in TextLayoutInput input)
        {
            float height = -1f;
            switch (input.Overflow)
            {
                case TextOverflow.Ellipsis:
                    height = input.HeightPx;
                    settings.Overflow = AtgOverflow.Ellipsis;
                    break;
                case TextOverflow.Truncate:
                    height = input.HeightPx;
                    settings.Overflow = AtgOverflow.Clip;
                    break;
                default:
                    // Middle and bottom alignment need a box to align in; use the taller of the rect and the text.
                    if (settings.VerticalAlignment != AtgVerticalAlignment.Top)
                    {
                        var measure = settings;
                        measure.HeightPx = -1f;
                        s_overflowHandle ??= new AtgTextHandle();
                        height = Mathf.Max(input.HeightPx, s_overflowHandle.MeasurePx(ref measure).y);
                    }
                    break;
            }

            if (input.MaxLines > 0 && _baseFont != null)
            {
                // Half a pixel under the exact height, so a line whose top lands on the boundary is not counted.
                float cap = AtgFontAssets.LineHeightPx(_baseFont, input.FontSizePx) * input.MaxLines - 0.5f;
                height = height < 0f ? cap : Mathf.Min(height, cap);
                if (input.Overflow == TextOverflow.Overflow)
                    settings.Overflow = AtgOverflow.Clip;
                // The engine aligns the lines in the box it is given, now shorter than the one it was given: that box sits
                // in the given one as the alignment says, so a text held to fewer lines than it has room for is in the
                // middle, or at the bottom, of it all the same.
                if (input.HeightPx > height)
                    _topPx = (input.HeightPx - height) * ((int)input.Alignment / 3) * 0.5f;
            }

            settings.HeightPx = height;
        }

        // Where the placeholders were drawn, by their codes.
        private void CollectObjectQuads()
        {
            if (_layoutText.ObjectCount == 0)
                return;
            for (int g = 0; g < _handle.GroupCount; g++)
            {
                var group = _handle.GetGroup(g);
                if (!AtgSpriteAssets.IsPlaceholder(in group))
                    continue;
                for (int q = 0; q < group.Count; q++)
                {
                    _handle.GetQuad(g, q, out var quad);
                    _objectQuads.Add((quad.GlyphId, quad.Bounds));
                }
            }
        }

        private void CollectDecorations()
        {
            for (int d = 0; d < _decorationRuns.Count; d++)
            {
                var run = _decorationRuns[d];
                if (run.Font == null)
                    continue;
                var metrics = AtgFontAssets.MetricsPx(run.Font, run.FontSizePx);
                _handle.GetRangeRects(run.Start, run.End, _rects);
                for (int i = 0; i < _rects.Count; i++)
                {
                    var lineRect = _rects[i];
                    // The line box bottom sits at the descent line; descent is negative below the baseline.
                    float baseline = lineRect.yMax + metrics.Descent;
                    if (run.Underline)
                    {
                        float thickness = metrics.UnderlineThickness;
                        float center = baseline - metrics.UnderlineOffset;
                        _decorations.Add(new DecorationQuad { Px = new Rect(lineRect.xMin, center - thickness * 0.5f, lineRect.width, thickness), Color = run.Color });
                    }
                    if (run.Strikethrough)
                    {
                        float thickness = metrics.StrikethroughThickness;
                        float center = baseline - metrics.StrikethroughOffset;
                        _decorations.Add(new DecorationQuad { Px = new Rect(lineRect.xMin, center - thickness * 0.5f, lineRect.width, thickness), Color = run.Color });
                    }
                }
            }
        }

        // ── Matches ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Where an element or block match went in the last layout, as big as it was laid out at
        /// (<see cref="TextLayoutInput.ObjectSizes"/>): an element standing in its line, hanging below the baseline by as
        /// much of it as its alignment leaves there, a block on a line of its own. In layout pixels. False for a match that
        /// is neither, or one the text was cut short before.
        /// </summary>
        public bool TryGetObjectRect(int match, out Rect rect)
        {
            rect = default;
            int index = _layoutText.ObjectIndex(match);
            if (!_generated || index < 0) return false;
            int code = AtgSpriteAssets.PlaceholderCode(index);
            int found = -1, candidates = 0;
            for (int q = 0; q < _objectQuads.Count; q++)
            {
                if (_objectQuads[q].Code != code) continue;
                if (candidates++ == 0) found = q;
            }
            if (found < 0) return false;
            if (candidates > 1 && _layoutText.TryGetRange(match, out int start, out _))
            {
                // The codes repeat past the placeholders there are: the quad nearest where its character stands.
                _handle.GetRangeRects(start, start + 1, _rects);
                if (_rects.Count > 0)
                {
                    var at = _rects[0].center;
                    float nearest = float.PositiveInfinity;
                    for (int q = 0; q < _objectQuads.Count; q++)
                    {
                        if (_objectQuads[q].Code != code) continue;
                        float distance = (_objectQuads[q].Box.center - at).sqrMagnitude;
                        if (distance >= nearest) continue;
                        nearest = distance;
                        found = q;
                    }
                }
            }
            var box = _objectQuads[found].Box;
            var size = match < _objectSizes.Count ? _objectSizes[match] : box.size;
            // A block starts at the line's start, whatever the alignment stood its sprite at: it goes across the width.
            float x = Document.Matches[match].Role == TextRole.Block ? 0f : box.x;
            rect = new Rect(x, box.y, size.x, Mathf.Max(size.y, box.height));
            return true;
        }

        /// <summary>
        /// The rects a span match's text covers in the last layout, its padding included, in layout pixels: one per line
        /// it is on, and in a line that reads both ways one per stretch reading one way. Padding left alone on a line by a
        /// span longer than a line has none. Empty for a match that shows nothing.
        /// </summary>
        public void GetMatchRects(int match, List<Rect> results)
        {
            results.Clear();
            if (!_handle.IsLaidOut || !_layoutText.TryGetRange(match, out int start, out int end))
                return;
            _handle.GetRangeRects(start, end, results);
            var matches = Document.Matches;
            var rule = match < matches.Count ? matches[match].Rule : null;
            if (rule == null || rule.PaddingEm <= 0f || end - start <= 2 * LayoutText.PadCount || results.Count < 2)
                return;
            if (_handle.LineOf(end - 1) != _handle.LineOf(end - LayoutText.PadCount - 1))
                results.RemoveAt(results.Count - 1);
            if (results.Count > 1 && _handle.LineOf(start) != _handle.LineOf(start + LayoutText.PadCount))
                results.RemoveAt(0);
        }

        // ── Measuring ────────────────────────────────────────────────────────────

        /// <summary>
        /// The size a text would take, in layout pixels, without a component: for virtualised lists that need row
        /// heights ahead of time. A negative width is unconstrained. Re-entrant.
        /// </summary>
        public static Vector2 Measure(string text, FontStack font, float fontSizePx, float widthPx, bool wordWrap = true, bool bold = false,
            bool italic = false, TextRuleSet markup = null)
        {
            var input = TextLayoutInput.Default;
            input.Text = text;
            input.Markup = markup;
            input.Font = font;
            input.FontSizePx = fontSizePx;
            input.WidthPx = widthPx;
            input.WordWrap = wordWrap;
            input.Bold = bold;
            input.Italic = italic;
            return Measure(in input);
        }

        /// <summary>
        /// The size a text would take laid out from an input, in layout pixels: at its width (negative for unconstrained)
        /// with its wrapping, spacing, markup and objects, and no taller than <see cref="TextLayoutInput.MaxLines"/> lines
        /// of its font; its height is not a constraint. A block is as wide as it is, at least. Re-entrant, so what is
        /// measured may measure text of its own.
        /// </summary>
        public static Vector2 Measure(in TextLayoutInput input)
        {
            if (string.IsNullOrEmpty(input.Text))
                return Vector2.zero;
            var scope = EnterScope();
            try
            {
                AtgEngine.Initialize();
                var settings = scope.Settings(in input, null);
                Vector2 size;
                if (scope._layoutText.ObjectCount > 0)
                {
                    // Text with objects in it is measured as it is laid out, so it fits the box it is measured for: what is
                    // laid out there is laid out the same way.
                    scope._handle.Generate(ref settings);
                    size = scope._handle.SizePx;
                    size.x = Mathf.Max(size.x, scope.WidestBlock(input.ObjectSizes));
                }
                else
                {
                    size = scope._handle.MeasurePx(ref settings);
                }
                // As many lines as a layout keeps (see ApplyOverflow).
                if (input.MaxLines > 0 && scope._baseFont != null)
                    size.y = Mathf.Min(size.y, AtgFontAssets.LineHeightPx(scope._baseFont, input.FontSizePx) * input.MaxLines);
                return size;
            }
            finally
            {
                s_measureDepth--;
            }
        }

        /// <summary>
        /// The widest stretch of a text between spaces, laid out from an input one line to a paragraph, in layout pixels:
        /// how narrow it can be wrapped before a word would run past its edge, as CSS's min-content width is but breaking
        /// at spaces only. Taken from the text as laid out, so markup counts as it is drawn: a link by its text, a heading
        /// at its size, a padded span with its padding, an element or block by its size.
        /// </summary>
        public static float LongestWord(in TextLayoutInput input)
        {
            if (string.IsNullOrEmpty(input.Text))
                return 0f;
            var scope = EnterScope();
            try
            {
                // Widths alone are read: no box to align in, nothing cut short, no underlines to place.
                var unwrapped = input;
                unwrapped.WidthPx = unwrapped.HeightPx = -1f;
                unwrapped.WordWrap = false;
                unwrapped.Alignment = TextAnchor.UpperLeft;
                unwrapped.Overflow = TextOverflow.Overflow;
                unwrapped.MaxLines = 0;
                unwrapped.Underline = unwrapped.Strikethrough = false;
                scope.Generate(in unwrapped);
                if (!scope._handle.IsLaidOut)
                    return 0f;
                // Words as the layout text has them: no-break spaces (padding) hold on to the word they pad.
                string text = scope._layoutText.Text;
                float widest = 0f;
                int start = -1;
                for (int i = 0; i <= text.Length; i++)
                {
                    bool breaks = i == text.Length || BreaksAt(text[i]);
                    if (!breaks)
                    {
                        if (start < 0)
                            start = i;
                        continue;
                    }
                    if (start >= 0 && scope.Extent(start, i, out float left, out float right, out _, out _))
                        widest = Mathf.Max(widest, right - left);
                    start = -1;
                }
                // A block is its own width, not its placeholder's.
                return Mathf.Max(widest, scope.WidestBlock(input.ObjectSizes));
            }
            finally
            {
                s_measureDepth--;
            }
        }

        // The widest of the blocks laid out, by their sizes: what the text is as wide as, at least.
        private float WidestBlock(IReadOnlyList<Vector2> sizes)
        {
            float widest = 0f;
            if (sizes == null) return widest;
            foreach (var run in _layoutText.Runs)
                if (run.Kind == LayoutRunKind.Block && run.Match < sizes.Count)
                    widest = Mathf.Max(widest, sizes[run.Match].x);
            return widest;
        }

        // Whether a line may break at a character of the layout text: a space, but not a no-break space (U+00A0, which
        // padding is, U+2007 or U+202F).
        private static bool BreaksAt(char c) => char.IsWhiteSpace(c) && c != (char)0x00A0 && c != (char)0x2007 && c != (char)0x202F;

        // The measure layout for the depth measuring has reached; leave it by taking s_measureDepth back down.
        private static TextLayout EnterScope()
        {
            if (s_measureDepth == s_measureScopes.Count)
                s_measureScopes.Add(new TextLayout());
            return s_measureScopes[s_measureDepth++];
        }

#if UNITY_EDITOR
        // The measure layouts outlive every text, and a script reload takes their managed side with it: their native
        // layouts are let go of first.
        [UnityEditor.InitializeOnLoadMethod]
        private static void DisposeOnReload() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () =>
        {
            foreach (var scope in s_measureScopes)
                scope.Dispose();
            s_measureScopes.Clear();
            s_overflowHandle?.Dispose();
            s_overflowHandle = null;
        };
#endif

        /// <summary>The stack a text draws with: its own, else the project default, else the OS default.</summary>
        public static FontStack EffectiveStack(FontStack font)
        {
            if (font != null)
                return font;
            var settings = TextSettings.Instance;
            return settings != null && settings.DefaultFont != null ? settings.DefaultFont : FontStack.Default;
        }

        public void Dispose()
        {
            _handle.Dispose();
            _generated = false;
            _laidOutDocument = null;
            ClearQueries();
        }
    }
}
