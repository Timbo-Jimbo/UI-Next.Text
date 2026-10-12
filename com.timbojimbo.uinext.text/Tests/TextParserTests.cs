using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TimboJimbo.UINext.Text;
using UnityEngine;

namespace TimboJimboTests.UINext.Text
{
    /// <summary>Rules find text in order, the first to match taking it; the parser records source indices and inserts nothing.</summary>
    public class TextParserTests
    {
        private TextDocument _doc;
        private TestMarkdown _markdown;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _doc = new TextDocument();
            _markdown = new TestMarkdown();
        }

        [TearDown]
        public void TearDown()
        {
            _markdown.Dispose();
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        private TestRule Track(TestRule rule)
        {
            _created.Add(rule);
            return rule;
        }

        private void Parse(string source) => _doc.Parse(source, _markdown.Rules);

        private void Parse(string source, params TextRule[] rules) => _doc.Parse(source, rules);

        // What is shown, in order: text, line breaks, and a box for each element or block.
        private string Shown()
        {
            var shown = new StringBuilder();
            foreach (var piece in _doc.Pieces)
            {
                switch (piece.Kind)
                {
                    case PieceKind.Text: shown.Append(_doc.Text, piece.Start, piece.Length); break;
                    case PieceKind.Break: shown.Append('\n'); break;
                    case PieceKind.Object: shown.Append('□'); break;
                }
            }
            return shown.ToString();
        }

        private TextMatch[] MatchesOf(TextRule rule) => _doc.Matches.Where(m => m.Rule == rule).ToArray();

        private int IndexOf(TextMatch match) => _doc.Matches.ToList().FindIndex(m => m.Start == match.Start && m.Rule == match.Rule);

        [Test]
        public void PlainTextIsShownAsItIs()
        {
            Parse("Hello world");
            Assert.AreEqual("Hello world", Shown());
            Assert.AreEqual(0, _doc.Matches.Count);
            Assert.AreEqual(1, _doc.Pieces.Count);
        }

        [Test]
        public void EmphasisTakesItsText()
        {
            Parse("**bold** *italic* __under__ ~~gone~~");
            Assert.AreEqual("bold italic under gone", Shown());
            var bold = MatchesOf(_markdown.Bold).Single();
            Assert.AreEqual("bold", bold.Text);
            Assert.AreEqual(0, bold.Start);
            Assert.AreEqual(8, bold.Length);
            Assert.AreEqual(2, bold.ContentStart);
            Assert.AreEqual("italic", MatchesOf(_markdown.Italic).Single().Text);
            Assert.AreEqual("under", MatchesOf(_markdown.Underline).Single().Text);
            Assert.AreEqual("gone", MatchesOf(_markdown.Strike).Single().Text);
        }

        [Test]
        public void EmphasisNests()
        {
            Parse("***both*** and **bold *and italic***");
            Assert.AreEqual("both and bold and italic", Shown());
            var bold = MatchesOf(_markdown.Bold);
            var italic = MatchesOf(_markdown.Italic);
            Assert.AreEqual("*both*", bold[0].Text);
            Assert.AreEqual("both", italic[0].Text);
            Assert.AreEqual(IndexOf(bold[0]), italic[0].Parent);
            Assert.AreEqual("bold *and italic*", bold[1].Text);
            Assert.AreEqual("and italic", italic[1].Text);
            Assert.AreEqual(IndexOf(bold[1]), italic[1].Parent);
        }

        [Test]
        public void ItalicCanHoldBold()
        {
            Parse("*a **b** c*");
            Assert.AreEqual("a b c", Shown());
            var italic = MatchesOf(_markdown.Italic).Single();
            Assert.AreEqual("a **b** c", italic.Text);
            Assert.AreEqual(IndexOf(italic), MatchesOf(_markdown.Bold).Single().Parent);
        }

        [Test]
        public void UnmatchedAndIntrawordMarkersStayText()
        {
            const string source = "a * b snake_case_name 2*3";
            Parse(source);
            Assert.AreEqual(source, Shown());
            Assert.AreEqual(0, _doc.Matches.Count);
        }

        [Test]
        public void EscapesShowThePunctuation()
        {
            Parse(@"\*not italic\* and \`not code\`");
            Assert.AreEqual("*not italic* and `not code`", Shown());
            Assert.AreEqual(0, _doc.Matches.Count);
        }

        [Test]
        public void CodeIsLiteral()
        {
            Parse("run `git status` now");
            Assert.AreEqual("run git status now", Shown());
            var code = MatchesOf(_markdown.Code).Single();
            Assert.AreEqual("git status", code.Text);
            Assert.AreEqual(4, code.Start);
            Assert.AreEqual(12, code.Length);

            Parse("`**not bold**` and ``a`b``");
            var codes = MatchesOf(_markdown.Code);
            Assert.AreEqual(2, _doc.Matches.Count);
            Assert.AreEqual("**not bold**", codes[0].Text);
            Assert.AreEqual("a`b", codes[1].Text, "a longer run of backticks holds a shorter one");
        }

        [Test]
        public void PaddedSpanIsPaddedEitherSide()
        {
            Parse("run `git` now");
            var kinds = string.Join(",", _doc.Pieces.Select(p => p.Kind));
            Assert.AreEqual("Text,PadStart,Text,PadEnd,Text", kinds);
        }

        [Test]
        public void LinksCarryTheirTarget()
        {
            Parse("see [the **docs**](https://example.com/docs) now");
            Assert.AreEqual("see the docs now", Shown());
            var link = MatchesOf(_markdown.Link).Single();
            Assert.AreEqual("the **docs**", link.Text);
            Assert.AreEqual("https://example.com/docs", link.Argument);
            Assert.AreEqual(IndexOf(link), MatchesOf(_markdown.Bold).Single().Parent, "a link's text is read");
        }

        [Test]
        public void BareUrlsLeaveTrailingPunctuationAndHoldTheirMarkers()
        {
            const string source = "go to https://unity.com/a_b_c, then rest.";
            Parse(source);
            Assert.AreEqual(source, Shown());
            var link = _doc.Matches.Single();
            Assert.AreEqual("https://unity.com/a_b_c", link.Text);
            Assert.AreEqual("https://unity.com/a_b_c", link.Argument);
        }

        [Test]
        public void LineRulesTakeTheirLines()
        {
            Parse("# Title\n- item\n1. first\n> quoted");
            Assert.AreEqual("Title\n□\n□\n□", Shown());
            Assert.AreEqual("Title", MatchesOf(_markdown.Heading).Single().Text);
            var bullet = MatchesOf(_markdown.Bullet).Single();
            Assert.AreEqual("item", bullet.Text);
            Assert.AreEqual("-", bullet.Argument);
            var numbered = MatchesOf(_markdown.Numbered).Single();
            Assert.AreEqual("first", numbered.Text);
            Assert.AreEqual("1.", numbered.Argument);
            Assert.AreEqual("quoted", MatchesOf(_markdown.Quote).Single().Text);
        }

        [Test]
        public void MergingLinesMakeOneMatch()
        {
            Parse("> a\n> **b**\n>\nc");
            Assert.AreEqual("□\nc", Shown());
            var quote = MatchesOf(_markdown.Quote).Single();
            Assert.AreEqual("a\n**b**\n", quote.Text);
            Assert.AreEqual(1, _doc.Matches.Count, "a block's text is read by what shows it");
        }

        [Test]
        public void LeadingSpacesDoNotHideAMarker()
        {
            Parse("  > quoted");
            Assert.AreEqual("quoted", MatchesOf(_markdown.Quote).Single().Text);
        }

        [Test]
        public void FencesHoldTheirLines()
        {
            Parse("before\n```cs\nvar x = 1;\nreturn x;\n```\nafter");
            Assert.AreEqual("before\n□\nafter", Shown());
            var block = MatchesOf(_markdown.CodeBlock).Single();
            Assert.AreEqual("var x = 1;\nreturn x;", block.Text);
            Assert.AreEqual("cs", block.Argument);
            Assert.AreEqual(7, block.Start);

            Parse("```\n```");
            Assert.AreEqual("", MatchesOf(_markdown.CodeBlock).Single().Text);
            Parse("```\nnever closed");
            Assert.AreEqual("never closed", MatchesOf(_markdown.CodeBlock).Single().Text);
            Parse("```print(1)```");
            Assert.AreEqual("print(1)", MatchesOf(_markdown.Code).Single().Text, "a fence with a backtick after it is code in its line");
        }

        [Test]
        public void NewlinesOfBothKindsBreakLines()
        {
            Parse("one\ntwo\r\nthree\n");
            Assert.AreEqual("one\ntwo\nthree\n", Shown());
            var breaks = _doc.Pieces.Where(p => p.Kind == PieceKind.Break).Select(p => p.Start).ToArray();
            CollectionAssert.AreEqual(new[] { 3, 8, 14 }, breaks, "each at its source line break");
        }

        [Test]
        public void PrefixStartsAWord()
        {
            var mention = Track(TestRule.Create(TextTrigger.Prefix("@"), TextRole.Element));
            Parse("ping @timbo.j and mail@example.com", mention);
            var match = _doc.Matches.Single();
            Assert.AreEqual("timbo.j", match.Text);
            Assert.AreEqual(5, match.Start);
            Assert.AreEqual(8, match.Length);
            Assert.AreEqual("ping □ and mail@example.com", Shown());
        }

        [Test]
        public void ARuleCanDeclineWhatItFinds()
        {
            var emoji = Track(TestRule.Create(TextTrigger.Wrapped(":"), TextRole.Element));
            emoji.Accept = match => match.Text == "smile";
            Parse(":nope: :smile:", emoji);
            Assert.AreEqual(":nope: □", Shown());
            Assert.AreEqual(7, _doc.Matches.Single().Start);
        }

        [Test]
        public void TheFirstRuleToMatchTakesIt()
        {
            var first = Track(TestRule.Create(TextTrigger.Wrapped("==")));
            var second = Track(TestRule.Create(TextTrigger.Wrapped("==")));
            Parse("a ==b== c", first, second);
            Assert.AreSame(first, _doc.Matches.Single().Rule);
        }

        [Test]
        public void PatternGroupsGiveTextAndArgument()
        {
            var pattern = Track(TestRule.Create(TextTrigger.Regex(@"\{(?<content>[^}|]+)\|(?<arg>[^}]+)\}")));
            Parse("say {hi|wave} now", pattern);
            var match = _doc.Matches.Single();
            Assert.AreEqual("hi", match.Text);
            Assert.AreEqual("wave", match.Argument);
            Assert.AreEqual("say hi now", Shown());
        }

        [Test]
        public void EmptyAndNullSourcesShowNothing()
        {
            Parse("");
            Assert.AreEqual(0, _doc.Pieces.Count);
            Parse(null);
            Assert.AreEqual("", _doc.Text);
            Assert.AreEqual(0, _doc.Matches.Count);
        }
    }
}
