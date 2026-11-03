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
