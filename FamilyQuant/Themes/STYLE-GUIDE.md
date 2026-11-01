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

Keep one short explanation line visible. Put detailed instructions and every
displaced fact in Help or a tooltip. Name a command by what it does and explain
disabled state in its tooltip. Do not add colour to plain values.

Existing screens retain their commands and facts until their scheduled rebuild.
Each rebuilt screen adopts these roles, tokens and copy rules, and must render in
both modes at the two harness viewport sizes before review.
