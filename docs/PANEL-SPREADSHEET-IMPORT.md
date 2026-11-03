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

This commit is types/interfaces only. Implementations will be `PanelSpreadsheetReader` and
`FamilyPanelSpreadsheetImporter`. No family list, source header or decision label is built in.
