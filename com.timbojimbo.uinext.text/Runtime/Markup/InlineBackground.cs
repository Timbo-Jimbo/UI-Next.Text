using UnityEngine;

namespace TimboJimbo.UINext.Text.Markup
{
    /// <summary>
    /// A background behind matched text, drawn by what draws the text (UI Next's Label) with no prefab: a fill, a border
    /// and round corners, with room either side that the layout keeps. The built-in ones are behind `code` spans and
    /// fenced code blocks, unless a theme has handlers of its own for those kinds (<see cref="BuiltIn"/>). Create one as
    /// an asset, with a trigger, to put a background behind text of your own (a tag, a highlight).
    /// </summary>
    [CreateAssetMenu(menuName = "Timbo Jimbo/UI Next/Inline Handlers/Background", fileName = "InlineBackground")]
    public sealed class InlineBackground : InlineHandler
    {
        [SerializeField] private Color _color = new(1f, 1f, 1f, 0.1f);
        [SerializeField, Min(0f)] private float _borderWidth = 1f;
        [SerializeField] private Color _borderColor = new(1f, 1f, 1f, 0.15f);

        [Tooltip("x: horizontal padding, part of the layout (the text either side moves over). y: vertical padding, drawn over the line gap. In em.")]
        [SerializeField] private Vector2 _paddingEm = new(0.2f, 0.05f);

        [Tooltip("The corners' radius, in em.")]
        [SerializeField, Min(0f)] private float _cornerRadiusEm = 0.25f;

        public Color Color => _color;
        public float BorderWidth => _borderWidth;
        public Color BorderColor => _borderColor;

        public override float PaddingEm => _paddingEm.x;

        /// <summary>How far the background reaches above and below its text, over the line gap, in em.</summary>
        public float VerticalPaddingEm => _paddingEm.y;

        public float CornerRadiusEm => _cornerRadiusEm;

        // ---- Built-ins ----

        private static InlineBackground s_inline;
        private static InlineBackground s_block;

        /// <summary>
        /// The background a theme with no handler of its own puts behind a token of a built-in kind
        /// (<see cref="MarkdownParser.CodeKind"/>, <see cref="MarkdownParser.CodeBlockKind"/>), made the first time it is
        /// asked for; null for any other kind.
        /// </summary>
        public static InlineHandler BuiltIn(string kind)
        {
            switch (kind)
            {
                case MarkdownParser.CodeKind:
                    return s_inline != null ? s_inline : s_inline = Create("Built-in code background", kind, InlinePlacement.Fragments, new Vector2(0.2f, 0.05f));
                case MarkdownParser.CodeBlockKind:
                    return s_block != null ? s_block : s_block = Create("Built-in code block background", kind, InlinePlacement.Block, new Vector2(0.4f, 0.25f));
                default:
                    return null;
            }
        }

        private static InlineBackground Create(string name, string kind, InlinePlacement placement, Vector2 paddingEm)
        {
            var handler = CreateInstance<InlineBackground>();
            handler.name = name;
            handler.hideFlags = HideFlags.HideAndDontSave;
            handler._paddingEm = paddingEm;
            handler.Configure(kind, default, placement);
            return handler;
        }
    }
}
