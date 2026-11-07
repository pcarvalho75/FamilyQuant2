# Tooltips, design rule 7

Paulo, 11-07: "put informative tooltips as in the program, there are barely tooltips". Rule 7 of the roadmap's
design rules, decision D77.

**Every control explains itself on hover.** Every button, menu item, check box, option, input box, list, tab,
expander and column header has a tooltip of one or two plain sentences: what it does, and what changes (the
project, a file on disk, or nothing; whether Undo reverts it). An input box also says its meaning, unit, default
and allowed range. A disabled control says why and what enables it. A tooltip never only repeats its label, uses
the same terms as the Help and the website, and follows D21: no organism, venom, toxin or sample numbers. No em or
en dashes and no colour words: colour means status, so a tooltip names the status instead.

## How to write one

The look is shared, so a window writes only the text:

- XAML: `ToolTip="..."` on the control. Nothing else: no `ToolTipService` timing, width or colour.
- A DataGrid column header: `theme:TableAppearance.HeaderToolTip="..."` on the column
  (`xmlns:theme="clr-namespace:FamilyQuant.Themes"`). It becomes a setter of the column's header style, which
  the table appearance keeps when it restyles the headers. A column that sets its own `HeaderStyle` keeps a
  `ToolTip` setter in it, or better drops the style and uses the attribute.
- A control built in code: `element.ToolTip = "...";` with the same wording rules.
- A tooltip whose text depends on state, such as the reason a button is disabled, binds to a view-model
  property: `ToolTip="{Binding ApplyHint}"`.

The implicit ToolTip style in `FamilyQuant/Themes/Controls.xaml` wraps at 380 px on the theme tokens
(`SurfaceBrush`, `TextBrush`, `EdgeBrush`, caption size), in dark and light. `FamilyQuant/Themes/ToolTipDefaults.cs`,
applied once by `App`, opens every tooltip after 500 ms, keeps it 20 s and shows it on disabled controls. Local
setters of `ToolTipService.ShowDuration` in code-built styles are older choices and can be removed.

## One worked example per control kind

| Kind | Control | Tooltip |
|---|---|---|
| Button | Check Pfam names (panel editor) | Compares each family's Pfam names with InterPro and lists the differences. The panel is not changed. |
| Menu item | File > Import family list... | Opens the wizard that reads a family list from an Excel workbook. Nothing changes in the project until you confirm its last page. |
| Check box | Use the fills of this column as a category (import) | Turns the cell fill of the chosen column into a category, one name per fill that you type. The fill is never read as a decision. |
| Option | One family per name (a cell listing several families) | Imports each name in the cell as its own family, with keys you can edit. Nothing changes until you confirm the import. |
| Input box with unit, default and range | Header row (panel editor) | The row that holds the column names, counted as in Excel. Default 1; any whole number from 1. |
| List or combo box | Keyword categories (scope counts) | Shows every keyword category, or only Molecular function keywords. Only this table changes; the scope stays as it is. |
| Tab | Panel checklist | For each family of your panel, the database proteins that match it and why any are left outside the proposal. Read only. |
| Expander | Counts behind the suggested scope | Shows the taxa, keywords and reviewed entries the suggested scope was built from. Opening it changes nothing. |
| Column header | Reviewed (family list) | Proteins marked reviewed in the stored UniProt evidence, including unticked members; they are part of Total. |
| Disabled control | Apply grouping, before Update proposal | Unavailable because the scope changed after the last proposal. Choose Update proposal, review the ticks, then Apply grouping. |

Not like this: "Saves the file." on Save (repeats the label), "Click to open the wizard." (says nothing about what
changes), "Red rows are flagged." (a colour word; say "A row with a warning flag").

## Checks

The census (Z12q1) opens every window as the screens harness does and lists, per window, each interactive control
with its tooltip, `NONE`, or `REPEATS-LABEL`. The suite check (Z12q6) fails on `NONE` or `REPEATS-LABEL` and reads
every tooltip for D21, dashes and colour words. Commands are in [INDEX.md](INDEX.md).
