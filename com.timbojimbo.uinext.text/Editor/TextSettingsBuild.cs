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
    /// before the first scene: the engine's ICU data (line breaking, bidi, emoji sequences), which nothing in a scene
    /// refers to, and the active settings asset with the fonts and themes it refers to. The list is restored afterwards.
    /// An aborted build leaves them in it, which is harmless: the next build finds them there and leaves them.
    /// </summary>
    internal sealed class TextSettingsBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private static readonly List<Object> s_added = new();

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            s_added.Clear();
            var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());
            Add(preloaded, AtgEngine.FindEditorIcuData());
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
