## [Unreleased]

### Changed

- Markup is read by rules: a `TextRule` has a trigger, the characters around the text it takes (wrapped, prefix, line, merging a run of lines, fenced, link, or a pattern), and a role, how what it takes is shown (a span, an element or a block); a `TextRuleSet` holds them in order, and at each place the first that matches, and accepts what it found, takes it. Spans nest, and a literal rule keeps what it wraps from the rest. Markdown is no longer built in: UI Next's built-in Markdown is a set of rules like any other.
- `TextDocument` holds what the rules matched (`TextMatch`: the source range, the text, the argument, the span it is in), read by `TextDocument.Parse`. `TextLayoutInput.Markup` (null for plain text) replaces `RichText` and `Theme`, `TextLayoutInput.Document` lays out a text read already, and `TextLayoutInput.ObjectSizes` sizes elements and blocks.
- Elements and blocks take exactly the room what draws the text gives them: an element stands in its line, on the baseline or centred on the text, and a block has a line of its own, as tall as it is. `TextLayout.TryGetObjectRect` says where each went, `GetMatchRects` gives a span's pieces of its lines, and `MatchAt` the tappable span under a point, in place of `LinkAt`.
- A colour a span gives its text is its own: `TextLayout.TextColor` marks the text's own colour, which is what a tint colours.
- `TextSettings.DefaultMarkup` replaces `DefaultTheme`; `Generate`, `Measure` and `LongestWord` take no `InlineContext`.
- `TextTrigger` fields show in the inspector only where their kind uses them.

### Removed

- `MarkdownParser`, `TextTheme`, `InlineHandler`, `PrefabInlineHandler`, `InlineBackground` and what went with them (`InlineToken`, `TextLink`, `TextRun`, `RunStyle`, `BlockKind`, `InlinePlacement`, `InlinePlacementRequest`, `CollectPlacements`, `PlaceholderBoxes`, `EffectiveTheme`), and the `TimboJimbo.UINext.Text.Markup` namespace, whose types are in `TimboJimbo.UINext.Text`. Quotes and list items are no longer indents and bullets put into the text: UI Next shows them as blocks.

## [0.1.1] - 12/10/2026

### Fixed

- A text with no theme of its own lays out with the project's Default Theme (**Project Settings > Timbo Jimbo > UI Next Text**) before the built-in one, as it takes the project's default font: the default theme reached no text. `TextLayout.EffectiveTheme` says which theme a text gets.

## [0.1.0] - 12/10/2026

UI Text's engine (as of UI Text 0.4.0), moved over to UI Next.

### Added

- Text laid out by Unity's Advanced Text Generator, through a bridge assembly (`Unity.TextCore.FontEngine.Tools`): HarfBuzz shaping, ICU line breaking and bidi, OS font fallback and colour emoji.
- `TextLayout`, laying out a text without GameObjects, and its queries: carets, lines, the position nearest a point, a character's direction and edges, the caret beside a caret on screen, the link under a point (`LinkAt`), underline and strikethrough segments (`Decorations`) and what inline handlers ask for (`CollectPlacements`); `Measure` and `LongestWord` for layout.
- `FontStack` (OS families, project font files and dynamic font assets, in order, with OS fallbacks) and `FontSource`, with real bold and italic faces where there are any.
- A Markdown dialect (bold, italic, underline, strikethrough, code, links, headings, lists, quotes and fenced code), `TextTheme`, and inline handlers (`InlineHandler`, `PrefabInlineHandler`, `IInlineContent`) placing content over or behind matched text.
- `InlineBackground`: a background behind matched text that what draws it draws itself (a fill, a border, round corners and padding the layout keeps), with no prefab; the built-in ones are behind `code` spans and fenced code blocks. `InlinePlacementRequest.Handler` says which handler a placement is for.
- `TextSettings` and its page under **Edit > Project Settings > Timbo Jimbo > UI Next Text**: the default font stack and theme, preloaded into builds. A project with none registered takes the first settings asset it finds. `TextSettings.Changed` is raised when a font stack, a theme or the settings change, for whatever draws text to lay it out again.

### Coming from UI Text

- Namespaces are `TimboJimbo.UINext.Text` (with `.Markup` and `.Bridge`); `TextBlockDirection`, `TextBlockOverflow` and `TextBlockSettings` are `TextDirection`, `TextOverflow` and `TextSettings`; the asset menus are under **Timbo Jimbo > UI Next**; the settings key is `com.timbojimbo.uinext.text.settings`.
- The bridge takes another name the TextCore module grants its internals to, and every asset has a new GUID, so UI Text can stay in a project beside it while it is retired.
- `InlineContext.Owner` is whatever draws the text (UI Next's `Label`), not a TextBlock.
- Left behind: `TextBlock` (UGUI) with its shaders and editor, as UI Next's `Label` draws text; the Box code backgrounds, which drew with the old UI package, as `InlineBackground` does that; `ITextBlockGlyphModifier`, TextBlock's mesh hook; and the UGUI Chat sample.
