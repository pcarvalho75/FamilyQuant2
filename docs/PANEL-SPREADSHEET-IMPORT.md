# Spreadsheet importer API contract, J1b

Types are in `FamilyQuant.Panels.Import`, under `FamilyQuant.Core/Panel/Import/`.
The interface is UI-neutral so an import screen can use a stand-in before the reader lands.

1. `IPanelSpreadsheetReader.ReadSheetNames(path)` lists the actual workbook tabs.
2. `ReadTable(path, sheetName, headerRowNumber=1)` previews actual headers and raw row text. It
   returns `PanelSpreadsheetTable`, including source name/SHA256, sheet, header row, ordered
   headers and row numbers. `PanelSpreadsheetRow.Cells` maps each actual header to its cell text.
3. Build `FamilyPanelImport.ColumnHeaders` by mapping `PanelImportFields.FamilyKey`, `DisplayName`,
   `Decision` and optionally `Pfam` to the headers the user chose. No source-language header is
   a role key. List the distinct labels in the chosen decision column and ask the user to map each
   to a `FamilyPanelDecisionMapping` (including an explicit family key for a merge).
4. `IFamilyPanelSpreadsheetImporter.Import(table, choices, decisions, panelName)` validates and
   returns a detached FamilyPanel. It preserves every original column as named TextFields.
   The screen applies it through EV's panel transaction/Undo seam. Creating it alone mutates no file.
5. `Replay(path, previousPanel)` requires the recorded source hash and repeats the user's saved
   sheet, column and decision choices. The panel and recipe already persist those choices.

Implementations are `PanelSpreadsheetReader` and `FamilyPanelSpreadsheetImporter`. The reader opens
the workbook read-only from a detached byte snapshot and preserves shared/inline rich text, accents,
line breaks and merged-cell text. Duplicate headers, cell errors and formulas without cached results
are reported plainly. Numeric cached values retain their invariant workbook representation; formulas
are not evaluated and Excel visual number formatting is not recreated.

The importer resolves every role through the chosen header. It records HeaderRowNumber, source hash,
sheet, headers and all source decision mappings in FamilyPanel.Import / DecisionMappings. It requires
an explicit decision mapping for every observed label and retains excluded and merged rows in the
panel. Required headers, duplicate keys, missing merge targets and cycles are checked before return;
EV's FamilyPanelValidation also validates the result. Import alone changes no open document.

Pfam accessions are read by their general accession syntax. An explicitly paired `name (accession)`
keeps its source name for EV's InterPro check; a bare accession gets no invented name. Every complete
source cell remains in TextFields, including any unparsed domain-name claims and subclass prose.
Source text is never interpreted as a subclass architecture or an abundance measurement.

Checks: `--test-panel-spreadsheet` covers header selection, shared/rich/merged text, decision/merge
validation, immutable input, culture stability and project/recipe replay. The native check
`--panel-spreadsheet-live <workbook> <panel-choice-json> <new-output-dir> <expected-rows>` copies the
workbook into its own output directory, runs the production importer, saves panel.fqproj and replays
the stored recipe's choices. The JSON is a FamilyPanel containing Name, Import and DecisionMappings;
it is user data. No family list, source header or decision label is built into the importer.

J1b validation: Release had zero warnings/errors; focused checks passed 13/0/0. The native check
read a copy of Ana's actual workbook with explicit D35 choices from
`D:/FamilyQuant-data/out-assay/j1b-choices.json`: all 41 rows and all 615 original cell values stayed
exact, the saved panel/recipe reopened and replayed, and both workbooks retained their SHA256
(5/0/0). Artifacts are at `D:/FamilyQuant-data/out-assay/j1b-live-02/`.
The importer reads the selected family table. It retains subclass prose as source text; translating
that prose or other tabs into executable subclass definitions belongs to explicit panel editing.

## Graceful import, Z12p1

Z12p1 lets a consensus sheet be imported as it is. The wizard (Z12p2b) uses these calls; the J1b calls
above keep working and replay panels imported before unchanged.

1. `IPanelSheetPreviewReader.ReadPreview(path, sheet = "")` returns every tab (hidden ones listed
   separately), a dense grid of the chosen sheet with each cell's text, direct fill colour (#RRGGBB
   and a plain colour word for screen readers), merged range, a proposed header row with a reason
   sentence, and problems. It never throws for an unreadable file. The header row is proposed from
   Excel's filter buttons, then an Excel table, then frozen rows, then the first row with several
   different labels above rows of data. The file is read with sharing, so a workbook open in Excel
   can be read.
2. `ReadTable(path, sheet, headerRow)` now also returns column letters, the empty rows between data
   rows, each row's fills and merged ranges, and notes instead of exceptions: a populated column
   without a name becomes "Column G", a repeated name gets " (column C)", a formula without a saved
   result is read as empty and an Excel error is read as its text. Only an unreadable file or a
   missing sheet still throws.
3. `Propose(table)` proposes roles from the shape of the values, never from header words: short
   distinct codes for the abbreviation, the first and second columns of names for the name and the
   other-language name, a column mostly holding Pfam accessions for Pfam, and the remaining column
   with the longest text for notes. Decision and colour category are never proposed. Its choices carry
   `ImportVersion = PanelImportRules.Current`.
4. `ProposeDecisions(table, choices)` proposes one meaning per distinct decision label with its rows
   and a reason: Merge into a family when the label has a merging word and names exactly one
   abbreviation of the table, Not a family when it has an excluding word, otherwise Quantify. The
   words are a short English, Portuguese and Spanish list in the source, compared without accents or
   case. They only propose; the user confirms each label and the stored mapping is the user's.
5. `Check(table, choices, decisions, name)` never throws for data. Every problem is a plain sentence
   naming the row and the column, with a kind, a severity (Note or Blocking), the value concerned and a
   suggestion where there is one. It also returns each row's outcome, the decision labels, the colours
   of the colour-category column and the panel when nothing blocks. `Import` is `Check` and throws the
   first blocking sentence.

Rules of a graceful import (version 1): the decision column is optional, and without it every family
is Quantify; an empty decision cell is Quantify with a note. Keys and names are trimmed; every original
cell stays verbatim in TextFields. A row that repeats the column names, or whose text is one merged
cell across columns like a section title, is skipped with a note and can be kept. A repeated
abbreviation blocks on the later row, names the earlier row and suggests a rename such as HYAL-2; the
answer is a rename, a merge into the earlier family or a skip. A row with an empty abbreviation or name
blocks with a suggestion. The answers are stored as `FamilyPanelImport.RowChoices` by row number.

Colours are read only from direct cell fills: an explicit colour, the workbook theme with its tint, or
the indexed palette. Conditional formatting, table styles, gradients and row or column styles are not
read, and a tinted theme colour can differ from Excel's display by one unit. A colour becomes a
category only when the user chooses a colour-category column and names every colour in it
(`ColorLabels`); the family then gets a text field (`ColorCategoryName`, by default the header plus
" colour") with that name. The colour itself is never interpreted.

`ImportVersion`, `RowChoices`, `ColorLabels` and `ColorCategoryName` are omitted from JSON when unset,
so a panel imported before keeps its exact JSON and version 0 replays the J1b rules. The other-language
name is stored in `FamilyPanelFamily.Names` under the actual header; `PanelImportFields.OtherNameText`
reads it. The program does not know which language a column is. A family key is the trimmed
abbreviation cell or the user's stored rename, and Replay of the same file reproduces every key.

Checks: `--test-panel-import-graceful` builds a synthetic workbook shaped like a consensus sheet
(title row, accented headers, filter, coloured abbreviations, section title, empty and repeated header
rows, a repeated abbreviation, free-text decision labels including a merge, formula and error cells,
extra and hidden sheets) and covers each rule above, replay, project and recipe round trips, culture
stability and the unchanged workbook. A read-only probe of a copy of the real workbook, not kept,
found its header row from the filter, proposed the expected columns and imported all 41 rows with no
blocking problem, with and without the proposed decision meanings.

## Rows listing several families, Z12p3

Under the graceful rules, a name or other-language name cell that lists several names is returned as such.
Names are separated by commas, semicolons and line breaks outside parentheses or brackets, and a leading
bullet is dropped. The row outcome names the list column (`ListColumn`) and the proposed families
(`ProposedParts`), each keyed from the row's abbreviation plus the name, for example "Outras raras:
TCTP", made unique against every abbreviation of the table. A note offers the two choices; until one is
made the row stays one family named as written, so a single name with a comma costs nothing.

- `Split` creates one family per name, from the choice's `Parts` (keys and names the user edited) or,
  when empty, from the proposed parts. Each family keeps the row's decision, colour category and every
  original cell. An other-language name is kept only when that column splits into the same number of
  names. The row's Pfam text stays text only, with a note, because it cannot be told apart per family.
  A repeated, empty abbreviation or empty name in a part blocks with a sentence naming the row, the
  column and the part. Splitting a row that lists one name asks for the families instead of guessing.
- `ReportUnderOthers` keeps the row as one family with that decision, overriding the mapped label.

Both choices are stored in `RowChoices` and replay with the panel. Version 0 choices look for no lists.
Checks: `--test-panel-import-several`. The read-only probe of the real workbook copy flagged exactly one
row, the row of rare families with nine names in its English column, and no other row.

## Family-list template, Z12p2d

`PanelImportTemplate.Write(path)` writes an empty family list for the user to fill in: a Families sheet
with the recommended columns Abbreviation, Name, Other-language name, Notes, Pfam domains and Decision
(filter buttons, a frozen header row, a Decision drop-down that offers Quantify, Report under others and
Not a family and still accepts other text), then an Instructions sheet explaining every column, the
decision names, rows listing several families, colours, extra columns and saving, with example rows.
A hidden defined name marks the workbook; `ReadTable` and `ReadPreview` report it as `IsTemplate`.

For a marked table, `Propose` maps every template column present to its role and returns the decision
mappings for the program's own decision names, so a filled template imports with no question. The
program's decision names (Quantify, Report under others, Not a family, and "Merge into" followed by an
abbreviation of the table) are recognised in any sheet, without regard to case or surrounding spaces,
and `PanelDecisionProposal.Exact` says so; any other label is still proposed for the user to confirm.
An empty Decision cell is Quantify, as in every graceful import. The wizard and the File menu (FQHome)
call `Write`; the template is not the J1e panel spreadsheet (`FamilyPanelTemplate`), which saves an
existing panel with its hidden snapshot.

Checks: `--test-panel-import-template` writes the template, checks its sheets, marker, header proposal,
instructions and drop-down, validates it with the Open XML schema validator, fills it as a user would,
imports it with no mapping, replays it and compares its content across cultures. The file has not been
opened in Excel itself on this machine.
