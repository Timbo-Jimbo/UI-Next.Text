using System;
using UnityEngine;

namespace TimboJimbo.UINext.Text
{
    /// <summary>How a <see cref="TextTrigger"/> finds text: by the characters around it.</summary>
    public enum TextTriggerKind
    {
        /// <summary>
        /// Text between an opening and a closing delimiter on one line: **bold**, `code`, ==a highlight==, :emoji:. Its
        /// text is what is between them.
        /// </summary>
        Wrapped,

        /// <summary>A prefix and the word after it: @name, #tag. Both are shown; its text is the word.</summary>
        Prefix,

        /// <summary>
        /// A line starting with a marker ("# " a heading, "> " a quote, "- " a list item) or, merging, a run of such lines.
        /// Its text is the lines without their markers, and its argument the first marker, trimmed ("1.").
        /// </summary>
        Line,

        /// <summary>
        /// The lines between an opening fence (```) and a closing one at least as long, or the end of the text. Its text
        /// is those lines, and its argument what follows the opening fence (a language).
        /// </summary>
        Fenced,

        /// <summary>
        /// A Markdown link, [text](target), and, with bare URLs, web addresses as they are written. Its text is what is
        /// shown, and its argument the target.
        /// </summary>
        Link,

        /// <summary>
        /// A .NET regular expression, within a line. A group named <c>content</c> is its text, shown when it stays text
        /// (otherwise all it matches), and one named <c>arg</c> its argument.
        /// </summary>
        Pattern,
    }

    /// <summary>
    /// What a <see cref="TextRule"/> looks for: the characters around the text it takes. Only the fields its
    /// <see cref="Kind"/> uses apply.
    /// </summary>
    [Serializable]
    public struct TextTrigger
    {
        public TextTriggerKind Kind;

        [Tooltip("Wrapped: the opening delimiter. Prefix: the prefix. Line: the marker. Fenced: the fence.")]
        public string Open;

        [Tooltip("Wrapped: the closing delimiter; empty for the opening one again.")]
        public string Close;

        [Tooltip("Pattern: the regular expression. Line: one for the marker, in place of Open (\\d+\\. for numbered items).")]
        public string Pattern;

        [Tooltip("Wrapped: its delimiters may stand inside a word, as ** does in un**believ**able. Off, they stand at a word's edges, so snake_case stays text.")]
        public bool InsideWords;

        [Tooltip("Line: a run of lines with the marker is one match, as the lines of a quote are.")]
        public bool Merge;

        [Tooltip("Link: web addresses written as they are (https://…) too.")]
        public bool BareUrls;

        [Tooltip("Its text is taken as written, with no rule matching inside it, as code is. A delimiter of one character repeated may then be written longer to wrap text holding it: ``a`b``.")]
        public bool Literal;

        /// <summary>Text between <paramref name="open"/> and <paramref name="close"/> (null for <paramref name="open"/> again).</summary>
        public static TextTrigger Wrapped(string open, string close = null, bool insideWords = false, bool literal = false) =>
            new() { Kind = TextTriggerKind.Wrapped, Open = open, Close = close, InsideWords = insideWords, Literal = literal };

        /// <summary><paramref name="prefix"/> and the word after it.</summary>
        public static TextTrigger Prefix(string prefix) => new() { Kind = TextTriggerKind.Prefix, Open = prefix };

        /// <summary>A line starting with <paramref name="marker"/>, or with <paramref name="merge"/>, a run of them.</summary>
        public static TextTrigger Line(string marker, bool merge = false, bool literal = false) =>
            new() { Kind = TextTriggerKind.Line, Open = marker, Merge = merge, Literal = literal };

        /// <summary>A line starting with a marker <paramref name="pattern"/> matches, or with <paramref name="merge"/>, a run of them.</summary>
        public static TextTrigger LineMatching(string pattern, bool merge = false, bool literal = false) =>
            new() { Kind = TextTriggerKind.Line, Pattern = pattern, Merge = merge, Literal = literal };

        /// <summary>The lines between two <paramref name="fence"/> lines.</summary>
        public static TextTrigger Fenced(string fence = "```", bool literal = true) =>
            new() { Kind = TextTriggerKind.Fenced, Open = fence, Literal = literal };

        /// <summary>Markdown links, and with <paramref name="bareUrls"/>, web addresses as they are written.</summary>
        public static TextTrigger Link(bool bareUrls = true) => new() { Kind = TextTriggerKind.Link, BareUrls = bareUrls };

        /// <summary>What a regular expression matches within a line (see <see cref="TextTriggerKind.Pattern"/>).</summary>
        public static TextTrigger Regex(string pattern, bool literal = false) =>
            new() { Kind = TextTriggerKind.Pattern, Pattern = pattern, Literal = literal };
    }
}
