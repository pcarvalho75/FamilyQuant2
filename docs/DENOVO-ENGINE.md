# De novo inspection engine

X6 keeps every existing public reader, reconciliation, report and control
compatibility API. The code that chooses the extraction/homology population and
shapes review rows belongs to Core; the view model applies completed results and
formats its existing status text.

| Component | Responsibility |
|---|---|
| `DeNovo/Readers/PeaksCsvReader.cs` | Existing header-driven source CSV reader, native confidence fields and input fingerprints |
| `DeNovo/Readers/DeNovoSourceRows.cs` | Existing source evidence and de novo row models, unchanged namespace/properties |
| `DeNovo/Readers/PeaksDatabasePsmReader.cs` | Existing separate database-search reader, unchanged content |
| `DeNovo/DeNovoReconciler.cs` | Existing same-file/scan/charge/direct-mz decisions and conservative sequence normalization |
| `DeNovo/Inspection/ReconciliationModels.cs` | Existing verdict, PSM and decision-row contracts |
| `DeNovo/Inspection/DeNovoInspectionSelection.cs` | Retained rows by run and deterministic homology queries, shared by inspection and full XIC |
| `DeNovo/Inspection/DeNovoReviewTableBuilder.cs` | Legacy and source-aware review-table projection from decision rows |
| `DeNovo/Inspection/ReviewQueueRow.cs` | Existing legacy table-row contract |
| `DeNovo/Inspection/DeNovoOperationResults.cs` | Existing import, reconciliation, extraction, mapping and partition results |
| `DeNovoScreenOperations.ReconcileForInspection` | Reconciliation plus source-aware table projection, completed before UI attachment |

`DeNovoSession.KeptRowsByRun`, `KeptRunNames` and `PeptidesForHomology` are thin
delegates. `DeNovoViewModel` delegates selected-source file counting and the
source-aware reconciliation workflow. The control retains its binding, readiness,
tooltips and compatibility adapters. Other screens were not edited.

The source CSV parser, PEAKS database reader and reconciliation decision bodies
were retained. The SEPro adapter remains the existing Core vendor boundary.
The CSV writers retain their distinct quoting, headers and numeric formats.
No scientific threshold, score conversion, source cutoff or matching rule changed.

## Preserved selection and table rules

- Only `DeNovoOnly` and `CoIsolatedExtra` enter additive extraction and reconciled
  homology selection. Review alternatives and weak equalities stay in the queue.
- Run grouping uses the existing normalized names, ignores empty run names and
  compares case-insensitively. Row order within each run is retained.
- Before reconciliation, homology sees the filtered import. A completed empty
  reconciliation remains empty; it never falls back to the import.
- Homology query cleaning keeps the existing PTM removal and I/L folding, excludes
  invalid/empty sequences, deduplicates ordinally and sorts ordinally.
- Review tables keep only comparable alternatives and weak equalities, ordered
  by ordinal run name and scan. Missing local confidence stays zero, and source
  confidence remains in its native field without conversion.
- Reconciliation keeps its direct 20-ppm precursor comparison. The isotope-aware
  quantitation equivalence is a different rule and was not substituted here.

## Focused commands

The flags are registered in `Program.Commands.DeNovoEngine.cs`:

```text
FamilyQuant.Tests.exe --test-denovo-engine
FamilyQuant.Tests.exe --test-denovo-engine-real <PEAKS-only-folder> <acquisition-folder> <output-folder>
FamilyQuant.Tests.exe --denovo-engine-snapshot <PEAKS-only-folder> <full-source-folder> <sepr2> <empty-output-folder>
```

The fixture route exercises Core selection/table contracts and the existing
reader, reconciliation and source-aware checks. The real route runs the existing
real import/reconciliation and actual screen-table/export assertions. It explicitly
does not run unrelated legacy RAW extraction. The ordinary suite retains all of
those legacy checks through the default `includeLegacyExtraction = true` path.

The native snapshot uses the production view model and Core for legacy PEAKS,
PEAKS all, DeepNovo and their all-source union. It writes decision/selection/table
data, UI state/status and the three CSVs per mode. It contains no volatile clock
fields and reads no RAW spectra. Python/hash tools may compare those bytes; they
do not create the inspection results.

## X6 measured packet

Inputs are the read-only `PEAKS-EXPORT_FamilyQuant`, `PeaksFull` and `PLVI.sepr2`
under the shared FamilyQuant acquisition directory. Program outputs and logs are
under `D:/FamilyQuant-data/out-floater/X6`.

| Mode | Rows read | Rows kept | Legacy review rows | Selected homology queries |
|---|---:|---:|---:|---:|
| PEAKS only | 181,643 | 35,686 | 1,019 | 21,095 |
| PEAKS all | 221,135 | 53,282 | 11,845 | 21,736 |
| DeepNovo | 42,549 | 17,875 | 5,856 | 1,811 |
| PEAKS all + DeepNovo | 263,684 | 71,157 | 17,701 | 22,561 |

The missing-Core-selection contract was red before extraction (0/1/0). Focused
checks passed 93/0/0; the focused real-data route passed 34/0/0, including the
complete 133,513-PSM list and real screen table/export comparisons. All 16 files
from `before`, `after-utf8-1` and `after-utf8-2` have identical SHA-256 hashes.
No timestamp normalization was needed. The earlier `after-1` output is rejected
encoding-regression evidence and is not part of acceptance.

These measurements cover import, reconciliation, selection, table projection and
exports on this dataset. No fresh RAW extraction, new homology algorithm or
collaborator-delivery claim is made by this packet. At the single READY integration
boundary, Release build had zero warnings/errors, the quick suite passed
2,730/0/17 and frozen golden passed 4/0/0. Logs are `build-ready.log`,
`quick-ready-summary.log` and `golden-ready.log`. The orchestrator owns the full
suite; it was not run by this lane.
