# Interactive HTML report

The HTML is a graphical front door to the selected Word report and companion workbook.
It uses the same prepared document and stored structured tables; the writer runs no analysis.

## Payload contract, version 1

The template is owned by FQLook at `FamilyQuant.Core/Report/Html/report-template.html`.
The writer owns the C# payload and embeds the template as `FamilyQuant.Report.Html.Template`.
The template must contain exactly one literal insertion point:

```html
<script id="fq-data" type="application/json"></script>
```

The writer fills that element using System.Text.Json's safe default encoder. User text,
including a literal closing script tag, never becomes markup. The script reads the element's
textContent and parses JSON. No fetch, libraries, web fonts, network requests or AI calls.

The schema is `familyquant-report-html/v1`; C# types are in `HtmlReportPayload.cs`.
JSON uses camelCase. `fields` is an object keyed by stable field IDs. Each field has `text`
(final display string), nullable `value` (geometry only), `unit`, and a private trace `source`.
The page prints `text` verbatim and may append the nonnumeric unit; it must never print a
formatted raw value, computed total, array length, percentage or counter. Every rendered field
must carry `data-fq-field="<id>"`, including SVG text, hover cards and dialog content. Use textContent
or DOM text nodes for all data. Labels containing digits are fields too.

`title`, `authors`, `interpretation`, all family/run/part names, and metric properties are field
IDs, not display text. Missing IDs or null values mean unavailable; they never imply zero.
`composition.families` has name, mean, sd, cv, wholeMeasured, proteins, uniqueProteins, peptides,
isOthers, runs, parts and coverage. Run entries have name/share/measured field IDs. Parts have a
fixed kind and name/share field IDs; their percentages are pooled within-family values, not
mean per-run shares. The page must state that basis when drilling. IsAdditive=false forbids a
normalized composition view. Canonical Others is one aggregate; its hidden names are not embedded.

`creditedRuns` shows the existing credited-family share of whole measured signal by run.
`databaseGap` contains native fraction values (unit `fraction`, not a newly calculated percentage).
`highlights` contains name/value/nullable description field IDs, only from project-level
values already printed in the report summary. No total is calculated from family rows.
`methods` contains field IDs for each step title and fact name/value. `quality`,
`sensitivity` and family `coverage` may retain original Word PNGs where a live plot is unavailable.
Figures have title/caption field IDs, a data:image/png;base64 URI and sourceFigureId; mark the image
with data-fq-figure. Source identity covers their drawn labels without introducing OCR results.

Family `coverage` entries also expose optional live geometry: protein/lengthLabel field IDs,
length (geometry only), peptides/domains arrays of {start,end,label,kind,range|null}. Coordinates
are one-based inclusive; label/range are field IDs, never formatted by the page. Kind is an
evidence role such as ExactContainment/Aligned or domain/feature. Sequence lengths and peptide
intervals are present in the saved localization models; verified domain fragments come from
the same stored position evidence as Word. The producer exposes the same prepared coverage
data, including gaps between fragments, and does not fill unavailable positions. Png is null
when a live strip is available; otherwise the original Word PNG remains. No displayed endpoint,
length, interval total or coverage percentage may be computed by page script.

Only selected section/block data enter the payload. Family-section exclusions remove that
section's details; headline values may still appear when their Results table is selected,
exactly as in Word. Word/workbook links are escaped relative filenames in `links`.

Source references use `word-cell`, `workbook-cell`, `word-property`, `word-text`, `figure-title`
or `figure-caption`. Table/row/column are one-based and include the header row. A workbook source
may carry G6 formatting and a presentation scale; these are checked against the native cell.
The number trace will dispatch on HTML and verify these bindings against the sibling Word and
workbook, using the existing numeric-token/classification machinery.
