# Scientific estimands and acceptance criteria

FamilyQuant reports three deliberately different readings of recovered MS1 ion current. They are not interchangeable.

1. **Primary estimate: identified-reference, mass-calibrated envelope.** Identified peptides define the observed envelope/M+0 ratio within fixed precursor-mass strata. Each de novo M+0 area is multiplied by the ratio for its mass stratum (falling back to the pooled identified ratio only when a stratum has no identified signal). The primary recovered fraction is identified observed envelope plus this reference-calibrated de novo estimate, divided by raw-file TIC.
2. **M+0 sensitivity reading.** Identified M+0 plus de novo M+0 divided by TIC. This is comparatively insensitive to the isotope-window ceiling, but it is not a formal lower bound because an incorrect precursor assignment or unrelated co-eluting signal can contaminate M+0.
3. **Unadjusted six-window diagnostic.** Identified and de novo observed M+0..M+5 areas divided by TIC. This is retained to expose the effect of crediting the same six isotope windows on both sides; it is not labelled “explained ion current.”

The whole-map isotope-series detector is a diagnostic estimate, not a partition of the TIC and not an FDR-calibrated peptide classifier. Its residuals must not be named “unidentified peptide” or “non-peptide/noise.”

Clustering at 70% is reported as a single-linkage, 70%-edge connected component. Counts distinguish non-contaminant target clusters, contaminant-bearing target clusters, and pure-decoy clusters. A connected component does not imply every pair of members is at least 70% identical.

Each configured de novo source is accepted only when all three operational filters pass: peptide length at least 7 residues, that source's configured native score threshold, and at least 5 consecutive residues each carrying local confidence at least 90%. The current defaults are PEAKS ALC at least 80% and DeepNovo SCORE at least 80%; these are separate scales, and equal numeric cutoffs do not imply equal confidence. Missing or unparseable local-confidence arrays fail the screen. Neither threshold is a calibrated de novo false-discovery rate or directly comparable with a search-engine XCorr or q-value. FamilyQuant stores each source's complete score distribution and retained subset in the project manifest so the score-distribution figure is reproducible after reopening the full project.

## Acceptance criteria

- Raw-file dependent caches are rejected when the source-content SHA-256 changes.
- Curation/sweep caches are rejected when the database-content SHA-256, clustering method, or source-row count changes.
- Target-decoy homology uses best-score competition; a decoy wins an exact tie.
- TIC and aggregate normalization safety gates run even when normalization would otherwise be a no-op.
- The primary, M+0, and unadjusted readings are exported under distinct column names and provenance labels.
- De novo entries that exactly overlap an identified sequence+charge (including an I/L-folded match) in the same run are conservatively suppressed from cross-side recovery sums.
- The detector requires at least three isotope traces and is described as a resolved isotope-series estimate, never as “strong peptide evidence.”
- The talk output includes a machine-readable project, numbers table, method/provenance text, and figures generated from the same computation.
- The spectrum comparison table drives scan-, source-, precursor-, and quantitation-level figures. Identity sweeps, reference-envelope diagnostics, and family sensitivity use dedicated persisted/exported tables; complete reproduction starts from the saved project and evidence-pack exporter, not from one CSV alone.
- An identified peptide is attributed to the families of the proteins that contain it (I/L folded): the target proteins tied at its best homology score. A de novo peptide is attributed to the families of every target protein passing the identity gate. Both follow the target-decoy competition. A homolog that only resembles an identified peptide is not evidence for that peptide's family.
- Family attribution is many-to-many. `FamilyUserId` is non-null only for an exactly-one-family peptide; `FamilyUserIds` lists every ID, and `FamilyAssignments` plus the legacy `FamilyAssignment` alias preserve every full label. `FamilyMappingMinimumIdentity` records the homology threshold used, and `FamilyMappingStatus` distinguishes mapping-not-run, unusable-curation, mapped, and run-but-unattributed rows. Shared peptides may contribute to several family rollups but retain one precursor quantitation.
