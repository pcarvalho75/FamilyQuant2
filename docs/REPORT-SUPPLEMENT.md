# Report supplement workbook

`ReportSupplementWorkbook.BuildBytes(document, sheets, selection)` writes the companion
workbook from RS's `ReportSupplementSheet(Id, OwnerSectionId, Name, Table)` model. The
document builder supplies full native CSV tables and exact citation names. The writer
validates those names and never renames them silently or recalculates any quantity.
`ReportTableSupplements.Complete` appends a sheet for each printed table that has no
sheet yet. The exporter calls it after existing detail and caption-trace sheets; the
writer also calls it for direct Core callers. Existing sheets retain their names,
relative order, headers and cells.

The workbook starts with a Contents index listing only selected detail sheets. Excluding
an owning section or a table id omits that sheet. If no detail remains, Contents has only
its header. A valid workbook is still produced without fabricated result rows.

Every source cell is literal text, including numeric lexemes, empty availability cells,
leading zeros, formula-looking values, carriage returns and SpreadsheetML escape-like
identifiers. Existing CSV sheets occupy the same rows and columns as the source CSV; no
title or explanatory rows are inserted. Sheets have Arial text, a bold frozen header,
filters, wrapping and bounded column widths. There are no formulas to recalculate.

Worksheet data is streamed. Package entries have stable order, relationship ids and ZIP
timestamps, so the same model produces the same bytes under different cultures and clocks.
Unsupported dimensions or cell text are refused without truncation. The limits follow
[Microsoft's Excel specifications](https://support.microsoft.com/en-us/excel/excel-specifications-and-limits?facet1=marketing)
and [worksheet-name rules](https://support.microsoft.com/en-gb/excel/rename-a-worksheet).

`PathForReport(reportPath)` returns `<report>_supplement.xlsx` beside the Word document.
`Write` stages one workbook and optionally publishes it through an operation context.
When publishing Word and workbook together, the shell stages both first and uses
`ReportArtifactPublication` for one commit. Cancellation or IO failure between renames
restores the old pair or removes the new pair. Recovery copies remain identifiable if
restoration itself fails; their staging directory must then be retained.

Focused commands after a Release build, with the lane-specific TEMP from INDEX.md:

```powershell
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-supplement <fresh-output>
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-supplement-excel <generated-output>
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-publication
```

The first command builds labelled synthetic tables from native program CSVs and checks
all cells, schema, exact selection and culture-independent bytes. The second opens both
generated variants in a private hidden Excel instance, reads its actual cell values,
closes it and verifies the files were unchanged. The third exercises pair publication,
including Cancel after each rename, previous-file restoration and recovery retention.
These fixture checks are not fresh RAW or biological validation.

## Printed tables and stored precision

Short tables kept in Word now have their own worksheets, including project/database
counts, scientific inputs, search/extraction rules, de novo sources/cutoffs, local
confidence gates and stored family-study rules. Contents retains each table's stable
block id, owning section and worksheet name. The same section/table exclusions apply.

New table sheets state the Word formatting rule in the header row. Method cells use
the exact stored invariant text. Where the report displays percentages with six
significant digits, the primary sheet cells keep unrounded percentages in the same
units. Additional columns retain the exact stored fraction/ratio and the Word display
text; their headers explain multiplication by 100 and six-significant-digit formatting.
These values come from native round-trip columns, never from reparsing rounded Word
cells. A missing source column or disagreement with Word is refused. Missing values
remain empty or explicitly unavailable, never zero. Existing CSV exports are unchanged.
The additive `report_settings.csv` also records the report-family threshold used in
the prose: stored fraction, unrounded percentage and the exact six-significant-digit
Word display. Its native export sheet lets the number trace cover that setting too.

The method worksheet names are stable:

| Table id | Worksheet |
|---|---|
| `methods-project` | `Project and database_442FA9` |
| `methods-inputs` | `Scientific inputs_0C3F74` |
| `methods-rules` | `Search and quantitation _E85762` |
| `methods-denovo` | `De novo sources and cuto_68F9D2` |
| `methods-denovo-gates` | `De novo length and local_05C6C4` |
| `methods-stored-studies` | `Family study rules_E1E305` |

`--test-report-table-sheets <fresh-output>` exercises actual Word/workbook table
coverage, exact method cells, raw precision, header guidance, selections, refusal
boundaries and culture-invariant bytes. It also runs once in DefaultSuite.
