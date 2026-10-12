using UnityEngine;

namespace TimboJimbo.UINext.Text
{
    /// <summary>
    /// Project-wide text settings, edited under Edit > Project Settings > Timbo Jimbo > UI Next Text. The asset lives
    /// anywhere under Assets; the settings page registers it as an EditorBuildSettings config object (stored in
    /// ProjectSettings/EditorBuildSettings.asset) and builds preload it, so a player finds it without a Resources
    /// folder or a fixed name. Texts that leave a field unset fall back to these values.
    /// </summary>
    public sealed class TextSettings : ScriptableObject
    {
        /// <summary>The EditorBuildSettings config object key the active asset is registered under.</summary>
        public const string ConfigObjectKey = "com.timbojimbo.uinext.text.settings";

        [Tooltip("The Font Stack used by texts that don't set their own. Empty means the OS default font.")]
        [SerializeField] private FontStack _defaultFont;
        [Tooltip("The Markup texts that don't set their own are read with. Empty means UI Next's built-in Markdown.")]
        [SerializeField] private TextRuleSet _defaultMarkup;

        private static TextSettings s_instance;

        /// <summary>The active settings asset, or null when the project has none.</summary>
        public static TextSettings Instance
        {
            get
            {
#if UNITY_EDITOR
                if (s_instance == null)
                    UnityEditor.EditorBuildSettings.TryGetConfigObject(ConfigObjectKey, out s_instance);
#endif
                return s_instance;
            }
        }

        public FontStack DefaultFont => _defaultFont;

        /// <summary>The rules texts that set none of their own are read with, or null for what draws them to choose.</summary>
        public TextRuleSet DefaultMarkup => _defaultMarkup;

        /// <summary>
        /// Raised when something texts are laid out with changes: the active settings or what they hold, a font stack, a
        /// markup or a rule. Whatever draws text (UI Next's Label) reads and lays it out again.
        /// </summary>
        public static event System.Action Changed;

        /// <summary>Tells whatever draws text that something it is laid out with has changed.</summary>
        public static void NotifyChanged() => Changed?.Invoke();

#if UNITY_EDITOR
        /// <summary>Called by the settings page after it changes the registered asset; every text redraws with the new defaults.</summary>
        internal static void SetActive(TextSettings settings)
        {
            s_instance = settings;
            NotifyChanged();
        }

        // Edits apply live to every open text.
        private void OnValidate()
        {
            if (this == s_instance)
                NotifyChanged();
        }
#else
        // A player has the asset as a preloaded asset, loaded before the first scene; nothing else references it,
        // so it makes itself the instance here.
        private void OnEnable() => s_instance = this;
#endif
    }
}
