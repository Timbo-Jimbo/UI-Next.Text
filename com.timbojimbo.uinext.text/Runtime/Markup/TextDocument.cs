using System.Collections.Generic;

namespace TimboJimbo.UINext.Text
{
    /// <summary>
    /// Text a rule found: where it is in the source, the part of it shown when it stays text, its text and argument for
    /// what shows it, and the match it is inside. Indices are UTF-16 offsets into the source.
    /// </summary>
    public readonly struct TextMatch
    {
        /// <summary>The rule that found it.</summary>
        public readonly TextRule Rule;

        /// <summary>Where all of it is in the source, markup included.</summary>
        public readonly int Start;
        public readonly int Length;

        /// <summary>
        /// The part of it shown when it stays text: what is between its delimiters, a prefix and its word, a link's text,
        /// the lines after their markers, the lines between the fences.
        /// </summary>
        public readonly int ContentStart;
        public readonly int ContentLength;

        /// <summary>
        /// Its text, for what shows it: what is between its delimiters, the word after a prefix, a link's text, its lines
        /// without their markers (joined by line breaks), the lines between the fences. As written, escapes included.
        /// </summary>
        public readonly string Text;

        /// <summary>
        /// What else it carries, or null: a link's target, what follows an opening fence (a language), a line's marker
        /// ("1.", "-"), a pattern's <c>arg</c> group.
        /// </summary>
        public readonly string Argument;

        /// <summary>The index of the match it is inside (a span), or -1.</summary>
        public readonly int Parent;

        public TextMatch(TextRule rule, int start, int length, int contentStart, int contentLength, string text, string argument, int parent)
        {
            Rule = rule;
            Start = start;
            Length = length;
            ContentStart = contentStart;
            ContentLength = contentLength;
            Text = text;
            Argument = argument;
            Parent = parent;
        }

        public int End => Start + Length;
        public int ContentEnd => ContentStart + ContentLength;

        /// <summary>How it is shown: its rule's role.</summary>
        public TextRole Role => Rule != null ? Rule.Role : TextRole.Span;
    }

    // What a document shows, in order: source text, the line breaks between lines, the objects elements and blocks
    // become, and the padding either side of a padded span's text.
    internal enum PieceKind : byte
    {
        Text,
        Break,
        Object,
        PadStart,
        PadEnd,
    }

    internal struct TextPiece
    {
        public PieceKind Kind;
        // Text: where it is in the source. A break: the source index of its line break.
        public int Start;
        public int Length;
        // Text: the innermost span it is in (-1 for none). An object or a pad: its match.
        public int Match;
    }

    /// <summary>
    /// A source text as rules read it: the matches they found, in the order they start (a match before those inside it),
    /// and what is shown, in source indices. Parse it with the rules of a <see cref="TextRuleSet"/>, and give it to a
    /// layout (<see cref="TextLayoutInput.Document"/>) to save the layout reading it again. Reusable; parsing again keeps
    /// the allocations.
    /// </summary>
    public sealed class TextDocument
    {
        private readonly List<TextMatch> _matches = new();
        internal readonly List<TextPiece> Pieces = new();

        /// <summary>The source as given.</summary>
        public string Text { get; private set; } = "";

        // Counts its readings, for what is built from it (a layout text) to see it read again.
        internal int Version { get; private set; }

        /// <summary>What the rules found, in the order they start, each before the matches inside it.</summary>
        public IReadOnlyList<TextMatch> Matches => _matches;

        /// <summary>
        /// Reads <paramref name="source"/> with <paramref name="rules"/>, in order: at each place the first that matches
        /// takes it (see <see cref="TextRule"/>).
        /// </summary>
        public void Parse(string source, IReadOnlyList<TextRule> rules)
        {
            using var parse = TextProfiling.Parse.Auto();
            TextParser.Parse(source, rules, this);
        }

        /// <summary>Takes <paramref name="source"/> as plain text: shown as written, with no matches.</summary>
        public void SetPlain(string source)
        {
            Reset(source);
            if (Text.Length > 0)
                AddText(0, Text.Length, -1);
        }

        internal void Reset(string source)
        {
            Version++;
            Text = source ?? "";
            _matches.Clear();
            Pieces.Clear();
        }

        internal int Add(in TextMatch match)
        {
            _matches.Add(match);
            return _matches.Count - 1;
        }

        internal void AddText(int start, int length, int match)
        {
            if (length > 0)
                Pieces.Add(new TextPiece { Kind = PieceKind.Text, Start = start, Length = length, Match = match });
        }

        internal void AddBreak(int sourceIndex) => Pieces.Add(new TextPiece { Kind = PieceKind.Break, Start = sourceIndex, Match = -1 });

        internal void AddObject(int match) => Pieces.Add(new TextPiece { Kind = PieceKind.Object, Start = -1, Match = match });

        internal void AddPad(PieceKind kind, int match) => Pieces.Add(new TextPiece { Kind = kind, Start = -1, Match = match });
    }
}
