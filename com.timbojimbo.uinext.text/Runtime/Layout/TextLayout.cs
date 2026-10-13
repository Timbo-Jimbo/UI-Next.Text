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
        private TextAnchor _alignment;
        // Where the engine's box is in the box given (see OriginPx), and the height the engine was given (negative: none).
        private Vector2 _origin;
        private float _engineHeightPx = -1f;
        private bool _wordWrap;
        private float _paragraphSpacingPx;
        private bool _rightToLeft;
        private bool _generated;

        private static readonly List<TextLayout> s_measureScopes = new();
        private static readonly List<TextRule> s_rules = new();
        private static int s_measureDepth;

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
        /// Where the layout's own box starts in the box it was given, in layout pixels; everything it gives in layout
        /// pixels (its quads, decorations, rects and query answers) is measured from there. Below the top where
        /// <see cref="TextLayoutInput.MaxLines"/> made its box shorter than the one given, which it then sits in as its
        /// alignment says (in the middle, or at the bottom); and where <see cref="Reflow"/> kept a layout made for another
        /// box, wherever its alignment puts it in this one.
        /// </summary>
        public Vector2 OriginPx => _origin;

        /// <summary>How far below the top of the box it was given the layout's own box starts (<see cref="OriginPx"/>'s y).</summary>
        public float TopPx => _origin.y;

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
            using var generate = TextProfiling.Generate.Auto();
            Forget();
            AtgEngine.Initialize();
            var settings = Settings(in input, _decorationRuns);
            // Nothing to lay out: the handle has let go of the last text, so neither its size nor its queries answer for it.
            if (string.IsNullOrEmpty(settings.Text))
                return false;

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
            _engineHeightPx = settings.HeightPx;
            _wordWrap = input.WordWrap;
            if (!_generated)
                return false;

            CollectObjectQuads();
            CollectDecorations();
            return true;
        }

        /// <summary>
        /// Lays the text it last laid out out in another box (<paramref name="input"/>'s width and height), everything
        /// else in <paramref name="input"/> as it was then but its <see cref="TextLayoutInput.MaxLines"/>' room: where no
        /// line would wrap or be cut short differently there, the layout is kept and moved where its alignment puts it in
        /// the new box (<see cref="OriginPx"/>), which costs nothing; otherwise it is laid out again. A layout made with
        /// no width (<see cref="MeasureAhead"/>'s) is kept in any box at least as wide as its widest line. False when
        /// there is nothing to draw.
        /// </summary>
        public bool Reflow(in TextLayoutInput input) => Move(in input) ? _generated : Generate(in input);

        // Moves the layout to where laying it out in the input's box would put it, when that would lay it out the same.
        private bool Move(in TextLayoutInput input)
        {
            if (!_handle.IsLaidOut) return false;
            var size = _handle.SizePx;
            bool elided = _handle.IsElided;
            // Across: at the width it was laid out at; or, nothing cut short and no line broken by its width, at any width
            // all its lines fit.
            float width = input.WidthPx;
            bool sameWidth = width < 0f ? _widthPx < 0f : _widthPx >= 0f && Px(width) == Px(_widthPx);
            if (!sameWidth && (elided || (_wordWrap && _widthPx >= 0f) || (width >= 0f && Px(width) < Px(size.x))))
                return false;
            // Down: in the height the engine was given; or, nothing cut short, in any height all its lines fit.
            float height = EngineHeight(in input, size.y, out float top);
            bool sameHeight = height < 0f ? _engineHeightPx < 0f : _engineHeightPx >= 0f && Px(height) == Px(_engineHeightPx);
            if (!sameHeight && (elided || (height >= 0f && Px(height) < Px(size.y))))
                return false;
            // The engine aligns each line across in its width (the widest line's with none), and the lines down in its height.
            float across = (int)_alignment % 3 * 0.5f, down = (int)_alignment / 3 * 0.5f;
            float wasWidth = _widthPx >= 0f ? _widthPx : size.x, nowWidth = width >= 0f ? width : size.x;
            float wasHeight = _engineHeightPx >= 0f ? _engineHeightPx : size.y, nowHeight = height >= 0f ? height : size.y;
            _origin = new Vector2((nowWidth - wasWidth) * across, top + (nowHeight - wasHeight) * down);
            return true;
        }

        // The engine works in 64ths of a pixel.
        private static int Px(float px) => Mathf.RoundToInt(px * 64f);

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
            // Middle and bottom alignment need a box to align in, as tall as the text at least: it is measured here, where
            // what the engine works out of it is kept for laying it out.
            float measured = -1f;
            if (input.Overflow == TextOverflow.Overflow && input.HeightPx >= 0f && settings.VerticalAlignment != AtgVerticalAlignment.Top)
            {
                var measure = settings;
                measure.HeightPx = -1f;
                measured = _handle.MeasurePx(ref measure).y;
            }
            float height = EngineHeight(in input, measured, out float top);
            _origin = new Vector2(0f, top);
            if (input.Overflow == TextOverflow.Truncate || (input.MaxLines > 0 && _baseFont != null && input.Overflow == TextOverflow.Overflow))
                settings.Overflow = AtgOverflow.Clip;

            // An ellipsis where there is something to cut short: lines past a height, or one line at its width. With neither,
            // the engine, told to, breaks words at any character to make lines (#design after its #), as wrapping text
            // without a height to cut at must not.
            if (input.Overflow == TextOverflow.Ellipsis && (height >= 0f || !input.WordWrap))
                settings.Overflow = AtgOverflow.Ellipsis;

            settings.HeightPx = height;
        }

        // The height the engine is given to lay a text `measured` tall out in from an input (negative: none), and how far
        // below the top of the input's box that height starts.
        private float EngineHeight(in TextLayoutInput input, float measured, out float top)
        {
            float height = -1f;
            switch (input.Overflow)
            {
                case TextOverflow.Ellipsis:
                case TextOverflow.Truncate:
                    height = input.HeightPx;
                    break;
                default:
                    // Middle and bottom alignment need a box to align in: the taller of the one given and the text.
                    if (input.HeightPx >= 0f && (int)input.Alignment / 3 != 0)
                        height = Mathf.Max(input.HeightPx, measured);
                    break;
            }
            top = 0f;
            if (input.MaxLines > 0 && _baseFont != null)
            {
                // Half a pixel under the exact height, so a line whose top lands on the boundary is not counted.
                float cap = AtgFontAssets.LineHeightPx(_baseFont, input.FontSizePx) * input.MaxLines - 0.5f;
                height = height < 0f ? cap : Mathf.Min(height, cap);
                // The engine aligns the lines in the box it is given, now shorter than the one it was given: that box sits
                // in the given one as the alignment says, so a text held to fewer lines than it has room for is in the
                // middle, or at the bottom, of it all the same.
                if (input.HeightPx > height)
                    top = (input.HeightPx - height) * ((int)input.Alignment / 3) * 0.5f;
            }
            return height;
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
            if (Document.Matches[match].Role == TextRole.Block)
            {
                // A block goes across the width from the top of its line, which the line break after it makes as tall as
                // it (see SpanBuilder): the engine stands its placeholder on the baseline, wherever the alignment puts it.
                _layoutText.TryGetRange(match, out int at, out _);
                _handle.GetRangeRects(at, at + 1, _rects);
                rect = new Rect(0f, _rects.Count > 0 ? _rects[0].y : box.y, size.x, size.y);
                return true;
            }
            rect = new Rect(box.x, box.y, size.x, Mathf.Max(size.y, box.height));
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
                using var measure = TextProfiling.Measure.Auto();
                scope.Forget();
                AtgEngine.Initialize();
                var settings = scope.Settings(in input, null);
                Vector2 size;
                if (scope._layoutText.ObjectCount > 0)
                {
                    // Text with objects in it is measured as it is laid out, so it fits the box it is measured for: what is
                    // laid out there is laid out the same way.
                    scope._handle.Generate(ref settings);
                    size = scope.Extent(input.ObjectSizes);
                    scope._handle.Clear();
                }
                else
                {
                    size = scope._handle.MeasurePx(ref settings);
                }
                return scope.Capped(size, in input);
            }
            finally
            {
                s_measureDepth--;
            }
        }

        /// <summary>
        /// Lays a text out here with no height to fit (as <see cref="Generate"/> with <see cref="TextLayoutInput.HeightPx"/>
        /// negative) and gives its size, as <see cref="Measure(in TextLayoutInput)"/> does: measuring ahead of drawing. The
        /// layout is kept, so drawing the text in a box it fits costs nothing more (<see cref="Reflow"/>), and the engine
        /// keeps what it worked out of the text (its shaping) for this layout, so laying it out again at another width
        /// costs a fraction of the first time, where <see cref="Measure(in TextLayoutInput)"/> shares that with every text.
        /// </summary>
        public Vector2 MeasureAhead(in TextLayoutInput input)
        {
            using var measure = TextProfiling.Measure.Auto();
            var unbounded = input;
            unbounded.HeightPx = -1f;
            Generate(in unbounded);
            return _handle.IsLaidOut ? Capped(Extent(input.ObjectSizes), in input) : Vector2.zero;
        }

        // The size of the text laid out, as wide as its widest block and as tall as its blocks reach.
        private Vector2 Extent(IReadOnlyList<Vector2> sizes)
        {
            var size = _handle.SizePx;
            if (_layoutText.ObjectCount == 0) return size;
            size.x = Mathf.Max(size.x, WidestBlock(sizes));
            size.y = Mathf.Max(size.y, BlocksBottom(sizes));
            return size;
        }

        // A size no taller than as many lines as a layout keeps (see EngineHeight).
        private Vector2 Capped(Vector2 size, in TextLayoutInput input)
        {
            if (input.MaxLines > 0 && _baseFont != null)
                size.y = Mathf.Min(size.y, AtgFontAssets.LineHeightPx(_baseFont, input.FontSizePx) * input.MaxLines);
            return size;
        }

        // Forgets the last layout: nothing to draw, and its queries answer as for an empty text.
        private void Forget()
        {
            _generated = false;
            _origin = Vector2.zero;
            _engineHeightPx = -1f;
            ClearQueries();
            _decorations.Clear();
            _decorationRuns.Clear();
            _objectQuads.Clear();
            _objectSizes.Clear();
            _handle.Clear();
        }

        // How far down the blocks laid out reach, each from the top of its line: past the text's own height for a block it
        // ends with, which has no line break after it to make its line as tall as it.
        private float BlocksBottom(IReadOnlyList<Vector2> sizes)
        {
            float bottom = 0f;
            if (sizes == null) return bottom;
            foreach (var run in _layoutText.Runs)
            {
                if (run.Kind != LayoutRunKind.Block || run.Match >= sizes.Count) continue;
                _handle.GetRangeRects(run.Start, run.Start + 1, _rects);
                if (_rects.Count > 0)
                    bottom = Mathf.Max(bottom, _rects[0].y + sizes[run.Match].y);
            }
            return bottom;
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
