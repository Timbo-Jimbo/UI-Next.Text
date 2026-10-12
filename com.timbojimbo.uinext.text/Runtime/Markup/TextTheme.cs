using System.Collections.Generic;
using UnityEngine;

namespace TimboJimbo.UINext.Text.Markup
{
    /// <summary>
    /// How markup renders: heading sizes, link and quote colours, the code font, and the inline handlers the
    /// parser consults. Assign one to a text or to the project settings; without one the built-in defaults apply.
    /// </summary>
    [CreateAssetMenu(menuName = "Timbo Jimbo/UI Next/Text Theme", fileName = "TextTheme")]
    public sealed class TextTheme : ScriptableObject
    {
        [Header("Headings")]
        [SerializeField] private float _heading1Scale = 1.6f;
        [SerializeField] private float _heading2Scale = 1.35f;
        [SerializeField] private float _heading3Scale = 1.15f;

        [Header("Links")]
        [SerializeField] private Color _linkColor = new(0.35f, 0.65f, 1f);
        [SerializeField] private bool _underlineLinks = true;

        [Header("Quotes")]
        [SerializeField] private Color _quoteColor = new(0.8f, 0.8f, 0.8f);

        [Header("Code")]
        [Tooltip("The Font Stack for code; empty keeps the paragraph font.")]
        [SerializeField] private FontStack _codeFont;
        [SerializeField] private float _codeScale = 0.9f;
        [SerializeField] private Color _codeColor = new(1f, 0.85f, 0.6f);

        [Header("Decorations")]
        [SerializeField, Tooltip("Underline and strikethrough thickness as a fraction of the font size; 0 uses the font's own metrics.")]
        private float _lineThicknessEm;

        [Header("Inline handlers")]
        [SerializeField] private List<InlineHandler> _handlers = new();

        private static TextTheme s_default;

        /// <summary>The handler responsible for a token kind: the theme's own, or none.</summary>
        public InlineHandler FindHandler(string kind)
        {
            for (int i = 0; i < _handlers.Count; i++)
                if (_handlers[i] != null && _handlers[i].Kind == kind)
                    return _handlers[i];
            return null;
        }

        /// <summary>The built-in defaults, used when neither the text nor the project settings assign a theme.</summary>
        public static TextTheme Default
        {
            get
            {
                if (s_default == null)
                {
                    s_default = CreateInstance<TextTheme>();
                    s_default.name = "Default Text Theme";
                    s_default.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_default;
            }
        }

        public float HeadingScale(BlockKind block) => block switch
        {
            BlockKind.Heading1 => _heading1Scale,
            BlockKind.Heading2 => _heading2Scale,
            BlockKind.Heading3 => _heading3Scale,
            _ => 1f,
        };

        public Color LinkColor => _linkColor;
        public bool UnderlineLinks => _underlineLinks;
        public Color QuoteColor => _quoteColor;
        public FontStack CodeFont => _codeFont;
        public float CodeScale => _codeScale;
        public Color CodeColor => _codeColor;
        public float LineThicknessEm => _lineThicknessEm;
        /// <summary>The theme's own handlers, which the parser matches triggers against.</summary>
        public List<InlineHandler> Handlers => _handlers;

#if UNITY_EDITOR
        // Edits apply live to every open text.
        private void OnValidate() => TextSettings.NotifyChanged();
#endif
    }
}
