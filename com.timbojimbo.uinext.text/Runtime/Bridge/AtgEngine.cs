// This assembly is named Unity.TextCore.FontEngine.Tools on purpose: the engine's TextCore module lists that name in its
// InternalsVisibleTo attributes, which is what lets this code call the internal Advanced Text Generator. It is the
// only assembly in the package that touches Unity internals, and it exposes them through the public types in this
// folder so that everything else compiles against a stable, version-independent surface. UI Text, the package this
// came from, takes another name the module lists (UnityEngine.TextCore.Tools), so the two can be in one project.
using System;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TimboJimbo.UINext.Text.Bridge
{
    /// <summary>Process-wide generator state: the native text library, loaded once with ICU data.</summary>
    public static class AtgEngine
    {
        private const string IcuDataAssetName = "icudt73l.bytes";

        private static TextLib s_lib;
        private static bool s_shuttingDown;

        /// <summary>
        /// True once the domain is being torn down (an assembly reload in the editor, or the application quitting).
        /// The native engine and the font assets it reads go away in that phase while the editor still flushes
        /// canvases (closing the Scene View's preview scene flushes an undo record, which forces a canvas update
        /// that rebuilds dirty texts), so no text may be generated or measured after this turns true.
        /// </summary>
        public static bool IsShuttingDown => s_shuttingDown;

        /// <summary>
        /// Creates the generator if needed, with the engine's ICU data (line breaking, bidi, emoji sequences): the
        /// editor's built-in copy, or in a player the copy every build preloads (the editor package's
        /// TextSettingsBuild), which Unity's own lookup finds loaded. Without it the native side falls back to basic
        /// line breaking, and emoji sequences may not render correctly.
        /// </summary>
        public static void Initialize()
        {
            if (s_lib != null)
                return;

#if UNITY_EDITOR
            var icu = FindEditorIcuData();
#else
            var icu = TextHandle.GetICUAssetStaticFalback();
#endif
            if (icu == null)
                Debug.LogWarning("[UI.Text] ICU data asset not found: falling back to basic line breaking.");
            s_lib = new TextLib(icu != null ? icu.bytes : Array.Empty<byte>());

            // In the editor Application.quitting means play mode is ending, not that the domain is going away, and
            // with domain reload disabled the flag would then outlive the play session; the reload event is the
            // teardown signal there.
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () => s_shuttingDown = true;
#else
            Application.quitting += () => s_shuttingDown = true;
#endif
        }

        internal static TextLib Lib
        {
            get
            {
                if (s_lib == null)
                    Initialize();
                return s_lib;
            }
        }

#if UNITY_EDITOR
        /// <summary>The engine's bundled ICU data, a built-in extra resource, which builds preload.</summary>
        public static UnityEngine.TextAsset FindEditorIcuData()
        {
            return UnityEditor.AssetDatabase.GetBuiltinExtraResource<UnityEngine.TextAsset>(IcuDataAssetName);
        }
#endif
    }
}
