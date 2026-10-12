using System;
using System.Collections.Generic;
using TimboJimbo.UINext.Text.Bridge;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UINext.Text
{
    /// <summary>A range of the layout text that needs an underline or strikethrough drawn after layout.</summary>
    internal struct DecorationRun
    {
        public int Start, End;
        public bool Underline, Strikethrough;
        public Color32 Color;
        public FontAsset Font;
        public float FontSizePx;
    }

    /// <summary>A growable span array the generator settings can point at directly.</summary>
    internal sealed class SpanBuffer
    {
        public AtgSpan[] Items = new AtgSpan[64];
        public int Count;

        public void Clear() => Count = 0;

        public void Add(in AtgSpan span)
        {
            if (Count == Items.Length)
                Array.Resize(ref Items, Items.Length * 2);
            Items[Count++] = span;
        }
    }

    /// <summary>
    /// Turns layout runs into generator spans: each span's text in its rules' style, padding sized exactly, a placeholder
    /// as big as each element and block, and the tappable span each run is in as its link id.
    /// </summary>
    internal static class SpanBuilder
    {
        // The least a placeholder is tall, in em of the text: the engine sizes a sprite to a height and keeps the aspect of
        // its metrics, and one much shorter than its text comes out far too wide (a block 381 points wide and a 64th of a
        // point tall came out 256 times as wide).
        private const float MinPlaceholderEm = 0.5f;

        private static readonly List<TextStyle> s_styles = new();
        private static readonly List<int> s_taps = new();

        /// <summary>
        /// Fills <paramref name="buffer"/> with spans for the layout's runs, sizing elements and blocks from
        /// <paramref name="sizes"/> (by match), and records decoration runs when the list is given.
        /// </summary>
        public static void Build(TextDocument document, LayoutText layout, FontAsset baseFont, float baseSizePx, bool baseBold,
            IReadOnlyList<Vector2> sizes, List<DecorationRun> decorations, SpanBuffer buffer)
        {
            buffer.Clear();
            var matches = document.Matches;

            // Each match's style over the style of the span it is in, and the innermost tappable span it is (or is in).
            s_styles.Clear();
            s_taps.Clear();
            for (int m = 0; m < matches.Count; m++)
            {
                var match = matches[m];
                var style = match.Parent >= 0 ? s_styles[match.Parent] : TextStyle.Plain;
                int tap = match.Parent >= 0 ? s_taps[match.Parent] : -1;
                if (match.Role == TextRole.Span && match.Rule != null)
                {
                    match.Rule.Style(in match, ref style);
                    if (match.Rule.Tappable)
                        tap = m;
                }
                s_styles.Add(style);
                s_taps.Add(tap);
            }

            var runs = layout.Runs;
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                var span = AtgSpan.Range(run.Start, run.Length);
                switch (run.Kind)
                {
                    case LayoutRunKind.Element:
                    {
                        // Sitting in its line as its rule says, centred on the text of the span it is in. The sprite itself
                        // keeps the text's own size and font, which the engine sizes sprites by as it has been seen to.
                        var match = matches[run.Match];
                        var around = match.Parent >= 0 ? s_styles[match.Parent] : TextStyle.Plain;
                        float sizePx = baseSizePx * ScaleOf(in around);
                        var size = SizeOf(sizes, run.Match, Vector2.zero);
                        float height = Tall(size.y, sizePx);
                        span.Sprite = AtgSpriteAssets.Placeholder;
                        span.SpriteWidthPx = Mathf.Max(size.x, 1f);
                        span.SpriteHeightPx = Mathf.Max(AbovePx(match.Rule.Alignment, height, FontOf(in around, baseFont), sizePx), MinPlaceholderEm * sizePx);
                        span.LinkId = match.Parent >= 0 ? s_taps[match.Parent] : -1;
                        buffer.Add(span);
                        continue;
                    }
                    case LayoutRunKind.Block:
                    {
                        // A mark alone on its line: the engine stands a sprite on the baseline without making its line any
                        // taller, so it is the line break after it that makes room for the block. Its width is the block's
                        // own, kept out of the text's (what draws it puts it across the width).
                        var size = SizeOf(sizes, run.Match, Vector2.zero);
                        span.Sprite = AtgSpriteAssets.Placeholder;
                        span.SpriteWidthPx = Mathf.Clamp(size.x, 1f, baseSizePx);
                        span.SpriteHeightPx = MinPlaceholderEm * baseSizePx;
                        span.LineHeightPx = size.y > 0f ? size.y : baseSizePx;
                        buffer.Add(span);
                        continue;
                    }
                    case LayoutRunKind.BlockEnd:
                    {
                        // The line break after a block: the engine starts the next line its line height below the top of the
                        // line it ends, which makes the block's line as tall as the block (a line's worth until layout has
                        // given the block a height).
                        var size = SizeOf(sizes, run.Match, Vector2.zero);
                        span.LineHeightPx = size.y > 0f ? size.y : baseSizePx;
                        buffer.Add(span);
                        continue;
                    }
                }

                // Text and padding, in the style of the span they are in.
                var styled = run.Match >= 0 ? s_styles[run.Match] : TextStyle.Plain;
                bool any = false;
                var spanFont = FontOf(in styled, baseFont);
                if (spanFont != baseFont)
                {
                    span.Font = spanFont;
                    any = true;
                }
                float px = baseSizePx * ScaleOf(in styled);
                if (!Mathf.Approximately(px, baseSizePx))
                {
                    span.FontSizePx = px;
                    any = true;
                }
                if (styled.Color.HasValue)
                {
                    span.HasColor = true;
                    span.Color = OwnColor(styled.Color.Value);
                    any = true;
                }
                if (styled.Bold && !baseBold)
                {
                    span.Weight = AtgWeight.Bold;
                    any = true;
                }
                if (styled.Italic)
                {
                    span.Style |= AtgStyle.Italic;
                    any = true;
                }
                if (run.Match >= 0 && s_taps[run.Match] >= 0)
                {
                    span.LinkId = s_taps[run.Match];
                    any = true;
                }

                if (run.Kind == LayoutRunKind.Pad)
                {
                    // Padding: the no-break spaces get the font size that makes their run exactly the padding wide (at
                    // most the span's own size), so the text either side moves over and the span's rects include it.
                    var rule = matches[run.Match].Rule;
                    float padPx = (rule != null ? rule.PaddingEm : 0f) * px;
                    span.FontSizePx = Mathf.Min(px, padPx / (LayoutText.PadCount * AtgFontAssets.SpaceAdvanceEm(spanFont)));
                    buffer.Add(span);
                    continue;
                }

                if (decorations != null && (styled.Underline || styled.Strikethrough))
                {
                    decorations.Add(new DecorationRun
                    {
                        Start = run.Start,
                        End = run.End,
                        Underline = styled.Underline,
                        Strikethrough = styled.Strikethrough,
                        Color = styled.Color.HasValue ? OwnColor(styled.Color.Value) : TextLayout.TextColor,
                        Font = spanFont,
                        FontSizePx = px,
                    });
                }

                if (any)
                    buffer.Add(span);
            }
        }

        /// <summary>
        /// How much of an element sits above the baseline, in pixels: all of it on the baseline or, centred, enough to put
        /// its middle on the middle of the text, halfway between the font's ascent and descent.
        /// </summary>
        public static float AbovePx(ElementAlignment alignment, float heightPx, FontAsset font, float sizePx)
        {
            if (alignment == ElementAlignment.Baseline || font == null)
                return heightPx;
            var metrics = AtgFontAssets.MetricsPx(font, sizePx);
            float middle = (metrics.Ascent + metrics.Descent) * 0.5f;
            return Mathf.Clamp(heightPx * 0.5f + middle, 0f, heightPx);
        }

        /// <summary>
        /// A colour a span sets, kept apart from <see cref="TextLayout.TextColor"/>: white with all its opacity is the text's
        /// own colour, so a span's white is drawn a step less opaque.
        /// </summary>
        internal static Color32 OwnColor(Color color)
        {
            Color32 own = color;
            if (own.r == 255 && own.g == 255 && own.b == 255 && own.a == 255)
                own.a = 254;
            return own;
        }

        private static FontAsset FontOf(in TextStyle style, FontAsset baseFont)
        {
            var font = style.Font != null ? style.Font.Primary : null;
            return font != null && AtgFontAssets.IsDynamic(font) ? font : baseFont;
        }

        private static float ScaleOf(in TextStyle style) => style.Scale > 0f ? style.Scale : 1f;

        // A placeholder's height for an object `height` tall in text `sizePx` big: a line's worth while layout has not given
        // it one yet (widths are shared out before heights), and never much shorter than the text.
        private static float Tall(float height, float sizePx) => Mathf.Max(height > 0f ? height : sizePx, MinPlaceholderEm * sizePx);

        private static Vector2 SizeOf(IReadOnlyList<Vector2> sizes, int match, Vector2 fallback) =>
            sizes != null && match < sizes.Count ? sizes[match] : fallback;
    }
}
