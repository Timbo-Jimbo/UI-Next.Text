## [Unreleased]

### Added

- Profiler markers, in UI Next's category ("UI Next"): reading markup (`UINext.Text.Parse`), laying text out (`UINext.Text.Generate`) and measuring it (`UINext.Text.Measure`).
- `TextLayout.MeasureAhead`: a text measured by laying it out in a layout, which keeps the layout, so drawing it costs nothing more, and keeps what the engine worked out of the text (its shaping), so laying it out again at another width costs a fraction of the first time.
- `TextLayout.Reflow`: the text last laid out, laid out in another box. Where nothing in it would wrap or be cut short differently there (a layout made with no width, in a box at least as wide as its widest line, say), it is kept and moved where its alignment puts it, which costs nothing. `TextLayout.OriginPx` says where its own box starts in the box given, as `TopPx` said down alone.
- Experimental (`AtgReplay`): plain text of one line is laid out without the engine, from what the engine did with the same font and size before: each character's quad and where it starts and ends a line, and how far apart each pair of characters stands, learned as the engine lays plain texts out, so a replayed text is placed exactly where the engine would put it. Anything else (spans, other scripts, wrapping, bold faces, a character or pair not yet seen, a face the engine once disagreed with) goes to the engine, and so does a query that needs the engine's own state. On a Pixel 8 a counter changing every frame lays out in 72 rather than 216 µs.

### Changed

- Reading markup sorts a rule set's rules once, until they or the text settings change, and matches a line marker's pattern (a numbered item's) only where its line starts, rather than looking for it along the line.
- A text overflowing its box, aligned to its middle or bottom, is measured with its layout's own engine state, which keeps what the engine works out for laying it out, rather than with one shared by every text.
- Laying a text out again (at another width, say) builds what the engine is given from its document only when the document was read again, and a text no rule matched in is given to the engine as it is, rather than copied: a feed of cards resizing every frame went from 12 KB of garbage a frame to none.

### Removed

- `TextLayout.LongestWord`: what draws text no longer asks how narrow it can be wrapped.

## [0.2.0] - 13/10/2026

### Added

- `FontStack.Prewarm` resolves a stack's fonts, their bold and italic faces with them, and draws characters into their atlases (the printable ASCII unless given others), for a loading screen: the first text drawn with a stack otherwise does this as it goes, which can hold up the frame it first shows in.

### Changed

- Markup is read by rules: a `TextRule` has a trigger, the characters around the text it takes (wrapped, prefix, line, merging a run of lines, fenced, link, or a pattern), and a role, how what it takes is shown (a span, an element or a block); a `TextRuleSet` holds them in order, and at each place the first that matches, and accepts what it found, takes it. Spans nest, and a literal rule keeps what it wraps from the rest. Markdown is no longer built in: UI Next's built-in Markdown is a set of rules like any other.
- `TextDocument` holds what the rules matched (`TextMatch`: the source range, the text, the argument, the span it is in), read by `TextDocument.Parse`. `TextLayoutInput.Markup` (null for plain text) replaces `RichText` and `Theme`, `TextLayoutInput.Document` lays out a text read already, and `TextLayoutInput.ObjectSizes` sizes elements and blocks.
- Elements and blocks take exactly the room what draws the text gives them: an element stands in its line, on the baseline or centred on the text, and a block has a line of its own, as tall as it is: the engine makes no line taller for what stands in it, so the line break after a block takes the block's height as its line height. A text holding either is measured by laying it out, as it is drawn. `TextLayout.TryGetObjectRect` says where each went, `GetMatchRects` gives a span's pieces of its lines, and `MatchAt` the tappable span under a point, in place of `LinkAt`.
- A colour a span gives its text is its own: `TextLayout.TextColor` marks the text's own colour, which is what a tint colours.
- `TextSettings.DefaultMarkup` replaces `DefaultTheme`; `Generate`, `Measure` and `LongestWord` take no `InlineContext`.
- `TextTrigger` fields show in the inspector only where their kind uses them.

### Fixed

- Wrapped text given an ellipsis but no height broke its words at any character: the engine, told to ellipsize with nothing to cut short, makes lines that way. It is told only where there is a height to cut at, or one line to cut at its width.

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
