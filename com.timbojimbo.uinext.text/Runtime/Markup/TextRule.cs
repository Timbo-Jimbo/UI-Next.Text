using System.Collections.Generic;
using UnityEngine;

namespace TimboJimbo.UINext.Text
{
    /// <summary>How the text a <see cref="TextRule"/> finds is shown.</summary>
    public enum TextRole
    {
        /// <summary>It stays text, wrapping with the rest: styled, perhaps with something behind it, perhaps tapped.</summary>
        Span,

        /// <summary>It becomes one object in its line, sitting there as a big character would: a chip, an image.</summary>
        Element,

        /// <summary>It becomes one object across the whole width, between the lines above and below it: a code block, a quote.</summary>
        Block,
    }

    /// <summary>How an element sits in its line.</summary>
    public enum ElementAlignment
    {
        /// <summary>Its middle on the middle of the text, halfway between the font's ascent and descent.</summary>
        Center,

        /// <summary>Its bottom on the baseline, as an image in a line of text sits.</summary>
        Baseline,
    }

    /// <summary>
    /// What a span changes about its text. Rules style a span outermost first, each over the last, so a code span in a
    /// heading is sized against the heading. Unset fields keep what the text had.
    /// </summary>
    public struct TextStyle
    {
        /// <summary>The fonts it draws with, or null to keep them.</summary>
        public FontStack Font;

        /// <summary>
        /// Its size as a multiple of the text's: 1 for the same. Multiply it (<c>style.Scale *= 0.9f</c>) rather than set
        /// it, so the span inside another is sized against that one.
        /// </summary>
        public float Scale;

        /// <summary>Its colour, kept whatever colour the text is drawn in; null for the text's own.</summary>
        public Color? Color;

        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strikethrough;

        /// <summary>The text's own style, before any rule: its size, colour and fonts.</summary>
        public static TextStyle Plain => new() { Scale = 1f };
    }

    /// <summary>
    /// Finds text by its <see cref="Trigger"/> and says how it is shown (its <see cref="Role"/>): kept as text and styled,
    /// made an element in its line, or made a block across the width. A <see cref="TextRuleSet"/> holds rules in order, and
    /// at each place the first whose trigger matches, and which accepts what it found, takes it. What draws the text gives
    /// elements and blocks their sizes; UI Next's handlers are rules with prefabs to make them from.
    /// </summary>
    public abstract class TextRule : ScriptableObject
    {
        [SerializeField] private TextTrigger _trigger = TextTrigger.Wrapped("==");

        /// <summary>What it looks for. Set from code, every text is read again.</summary>
        public TextTrigger Trigger
        {
            get => _trigger;
            set
            {
                _trigger = value;
                TextSettings.NotifyChanged();
            }
        }

        /// <summary>Sets the trigger of a rule made in code before any text is read with it, telling no text.</summary>
        protected void Configure(TextTrigger trigger) => _trigger = trigger;

        /// <summary>How what it finds is shown.</summary>
        public abstract TextRole Role { get; }

        /// <summary>
        /// Whether a match its trigger found is really its own: an emoji rule looks the name up here, so :30: in a time
        /// stays text. A match it declines is left to the rules after it. The default takes every one.
        /// </summary>
        public virtual bool Accepts(in TextMatch match) => true;

        /// <summary>
        /// A span's style: changes what <paramref name="style"/> has from the spans it is inside. Called whenever the text
        /// is laid out or measured, so it should give the same style every time.
        /// </summary>
        public virtual void Style(in TextMatch match, ref TextStyle style) { }

        /// <summary>
        /// A span's room either side of its text, in em of its size, as CSS's padding on an inline element is: the text
        /// either side moves over, a span that wraps has it at its start and end only, and its rects include it.
        /// </summary>
        public virtual float PaddingEm => 0f;

        /// <summary>Whether taps on a span's text are found (<see cref="TextLayout.MatchAt"/>).</summary>
        public virtual bool Tappable => false;

        /// <summary>How an element sits in its line.</summary>
        public virtual ElementAlignment Alignment => ElementAlignment.Center;

#if UNITY_EDITOR
        // Edits apply live to every open text.
        protected virtual void OnValidate() => TextSettings.NotifyChanged();
#endif
    }

    /// <summary>
    /// Rules in the order they are tried: what a text is read with. UI Next's Markup asset is one; a text with none is
    /// plain.
    /// </summary>
    public abstract class TextRuleSet : ScriptableObject
    {
        /// <summary>Adds its rules to <paramref name="rules"/>, in the order they are tried.</summary>
        public abstract void GetRules(List<TextRule> rules);

#if UNITY_EDITOR
        // Edits apply live to every open text.
        protected virtual void OnValidate() => TextSettings.NotifyChanged();
#endif
    }
}
