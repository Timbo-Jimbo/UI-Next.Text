using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UINext.Text.Bridge
{
    /// <summary>
    /// The fallback chain the generator consults when the primary font lacks a glyph: explicit fonts first, then
    /// emoji assets, then the OS fallback fonts unless a set opts out. All sets share one engine TextSettings
    /// object; a set's lists are pushed into it when a text using the set is generated, so the OS fonts exist once.
    /// </summary>
    public sealed class AtgFallbackSet : IDisposable
    {
        private static TextSettings s_settings;
        // The engine's own OS fallback list object; we own its contents so a set can leave the OS fonts out.
        private static List<FontAsset> s_osList;
        private static List<FontAsset> s_osFallbacks;
        private static AtgFallbackSet s_applied;
        private static int s_appliedVersion;
        private static AtgFallbackSet s_shared;

        private List<FontAsset> _fonts = new();
        private List<UnityEngine.TextCore.Text.TextAsset> _emoji = new();
        private bool _includeOsFallbacks = true;
        private int _version;
        private bool _disposed;

        public string Name { get; }

        /// <summary>A set with no explicit fallbacks and the OS fonts on: what bridge callers without a stack get.</summary>
        public static AtgFallbackSet Shared => s_shared ??= new AtgFallbackSet("UI.Text Shared");

        public AtgFallbackSet(string name)
        {
            Name = name;
        }

        /// <summary>Fonts tried, in order, before the OS fallbacks. Static font assets are ignored by the engine.</summary>
        public void SetFonts(List<FontAsset> fonts)
        {
            ThrowIfDisposed();
            _fonts = fonts ?? new List<FontAsset>();
            _version++;
        }

        /// <summary>Font or sprite assets consulted for characters Unicode defines as emoji.</summary>
        public void SetEmojiAssets(List<UnityEngine.TextCore.Text.TextAsset> assets)
        {
            ThrowIfDisposed();
            _emoji = assets ?? new List<UnityEngine.TextCore.Text.TextAsset>();
            _version++;
        }

        /// <summary>Whether the OS fallback fonts are consulted after the explicit ones. On by default.</summary>
        public bool IncludeOsFallbacks
        {
            get => _includeOsFallbacks;
            set
            {
                if (_includeOsFallbacks == value) return;
                _includeOsFallbacks = value;
                _version++;
            }
        }

        /// <summary>The OS default UI font, the first OS fallback font: what a text draws with when nothing else resolves.</summary>
        public static FontAsset DefaultFont
        {
            get
            {
                EnsureSettings();
                return s_osFallbacks.Count > 0 ? s_osFallbacks[0] : s_settings.GetFontAsset();
            }
        }

        /// <summary>The OS fallback fonts in the order the engine consults them.</summary>
        public static IReadOnlyList<FontAsset> OsFallbacks
        {
            get
            {
                EnsureSettings();
                return s_osFallbacks;
            }
        }

        internal IntPtr Native
        {
            get
            {
                ThrowIfDisposed();
                EnsureSettings();
                if (!ReferenceEquals(s_applied, this) || s_appliedVersion != _version)
                {
                    s_settings.fallbackFontAssets = _fonts;
                    s_settings.emojiFallbackTextAssets = _emoji;
                    // The engine only consults the emoji assets with emoji support on; off (its default) leaves
                    // emoji to the ordinary fallbacks, as a set without emoji assets has always had them. Obsolete in
                    // 6000.5, which says ATG supports emoji by default; set all the same, for any version that reads it.
#pragma warning disable CS0618
                    s_settings.enableEmojiSupport = _emoji.Count > 0;
#pragma warning restore CS0618
                    s_osList.Clear();
                    if (_includeOsFallbacks)
                        s_osList.AddRange(s_osFallbacks);
                    s_settings.SetNativeTextSettingsDirty();
                    s_applied = this;
                    s_appliedVersion = _version;
                }
                s_settings.UpdateNativeTextSettings();
                return s_settings.nativeTextSettings;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (ReferenceEquals(s_applied, this))
                s_applied = null;
            if (ReferenceEquals(s_shared, this))
                s_shared = null;
        }

        private static void EnsureSettings()
        {
            if (s_settings != null)
                return;
            s_settings = ScriptableObject.CreateInstance<TextSettings>();
            s_settings.name = "UI.Text TextSettings";
            s_settings.hideFlags = HideFlags.HideAndDontSave;

            // The engine builds its OS fallback list the first time it is read: one dynamic font asset per family
            // the OS names, glyphs rasterised on demand. We keep those assets, take a copy of the list, and add the
            // real bold and italic faces to its first entry, the OS default UI font, so default-font headings are
            // not synthesised.
            s_osList = s_settings.fallbackOSFontAssets;
            s_osFallbacks = new List<FontAsset>(s_osList);
            if (s_osFallbacks.Count > 0)
                AtgFontAssets.AddOsVariants(s_osFallbacks[0]);
            s_settings.SetNativeTextSettingsDirty();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AtgFallbackSet));
        }
    }
}
