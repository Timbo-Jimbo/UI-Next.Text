using System;
using System.Collections.Generic;
using TimboJimbo.UINext.Text;
using UnityEngine;

namespace TimboJimboTests.UINext.Text
{
    /// <summary>A rule made in a test: any trigger and role, with a padding, a tap, an acceptance check and a style.</summary>
    internal sealed class TestRule : TextRule
    {
        private TextRole _role;
        private float _paddingEm;
        private bool _tappable;
        public Func<TextMatch, bool> Accept;
        public Func<TextStyle, TextStyle> Restyle;

        public override TextRole Role => _role;
        public override float PaddingEm => _paddingEm;
        public override bool Tappable => _tappable;
        public override bool Accepts(in TextMatch match) => Accept == null || Accept(match);

        public override void Style(in TextMatch match, ref TextStyle style)
        {
            if (Restyle != null)
                style = Restyle(style);
        }

        public static TestRule Create(TextTrigger trigger, TextRole role = TextRole.Span, float paddingEm = 0f, bool tappable = false)
        {
            var rule = CreateInstance<TestRule>();
            rule.hideFlags = HideFlags.HideAndDontSave;
            rule.Trigger = trigger;
            rule._role = role;
            rule._paddingEm = paddingEm;
            rule._tappable = tappable;
            return rule;
        }
    }

    /// <summary>Markdown, as test rules: what the built-in Markdown matches, without its prefabs.</summary>
    internal sealed class TestMarkdown : IDisposable
    {
        public readonly TestRule CodeBlock = TestRule.Create(TextTrigger.Fenced(), TextRole.Block);
        public readonly TestRule Heading = TestRule.Create(TextTrigger.Line("# "));
        public readonly TestRule Quote = TestRule.Create(TextTrigger.Line("> ", merge: true), TextRole.Block);
        public readonly TestRule Bullet = TestRule.Create(TextTrigger.LineMatching(@"[-*+] "), TextRole.Block);
        public readonly TestRule Numbered = TestRule.Create(TextTrigger.LineMatching(@"\d+[.)] "), TextRole.Block);
        public readonly TestRule Code = TestRule.Create(TextTrigger.Wrapped("`", literal: true), paddingEm: 0.2f);
        public readonly TestRule Link = TestRule.Create(TextTrigger.Link(), tappable: true);
        public readonly TestRule Bold = TestRule.Create(TextTrigger.Wrapped("**", insideWords: true));
        public readonly TestRule Underline = TestRule.Create(TextTrigger.Wrapped("__"));
        public readonly TestRule Strike = TestRule.Create(TextTrigger.Wrapped("~~", insideWords: true));
        public readonly TestRule Italic = TestRule.Create(TextTrigger.Wrapped("*", insideWords: true));
        public readonly TestRule ItalicUnderscore = TestRule.Create(TextTrigger.Wrapped("_"));

        public List<TextRule> Rules => new()
        {
            CodeBlock, Heading, Quote, Bullet, Numbered, Code, Link, Bold, Underline, Strike, Italic, ItalicUnderscore,
        };

        public void Dispose()
        {
            foreach (var rule in Rules)
                UnityEngine.Object.DestroyImmediate(rule);
        }
    }
}
