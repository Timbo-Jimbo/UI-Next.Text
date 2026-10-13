using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UINext.Text.Bridge
{
    /// <summary>
    /// Lays plain text of one line out without the engine, from what the engine did with the same font before: each
    /// character's quad (its shape about its top-left corner, its atlas UVs and weight), where it stands when it starts a
    /// line and how far a line runs past it when it ends one, and how far apart each pair of characters stands. They are
    /// learned as the engine lays plain texts out, in its 64ths of a pixel, so a text whose characters and pairs have all
    /// been seen is placed exactly where the engine would put them, for a fraction of the cost: a counter, a clock, a
    /// price changing every frame. Anything else (spans, other scripts, wrapping, a character or pair not yet seen, a
    /// face the engine once disagreed with) goes to the engine.
    /// </summary>
    public static class AtgReplay
    {
        /// <summary>Whether texts are replayed at all; off, every text goes to the engine (and nothing is learned).</summary>
        public static bool Enabled = true;

        private struct Key : IEquatable<Key>
        {
            public FontAsset Font;
            public int Size, Weight, Style, Spacing, WordSpacing, Padding;
            public bool Equals(Key o) => ReferenceEquals(Font, o.Font) && Size == o.Size && Weight == o.Weight && Style == o.Style
                && Spacing == o.Spacing && WordSpacing == o.WordSpacing && Padding == o.Padding;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => ((System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Font) * 31 + Size) * 31 + Weight * 7 + Style) * 31
                + Spacing * 17 + WordSpacing * 13 + Padding;
        }

        // Compares keys without boxing them, which IL2CPP's default comparer for a struct key does on every lookup.
        private sealed class KeyComparer : IEqualityComparer<Key>
        {
            public bool Equals(Key a, Key b) => a.Equals(b);
            public int GetHashCode(Key key) => key.GetHashCode();
        }

        private struct Glyph
        {
            public int Id;
            public GlyphRect Rect;
            // Its quad's corners about its top-left corner, and its top-left corner's height on the line.
            public Vector2 BottomLeft, TopRight, BottomRight;
            public float Top;
            public Vector2 UvBottomLeft, UvTopLeft, UvTopRight, UvBottomRight;
            public Color32 Color;
            public float Dilate;
            // Where its top-left corner is when it starts a line, and how far the line runs past it when it ends one.
            public float Head, End;
            public bool HasHead, HasEnd;
        }

        private sealed class Face
        {
            public Texture2D Atlas;
            public AtgQuadGroup Group;
            public float LineHeight = -1f;
            public bool Broken;
            public readonly Dictionary<char, Glyph> Glyphs = new();
            public readonly Dictionary<int, float> Pairs = new();
        }

        private static readonly Dictionary<Key, Face> s_faces = new(new KeyComparer());

        /// <summary>Forgets everything learned (fonts or their atlases changed).</summary>
        public static void Clear() => s_faces.Clear();

        // Whether a text is one the replay can lay out: plain (no spans), left to right, of characters from the simple Latin
        // ranges and no line break, its box either none or one it starts at the top-left of.
        private static bool Eligible(ref AtgTextSettings s)
        {
            if (!Enabled || s.SpanCount > 0 || s.Direction != AtgDirection.LeftToRight || string.IsNullOrEmpty(s.Text) || s.Text.Length > 256)
                return false;
            if (s.WidthPx >= 0f && s.HorizontalAlignment != AtgHorizontalAlignment.Left) return false;
            if (s.HeightPx >= 0f && s.VerticalAlignment != AtgVerticalAlignment.Top) return false;
            if (s.CollapseWhitespace || s.ParseEscapeSequences) return false;
            foreach (char c in s.Text)
                if (c < 0x20 || (c > 0x7E && c < 0xA0) || c > 0xFF)
                    return false;
            return true;
        }

        private static bool KeyOf(ref AtgTextSettings s, out Key key, out FontAsset font)
        {
            font = s.Font != null ? s.Font : AtgFallbackSet.DefaultFont;
            key = default;
            if (font == null) return false;
            key = new Key
            {
                Font = font,
                Size = Mathf.RoundToInt(s.FontSizePx * 64f),
                Weight = (int)s.Weight,
                Style = (int)s.Style,
                Spacing = Mathf.RoundToInt(s.CharacterSpacingPx * 64f),
                WordSpacing = Mathf.RoundToInt(s.WordSpacingPx * 64f),
                Padding = Mathf.RoundToInt(s.VertexPaddingPx * 64f),
            };
            return true;
        }

        /// <summary>
        /// Lays a text out from what was learned, into <paramref name="quads"/>, its group and its size; false when it
        /// cannot be, and the engine must.
        /// </summary>
        public static bool TryLayOut(ref AtgTextSettings s, List<AtgQuad> quads, out AtgQuadGroup group, out Vector2 size)
        {
            group = default;
            size = default;
            if (!Eligible(ref s) || !KeyOf(ref s, out var key, out var font)) return false;
            if (!s_faces.TryGetValue(key, out var face) || face.Broken || face.LineHeight < 0f) return false;
            if (font.atlasTextures == null || font.atlasTextures.Length == 0 || font.atlasTextures[0] != face.Atlas) return false;
            var text = s.Text;
            quads.Clear();
            float x = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!face.Glyphs.TryGetValue(c, out var glyph)) return false;
                // The atlas made again moves glyphs: what was learned of one no longer where it was is not used.
                var atlasGlyph = font.GetGlyphInCache((uint)glyph.Id);
                if (atlasGlyph == null || atlasGlyph.glyphRect != glyph.Rect) return false;
                if (i == 0)
                {
                    if (!glyph.HasHead) return false;
                    x = glyph.Head;
                }
                else if (!face.Pairs.TryGetValue(PairOf(text[i - 1], c), out float apart))
                {
                    return false;
                }
                else
                {
                    x += apart;
                }
                if (i == text.Length - 1)
                {
                    if (!glyph.HasEnd) return false;
                    size = new Vector2(x + glyph.End, face.LineHeight);
                }
                var topLeft = new Vector2(x, glyph.Top);
                quads.Add(new AtgQuad
                {
                    GlyphId = glyph.Id,
                    TopLeft = new AtgVertex { Position = topLeft, Color = glyph.Color, Uv = glyph.UvTopLeft, Dilate = glyph.Dilate },
                    BottomLeft = new AtgVertex { Position = topLeft + glyph.BottomLeft, Color = glyph.Color, Uv = glyph.UvBottomLeft, Dilate = glyph.Dilate },
                    TopRight = new AtgVertex { Position = topLeft + glyph.TopRight, Color = glyph.Color, Uv = glyph.UvTopRight, Dilate = glyph.Dilate },
                    BottomRight = new AtgVertex { Position = topLeft + glyph.BottomRight, Color = glyph.Color, Uv = glyph.UvBottomRight, Dilate = glyph.Dilate },
                });
            }
            // A line too long for its box wraps or is cut short, and one too tall is cut: the engine's.
            if (s.WidthPx >= 0f && Px(size.x) > Px(s.WidthPx)) return false;
            if (s.HeightPx >= 0f && Px(size.y) > Px(s.HeightPx)) return false;
            group = face.Group;
            group.Count = quads.Count;
            return true;
        }

        /// <summary>Learns from a text the engine has just laid out in <paramref name="handle"/>, when it is plain and one line.</summary>
        public static void Learn(ref AtgTextSettings s, AtgTextHandle handle)
        {
            if (!Eligible(ref s) || handle.IsElided || handle.GroupCount != 1 || !KeyOf(ref s, out var key, out var font)) return;
            var info = handle.GetGroup(0);
            var text = s.Text;
            if (info.IsSprite || info.Count != text.Length || font.atlasTextures == null || font.atlasTextures.Length == 0 || info.Atlas != font.atlasTextures[0])
                return;
            if (handle.LineOf(text.Length - 1) != 0) return;
            if (!s_faces.TryGetValue(key, out var face))
            {
                face = new Face { Atlas = info.Atlas, Group = info };
                s_faces[key] = face;
            }
            if (face.Broken || face.Atlas != info.Atlas) return;
            var size = handle.SizePx;
            if (face.LineHeight >= 0f && face.LineHeight != size.y) { face.Broken = true; return; }
            face.LineHeight = size.y;
            float previousX = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                handle.GetQuad(0, i, out var quad);
                // A glyph of the face's own font, as it is in its atlas now; a character is always the same glyph, or the
                // engine shapes in ways the replay does not (a contextual form), and the face is not replayed.
                var atlasGlyph = font.GetGlyphInCache((uint)quad.GlyphId);
                if (atlasGlyph == null) return;
                var topLeft = quad.TopLeft.Position;
                var glyph = new Glyph
                {
                    Id = quad.GlyphId,
                    Rect = atlasGlyph.glyphRect,
                    BottomLeft = quad.BottomLeft.Position - topLeft,
                    TopRight = quad.TopRight.Position - topLeft,
                    BottomRight = quad.BottomRight.Position - topLeft,
                    Top = topLeft.y,
                    UvBottomLeft = quad.BottomLeft.Uv,
                    UvTopLeft = quad.TopLeft.Uv,
                    UvTopRight = quad.TopRight.Uv,
                    UvBottomRight = quad.BottomRight.Uv,
                    Color = quad.BottomLeft.Color,
                    Dilate = quad.BottomLeft.Dilate,
                };
                if (face.Glyphs.TryGetValue(c, out var known))
                {
                    if (!SameShape(in known, in glyph)) { face.Broken = true; return; }
                    glyph.Head = known.Head;
                    glyph.HasHead = known.HasHead;
                    glyph.End = known.End;
                    glyph.HasEnd = known.HasEnd;
                }
                if (i == 0)
                {
                    if (glyph.HasHead && glyph.Head != topLeft.x) { face.Broken = true; return; }
                    glyph.Head = topLeft.x;
                    glyph.HasHead = true;
                }
                else
                {
                    int pair = PairOf(text[i - 1], c);
                    float apart = topLeft.x - previousX;
                    if (face.Pairs.TryGetValue(pair, out float knownApart) && knownApart != apart) { face.Broken = true; return; }
                    face.Pairs[pair] = apart;
                }
                if (i == text.Length - 1)
                {
                    float end = size.x - topLeft.x;
                    if (glyph.HasEnd && glyph.End != end) { face.Broken = true; return; }
                    glyph.End = end;
                    glyph.HasEnd = true;
                }
                face.Glyphs[c] = glyph;
                previousX = topLeft.x;
            }
        }

        private static bool SameShape(in Glyph a, in Glyph b) =>
            a.Id == b.Id && a.Rect == b.Rect && a.BottomLeft == b.BottomLeft && a.TopRight == b.TopRight && a.BottomRight == b.BottomRight
            && a.Top == b.Top && a.UvBottomLeft == b.UvBottomLeft && a.UvTopRight == b.UvTopRight && a.Dilate == b.Dilate;

        private static int PairOf(char a, char b) => (a << 16) | b;

        // The engine works in 64ths of a pixel.
        private static int Px(float px) => Mathf.RoundToInt(px * 64f);
    }
}
