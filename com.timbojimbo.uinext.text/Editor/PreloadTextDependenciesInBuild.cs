using System.Collections.Generic;
using TimboJimbo.UINext.Text.Bridge;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TimboJimboEditor.UINext.Text
{
    /// <summary>
    /// Puts what texts need in a player into its preloaded assets for the duration of a build, so it ships and loads
    /// before the first scene: the engine's ICU data (line breaking, bidi, emoji sequences) and TextCore's font shaders,
    /// which nothing in a scene refers to, and the active settings asset with the fonts and themes it refers to. The
    /// list is restored afterwards. An aborted build leaves them in it, which is harmless: the next build finds them
    /// there and leaves them.
    /// </summary>
    /// <remarks>
    /// TextCore gives every font asset it makes from a font (a project font, an OS font) a material on one of its
    /// shaders, found by name, which a player has only if something ships it: the sprite shader for a colour font (a
    /// bundled colour emoji font), and the distance field shader for any other. Without the sprite shader it makes no
    /// asset at all, without a word, and emoji fall through to the OS fonts (on Android 13 and later, to a COLRv1 font the
    /// engine cannot draw); without the distance field shader making the asset throws. Both are built-in extras. The
    /// bitmap shader TextCore also looks up is only for raster render modes, which the bridge never asks for.
    /// </remarks>
    internal sealed class PreloadTextDependenciesInBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private static readonly string[] FontShaderNames =
        {
            "Hidden/TextCore/Sprite",
            "Hidden/TextCore/Distance Field SSD",
        };

        private static readonly List<Object> s_added = new();

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            s_added.Clear();
            var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());
            Add(preloaded, AtgEngine.FindEditorIcuData());
            foreach (var name in FontShaderNames)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                    Debug.LogWarning($"[UI.Text] Shader '{name}' not found: fonts that need it will not load in this player.");
                Add(preloaded, shader);
            }
            Add(preloaded, TextSettingsProvider.Active);
            if (s_added.Count > 0)
                PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (s_added.Count == 0)
                return;
            var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());
            foreach (var asset in s_added)
                preloaded.Remove(asset);
            s_added.Clear();
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }

        private static void Add(List<Object> preloaded, Object asset)
        {
            if (asset == null || preloaded.Contains(asset))
                return;
            preloaded.Add(asset);
            s_added.Add(asset);
        }
    }
}
