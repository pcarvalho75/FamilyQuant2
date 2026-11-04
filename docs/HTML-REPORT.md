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

D47 adds optional `short` to a field: it is another field ID, with G3 display text rounded
by C# from the native value. Charts print that short field; pop-ups print the original exact
field. The short field has its own checked source/format, avoiding double rounding from a
Word display when a full-precision workbook or stored-study value exists. Both are traced.
Run-name fields also have a short field ID: a shortest distinct natural-token ending, retaining
leading zeros. Its value is null (a label), and format `run-suffix` binds it to the complete run
name source cell. The full name remains the exact pop-up field. Shared run labels reuse field IDs.
Methods and quality facts remain empty under D47; the page is plots, labels and short info.

`dotMeasures` is an optional array for the single configurable evidence dot plot. Each entry
has `{id,label,unit,scale,ticks,values}`. `label` and every item of `ticks` are ordinary field IDs;
`values` maps a family ID to its EXISTING exact field ID, preserving the same short/exact popup
pair. `scale` is `log` for mean share and counts, `linear` for SD/CV. No sums, ratios, fits or
new scientific observations are introduced. The browser uses raw values only for geometry and
prints the supplied strings. The available IDs, in stable order, are `mean`, `peptides`,
`proteins`, `uniqueProteins`, `sd`, `cv`; omit an entry with no available family values.
Canonical Others is not an individual dot. Missing, negative or nonfinite values are absent;
recorded zero remains available and must not be described as missing, even on a log axis.

Program-made log ticks are powers of ten bracketing the positive range, with one extra upper
decade for a single exact-decade value. An all-zero log measure has ticks 1 and 10, while its
zero-valued families retain their exact values for edge rendering/tooltips. Linear ticks start
at zero with a power-of-ten step through the upper range. At floating-point extremes only
finite representable ticks are emitted; the plot must also include its actual value extents.
All tick strings are plain invariant C# decimal text: count ticks print `1000`, not scientific
notation, and linear steps print `0.03` without binary rounding noise. Trailing fractional zeros
are removed; tiny nonzero ticks never round to zero. Measured-value short fields remain G3.
Raw tick values use the measure's units (share is percent).

`axis-label` and `axis-tick` references are explicit presentation constants, not Word cells or
stored observations. `HtmlReportAxisTicks.TryResolve` is their shared strict native validator.
A label's Property is the measure ID, Format is null, Scale is 1. A tick's Property is
`<id>/log10/<exponent>` or `<id>/linear/<exponent>/<multiple>` (multiple 0 through 10), Format is
`axis-plain`, Scale is 1; the known measure must support that scale, and all other reference slots
are empty. Source recipes bind exact text, unit and raw geometry for the number trace.

The D55 database-entry population is read by `FamilyDatabaseEntryCounts.Read` from the project's
stored curation, without Compute or search. Each eligible stored database record is one entry;
equal sequences are not deduplicated. The existing `SequenceLabels` identifier helpers exclude
decoys and contaminants. A recorded UserID is its source family; zero is a real family, while
a missing UserID is unassigned and excluded. Assignment presence roundtrips separately from the
legacy integer API, preserving the serialized values of normally assigned records.

The read-only view exposes source UserID counts and effective panel-key counts. It uses the
same slots as `EffectivePanelFamilyStudy`, so aliases and complete member-family unions follow
the native collapse. An unlinked source family is not added to a panel key. Conflicting panel
keys and all keys touched by a conflicted source family are unavailable (null), never zero or
a partial count; their members do not enter another union. An otherwise known family with no
remaining eligible entries has a real zero. Stale panel mapping is unavailable. Reading changes
neither curation nor saved studies. Canonical Others is an aggregate and is not plotted.

`databaseEntries` uses this population, not the peptide source census or greedy explanation.
The prepared report supplies this read-only view to the HTML context. Every available non-Others
headline family gets a `databaseEntries` field ID; the existing `HtmlDotMeasure` record exposes
it through its family-ID values map, with log scale and program-made plain-decimal ticks. A zero
is included, an unavailable/conflicted key is omitted, and no count view means no invented measure.
Existing field/axis IDs and values are allocated first and stay unchanged. Count references use
Kind `database-entry-count`, Property `/project/MyExtendedFastaItems`, Study `UserID` or `panel-key`,
and Family equal to the recorded source ID or effective key. All other locators are empty, Scale
is 1 and text is an invariant integer; this is a count of stored records, not a stored peptide result.
`databaseGap` remains reserved when
there is no explicit curated-family key. `proteins` remains the native greedy protein estimate,
distinct from the searched database population. Existing measured values and rounding stay literal.

`title`, `authors`, `interpretation`, all family/run/part names, and metric properties are field
IDs, not display text. Missing IDs or null values mean unavailable; they never imply zero.
`composition.families` has name, mean, sd, cv, wholeMeasured, proteins, uniqueProteins, peptides,
isOthers, runs, parts and coverage. Run entries have name/share/measured field IDs. Parts have a
fixed kind and name/share field IDs; their percentages are pooled within-family values, not
mean per-run shares. Optional isUnresolved comes only from the native unresolved flag or missing
native part key, never the displayed name, and may drive hatching. The page must state that basis
when drilling. IsAdditive=false forbids a normalized composition view. Canonical Others is one
aggregate; its hidden names are not embedded.

`creditedRuns` shows the existing credited-family share of whole measured signal by run.
`databaseGap` contains native fraction values (unit `fraction`, not a newly calculated percentage).
Its G3 short field keeps that same Word unit; the exact native fraction stays on the original
field for the pop-up. A page must not turn a Word fraction into a printed percentage itself.
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

Stored-only labels or full-precision fallbacks use `stored-value` with an RFC6901 Property
pointer rooted at `/manifest` or `/project`. The shared trace resolves these from the saved
project copy, never by running Compute. G3 short fields retain the native value's scale.
The C# context's optional coverage-provider callback receives family ID, field allocator and
the current selection; it must filter before allocating fields, keeping excluded data out of JSON.
`word-text` additionally carries ParagraphText and Occurrence (zero-based among Word paragraphs
with exactly that text). Allocation locates/validates the actual selected Word package paragraphs;
an ambiguous fragment or missing occurrence is refused. The Word writer is not changed.
