using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TimboJimbo.UINext.Text
{
    /// <summary>
    /// Reads a source with rules. The line rules (Fenced, Line) are tried at the start of each line; what no line rule
    /// takes is read with the inline rules (Wrapped, Prefix, Link, Pattern), and so is the text of a span that is not
    /// literal, so spans nest. At each place the first rule in order that matches, and accepts what it found, takes it. A
    /// backslash before ASCII punctuation shows the punctuation as written. Every index it records points into the source;
    /// it inserts nothing.
    /// </summary>
    internal static class TextParser
    {
        private struct Found
        {
            public int Start, End, ContentStart, ContentEnd;
            public string Text, Argument;
            // Its text is read no further, whatever its rule says: a web address, a prefixed word.
            public bool Literal;
        }

        private sealed class State
        {
            public TextDocument Doc;
            public string Source;
            public readonly List<TextRule> LineRules = new();
            public readonly List<TextRule> InlineRules = new();
            // The rules the line and inline rules were sorted from, and the settings' version then: they are sorted again
            // only when either changes, as a label reading its text each frame reads it with the same rules.
            public readonly List<TextRule> SortedFrom = new();
            public int SortedVersion = -1;
            public readonly List<int> LineStarts = new();
            public readonly List<int> LineEnds = new();
            // The characters an inline trigger can start with; any, with a pattern among them.
            public string Starts = "";
            public bool HasPatterns;
            // Characters a run of which wraps a literal span (`): delimiters inside one close nothing.
            public string LiteralRuns = "";
            // Each inline rule's pattern's next match in the range being read, one array per span being read inside.
            public readonly List<Match[]> Next = new();
            public int Depth;
            public readonly StringBuilder Builder = new();
        }

        [ThreadStatic] private static State s_state;
        private static readonly Dictionary<string, Regex> s_patterns = new();
        private static readonly Dictionary<string, Regex> s_markerPatterns = new();

        public static void Parse(string source, IReadOnlyList<TextRule> rules, TextDocument doc)
        {
            doc.Reset(source);
            source = doc.Text;
            if (source.Length == 0) return;

            var s = s_state ??= new State();
            s.Doc = doc;
            s.Source = source;
            s.Depth = 0;
            Sort(s, rules);
            SplitLines(s);

            int count = s.LineStarts.Count;
            for (int line = 0; line < count;)
            {
                // Lines, and what line rules took, are a line break apart, which is shown.
                if (line > 0)
                    doc.AddBreak(s.LineStarts[line] - 1);
                int next = ReadLineRules(s, line);
                if (next > line)
                {
                    line = next;
                    continue;
                }
                ReadInline(s, s.LineStarts[line], s.LineEnds[line], -1);
                line++;
            }
            s.Doc = null;
            s.Source = null;
        }

        // Splits the rules into line rules and inline rules, keeping their order, and notes what inline triggers start with;
        // kept while the rules and the settings are the same.
        private static void Sort(State s, IReadOnlyList<TextRule> rules)
        {
            if (s.SortedVersion == TextSettings.Version && SameRules(s.SortedFrom, rules)) return;
            s.SortedVersion = TextSettings.Version;
            s.SortedFrom.Clear();
            if (rules != null)
                for (int i = 0; i < rules.Count; i++)
                    s.SortedFrom.Add(rules[i]);
            s.LineRules.Clear();
            s.InlineRules.Clear();
            s.HasPatterns = false;
            var starts = new StringBuilder();
            var literal = new StringBuilder();
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    if (rule == null) continue;
                    var trigger = rule.Trigger;
                    switch (trigger.Kind)
                    {
                        case TextTriggerKind.Line:
                        case TextTriggerKind.Fenced:
                            s.LineRules.Add(rule);
                            break;
                        case TextTriggerKind.Pattern:
                            s.InlineRules.Add(rule);
                            s.HasPatterns = true;
                            break;
                        case TextTriggerKind.Link:
                            s.InlineRules.Add(rule);
                            starts.Append('[');
                            if (trigger.BareUrls) starts.Append("hH");
                            break;
                        default:
                            s.InlineRules.Add(rule);
                            if (string.IsNullOrEmpty(trigger.Open)) break;
                            starts.Append(trigger.Open[0]);
                            if (trigger.Kind == TextTriggerKind.Wrapped && trigger.Literal && IsRunDelimiter(in trigger))
                                literal.Append(trigger.Open[0]);
                            break;
                    }
                }
            }
            s.Starts = starts.ToString();
            s.LiteralRuns = literal.ToString();
        }

        private static bool SameRules(List<TextRule> sorted, IReadOnlyList<TextRule> rules)
        {
            int count = rules != null ? rules.Count : 0;
            if (sorted.Count != count) return false;
            for (int i = 0; i < count; i++)
                if (!ReferenceEquals(sorted[i], rules[i]))
                    return false;
            return true;
        }

        // Each line's start and end (before its line break); a text ending in a line break ends with an empty line.
        private static void SplitLines(State s)
        {
            s.LineStarts.Clear();
            s.LineEnds.Clear();
            var src = s.Source;
            for (int pos = 0;;)
            {
                int newline = src.IndexOf('\n', pos);
                int end = newline < 0 ? src.Length : newline;
                if (end > pos && src[end - 1] == '\r')
                    end--;
                s.LineStarts.Add(pos);
                s.LineEnds.Add(end);
                if (newline < 0) break;
                pos = newline + 1;
            }
        }

        // ── Line rules ────────────────────────────────────────────────────────────

        // The first line rule to take the line: the line after what it took; the line itself when none did.
        private static int ReadLineRules(State s, int line)
        {
            foreach (var rule in s.LineRules)
            {
                int next = rule.Trigger.Kind == TextTriggerKind.Fenced ? TryFenced(s, rule, line) : TryLine(s, rule, line);
                if (next > line) return next;
            }
            return line;
        }

        private static int TryFenced(State s, TextRule rule, int line)
        {
            var trigger = rule.Trigger;
            string fence = string.IsNullOrEmpty(trigger.Open) ? "```" : trigger.Open;
            var src = s.Source;
            int start = s.LineStarts[line], end = s.LineEnds[line];
            int i = SkipSpaces(src, start, end);
            if (!StartsWith(src, i, end, fence)) return line;
            // A fence of one character repeated can be longer, to hold a shorter one: ```` holds ```.
            char c = fence[0];
            int length = IsRun(fence) ? RunLength(src, i, end, c) : fence.Length;
            string info = src.Substring(i + length, end - i - length).Trim();
            // A backtick fence's language holds no backtick, as CommonMark's, so ```print(1)``` is code in its line.
            if (c == '`' && info.IndexOf('`') >= 0) return line;

            int count = s.LineStarts.Count, close = -1;
            for (int k = line + 1; k < count && close < 0; k++)
            {
                int ks = s.LineStarts[k], ke = s.LineEnds[k];
                int j = SkipSpaces(src, ks, ke);
                if (!StartsWith(src, j, ke, fence)) continue;
                int run = IsRun(fence) ? RunLength(src, j, ke, c) : fence.Length;
                if (run >= length && SkipSpaces(src, j + run, ke) == ke)
                    close = k;
            }

            // Its text: the lines between the fences (to the end of the text when nothing closes it).
            int first = line + 1, last = (close >= 0 ? close : count) - 1;
            bool empty = first > last;
            int contentStart = empty ? end : s.LineStarts[first];
            int contentEnd = empty ? end : s.LineEnds[last];
            int matchEnd = s.LineEnds[close >= 0 ? close : count - 1];
            string text = empty ? "" : src.Substring(contentStart, contentEnd - contentStart).Replace("\r\n", "\n");
            var match = new TextMatch(rule, start, matchEnd - start, contentStart, contentEnd - contentStart, text,
                info.Length > 0 ? info : null, -1);
            if (!rule.Accepts(in match)) return line;

            int m = s.Doc.Add(in match);
            if (rule.Role == TextRole.Span)
            {
                Pad(s, rule, m, PieceKind.PadStart);
                for (int k = first; k <= last; k++)
                {
                    if (k > first) s.Doc.AddBreak(s.LineStarts[k] - 1);
                    ReadSpanLine(s, rule, m, s.LineStarts[k], s.LineEnds[k]);
                }
                Pad(s, rule, m, PieceKind.PadEnd);
            }
            else
            {
                s.Doc.AddObject(m);
            }
            return (close >= 0 ? close : count - 1) + 1;
        }

        private static int TryLine(State s, TextRule rule, int line)
        {
            if (!MarkerAt(s, rule, line, out int markerStart, out int contentStart)) return line;
            var trigger = rule.Trigger;
            int last = line;
            if (trigger.Merge)
                while (last + 1 < s.LineStarts.Count && MarkerAt(s, rule, last + 1, out _, out _))
                    last++;

            // Its text: its lines after their markers, a line break apart.
            var src = s.Source;
            string text;
            if (last == line)
            {
                text = src.Substring(contentStart, s.LineEnds[line] - contentStart);
            }
            else
            {
                var builder = s.Builder.Clear();
                for (int k = line; k <= last; k++)
                {
                    MarkerAt(s, rule, k, out _, out int from);
                    if (k > line) builder.Append('\n');
                    builder.Append(src, from, s.LineEnds[k] - from);
                }
                text = builder.ToString();
            }
            string marker = src.Substring(markerStart, contentStart - markerStart).Trim();
            int start = s.LineStarts[line], end = s.LineEnds[last];
            var match = new TextMatch(rule, start, end - start, contentStart, end - contentStart, text, marker.Length > 0 ? marker : null, -1);
            if (!rule.Accepts(in match)) return line;

            int m = s.Doc.Add(in match);
            if (rule.Role == TextRole.Span)
            {
                Pad(s, rule, m, PieceKind.PadStart);
                for (int k = line; k <= last; k++)
                {
                    MarkerAt(s, rule, k, out _, out int from);
                    if (k > line) s.Doc.AddBreak(s.LineStarts[k] - 1);
                    ReadSpanLine(s, rule, m, from, s.LineEnds[k]);
                }
                Pad(s, rule, m, PieceKind.PadEnd);
            }
            else
            {
                s.Doc.AddObject(m);
            }
            return last + 1;
        }

        // Whether a line starts with a rule's marker, after any spaces, and where the marker and the text after it start.
        private static bool MarkerAt(State s, TextRule rule, int line, out int markerStart, out int contentStart)
        {
            var trigger = rule.Trigger;
            var src = s.Source;
            int start = s.LineStarts[line], end = s.LineEnds[line];
            int i = SkipSpaces(src, start, end);
            markerStart = contentStart = i;

            if (!string.IsNullOrEmpty(trigger.Pattern))
            {
                // Matched where the line's text starts, or not at all, rather than looked for along the line.
                var regex = MarkerPatternOf(trigger.Pattern);
                if (regex == null) return false;
                var found = regex.Match(src, i, end - i);
                if (!found.Success || found.Index != i || found.Length == 0) return false;
                contentStart = i + found.Length;
                return true;
            }

            var marker = trigger.Open;
            if (string.IsNullOrEmpty(marker)) return false;
            if (StartsWith(src, i, end, marker))
            {
                contentStart = i + marker.Length;
                return true;
            }
            // The marker alone on its line, the space after it gone with nothing to follow: an empty line of a quote.
            int bare = marker.Length;
            while (bare > 0 && char.IsWhiteSpace(marker[bare - 1]))
                bare--;
            if (bare == 0 || bare == marker.Length || i + bare > end || string.CompareOrdinal(src, i, marker, 0, bare) != 0) return false;
            if (SkipSpaces(src, i + bare, end) != end) return false;
            contentStart = end;
            return true;
        }

        // A line of a span's text: read with the inline rules, unless it is literal.
        private static void ReadSpanLine(State s, TextRule rule, int match, int start, int end)
        {
            if (rule.Trigger.Literal)
                AddText(s, start, end, match);
            else
                ReadInline(s, start, end, match);
        }

        // ── Inline rules ──────────────────────────────────────────────────────────

        // Reads [start, end) of the source with the inline rules, as the text of span `parent` (-1 for none).
        private static void ReadInline(State s, int start, int end, int parent)
        {
            var src = s.Source;
            Match[] next = null;
            if (s.HasPatterns)
            {
                while (s.Next.Count <= s.Depth)
                    s.Next.Add(Array.Empty<Match>());
                if (s.Next[s.Depth].Length < s.InlineRules.Count)
                    s.Next[s.Depth] = new Match[s.InlineRules.Count];
                next = s.Next[s.Depth];
                Array.Clear(next, 0, next.Length);
            }
            s.Depth++;

            int text = start;
            for (int i = start; i < end;)
            {
                char c = src[i];
                if (c == '\\' && i + 1 < end && IsAsciiPunctuation(src[i + 1]))
                {
                    AddText(s, text, i, parent);
                    text = i + 1;
                    i += 2;
                    continue;
                }
                if ((s.HasPatterns || s.Starts.IndexOf(c) >= 0) && TryInline(s, start, end, i, parent, text, next, out int after))
                {
                    i = text = after;
                    continue;
                }
                i++;
            }
            AddText(s, text, end, parent);
            s.Depth--;
        }

        // The first inline rule to take what is at i: its match added, with what it shows, and the text before it.
        private static bool TryInline(State s, int start, int end, int i, int parent, int text, Match[] next, out int after)
        {
            for (int r = 0; r < s.InlineRules.Count; r++)
            {
                var rule = s.InlineRules[r];
                var trigger = rule.Trigger;
                Found found;
                bool hit;
                switch (trigger.Kind)
                {
                    case TextTriggerKind.Wrapped:
                        hit = FindWrapped(s, in trigger, start, end, i, out found);
                        break;
                    case TextTriggerKind.Prefix:
                        hit = FindPrefix(s, in trigger, end, i, out found);
                        break;
                    case TextTriggerKind.Link:
                        hit = FindLink(s, in trigger, end, i, out found);
                        break;
                    case TextTriggerKind.Pattern:
                        hit = FindPattern(s, in trigger, end, i, ref next[r], out found);
                        break;
                    default:
                        hit = false;
                        found = default;
                        break;
                }
                if (!hit) continue;
                var match = new TextMatch(rule, found.Start, found.End - found.Start, found.ContentStart, found.ContentEnd - found.ContentStart,
                    found.Text, found.Argument, parent);
                if (!rule.Accepts(in match)) continue;

                AddText(s, text, i, parent);
                int m = s.Doc.Add(in match);
                if (rule.Role == TextRole.Span)
                {
                    Pad(s, rule, m, PieceKind.PadStart);
                    if (trigger.Literal || found.Literal)
                        AddText(s, found.ContentStart, found.ContentEnd, m);
                    else
                        ReadInline(s, found.ContentStart, found.ContentEnd, m);
                    Pad(s, rule, m, PieceKind.PadEnd);
                }
                else
                {
                    s.Doc.AddObject(m);
                }
                after = found.End;
                return true;
            }
            after = i;
            return false;
        }

        private static bool FindWrapped(State s, in TextTrigger trigger, int start, int end, int i, out Found found)
        {
            found = default;
            var src = s.Source;
            string open = trigger.Open;
            if (string.IsNullOrEmpty(open) || !StartsWith(src, i, end, open)) return false;
            string close = string.IsNullOrEmpty(trigger.Close) ? open : trigger.Close;
            bool run = IsRunDelimiter(in trigger);
            char c = open[0];
            // A delimiter of one character repeated opens only where a run of it starts (an escaped one starts none).
            if (run && i > start && src[i - 1] == c && !(i - 2 >= start && src[i - 2] == '\\')) return false;

            if (trigger.Literal && run)
            {
                // As code is: the whole run opens, a run as long closes, and nothing between is read, backslashes included.
                int length = RunLength(src, i, end, c);
                if (length < open.Length) return false;
                int from = i + length;
                int at = FindRun(src, from, end, c, length);
                if (at <= from) return false;
                found = new Found { Start = i, End = at + length, ContentStart = from, ContentEnd = at, Text = src.Substring(from, at - from) };
                return true;
            }

            // Text right after the opener, which stands at a word's start unless delimiters may stand inside words.
            int content = i + open.Length;
            if (content >= end || char.IsWhiteSpace(src[content])) return false;
            if (!trigger.InsideWords && i > 0 && IsWordChar(src[i - 1])) return false;
            int closer = run
                ? FindRunCloser(s, end, content, c, open.Length, trigger.InsideWords)
                : FindCloser(s, end, content, close, trigger.InsideWords);
            if (closer < 0) return false;
            found = new Found { Start = i, End = closer + close.Length, ContentStart = content, ContentEnd = closer, Text = src.Substring(content, closer - content) };
            return true;
        }

        // Where a run delimiter `length` `c`s long closes, its text starting at `from`: at the first run of `c` that can
        // close (after text, and at a word's end unless delimiters may stand inside words) once the runs opened inside it
        // are closed, its last `length` characters, as CommonMark matches emphasis from its delimiter runs. So
        // *a **b** c* is italic around bold, and ***both*** bold around italic. -1 for none.
        private static int FindRunCloser(State s, int end, int from, char c, int length, bool insideWords)
        {
            var src = s.Source;
            int opened = 0;
            for (int j = from; j < end; j++)
            {
                char ch = src[j];
                if (ch == '\\' && j + 1 < end && IsAsciiPunctuation(src[j + 1]))
                {
                    j++;
                    continue;
                }
                if (ch != c)
                {
                    SkipLiteral(s, end, ref j);
                    continue;
                }
                int runEnd = j + RunLength(src, j, end, c);
                int runLength = runEnd - j;
                char before = j > 0 ? src[j - 1] : ' ';
                char after = runEnd < end ? src[runEnd] : ' ';
                bool canClose = !char.IsWhiteSpace(before) && (insideWords || !IsWordChar(after));
                bool canOpen = !char.IsWhiteSpace(after) && (insideWords || !IsWordChar(before));
                if (canClose)
                {
                    int closed = Math.Min(opened, runLength);
                    opened -= closed;
                    int at = runEnd - length;
                    if (runLength - closed >= length && at > from) return at;
                }
                else if (canOpen)
                {
                    opened += runLength;
                }
                j = runEnd - 1;
            }
            return -1;
        }

        // Where a delimiter other than a run closes, its text starting at `from`: its first occurrence after text, and at a
        // word's end unless delimiters may stand inside words. -1 for none.
        private static int FindCloser(State s, int end, int from, string close, bool insideWords)
        {
            var src = s.Source;
            for (int j = from + 1; j + close.Length <= end; j++)
            {
                char ch = src[j];
                if (ch == '\\' && j + 1 < end && IsAsciiPunctuation(src[j + 1]))
                {
                    j++;
                    continue;
                }
                if (ch != close[0] && SkipLiteral(s, end, ref j)) continue;
                if (!StartsWith(src, j, end, close) || char.IsWhiteSpace(src[j - 1])) continue;
                int after = j + close.Length;
                if (!insideWords && after < end && IsWordChar(src[after])) continue;
                return j;
            }
            return -1;
        }

        // A literal span (`code`) starting at j, which closes nothing it holds: moves j to its last character. A run that
        // nothing closes is passed over whole.
        private static bool SkipLiteral(State s, int end, ref int j)
        {
            var src = s.Source;
            char ch = src[j];
            if (s.LiteralRuns.IndexOf(ch) < 0) return false;
            int length = RunLength(src, j, end, ch);
            int at = FindRun(src, j + length, end, ch, length);
            j = at > j + length ? at + length - 1 : j + length - 1;
            return true;
        }

        private static bool FindPrefix(State s, in TextTrigger trigger, int end, int i, out Found found)
        {
            found = default;
            var src = s.Source;
            string prefix = trigger.Open;
            if (string.IsNullOrEmpty(prefix) || !StartsWith(src, i, end, prefix)) return false;
            // Not inside a word, as in mail@example.com.
            if (i > 0 && IsWordChar(src[i - 1])) return false;
            int word = i + prefix.Length, j = word;
            while (j < end)
            {
                char ch = src[j];
                // A dot or a hyphen inside a word joins it to more: @first.last, #long-tag.
                if (IsWordChar(ch) || ((ch == '.' || ch == '-') && j > word && j + 1 < end && IsWordChar(src[j + 1])))
                    j++;
                else
                    break;
            }
            if (j == word) return false;
            found = new Found { Start = i, End = j, ContentStart = i, ContentEnd = j, Text = src.Substring(word, j - word), Literal = true };
            return true;
        }

        private static bool FindLink(State s, in TextTrigger trigger, int end, int i, out Found found)
        {
            found = default;
            var src = s.Source;
            char c = src[i];
            if (c == '[')
            {
                int close = IndexOfUnescaped(src, ']', i + 1, end);
                if (close <= i + 1 || close + 1 >= end || src[close + 1] != '(') return false;
                int targetEnd = close + 2 < end ? src.IndexOf(')', close + 2, end - close - 2) : -1;
                if (targetEnd < 0) return false;
                var target = src.Substring(close + 2, targetEnd - close - 2).Trim();
                if (target.Length == 0) return false;
                found = new Found
                {
                    Start = i, End = targetEnd + 1, ContentStart = i + 1, ContentEnd = close, Text = src.Substring(i + 1, close - i - 1),
                    Argument = target,
                };
                return true;
            }

            if (!trigger.BareUrls || (c != 'h' && c != 'H')) return false;
            if (!StartsWithIgnoreCase(src, i, end, "https://") && !StartsWithIgnoreCase(src, i, end, "http://")) return false;
            // An address stands on its own: after a space, an opening bracket or quote, or emphasis.
            if (i > 0 && !char.IsWhiteSpace(src[i - 1]) && "([<\"'*_~".IndexOf(src[i - 1]) < 0) return false;
            int j = i;
            while (j < end && !char.IsWhiteSpace(src[j]) && src[j] != '<' && src[j] != '>')
                j++;
            // Punctuation at its end is the sentence's, as is a closing bracket it did not open.
            while (j > i)
            {
                char last = src[j - 1];
                if (".,;:!?'\"*_~".IndexOf(last) >= 0
                    || (last == ')' && Count(src, i, j, '(') < Count(src, i, j, ')'))
                    || (last == ']' && Count(src, i, j, '[') < Count(src, i, j, ']')))
                    j--;
                else
                    break;
            }
            if (j - i <= 8) return false;
            var url = src.Substring(i, j - i);
            found = new Found { Start = i, End = j, ContentStart = i, ContentEnd = j, Text = url, Argument = url, Literal = true };
            return true;
        }

        private static bool FindPattern(State s, in TextTrigger trigger, int end, int i, ref Match next, out Found found)
        {
            found = default;
            var regex = PatternOf(trigger.Pattern);
            if (regex == null) return false;
            // Its next match from here, looked for again only once reading has gone past it.
            if (next == null || (next.Success && next.Index < i))
                next = regex.Match(s.Source, i, end - i);
            if (!next.Success || next.Index != i || next.Length == 0) return false;
            var content = next.Groups["content"];
            var argument = next.Groups["arg"];
            found = new Found
            {
                Start = i,
                End = i + next.Length,
                ContentStart = content.Success ? content.Index : i,
                ContentEnd = content.Success ? content.Index + content.Length : i + next.Length,
                Text = content.Success ? content.Value : next.Value,
                Argument = argument.Success ? argument.Value : null,
            };
            return true;
        }

        /// <summary>A pattern, compiled once; null, warned of once, when it is not a regular expression.</summary>
        internal static Regex PatternOf(string pattern) => Compile(s_patterns, pattern, anchored: false);

        // A line marker's pattern, compiled once to match only where matching starts (\G).
        private static Regex MarkerPatternOf(string pattern) => Compile(s_markerPatterns, pattern, anchored: true);

        // The expression is put together only as a pattern is first compiled, so looking one up allocates nothing.
        private static Regex Compile(Dictionary<string, Regex> compiled, string pattern, bool anchored)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            if (compiled.TryGetValue(pattern, out var regex)) return regex;
            try
            {
                regex = new Regex(anchored ? @"\G(?:" + pattern + ")" : pattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"UI Next Text: \"{pattern}\" is not a regular expression ({e.Message}), so its rule matches nothing.");
                regex = null;
            }
            compiled[pattern] = regex;
            return regex;
        }

        // ── Pieces ────────────────────────────────────────────────────────────────

        private static void AddText(State s, int from, int to, int match) => s.Doc.AddText(from, to - from, match);

        private static void Pad(State s, TextRule rule, int match, PieceKind kind)
        {
            if (rule.PaddingEm > 0f)
                s.Doc.AddPad(kind, match);
        }

        // ── Characters ────────────────────────────────────────────────────────────

        private static bool IsAsciiPunctuation(char c) => c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        // Whether a delimiter is one character repeated (`, **, ~~) and closed by itself, so it is matched as a run.
        private static bool IsRunDelimiter(in TextTrigger trigger) =>
            IsRun(trigger.Open) && (string.IsNullOrEmpty(trigger.Close) || trigger.Close == trigger.Open);

        private static bool IsRun(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 1; i < value.Length; i++)
                if (value[i] != value[0])
                    return false;
            return true;
        }

        private static int SkipSpaces(string src, int i, int end)
        {
            while (i < end && (src[i] == ' ' || src[i] == '\t'))
                i++;
            return i;
        }

        private static bool StartsWith(string src, int i, int end, string value) =>
            i + value.Length <= end && string.CompareOrdinal(src, i, value, 0, value.Length) == 0;

        private static bool StartsWithIgnoreCase(string src, int i, int end, string value) =>
            i + value.Length <= end && string.Compare(src, i, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;

        // How many `c`s run from i.
        private static int RunLength(string src, int i, int end, char c)
        {
            int j = i;
            while (j < end && src[j] == c)
                j++;
            return j - i;
        }

        // Where the next run of exactly `length` `c`s from an index starts, or -1.
        private static int FindRun(string src, int from, int end, char c, int length)
        {
            for (int j = from; j < end;)
            {
                int k = src.IndexOf(c, j, end - j);
                if (k < 0) return -1;
                int run = RunLength(src, k, end, c);
                if (run == length) return k;
                j = k + run;
            }
            return -1;
        }

        private static int IndexOfUnescaped(string src, char c, int from, int end)
        {
            for (int j = from; j < end; j++)
            {
                if (src[j] == '\\' && j + 1 < end && IsAsciiPunctuation(src[j + 1]))
                    j++;
                else if (src[j] == c)
                    return j;
            }
            return -1;
        }

        private static int Count(string src, int from, int end, char c)
        {
            int count = 0;
            for (int j = from; j < end; j++)
                if (src[j] == c)
                    count++;
            return count;
        }
    }
}
