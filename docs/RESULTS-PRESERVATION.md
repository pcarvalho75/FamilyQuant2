# Results preservation receipts

This packet proposes 445 observations for independent review: 46 native
interactions, 253 exact declarations, seven semantic tooltip checks, 82 native
frozen comparisons and 57 empty/export fact checks. No author approval or DROP
was recorded.

`RS-interactions.json` covers all 46 observations from the Results XAML and displaced
handler definitions: 11 controls, 12 labels/options, 11 tooltips, seven XAML handlers
and five handler definitions. The native fixture selects all five family limits,
checks both percentage events and follows the four retained export routes.
Peptide CSV, summary CSV and all five Light PNGs match bytes across two exports;
the PNG dimensions are 1920 by 1080. An incomplete evidence pack names the missing
overlap study and publishes no folder. That refusal is not a successful full-pack
export. Current shorter copy is explicitly mapped to each frozen observation for
semantic review. Both-theme captures were inspected.

`RS-members.json` covers 253 exact C# literal declarations. It uses the shared
`LedgerMemberFactComparison` with bounded source modules, exact literal syntax and
multiplicity, and complete containing-member token equality. Six chart classes
retain their owners while inheriting `ChartDrawingBase` instead of `FrameworkElement`;
two private display helpers moved from `AnalyticsControl` to `AnalyticsViewModel`.
Those relocations are explicit. Changed bodies and declarations remain unverified.
These are declaration receipts, not runtime scientific reproduction.
The shared comparator now disambiguates repeated literals on one frozen line;
the five formerly ambiguous key/CSV anchors retain their complete member tokens.

`RS-C13-tooltips.json` covers the other seven chart-tooltip fragments after C13's
two-sentence wording pass. It records each frozen fragment and the actual native
tooltip, with explicit checks for every retained fact and the dynamic isotope
ceiling. These are semantic wording receipts for independent review, not claims
that the old literal text or complete constructor tokens remain identical.

The native `RS-runtime.json` packet adds 82 observations: 32 summary fragments,
37 chart drawing/font fragments, 12 User-family normalization headers and the
delegated numeric CSV formatter. The
same probe source is compiled against the frozen application and current program.
It compares 178 named values, including missing/zero/positive TIC, absent/empty/
populated families, zero/measured/estimated exclusions, source provenance, and
English/French formatting. It also compares all bytes of 48 chart PNG fixtures
across frozen/current programs and a second current run. The frozen application
assembly is checked against the read-only reference before execution.

The only accepted summary text substitution is the explicit screen-name change
from `Layer 3` to `the quantitation panel`. These are native branch comparisons,
not unchanged-declaration claims. The negative control changes actual comparison
values and must fail; it cannot produce verified rows. Per-row fixture keys,
before/actual values, sources, assemblies and artifact hashes are retained.

`RS-export-facts.json` covers the 57 rewritten export/empty-state fragments.
The [fact audit](RESULTS-EXPORT-FACTS.md) records each before-state. Native checks
exercise all four retained exports through their controls, including complete
pack success, cancellation, detached-view refusal, real IO failure and missing
study refusal. Success counts/names come from the existing exporters' returned
file lists. The export producers and palettes are unchanged. Repeated PNG/CSV/
definition bytes agree; the complete plot-data JSON tree agrees after excluding
only its existing root `generatedUtc`. That metadata file is not byte-identical.

The two USearch-only normalization labels remain OPEN in FQAssay's cohort.
The earlier declaration member/reason records are retained in
`D:/FamilyQuant-data/out-results/C13-tips/rs-members/comparisons.json`. No same-text
fallback, guessed relocation or inferred DROP closes them.

The proof commands retain the executing checker/source and compiled assemblies,
logs and artifact hashes beside their registers. Reproduce from a fresh build with
timestamps obtained from `date '+%F %T %z'`:

```text
FamilyQuant.Tests.exe --prove-results-interactions <fresh-output> <date-timestamp>
FamilyQuant.Tests.exe --prove-results-members D:/FamilyQuant-data/out-shell/N3/source <fresh-output> <date-timestamp>
dotnet build FamilyQuant.Tests/ReferenceGoldenHost/ReferenceGoldenHost.csproj -c Release -v q
FamilyQuant.Tests.exe --prove-results-runtime D:/FamilyQuant-data/out-shell/N3/source FamilyQuant.Tests/ReferenceGoldenHost/bin/Release/net10.0-windows7.0/ReferenceGoldenHost.exe <fresh-output> <date-timestamp> [--negative-control]
FamilyQuant.Tests.exe --prove-results-export-facts D:/FamilyQuant-data/out-shell/N3/source <fresh-output> <date-timestamp>
```

Import each generated register with `--feature-ledger-evidence-import`, using a fresh
report output. The native interaction checks also run in the ordinary suite. This
packet does not establish fresh RAW reproduction, Ana-file reproduction, installation
or completion of the remaining ledger observations.
