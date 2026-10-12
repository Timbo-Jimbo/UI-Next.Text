namespace TimboJimbo.UINext.Text
{
    public enum TextOverflow
    {
        /// <summary>Wrap to the width and let extra lines spill below the rect, like TextMeshPro's Overflow.</summary>
        Overflow,
        /// <summary>Cut the text at the rect (or Max Lines) and end it with an ellipsis.</summary>
        Ellipsis,
        /// <summary>Cut the text at the rect (or Max Lines) without an ellipsis.</summary>
        Truncate,
    }

    public enum TextDirection
    {
        /// <summary>Paragraphs read left to right; right-to-left runs inside them still shape and order correctly.</summary>
        LeftToRight,
        /// <summary>Paragraphs read right to left; left-to-right runs inside them (a Latin word, a number) still read left to right.</summary>
        RightToLeft,
        /// <summary>
        /// The direction of the text's first strong character (a letter, or a directional mark): right to left when it
        /// is Arabic, Hebrew or another right-to-left script, otherwise left to right; with none (an empty text, or
        /// only numbers, punctuation and emoji), left to right. For text people write, such as a text field's or a
        /// chat message's. Over the whole text, as HTML's dir="auto" on an input: iOS and Android text views decide
        /// each paragraph by its own first letter, and a later paragraph in the other direction here reads the
        /// first's way.
        /// </summary>
        Auto,
        /// <summary>
        /// As <see cref="Auto"/>, but right to left when the text has no strong character: for a text field while the
        /// keyboard writes right to left, whose caret then starts at the right before the first letter is typed, as on
        /// iOS (Android's FIRSTSTRONG_RTL).
        /// </summary>
        AutoRightToLeft,
    }
}
