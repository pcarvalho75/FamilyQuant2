# Report supplement workbook

`ReportSupplementWorkbook.BuildBytes(document, sheets, selection)` writes the companion
workbook from RS's `ReportSupplementSheet(Id, OwnerSectionId, Name, Table)` model. The
document builder supplies full native CSV tables and exact citation names. The writer
validates those names and never renames them silently or recalculates any quantity.

The workbook starts with a Contents index listing only selected detail sheets. Excluding
an owning section or a table id omits that sheet. If no detail remains, Contents has only
its header. A valid workbook is still produced without fabricated result rows.

Every source cell is literal text, including numeric lexemes, empty availability cells,
leading zeros, formula-looking values, carriage returns and SpreadsheetML escape-like
identifiers. Header/data cells occupy the same rows and columns as the source CSV; no
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
