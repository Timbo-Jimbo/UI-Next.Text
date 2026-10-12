# Timbo Jimbo - UI Next Text

Text for [UI Next](https://github.com/Timbo-Jimbo/UI-Next), laid out by Unity's Advanced Text Generator (ATG), the engine behind UI Toolkit's advanced text: HarfBuzz shaping, ICU line breaking and bidi, OS font fallback and colour emoji. No native plugins, no bundled fonts: every script the OS ships a font for renders out of the box. UI Next's `Label` and the text fields of [UI Next Text Input](https://github.com/Timbo-Jimbo/UI-Next.TextInput) draw what it lays out.

It is UI Text's engine, moved over to UI Next: the same layout, fonts, markup and queries, with nothing of UGUI or the old UI package left in it.

Requires Unity 6000.5 or newer, mobile or desktop.

## Installation

Open **Window > Package Manager**, click Add, choose **Add package from git URL...** and paste `https://github.com/Timbo-Jimbo/UI-Next.Text.git?path=com.timbojimbo.uinext.text#0.1.0`. UI Next depends on it, so add it before UI Next.

## How it works

The generator's managed API is internal to Unity. The small `Unity.TextCore.FontEngine.Tools` assembly in `Runtime/Bridge` borrows a name the TextCore module lists in its `InternalsVisibleTo` attributes and wraps the generator behind a public API. UI Text took another name on that list, so the two packages can be in one project while UI Text is retired. Nothing else touches Unity internals, so a Unity release that moves things only ever breaks the bridge. This is unsupported by Unity; expect small patches per Unity version. `Tests/BridgeAssumptionTests` pins down the engine behaviours the package depends on, so a change shows up there first.

Above the bridge, `TextLayout` does the work without knowing about GameObjects: it parses the source, builds the text the engine lays out, generates, and answers questions about the result. What draws the text (UI Next's `Label`) gives it a rect, and draws its glyphs, its underline and strikethrough segments (`Decorations`) and what its inline handlers ask for (`CollectPlacements`), and asks it which link is under a point (`LinkAt`).

## Usage

**Fonts.** Every font reference is a `FontStack` (**Create > Timbo Jimbo > UI Next > Font Stack**): an ordered list of installed OS families, TTF/OTF files from the project, or dynamic font assets. The first source that resolves is the primary font, so a stack of Cascadia Mono, Menlo, DejaVu Sans Mono and Courier New picks the first one the device has; the rest fill in missing glyphs. With **OS Fallbacks** on, the fonts the OS names for missing glyphs come after the stack, which is how emoji and other scripts render from your own Latin font; off makes the stack self-contained. Leave a text's font empty for the project default (see Settings), and without one the OS default font, which is a stack with no sources. Static (pre-baked) font assets are not supported by the generator.

**Bold and italic.** An OS family is probed for its Bold, Italic and Bold Italic faces and a font file can name its variant files, so bold text uses a real face wherever one exists. Without one the engine synthesises bold by thickening the glyphs, and each source's Synthetic Bold slider (0 to 1, Unity's default 0.75) sets how much. Italic without a real face is a shear.

**Settings.** **Edit > Project Settings > Timbo Jimbo > UI Next Text** holds the project defaults: the font stack and theme texts fall back to. Create the asset from that page (it can live anywhere under Assets) or pick an existing one; a project with none registered takes the first it finds. The choice is stored in `ProjectSettings/EditorBuildSettings.asset` and builds preload the asset, so no Resources folder is involved. Edits apply live: `TextSettings.Changed` tells whatever draws text to lay it out again.

**Markup.** With rich text on, a small Markdown dialect applies: `**bold**`, `*italic*`, `__underline__`, `~~strike~~`, `` `code` ``, `[label](url)`, bare URLs, `\` escapes, `#` headings, `-`/`1.` lists, `>` quotes and ``` fenced code blocks. A newline is a line break. No HTML, images or tables, so it is safe for user-generated text. The parser records only source indices; `LayoutText` builds what the engine lays out and keeps the mapping both ways, so a caret or a selection can move between the source and the layout. A `TextTheme` asset (**Create > Timbo Jimbo > UI Next > Text Theme**) sets heading sizes, link and quote colours, the code font stack and the inline handlers.

**Inline content.** `InlineHandler` assets on a theme extend the markup with a trigger (`:emote:`, `@name`), a placement and what to put there. `Fragments` handlers style the text and can put something behind each line it spans; `Block` handlers put one thing across the full width of every line; `Atomic` handlers replace the token with an unbreakable box something sits over, sized in em and centred on the text unless the request says how much of it sits above the baseline. A handler can ask for layout padding: the text either side moves over, as CSS padding on an inline element does, and a span that wraps keeps it at its start and end only.

- `InlineBackground` is a background drawn by what draws the text, with no prefab: a fill, a border and round corners. The built-in ones are behind `code` spans and fenced code blocks unless the theme has handlers of its own for them; create one with a trigger to put a background behind text of your own.
- `PrefabInlineHandler` needs no code: its prefab (a UI Next node) is placed over its token. Subclass `InlineHandler` when the size or the acceptance depends on the token, as an emote set that declines unknown names or a mention chip measured to its text does. A prefab's root that implements `IInlineContent` is told the token it stands for.

**Direction.** A text's direction is the way its paragraphs read: left to right, right to left, or `Auto`, the way the text's first strong character (a letter) reads, as HTML's `dir="auto"` decides it. Right-to-left runs inside a left-to-right paragraph (and the other way about) shape and order correctly either way. `Auto` is for text people write, such as chat messages and text fields: Arabic or Hebrew reads right to left, with its punctuation at the right end. `AutoRightToLeft` is the same, but right to left while the text has no letter (an empty field whose keyboard writes right to left). Natural alignment makes an alignment's left and right the text's start and end, as UIKit's natural alignment and CSS's `text-align: start` do. `TextLayout.ReadsRightToLeft` says which way a plain text would be laid out.

**Carets and lines.** A `TextLayout` says where a caret goes (`GetCaretRect`), which position a point is nearest (`GetIndexAt`), how many lines there are and which one an index is on, in source indices and layout pixels: what a text field, a custom caret or a selection is drawn from. An index where a line wraps both ends that line and starts the next, taken at the end of the line when asked for upstream, as a caret's affinity does in UIKit and Flutter. Where text reads both ways, carets follow Core Text's primary caret, as UIKit's text views draw it; `IsCharacterRightToLeft`, `GetCharacterEdgeRect`, `GetIndexAtEdge` and `GetVisualNeighbour` give what editing such text needs.

## Known limits

- Flags render as letters on Windows because its emoji font has no flag glyphs; supply an emoji font in the stack for consistency.
- A padded inline span longer than a whole line (a long URL in backticks) has its leading padding wrapped onto a line of its own, which shows as an empty line before it.
- One direction for the whole text: `Auto` takes the first letter of the whole text, as HTML's `dir="auto"` on an input does, where iOS and Android text views decide each paragraph by its own.
- Inline objects (atomic handlers) stand in a private-use character, which the engine's bidi takes for a left-to-right letter: several of them in a row inside right-to-left text are ordered left to right. Underlines, strikethroughs and fragment backgrounds use the engine's own boxes for a line's trailing spaces.
- Inside a ligature (Arabic lam-alef) there is no caret position a point or an arrow key finds; deleting and the logical moves still reach it.
