# Shared screen rules

Use `PageTitle`, `SectionTitle`, `BodyText` and `CaptionText` for text roles.
Their sizes, line heights and font family come from Tokens.xaml. Standard controls
use the same control padding, margin and section padding tokens in both modes.

Use sentence case for headings and commands. Keep product names and acronyms
such as PatternLab, UniProt, XIC, CSV and ID intact. Status labels are data supplied
by the owning presentation model; do not rewrite scientific names or values.

Each panel has at most one `PrimaryButton`. Other actions use the quiet default
Fluent button. A read-only panel can have none. The shared styles use native
Fluent interaction and focus templates; they add no group outline or emoji.

Use the `FluentIcon` text style for icons, with a private-use glyph from the
Windows Segoe Fluent Icons font. Keep a plain text label beside the icon. The
suite rejects emoji in product C#/XAML, including Unicode escapes and entities.

Colours in application source come from Tokens.xaml or ChartPalette. The V3
guard rejects direct hex values, named brushes/colours and fixed RGB factories.
Per the orchestrator's V3 ruling, PresentationFigures.cs is its one explicitly
listed exception: those fixed colours draw exports and rule 9 protects the deck's
feeders. The guard prints that exception; it is not a hidden UI exemption.

Keep one short explanation line visible. Put detailed instructions and every
displaced fact in Help or a tooltip. Name a command by what it does and explain
disabled state in its tooltip. Do not add colour to plain values.

No user-visible text may name an organism, a venom or a toxin. Use general
examples that apply to any database. Names loaded from the user's database are
data, and remain as supplied. Comments and protected figure producers stay intact.
Product-authored Help, tooltips, status messages and export headers must not quote
historical sample counts. Show the loaded data's counts and selected cutoffs instead.

Existing screens retain their commands and facts until their scheduled rebuild.
Each rebuilt screen adopts these roles, tokens and copy rules, and must render in
both modes at the two harness viewport sizes before review.
