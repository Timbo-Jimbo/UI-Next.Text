using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TimboJimbo.UINext.Text;
using TimboJimbo.UINext.Text.Bridge;
using UnityEngine;

namespace TimboJimboTests.UINext.Text
{
    /// <summary>The layout text owns every insertion and the mapping back to the source.</summary>
    public class LayoutTextTests
    {
        private static readonly string Pads = new(LayoutText.Pad, LayoutText.PadCount);

        private TextDocument _doc;
        private LayoutText _layout;
        private TestMarkdown _markdown;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _doc = new TextDocument();
            _layout = new LayoutText();
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

        private void Build(string source, params TextRule[] rules)
        {
            _doc.Parse(source, rules.Length > 0 ? rules : _markdown.Rules);
            _layout.Build(_doc);
        }

        private string Kinds() => string.Join(",", _layout.Runs.Select(r => r.Kind.ToString()));

        private int MatchOf(TextRule rule) => _doc.Matches.ToList().FindIndex(m => m.Rule == rule);

        [Test]
        public void PlainBuildIsTheSourceItself()
        {
            _layout.BuildPlain("abc");
            Assert.AreEqual("abc", _layout.Text);
            Assert.AreEqual(0, _layout.Runs.Count);
            Assert.AreEqual(1, _layout.ToLayout(1));
            Assert.AreEqual(2, _layout.ToSource(2));
            Assert.AreEqual(3, _layout.ToLayout(3));
            Assert.AreEqual(3, _layout.ToSource(5));
        }

        [Test]
        public void MarkupIsDroppedAndIndicesMapAcrossTheGaps()
        {
            Build("**bold** x");
            Assert.AreEqual("bold x", _layout.Text);
            Assert.AreEqual("Text,Text", Kinds());
            Assert.AreEqual(MatchOf(_markdown.Bold), _layout.Runs[0].Match);
            Assert.AreEqual(-1, _layout.Runs[1].Match);

            Assert.AreEqual(0, _layout.ToLayout(2), "first shown character");
            Assert.AreEqual(0, _layout.ToLayout(0), "inside the opening marker: the next shown character");
            Assert.AreEqual(4, _layout.ToLayout(6), "inside the closing marker");
            Assert.AreEqual(2, _layout.ToSource(0));
            Assert.AreEqual(8, _layout.ToSource(4));
            Assert.AreEqual(10, _layout.ToSource(6), "past the end: the source length");
        }

        [Test]
        public void LineBreaksMapToTheSources()
        {
            Build("a\r\nb");
            Assert.AreEqual("a\nb", _layout.Text);
            Assert.AreEqual(2, _layout.ToSource(1), "the line break is the source's");
            Assert.AreEqual(2, _layout.ToLayout(3));
        }

        [Test]
        public void PaddedSpanHoldsItsPads()
        {
            Build("run `git` now");
            Assert.AreEqual("run " + Pads + "git" + Pads + " now", _layout.Text);
            Assert.AreEqual("Text,Pad,Text,Pad,Text", Kinds());
            Assert.IsTrue(_layout.TryGetRange(MatchOf(_markdown.Code), out int start, out int end));
            Assert.AreEqual(4, start);
            Assert.AreEqual(4 + 3 + 2 * LayoutText.PadCount, end);
        }

        [Test]
        public void NestedSpansReachOverWhatTheyHold()
        {
            Build("**a *b* c**");
            Assert.AreEqual("a b c", _layout.Text);
            Assert.IsTrue(_layout.TryGetRange(MatchOf(_markdown.Bold), out int boldStart, out int boldEnd));
            Assert.AreEqual((0, 5), (boldStart, boldEnd));
            Assert.IsTrue(_layout.TryGetRange(MatchOf(_markdown.Italic), out int italicStart, out int italicEnd));
            Assert.AreEqual((2, 3), (italicStart, italicEnd));
        }

        [Test]
        public void ElementBecomesOnePlaceholder()
        {
            var emoji = Track(TestRule.Create(TextTrigger.Wrapped(":"), TextRole.Element));
            Build("hi :smile: there", emoji);
            Assert.AreEqual("hi " + AtgSpriteAssets.PlaceholderCode(0) + " there", _layout.Text);
            Assert.AreEqual("Text,Element,Text", Kinds());
            Assert.AreEqual(0, _layout.ObjectIndex(0));
            Assert.AreEqual(1, _layout.ObjectCount);
            Assert.IsTrue(_layout.TryGetRange(0, out int start, out int end));
            Assert.AreEqual((3, 4), (start, end));
            Assert.AreEqual(10, _layout.ToSource(3), "the placeholder maps to the source after the match");
            Assert.AreEqual(4, _layout.ToLayout(5), "inside the match: the next shown character");
        }

        [Test]
        public void BlockStandsOnALineOfItsOwn()
        {
            Build("a\n```\ncode\n```\nb");
            Assert.AreEqual("a\n" + AtgSpriteAssets.PlaceholderCode(0) + "\nb", _layout.Text);
            Assert.AreEqual("Text,Block,BlockEnd,Text", Kinds());

            var callout = Track(TestRule.Create(TextTrigger.Wrapped("[[", "]]"), TextRole.Block));
            Build("x [[b]] y", callout);
            Assert.AreEqual("x \n" + AtgSpriteAssets.PlaceholderCode(0) + "\n y", _layout.Text, "a block in a line breaks it");
            Assert.AreEqual("Text,Block,BlockEnd,Text", Kinds());
        }
    }
}
