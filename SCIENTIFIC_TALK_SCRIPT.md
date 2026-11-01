# FamilyQuant BrProt talk: scientifically defensible script

> **Historical 1.4 record, not the current talk.** This isotope-centred script is superseded by the
> source-aware FamilyQuant 1.5 PEAKS/DeepNovo evidence pack and the simplified presentation. Do not cite
> its values or test counts without regenerating them from the current software.

## 1. How much venom signal did identification recover?

The answer I will defend is **42.64% of raw-file total ion current**, pooled across three technical replicates. This is an identified-reference, mass-calibrated estimate. It is not the old 51.69% six-window result, and it is not derived from the whole-map isotope detector.

The question is intentionally narrow: how much raw MS1 ion current is recovered by the identified and filtered de novo peptide anchors under one stated extraction and calibration procedure?

## 2. One dataset, three readings

There are three readings because no single one answers every sensitivity question.

- **17.33%** is the M+0-only sensitivity reading. It is insensitive to how many isotope windows we credit, but it is not a formal lower bound if an anchor is wrong or an unrelated ion occupies the window.
- **42.64%** is the primary estimate. Identified peptides use their observed envelopes; de novo M+0 is scaled by the identified envelope/M+0 relationship in fixed mass strata.
- **51.69%** is the unadjusted M0…M+5 diagnostic. It is reproducible, but the de novo envelope/M+0 ratio is implausibly high, so it is not the headline.

These are sensitivity readings and a model-based estimate, not a confidence interval.

## 3. Define “recovered” before calculating it

The unit is one peptide, within one run and charge state. Multiple MS2 anchors for the same sequence and charge collapse to one representative: the anchor with the largest M+0 area and its matching envelope. That changes the table from 160,337 anchor rows to 38,356 peptide representatives.

The numerator is the representative XIC area. The denominator is raw-file TIC integrated with the same intensity-versus-retention-time trapezoid. This grain and denominator prevent anchor resampling from masquerading as more peptide signal.

## 4. Inputs and provenance

The analysis covers three `20220624_Bjararaca_1ug_3step-240min` RAW acquisitions, with 41,035, 40,489, and 40,420 MS1 scans. Search results and filtered PEAKS de novo rows tell the engine where to look; FamilyQuant remeasures every XIC and the TIC from the RAW files.

The extraction uses 10 ppm, a six-window ceiling from M0 through M+5, and at least three points per isotope peak. The pooled measured TIC is `2.7058 × 10^12` intensity-minutes.

## 5. One engine, one ruler

The path is: anchor, RAW extraction, peptide-level collapse, de novo calibration, and division by TIC. Identified and de novo traces use the same extractor and integration basis.

The corrected code also suppresses a de novo contribution if the same run contains an identified sequence and charge, including an I/L-folded match. There were zero such exact overlaps in this dataset; the guard exists so another dataset cannot receive silent cross-side double credit.

## 6. Why 51.69% is not primary

The identified envelope/M+0 ratio is 2.372×. When identified isotope behavior is standardized to the de novo mass distribution, the comparison is about 2.652×. The directly credited de novo six-window ratio is 4.308×, roughly 62% above that mass-standardized reference.

That difference is the dominant scientific problem in the old headline. The 51.69% result is kept for auditability, but it is not described as “explained ion current.”

## 7. The primary calibration

Identified peptides teach an envelope/M+0 ratio inside fixed neutral-mass bins: below 800, 800-1200, 1200-1600, 1600-2000, 2000-2500, 2500-3000, and at least 3000 Da. Each de novo M+0 area is multiplied by the ratio in its bin. A pooled identified ratio is used only if a bin has no identified signal.

There is no arbitrary upper clamp on the ratio. A sufficiently heavy peptide can have M0 below later isotopologues, so a six-window envelope/M+0 ratio can legitimately exceed six.

This is a transparent calibration assumption, not external ground truth. That is why the M+0 and unadjusted readings remain visible.

## 8. Technical replicate behavior

The primary estimates are **41.57%**, **43.06%**, and **43.24%**. Their range is 1.67 percentage points. The M+0 readings are 17.12%, 17.38%, and 17.49%; the unadjusted diagnostics are 50.67%, 52.54%, and 51.80%.

The agreement demonstrates technical reproducibility of the algorithm. It does not prove that a calibration assumption shared by every run is unbiased.

## 9. The pooled result

The pooled 42.64% consists of **28.15 TIC points** from identified observed envelopes and **14.48 points** from the reference-calibrated de novo estimate. In integrated area, that is `1.1537 × 10^12` intensity-minutes.

The remaining 57.36% is deliberately unnamed. This estimator did not assign it. The analysis does not call it non-peptide, noise, or unidentified peptide signal.

## 10. Fresh regeneration and cache integrity

The final scientific run started from a cache directory that did not exist. All three RAW files were fingerprinted and re-extracted in 8.4 minutes. Six XIC cache files and three detector cache files carry source SHA-256 values. Curation and identity-sweep caches carry the database hash, row count, and clustering method.

The talk harness passed 48 of 48 scientific gates and round-tripped a machine-readable `scientific_results.fqproj`. The full desktop regression passed 1,124 tests with zero failures; 15 sections were skipped only because their optional positional data were not supplied in that regression lane.

## 11. The isotope-series detector is separate

The production detector now requires at least three isotopes and reports 59.60% of TIC as resolved isotope-series signal. An independent implementation gave 56.83%. Deliberately shifted spacings retained 14.86% and 9.48%.

Those controls are imperfect nulls, but they show substantial chance or coincident-series background. The detector is therefore a diagnostic, not a peptide-FDR-calibrated classifier and not a physical partition of TIC. Its residuals are not biological classes.

## 12. What “family” means here

At 70%, the exhaustive single-linkage analysis produces **312 connected components** over the entire target, contaminant, and reversed-decoy database. Of those, 154 contain a target, 67 contain a non-contaminant target, 88 contain a contaminant target, and 158 are pure decoy. One component belongs to both target subcategories.

Single linkage means A can connect to B and B to C without A being at least 70% identical to C. I will therefore say “connected component,” not “biological venom family.”

## 13. Identity sensitivity

The full sweep shows 143 non-contaminant target components at 95% and 67 at 70%. The target/decoy-clean floor is 75%; the pre-specified 70% analysis has one mixed target/decoy component and a mixing score of 0.0032.

The 70% curation should therefore be treated as a disclosed analysis choice, with 75% as a natural sensitivity analysis if the requirement is zero target/decoy mixing.

## 14. Exploratory family attribution

The strongest attributed signal reaches components containing snake venom metalloproteinases, thrombin-like enzymes, and C-type lectins. This is biologically plausible.

The chart is not compositional: a peptide that maps to two components counts in both, and a large de novo estimate remains unattributed. Homology attribution uses an 80% gate and target-decoy competition but is not a peptide-level FDR procedure. These bars support exploration, not additive percentages of the venom.

## 15. What the evidence supports

It is defensible to report a **42.64% primary estimate under the stated calibration**, technical reproducibility across three runs, and substantial added de novo signal after calibration.

It is not defensible to report “51.69% explained,” to manufacture unidentified-peptide or noise residuals from the detector, or to call all 312 database components biological venom families. No statistical confidence interval or external ground-truth benchmark is claimed.

## 16. Take-home

The scientific direction survives: peptide identification plus de novo anchoring recovers substantial MS1 signal. The headline changes because the estimator is now explicit and the diagnostics remain diagnostics.

**Final sentence:** FamilyQuant recovers an identified-reference estimate of **42.64% of raw TIC** in these three technical replicates; 17.33% and 51.69% are sensitivity diagnostics, not alternative claims of truth.

## Source artifacts

- `C:\Users\paulo\Desktop\FamilyQuant\BrProt_Talk\numbers.md`
- `C:\Users\paulo\Desktop\FamilyQuant\BrProt_Talk\scientific_results.fqproj`
- `C:\Users\paulo\OneDrive\Documents\ChatGPT\Dias\FamilyQuant_scientific_revision\SCIENTIFIC_METHOD.md`
- `C:\Users\paulo\OneDrive\Documents\ChatGPT\Dias\FamilyQuant_scientific_revision\artifacts\verification\talk_fresh_3raw.stdout.log`
- `C:\Users\paulo\OneDrive\Documents\ChatGPT\Dias\FamilyQuant_scientific_revision\artifacts\verification\regression_postfix.stdout.log`
