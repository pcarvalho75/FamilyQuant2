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

## Grouping by evidence (FamilyQuant 2.0)

Grouping by evidence places each protein in a family from curated annotation, homology and domains, never from the words of its name. The estimand is the UniProt family a protein belongs to, restricted to the scope the user types (words matched in the keywords and tissue of each protein). Every protein receives a family or none, a tier and a reason.

- **Nearest reviewed protein.** Every target is aligned with every reviewed protein (USEARCH `-search_global`, which aligns every pair; the indexed `-usearch_global` was rejected because its word index dropped pairs depending on database order). A neighbour counts at 50% identity or more over at least 70% of the query's length, never the same entry (accession compared, isoforms folded); ties across families are reported, not settled.
- **Reference panel.** The reviewed members of the families found in the file, from every taxon, are fetched from UniProt (all or nothing, release recorded) and stored with the file.
- **Tiers.** In scope with one UniProt family; in scope with a reviewed neighbour at 60% or more; in scope with a domain that marks a family (at least 80% of the labelled carriers in scope vote for it); out of scope with a neighbour at 70% or more and compatible domains; otherwise unassigned. A protein with several UniProt family memberships is left to the user.
- **Coherence.** Length, cysteine and helix ranges and domain sets are learned per family from the complete reviewed members (ranges need at least 10). A protein outside what they ever show is flagged with the reason and unticked by default.
- **Motifs** are typed by the user, stored in the project and counted per family on complete members. The **left-out list** names the proteins carrying a family's learned marker domain that were not placed in it, with the reason.

### What is measured, and the numbers on Ana's file

The validation report hides each reviewed protein's families (every membership) and calls it back from its nearest other reviewed protein, never itself; right means the neighbour has exactly the same families. On Ana's file (UniProt 2026_03 evidence and panel, scope `Toxin` and `venom gland`, 1,092 reviewed proteins in scope; `FamilyQuant.Tests.exe --validation-check` on `e4-ana-panel-neighbours.fcf`, report `e9-report.json`):

| References | Right | Wrong | No neighbour at 60% |
|---|---:|---:|---:|
| The file's reviewed proteins | 976 | 20 | 96 |
| The file's reviewed proteins and the panel | 986 | 17 | 89 |

The frozen Python baseline (exp6) gave 977, 15 and 100; each of the 13 proteins that differ is explained in `e9-differences-from-exp6.csv` (6 multi-family labels, 4 neighbours the indexed search never aligned, 3 ties).

Against the 224 reviewed members of the venom metalloproteinase (M12B) family, with Ana's words (metalloprotease, metalloproteinase, svmp; exclude inhibitor, adam, matrix, thrombospondin, atp):

| Route | Proteins | Reviewed members taken | Missed | Other reviewed proteins taken |
|---|---:|---:|---:|---:|
| Words | 844 | 213 | 11 | 12 |
| Words with exclude | 772 | 213 | 11 | 9 |
| Recluster 0.80, words | 853 | 213 | 11 | 16 |
| Recluster 0.80, words with exclude | 780 | 213 | 11 | 12 |
| Evidence | 744 | 224 | 0 | 0 |
| Evidence, ticked after coherence flags | 695 | 224 | 0 | 0 |

For reviewed proteins the evidence route is partly circular (the first tier reads their own family), so its row shows that nothing else slipped in; the leave-one-out above is its accuracy test. The disagreements with the frozen Python classes, each with a written verdict, are in `docs/SCIENCE-GATE.md`.

### Acceptance criteria

- No family name, protein name, Pfam or PANTHER code or scope word in the rule (`Evidence/`); the suite's rule guard fails on one.
- A leave-one-out never sees its own label, and a neighbour is never the protein itself.
- Two runs give byte-identical neighbours, flags, reports and CSVs; every CSV is RFC 4180 in InvariantCulture.
- The panel fetch is all or nothing: a refused page, a missing or changed release, an unreviewed entry, or a family short of UniProt's announced count leaves the file unchanged.
