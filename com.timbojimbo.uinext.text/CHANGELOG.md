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
