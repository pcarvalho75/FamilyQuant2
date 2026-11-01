# FamilyQuant scientific revision report

> **Historical 1.4 record, not the current release report.** The source-aware FamilyQuant 1.5
> PEAKS/DeepNovo workflow, spectrum comparison table, and simplified presentation supersede this report.
> Do not cite its values or test counts without regenerating them from the current software.

## Outcome

The codebase now compiles with **0 warnings and 0 errors**, the full regression passes **1,124 / 1,124 tests** with **0 failures** and 15 optional-data skips, and the real-data talk lane passes **48 / 48 scientific gates** after a fresh extraction of all three RAW files.

The defensible pooled result is:

| Reading | Share of raw TIC | Interpretation |
|---|---:|---|
| M+0 sensitivity | 17.33% | Ceiling-insensitive reading; not a formal lower bound |
| **Primary identified-reference estimate** | **42.64%** | Identified observed envelope + mass-stratified calibrated de novo M+0 |
| Unadjusted M0…M+5 diagnostic | 51.69% | Retained for auditability; not “explained ion current” |
| Resolved isotope-series diagnostic | 59.60% | Independent, not peptide-FDR calibrated, not a TIC partition |

## Corrections implemented

| Area | Correction |
|---|---|
| Recovery estimand | Added an explicit identified-reference, fixed-mass-bin de novo envelope estimator and kept M+0 and unadjusted readings under distinct names. |
| Anchor inflation | Enforced one representative per run, side, sequence, and charge; the largest-M+0 anchor supplies the matching envelope. |
| Cross-side overlap | Suppresses exact and I/L-folded sequence+charge overlaps within a run from de novo recovery sums. |
| Estimator guard | Removed an unjustified 6× upper clamp; later isotopologues can legitimately exceed M0. |
| Detector | Requires at least three isotopes; every surface calls it a resolved isotope-series diagnostic and states that it has no peptide FDR. |
| False closure | Removed exported “unidentified peptide-like” and “non-peptide/noise” residual fractions and redesigned the recovery chart around independent readings. |
| Target-decoy homology | Best target must strictly beat best decoy; an exact tie goes to the decoy. |
| Curation semantics | Reports single-linkage connected components and separates all, target-bearing, non-contaminant target, contaminant-bearing, and pure-decoy counts. |
| Cache integrity | RAW, curation, identity-sweep, and pairwise-memo caches now use content SHA-256 plus method/version provenance. |
| USEARCH execution | Verifies the bundled executable by SHA-256 and enforces a 30-minute timeout. |
| Normalization | NIAF and aggregate safety gates now run before a no-op return; TIC normalization refuses NIAF explicitly. |
| CSV/project semantics | Primary, M+0, and unadjusted diagnostic columns are distinct; calibrated values and overlap counts persist in `.fqproj`. |
| Dependency security | Direct-pinned OpenMcdf 3.2.0 instead of the vulnerable transitive 2.x package; real RAW extraction confirms binary compatibility. |
| Legacy deserialization | Added a prominent trusted-input boundary for `.sepr2` files that still require the external BinaryFormatter-based reader. |

## Real-data evidence

- Fresh cache directory did not exist before the run.
- Extraction time: 8.4 minutes.
- Pooled TIC: `2.7058 × 10^12` intensity-minutes.
- Identified observed envelope: 28.15% of TIC.
- De novo M+0: 5.46% of TIC.
- De novo reference-calibrated envelope: 14.48% of TIC.
- Primary per-run values: 41.57%, 43.06%, 43.24%.
- Exact/I-L cross-side overlaps suppressed: 0 in this dataset.
- Curation: 312 all-database connected components, 154 target-bearing, 67 non-contaminant target, 88 contaminant-bearing target, 158 pure-decoy.
- Final machine-readable project round-tripped successfully.

## Remaining red flags and limitations

These are not silent defects; they are the limits that should accompany any scientific claim.

1. **High: untrusted `.sepr2` files are unsafe.** The external SEPro reader uses BinaryFormatter. FamilyQuant cannot safely deserialize a malicious package. Only trusted local SEPro output should be opened.
2. **High scientific: 42.64% is model-based, not externally calibrated truth.** Mass strata do not capture every composition-dependent isotope pattern. A labelled standard or independent all-peptide feature workflow is needed for external validation and uncertainty estimation.
3. **Medium scientific: de novo filtering and homology are heuristic.** The PEAKS confidence rules and 80% family-homology attribution do not constitute a peptide-level FDR procedure. Target-decoy competition is conservative but does not convert the mapping into a calibrated FDR.
4. **Medium scientific: 70% is below the zero-mixing floor.** The identity sweep finds a target/decoy-clean floor of 75%; the pre-specified 70% curation contains one mixed component. Family conclusions should include 75% as a sensitivity analysis.
5. **Medium scientific: single linkage can chain.** A 70% connected component does not guarantee all member pairs are 70% identical. These are database components, not necessarily biological families.
6. **Medium scientific: peptide collapse can undercount multiple chromatographic forms.** The largest-M+0 representative is the defensible antidote to repeated MS2 anchors, but the same sequence/charge can in principle form multiple true chromatographic peaks. A future feature-ID layer should distinguish re-anchors from separable elution features.
7. **Medium interpretive: family bars overlap.** Count-in-every-family attribution means the family bars can exceed the primary total and must not be read as a composition that sums to 100%.
8. **Low maintainability: legacy field names remain.** Stored/API names such as `DetectedPeptideLikeArea` remain for backwards compatibility, although their exported labels and documentation now say “resolved isotope-series diagnostic.” A future file-format major version can rename them cleanly.
9. **Operational/legal: USEARCH licensing remains external.** The bundled personal-use binary has a trust and licensing boundary that should be reviewed before redistribution or commercial deployment.

## Code-review assessment

| Dimension | Assessment |
|---|---|
| Correctness | Strong after revision: explicit estimands, conservative overlap and target-decoy rules, content-bound caches, and real-data gates. |
| Security | Improved, with one unavoidable high-risk trusted-input boundary in the legacy `.sepr2` dependency. |
| Performance | Appropriate for three large RAW files; expensive stages are cached and processed one run at a time. Full fresh regeneration still takes minutes by design. |
| Maintainability | Improved provenance and tests; legacy public/storage names and a very large monolithic test harness remain technical debt. |
| Scientific defensibility | Suitable for the revised claim if the estimator assumption and remaining limitations are stated exactly as in the talk. |

## Verification artifacts

- `artifacts\verification\talk_fresh_3raw.stdout.log`
- `artifacts\verification\talk_final_labels.stdout.log`
- `artifacts\verification\regression_postfix.stdout.log`
- `C:\Users\paulo\Desktop\FamilyQuant\BrProt_Talk\numbers.md`
- `C:\Users\paulo\Desktop\FamilyQuant\BrProt_Talk\scientific_results.fqproj`
