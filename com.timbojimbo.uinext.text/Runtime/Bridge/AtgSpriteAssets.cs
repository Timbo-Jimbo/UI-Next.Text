using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UINext.Text.Bridge
{
    /// <summary>Sprite assets the generator can lay out inline: emote sheets, and the blank placeholder used to reserve room for inline objects.</summary>
    public static class AtgSpriteAssets
    {
        /// <summary>Code point of the first sprite in the placeholder asset. Put it in the text where an inline object goes.</summary>
        public const char PlaceholderCodePoint = '';

        /// <summary>
        /// How many placeholders the asset has, from <see cref="PlaceholderCodePoint"/> on: as many objects in one text get
        /// a code each, so the quad a layout draws for one says which it is (a sprite quad reports its character code).
        /// </summary>
        public const int PlaceholderCount = 256;

        /// <summary>The placeholder for the object at <paramref name="index"/> in its text, the codes repeating past <see cref="PlaceholderCount"/>.</summary>
        public static char PlaceholderCode(int index) => (char)(PlaceholderCodePoint + index % PlaceholderCount);

        private static SpriteAsset s_placeholder;
        private static Texture2D s_solid;

        /// <summary>An opaque white 4x4 texture, for drawing solid quads (underlines, rules) with the bitmap text material.</summary>
        public static Texture2D SolidTexture
        {
            get
            {
                if (s_solid != null)
                    return s_solid;
                s_solid = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "UI.Text solid", hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(255, 255, 255, 255);
                s_solid.SetPixels32(pixels);
                s_solid.Apply(false, true);
                return s_solid;
            }
        }

        /// <summary>True when a quad group draws from the placeholder asset and should be skipped when rendering.</summary>
        public static bool IsPlaceholder(in AtgQuadGroup group) => group.IsSprite && s_placeholder != null && group.Atlas == s_placeholder.spriteSheet;

        /// <summary>
        /// An asset of <see cref="PlaceholderCount"/> sprites, each a blank 4x4 texture. A span over a placeholder code
        /// point with this asset and explicit metrics reserves exactly that box in the layout; the quad it emits tells you
        /// where the box landed and can be skipped when drawing. The engine treats a sprite as a word of its own
        /// for line breaking, so this is for objects, not for spacing.
        /// </summary>
        public static SpriteAsset Placeholder
        {
            get
            {
                if (s_placeholder != null)
                    return s_placeholder;

                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "UI.Text placeholder", hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(0, 0, 0, 0);
                texture.SetPixels32(pixels);
                texture.Apply(false, true);

                var asset = ScriptableObject.CreateInstance<SpriteAsset>();
                asset.name = "UI.Text placeholder";
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.spriteSheet = texture;
                var glyphs = new List<SpriteGlyph>(PlaceholderCount);
                var characters = new List<SpriteCharacter>(PlaceholderCount);
                for (int i = 0; i < PlaceholderCount; i++)
                {
                    var glyph = new SpriteGlyph((uint)i, new GlyphMetrics(4, 4, 0, 4, 4), new GlyphRect(0, 0, 4, 4), 1f, 0);
                    glyphs.Add(glyph);
                    characters.Add(new SpriteCharacter(PlaceholderCodePoint + (uint)i, asset, glyph) { name = "placeholder " + i });
                }
                asset.spriteGlyphTable = glyphs;
                asset.spriteCharacterTable = characters;
                asset.UpdateLookupTables();
                s_placeholder = asset;
                return asset;
            }
        }
    }
}
