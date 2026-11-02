# Quantitation stages

X5 keeps the public `FullXicComputer` and `FamilyEnvelopeQuant` entry points and
their defaults. Files under `FamilyQuant.Core/Quantitation` keep the `FamilyQuant`
namespace. The seven relocated study files keep `FamilyQuant.DeNovo`; their
contents, public types and calculations are unchanged.

`FullXicComputer.ComputeFullXic` coordinates these stages:

| Stage | Input | Output |
|---|---|---|
| `FullXicInputStage.Prepare` | RAW directory, SEPro package, optional source directory, options, run restriction | `FullXicInput`: acquisitions by normalized name, PSMs, source populations, ordered matched runs |
| `FullXicSourceStage.Read` | Source directory, PSMs, source selections and criteria | `FullXicSourceInput`: all audit calls, accepted calls, reconciled extraction requests and source studies |
| `FullXicInputStage.CreateResult` | Prepared input and extraction options | Pending result and manifest with parameters, input provenance and source studies |
| `FullXicRunStage.Extract` | One matched run, prepared input, cache callbacks, pending result | Measured quant rows, TIC, RAW precursor metadata and independent detector diagnostic |
| `FullXicCompletionStage.Complete` | Prepared input, measured result, RAW metadata | Derived fractions, reference diagnostics, family mapping, complete spectrum table, accepted-score study and optional CSV |

Runs remain sequential. A missing run match refuses the operation before a RAW
is opened or output written. Metadata, detector, family-mapping and optional CSV
failures retain their existing distinct warning boundaries. All seven studies
called by Compute remain in `Quantitation/Studies`; none becomes a report-only
dependency or disappears from the manifest.

Family quantitation has these stages:

1. `SearchCuration` searches normalized sequences, retaining compact protein hits.
2. `AttributeHits` joins those hits onto current target `UserID` values and names.
   Regrouping can repeat this join without repeating the search.
3. `PreparePeptides` collapses anchors and applies reference-envelope calibration.
4. `AttributePeptides(preparedPeptides, attribution)` applies the existing
   cross-side suppression and resolves each surviving peptide's current families.
5. `BuildFromAttributedPeptides` rolls up those assignments in the original
   peptide order. `BuildFromPeptides`, `Build`, `BuildFromAttribution` and
   `BuildFromCuration` retain their existing signatures and behavior.

## Second attribution pass

The public stage output is `FamilyPeptideAttribution`:

- `Peptides` is an ordered read-only list of `FamilyAttributedPeptide`. Each item
  has `Peptide` (`NativePeptideQuant`) and distinct `FamilyUserIds`.
- `UnattributedDeNovoPeptides` is an ordered read-only list of
  `NativePeptideQuant`. It contains only de novo peptides with no first-pass
  family, after suppression of identified-equivalent precursors.

The peptide objects retain calibration, run, charge, representative scan and
source claims. They are shared with the prepared input and must be treated as
read-only by consumers. No RAW measurement or calibration is repeated at this
seam. B5 can search the residual against the family world, credit family-level
results separately and write a new export. It must not rewrite the first-pass
assignments, existing family totals or spectrum mapping provenance.

The pass itself is `FamilyWorldPass` (`Quantitation/FamilyWorldPass.cs`, B5). It reads
`UnattributedDeNovoPeptides` and searches each distinct normalized tag against a
`FamilyWorld` with `HomologyMapper.Search`, at the shipped identity gate and the
production de novo rule, with one reversed copy of every world protein as decoy.
A hit credits the UniProt families of the winning proteins, never a protein.
`FamilyWorldOptions` carries Paulo's decision D11 as its defaults: a tag needs at
least 9 residues (`TooShort` otherwise, not searched), and only the families in
scope can be credited; the other matched families are kept apart
(`OutOfScope`, `OutOfScopeFamilies`). The scope defaults to the families the world
was gathered for; a world that records none is refused. Until the clade fetch (B4)
stores a world, `FamilyWorld.FromFile` builds one from a family-world file's entries
and stored panel, with the panel's families as the scope.

`FamilyWorldPassResult` closes on the first pass's unattributed reference-calibrated
de novo envelope: each family's own peptides, the shared row and every other
outcome sum to it. `FamilyWorldPass.BuildCsv` is the new export; every row states
the rules in force (`RuleText`). No existing table, total or export changes. The
B1 checker (`--family-world-check`) runs the same pass under B1's settings and
asserts its numbers equal its own tally bit for bit, then runs it under D11.

## Guide invariants and checks

The eight invariants in `CLAUDE.md` retain their existing fixture coverage. The
new seam adds checks where moving suppression out of the rollup could lose a
precursor or credit it twice.

| Guide invariant | Executed by the quick suite |
|---|---|
| Match runs by file name | `FullXicRunMatchTests`: reversed input order, partial names, suffix refusal and case handling |
| Sum distinct peptides rather than repeated anchors | `NativeFractionUnitTests`, `FamilyCompositionTests`; `QuantitationStageTests` checks max-M0 residual selection and retained source claims |
| Both sides of the legacy three-way bar use M+0 | `ThreeWayPartitionTests` |
| Do not clamp the derived remainder | `ThreeWayPartitionTests` |
| Curation locks survive regrouping | Grouping, super-cluster and undo fixtures |
| Claims follow the exact selected evidence | Source-selection, provenance, source census and spectrum-table fixtures; the full project golden comparison checks four production exports |
| The de novo screen opens no message box | Source-contract and de novo view-model fixtures |
| Invariant parsing and CSV numbers | Locale and export fixtures; `QuantitationStageTests` repeats stage output and composition CSV under `pt-BR` |

`--test-quantitation-stages` runs the focused seam checks. The quick suite runs
them too. A golden comparison alone does not rerun the RAW computation.

For before/after extraction validation, the native test-program command is:

```text
FamilyQuant.Tests.exe --fullxic-pipeline-snapshot <project.fqproj> <RAW-folder> <de-novo-folder> <empty-output-folder> <cache-folder>
```

This dataset checker uses `PLVI.sepr2` in the supplied RAW folder. It takes the
curation, extraction parameters and source cutoffs from the supplied project.
The selected source distributions also supply the common length/stretch filter.
It refuses inconsistent stored filters instead of silently choosing one.
Outputs are a computed project, decompressed quant and spectrum tables, the
spectrum CSV, a canonical manifest and measured computation time. Only the two
manifest computation clock fields are normalized for byte comparison. A fresh
cache directory is required to demonstrate extraction rather than cache replay.
The output project ZIP itself retains normal save/computation times and is not
claimed byte-identical across saves.

The checker also retains the input project's `AppVersion` option as a controlled
comparison parameter. Its output is a test artifact, not a newly version-stamped
collaborator deliverable. Build logs identify the executable used for each run.

## X5 validation record

The lane's logs and program outputs are under
`D:/FamilyQuant-data/out-floater/X5`. The input is the stored
`BrProt_Talk/all+DeepNovo/scientific_results.fqproj`, with the three matching RAW
acquisitions, `PLVI.sepr2` and the `PeaksFull` source exports. These are bounded
checks on that project, not a claim about every acquisition or curation.

- The missing-seam check was red before product edits: 0 passed, 1 failed.
  The final focused stage checks passed 12/0/0 and dispatch checks passed 21/0/0.
- Release build had zero warnings/errors. The final quick suite passed
  2,537/0/17. The full suite belongs to the orchestrator and was not run here.
- Frozen golden passed 4/0/0. The four production exports are unchanged.
- Original baseline, repeated original control, X5-only candidate and both final
  integrated snapshots each contain 161,266 quant rows. Quant TSV, spectrum
  JSONL, spectrum CSV and canonical manifest have identical SHA-256 hashes across
  all five outputs. The two final runs use the identical integrated executable.
  Only the two computation clock fields are normalized in the canonical manifest.
- Completed fresh extraction evidence is the identified side of run 01. The
  original cold attempt was stopped by its confirmed owned PID during the de novo
  attempt, which is not counted as completed fresh evidence. The orchestrator
  approved the bounded scope. Full three-run comparisons reused private copies
  of caches, validated by the production readers against RAW content fingerprints
  and extraction parameters; the fresh identified run-01 cache was included.
- Original cached Compute wall times were 113.343 and 758.418 seconds. The
  X5-only candidate took 493.460 seconds. Final integrated runs took 214.975 and
  144.655 seconds wall, with 130.594 and 134.078 seconds CPU. The unchanged
  original varied strongly too; no sustained slowdown was observed against the
  contemporary control, and these measurements do not establish a speedup.

Key logs are `build-integration.log`, `focused-integration.log`, `dispatch.log`,
`quick-integration-summary.log`, `golden-integration.log`, `integrated-1.log`
and `integrated-2.log`. Each snapshot's `artifact-hashes.json` records the four
comparison hashes. The original and intermediate executables are retained in
`baseline-bin` and `candidate-bin-d2b` for review. Shared inputs, shared caches,
the frozen snapshot and protected deck producers were not written.
