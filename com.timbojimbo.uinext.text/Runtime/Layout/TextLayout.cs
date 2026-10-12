using System;
using System.Collections.Generic;
using TimboJimbo.UINext.Text.Bridge;
using TimboJimbo.UINext.Text.Markup;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UINext.Text
{
    /// <summary>Everything a layout needs. Lengths are layout pixels: the unit the text is laid out in, canvas units for a TextBlock.</summary>
    public struct TextLayoutInput
    {
        public string Text;
        public bool RichText;
        /// <summary>The stack to draw with; null uses the project default, then the OS default font.</summary>
        public FontStack Font;
        /// <summary>How markup renders; null uses the built-in theme.</summary>
        public TextTheme Theme;
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
            RichText = true,
            FontSizePx = 24f,
            WidthPx = -1f,
            HeightPx = -1f,
            AntiAliasMarginPx = 1f,
            WordWrap = true,
            Alignment = TextAnchor.UpperLeft,
        };
    }

    /// <summary>
    /// What to put over or behind part of the laid-out text: a prefab, or a background the renderer draws itself (an
    /// <see cref="InlineBackground"/> handler, with no prefab). The rectangle and metrics are layout pixels, y down from
    /// the layout's top-left corner.
    /// </summary>
    public struct InlinePlacementRequest
    {
        public int TokenIndex;
        /// <summary>The handler that asked for it.</summary>
        public InlineHandler Handler;
        /// <summary>What to place, or null for a background the renderer draws (<see cref="Handler"/> is an InlineBackground).</summary>
        public GameObject Prefab;
        /// <summary>True for backgrounds, drawn under the glyphs; false for objects, drawn over them.</summary>
        public bool BelowGlyphs;
        public Rect Px;
        /// <summary>This fragment's index and the total for its token.</summary>
        public int Index;
        public int Count;
        /// <summary>Baseline distance from the rectangle's top, then the text's ascent and descent.</summary>
        public float BaselinePx;
        public float AscentPx;
        public float DescentPx;
    }

    /// <summary>A solid rectangle drawn under the glyphs: an underline or strikethrough segment. Layout pixels, y down.</summary>
    public struct DecorationQuad
    {
        public Rect Px;
        public Color32 Color;
    }

    /// <summary>
    /// Lays out one text with the generator and answers questions about the result. Owns the parsed document,
    /// the layout text, the generator handle and the plans for decorations and inline prefabs, and knows nothing
    /// about GameObjects or meshes. What draws the text (UI Next's Label) feeds it a rect and draws the result.
    /// </summary>
    public sealed partial class TextLayout : IDisposable
    {
        private readonly AtgTextHandle _handle = new();
        private readonly TextDocument _document = new();
        private readonly LayoutText _layoutText = new();
        private readonly SpanBuffer _spans = new();
        private readonly List<DecorationRun> _decorationRuns = new();
        private readonly List<InlineObjectRequest> _atomicRequests = new();
        private readonly List<Rect> _placeholderBoxes = new();
        private readonly List<DecorationQuad> _decorations = new();
        private readonly List<Rect> _rects = new();
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
        private static int s_measureDepth;
        private static AtgTextHandle s_overflowHandle;

        /// <summary>The parsed source: lines, runs, links and tokens in source indices.</summary>
        public TextDocument Document => _document;

        /// <summary>The text the generator laid out, with its mapping to the source.</summary>
        public LayoutText LayoutText => _layoutText;

        /// <summary>The generator's result as quad groups per atlas, for building geometry.</summary>
        internal AtgTextHandle Handle => _handle;

        /// <summary>True after a generation that produced glyphs.</summary>
        public bool HasContent => _generated;

        public Vector2 SizePx => _handle.SizePx;

        /// <summary>
        /// How far below the top of the box it was given the layout's own box starts, in layout pixels; everything it
        /// gives in layout pixels (its quads, decorations, placements and query answers) is measured from there. 0 unless
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

        /// <summary>Underline and strikethrough segments, in the run's colour before tinting.</summary>
        public IReadOnlyList<DecorationQuad> Decorations => _decorations;

        /// <summary>Bounds of each atomic placeholder, in text order.</summary>
        public IReadOnlyList<Rect> PlaceholderBoxes => _placeholderBoxes;

        /// <summary>Lays the text out. False when there was nothing to draw.</summary>
        public bool Generate(in TextLayoutInput input, in InlineContext context)
        {
            _generated = false;
            _topPx = 0f;
            ClearQueries();
            _decorations.Clear();
            _placeholderBoxes.Clear();
            _decorationRuns.Clear();
            _atomicRequests.Clear();

            AtgEngine.Initialize();
            var theme = input.Theme != null ? input.Theme : TextTheme.Default;
            var settings = Settings(in input, theme, in context, _decorationRuns, _atomicRequests);
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
                    Color = new Color32(255, 255, 255, 255),
                    Font = _baseFont,
                    FontSizePx = input.FontSizePx,
                });
            }

            ApplyOverflow(ref settings, in input);
            // Room around every glyph for anti-aliasing, capped at what the atlas can represent.
            settings.VertexPaddingPx = _baseFont != null ? Mathf.Min(input.AntiAliasMarginPx, AtgFontAssets.MaxPaddingPx(_baseFont, input.FontSizePx)) : input.AntiAliasMarginPx;

            _generated = _handle.Generate(ref settings);
            if (!_generated)
                return false;

            CollectPlaceholderBoxes();
            CollectDecorations(theme.LineThicknessEm);
            return true;
        }

        /// <summary>Builds the generator settings and, for rich text, the document, layout text and spans.</summary>
        private AtgTextSettings Settings(in TextLayoutInput input, TextTheme theme, in InlineContext context, List<DecorationRun> decorations, List<InlineObjectRequest> atomicRequests)
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
            settings.Color = new Color32(255, 255, 255, 255);
            settings.Weight = input.Bold ? AtgWeight.Bold : AtgWeight.Regular;
            settings.Style = input.Italic ? AtgStyle.Italic : AtgStyle.None;
            settings.CharacterSpacingPx = input.CharacterSpacingPx;
            settings.WordSpacingPx = input.WordSpacingPx;
            settings.ParagraphSpacingPx = input.ParagraphSpacingPx;

            if (input.RichText)
            {
                MarkdownParser.Parse(input.Text, _document, theme.Handlers);
                _layoutText.Build(_document, theme);
                SpanBuilder.Build(_document, _layoutText, theme, _baseFont, input.FontSizePx, input.Bold, decorations, in context, atomicRequests, _spans);
                settings.Spans = _spans.Items;
                settings.SpanCount = _spans.Count;
            }
            else
            {
                _document.Reset(input.Text);
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

        private void CollectPlaceholderBoxes()
        {
            for (int g = 0; g < _handle.GroupCount; g++)
            {
                var group = _handle.GetGroup(g);
                if (!AtgSpriteAssets.IsPlaceholder(in group))
                    continue;
                for (int q = 0; q < group.Count; q++)
                {
                    _handle.GetQuad(g, q, out var quad);
                    _placeholderBoxes.Add(quad.Bounds);
                }
            }
        }

        private void CollectDecorations(float thicknessEm)
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
                        float thickness = thicknessEm > 0f ? thicknessEm * run.FontSizePx : metrics.UnderlineThickness;
                        float center = baseline - metrics.UnderlineOffset;
                        _decorations.Add(new DecorationQuad { Px = new Rect(lineRect.xMin, center - thickness * 0.5f, lineRect.width, thickness), Color = run.Color });
                    }
                    if (run.Strikethrough)
                    {
                        float thickness = thicknessEm > 0f ? thicknessEm * run.FontSizePx : metrics.StrikethroughThickness;
                        float center = baseline - metrics.StrikethroughOffset;
                        _decorations.Add(new DecorationQuad { Px = new Rect(lineRect.xMin, center - thickness * 0.5f, lineRect.width, thickness), Color = run.Color });
                    }
                }
            }
        }

        /// <summary>
        /// What the current layout wants placed, from the theme's handlers: their prefabs, and the backgrounds of
        /// <see cref="InlineBackground"/> handlers.
        /// </summary>
        public void CollectPlacements(in InlineContext context, List<InlinePlacementRequest> results)
        {
            results.Clear();
            if (!_generated || _document.Tokens.Count == 0)
                return;

            var metrics = _baseFont != null ? AtgFontAssets.MetricsPx(_baseFont, _fontSizePx) : default;
            float layoutWidth = _widthPx >= 0f ? _widthPx : _handle.SizePx.x;
            int ordinal = 0;

            for (int i = 0; i < _document.Tokens.Count; i++)
            {
                var token = _document.Tokens[i];
                var placed = _layoutText.Tokens[i];
                var handler = placed.Handler;
                if (handler == null || !placed.IsPlaced)
                    continue;

                if (placed.Placement == InlinePlacement.Atomic)
                {
                    var request = i < _atomicRequests.Count ? _atomicRequests[i] : default;
                    int k = ordinal++;
                    if (request.Prefab == null || k >= _placeholderBoxes.Count)
                        continue;
                    // The layout reserved the part of the box above the baseline; the prefab gets the whole box.
                    var box = _placeholderBoxes[k];
                    float above = box.height;
                    var size = request.SizeEm.x > 0f && request.SizeEm.y > 0f ? request.SizeEm : Vector2.one;
                    box.height = Mathf.Max(above, size.y * _fontSizePx);
                    results.Add(new InlinePlacementRequest
                    {
                        TokenIndex = i, Handler = handler, Prefab = request.Prefab, BelowGlyphs = false, Px = box, Index = 0, Count = 1,
                        BaselinePx = above, AscentPx = above, DescentPx = box.height - above,
                    });
                    continue;
                }

                var req = handler.Request(in token, in context);
                if (req.Prefab == null && handler is not InlineBackground)
                    continue;
                _handle.GetRangeRects(placed.Start, placed.End, _rects);
                int count = _rects.Count;
                if (count == 0)
                    continue;

                if (placed.Placement == InlinePlacement.Block)
                {
                    // One box across the full width, from the first line's top to the last line's bottom.
                    float yMin = float.MaxValue, yMax = float.MinValue;
                    for (int f = 0; f < count; f++)
                    {
                        yMin = Mathf.Min(yMin, _rects[f].yMin);
                        yMax = Mathf.Max(yMax, _rects[f].yMax);
                    }
                    results.Add(new InlinePlacementRequest
                    {
                        TokenIndex = i, Handler = handler, Prefab = req.Prefab, BelowGlyphs = true, Px = new Rect(0f, yMin, layoutWidth, yMax - yMin), Index = 0, Count = 1,
                        BaselinePx = _rects[0].height + metrics.Descent, AscentPx = metrics.Ascent, DescentPx = -metrics.Descent,
                    });
                    continue;
                }

                // A token longer than a whole line leaves its pads alone on a line of their own; those fragments
                // hold no text and get no prefab.
                int first = 0, last = count - 1;
                if (placed.PaddingEm > 0f && placed.Length > 2 * LayoutText.PadCount)
                {
                    if (_handle.LineOf(placed.Start) != _handle.LineOf(placed.Start + LayoutText.PadCount))
                        first++;
                    if (_handle.LineOf(placed.End - 1) != _handle.LineOf(placed.End - LayoutText.PadCount - 1))
                        last--;
                }
                int fragmentCount = last - first + 1;
                for (int f = first; f <= last; f++)
                {
                    var lineRect = _rects[f];
                    results.Add(new InlinePlacementRequest
                    {
                        TokenIndex = i, Handler = handler, Prefab = req.Prefab, BelowGlyphs = true, Px = lineRect, Index = f - first, Count = fragmentCount,
                        BaselinePx = lineRect.height + metrics.Descent, AscentPx = metrics.Ascent, DescentPx = -metrics.Descent,
                    });
                }
            }
        }

        /// <summary>
        /// The size a text would take, in layout pixels, without a component: for virtualised lists that need row
        /// heights ahead of time. A negative width is unconstrained. Re-entrant, since a handler may measure its own label.
        /// </summary>
        public static Vector2 Measure(string text, FontStack font, float fontSizePx, float widthPx, bool wordWrap = true, bool bold = false, bool italic = false, bool richText = false, TextTheme theme = null)
        {
            var input = TextLayoutInput.Default;
            input.Text = text;
            input.RichText = richText;
            input.Font = font;
            input.Theme = theme;
            input.FontSizePx = fontSizePx;
            input.WidthPx = widthPx;
            input.WordWrap = wordWrap;
            input.Bold = bold;
            input.Italic = italic;
            return Measure(in input, new InlineContext(null, fontSizePx, Color.white));
        }

        /// <summary>
        /// The size a text would take laid out from an input, in layout pixels: at its width (negative for unconstrained)
        /// with its wrapping, spacing and markup, and no taller than <see cref="TextLayoutInput.MaxLines"/> lines of its
        /// font; its height is not a constraint. Inline handlers size their objects for <paramref name="context"/>, as
        /// they do when the text is laid out. Re-entrant, since a handler may measure its own label.
        /// </summary>
        public static Vector2 Measure(in TextLayoutInput input, in InlineContext context)
        {
            if (string.IsNullOrEmpty(input.Text))
                return Vector2.zero;
            var scope = EnterScope();
            try
            {
                AtgEngine.Initialize();
                var settings = scope.Settings(in input, input.Theme != null ? input.Theme : TextTheme.Default, in context, null, null);
                var size = scope._handle.MeasurePx(ref settings);
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
        /// at spaces only. Taken from the text as laid out, so markup counts as it is drawn: a link by its label, a
        /// heading at its size, a padded code span with its padding, an inline object by its box.
        /// </summary>
        public static float LongestWord(in TextLayoutInput input, in InlineContext context)
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
                scope.Generate(in unwrapped, in context);
                if (!scope._handle.IsLaidOut)
                    return 0f;
                // Words as the layout text has them: no-break spaces (layout padding) hold on to the word they pad.
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
                return widest;
            }
            finally
            {
                s_measureDepth--;
            }
        }

        // Whether a line may break at a character of the layout text: a space, but not a no-break space (U+00A0, which
        // layout padding is, U+2007 or U+202F).
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
            ClearQueries();
        }
    }
}
