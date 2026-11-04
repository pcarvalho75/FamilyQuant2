# Report number trace

Database-entry fields use `database-entry-count` references to the stored curation collection.
The resolver calls the read-only D55 core count view once per trace. It checks canonical UserID
or effective panel key, invariant integer text, raw count, empty unit, Scale 1 and empty unrelated
locator/format slots. Headline association uses the same `ReportCompositionBasis`, including an
unkeyed collapsed slot's single source UserID; slot IDs are never parsed as source families.
Unavailable/conflicted populations, Others, dangling count IDs and mismatched values-map entries
are rejected. Reading the trace runs no Compute/search and changes no curation or saved studies.

G1d executed evidence is in `D:/FamilyQuant-data/out-results/G1d/native/`: `proof.json`,
`html-trace.csv` and `stored-export.log`. The guarded native export read the paired copied
project in `G1d/inputs-213` (SHA-256 `3D9DDBBACCE4E62D0DAC4EC9F66DC34B08566B6E02B74E5D17973B6917906081`).
It recorded both required cache hits, 4,414 bound fields, 6,437 trace rows and zero NOT FOUND.
Fourteen database count fields resolve to their stored populations; Others has no count.
All 15 headline labels and means equal the copied delivered Word report. Input hashes and
the stored snapshot stayed unchanged, and HTML preserved the native Word/workbook bytes.
All 4,395 pre-existing HTML fields also compare identical to the paired baseline.
These artifacts prove the added ledger contract; they do not alter frozen FQ15 receipts.

`--trace-report-numbers <report.docx> <sidecar-folder> <output.csv>` reads the report and its
native CSVs/companion workbook without writing those inputs. This is an audit aid; it does
not calculate or validate scientific results.

After a Release build:

```powershell
FamilyQuant.Tests.exe --trace-report-numbers <report.docx> <report_data_folder> <output.csv>
```

The command reads the Word body, table cells, captions, headers and footers. Each row
records a numeric token or classified identifier, its document location, full paragraph
context, status and selected filename/sheet and one-based cell. CSVs are searched
recursively; the sibling `<report>_supplement.xlsx` alone suffices if the CSV folder
is absent. Quoted and multiline CSV fields are supported. Output cannot overwrite
the report, workbook or a source CSV. Missing inputs/exports or malformed CSV are errors.

Every equal numeric cell remains a candidate. A citation prefers the named subject in the
sentence/clause or the whole printed table row. Labels come from source rows, including
family labels, run names and option names. Function words are ignored when comparing labels;
complete single-word labels and multiword label prefixes are usable. The closest preceding
label wins, followed by the longest label at that position. Sentence boundaries and semicolons
outside parentheses separate subjects, preserving a family name for its parenthesized counts.
Colons preserve option names. Equivalent cells for the same named subject keep stable source
order. The `subject` and `candidate_count` columns expose the selected label and alternatives.
The printed column heading breaks ties between equal fields for one subject. A number inside
a recorded subject name is ambiguous rather than being cited as another subject's count.

Without a named subject, a single equal source row/column (including identical repeated views)
can be cited. Several unrelated rows give `AMBIGUOUS`, with empty `csv` and `cell` fields.
This also prevents a cover version from being cited to an unrelated protein estimate.
Percentages consider both equal percentage cells and explicitly labelled fraction columns,
with the same subject preference across both representations.
Equality remains invariant and exact across digit grouping, trailing zeros and scientific
notation, never tolerance or substring matching. Percentage conversion is recorded in
`match_kind`; directly equal percentages exclude fraction columns. A list-sheet `Source
data row` pointer is usable only when the report explicitly names that structural field.
If the named subject has no equal candidate, another subject's equal value is ambiguous.

The generated caption sentence stating table dimensions is checked against the immediately
preceding Word table. Correct counts receive `TABLE ROWS`/`TABLE COLUMNS`; wrong counts or
a missing table give `NOT FOUND`, even when the same count occurs elsewhere in an export.

`NOT FOUND`, date/hash/identifier/year/run/page classifications, own-table dimension checks
and the exit code retain their prior meaning. Exit code 1 means a missing numeric token;
ambiguities are separate open audit items even if exit code is 0. J36 prints and records both
counts. Matching a subject is contextual evidence, not a guarantee that a sentence makes a
scientifically valid claim or uses the right denominator.
Explicit-context years, ISO dates, page numbers, run indices, SHA-256 fingerprints and
alphanumeric identifiers retain their classifications. Other values, including method
parameters and rounded captions, may remain `NOT FOUND`: inspect the context before
adding a missing program export or correcting the report. The trace reads no labels
drawn into PNGs and does not modify the report, calculate chart data or reproduce the analysis.

Append `--compare <before.csv>` to produce `<output>.changes.csv`, `<output>.ambiguous.csv`
and `<output>.comparison.json` beside the revised trace. The native C# harness lists every
changed status/source/cell and every ambiguous row, including original CSV row numbers and
full context. It refuses misaligned token identities and input/output collisions. The JSON
records both input trace hashes. Comparison does not change the missing-number exit status.

For the offline HTML companion, use `--trace-html-report-numbers <report.html>
<native-source-folder> <copied-project.fqproj> <output.csv>`. The existing
`--trace-report-numbers` command also accepts HTML and discovers a sibling `project.fqproj`.
The HTML route retains the same trace CSV columns and numeric tokenizer. It checks every
payload field, including pop-ups and short labels, against its explicit Word, workbook or
stored-project reference. It never searches for a coincidentally equal number elsewhere.
Stored references resolve persisted writable properties only. Scale and format must reproduce
the display text; short labels use the unrounded source value. Exact paragraph text and the
zero-based occurrence among identical paragraphs locate `word-text` references in Word.
Coverage endpoints and sequence lengths must equal the stored values; fallback PNG bytes
must equal the corresponding Word figure. Missing IDs, invalid references and mismatched
text or geometry produce `NOT FOUND` and exit code 1. No input package is rewritten.
Run-name short fields use `run-suffix`: their value stays null, their source must equal the
full-name source, and their text must be its suffix. Leading zeros remain text. Database gap
short labels use G3 while retaining the native fraction unit and scale.
Dot-plot axis labels and ticks use the shared `HtmlReportAxisTicks.TryResolve` contract.
Their text, geometry value and unit must match its result. Every `dotMeasures` label,
tick and per-family value ID is checked; each missing ID retains its own trace error.
Axis ticks use `axis-plain`: exact plain integer/decimal labels from their recipe, with raw
geometry retained. Scientific notation and binary rounding noise are rejected; measured-value
G3 short labels keep their existing format.
`--test-html-number-trace` runs positive and negative fixtures and is enrolled in the suite.
