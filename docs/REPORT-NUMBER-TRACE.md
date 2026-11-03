# Report number trace

After a Release build, run the test-side audit against a report and the CSV folder
written for that report:

```powershell
FamilyQuant.Tests.exe --trace-report-numbers <report.docx> <report_data_folder> <output.csv>
```

The command reads the Word body, table cells, captions, headers and footers. It emits
one row for each numeric token or classified identifier, with its document location,
full paragraph context, status and, for a match, the CSV filename or workbook sheet
and one-based spreadsheet cell. The CSV folder is searched recursively; the sibling
`<report>_supplement.xlsx` is also read when present. A workbook alone is sufficient
if the CSV folder is absent. Quoted and multiline CSV fields are supported. The
output cannot overwrite the report, workbook or a source CSV. The command exits with
status 1 if any number is `NOT FOUND`; a missing report, missing source exports or
malformed CSV is an error.

`MATCH` means numeric equality with a complete numeric data cell in a source export. The
comparison is invariant, exact and independent of digit grouping, trailing zeros or
scientific notation. A percentage can match a directly equal number outside a column
named `fraction`. If it matches a value divided by 100 in a fraction column, the row
records that conversion in `match_kind`. Values are never matched by a tolerance or
by a substring of a larger cell. If several cells contain the same number, the first
in sorted filename and row order is reported.

The generated caption sentence stating a table's row and column counts is checked
against the immediately preceding Word table. Its numbers are labeled `TABLE ROWS`
and `TABLE COLUMNS` when correct, or `NOT FOUND` when the table is missing or the
count differs. They are never cleared by an unrelated source cell.

Years in explicit year contexts, ISO dates, page numbers, run indices, SHA-256
fingerprints and alphanumeric identifiers receive a classification instead of a
numeric match. Other values, including method parameters, table dimensions and
rounded figure captions, are checked and can appear as `NOT FOUND` if the exports do
not contain those exact values. Check the context before deciding whether an unmatched
value needs an additional program export or a correction to the report.

A numerical match anywhere in the source exports is a candidate, not proof that the
particular table or figure used that cell. Common numbers can match coincidentally.
Review the named source and the report context, especially for scientific claims.
The command audits text in the Word package; it does not read labels drawn into PNG
figures, calculate chart data, update the report or reproduce the analysis.
