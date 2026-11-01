# Context and measurements for the 2026-10-24 council

## The users

- **Ana Gisele C. Neves Ferreira**, Laboratório de Toxinologia, IOC Fiocruz (Rio). Senior venom proteomics researcher, not a programmer; the main tester. Presents the results (IQ seminar in August, an international congress in Manaus in September, BrProt in November 2026).
- **Richard Hemmi Valente** (Fiocruz): runs PEAKS and DeepNovo; provided the de novo exports.
- **Luis Miguel Muñoz Gómez**: user and tester of earlier versions (his database gave the 12%/24% Divide and Conquer answers cited in commit 0f59d6d).
- Decision recorded on 2026-08-31 (Ana, Richard, Paulo): for the paper, use Comet PSMs plus traditional PEAKS de novo.

## Feedback history (the "Family Quant" email thread and today's messages)

| Date | Ana's point | Outcome |
|---|---|---|
| 08-16 | manual family numbers lost after word reclustering; USearch ID and User ID out of step | curation locks, synced ids (1.1.x) |
| 08-20 | sort direction of the id columns | fixed (1.1.2) |
| 08-20 | "identified peptides are only about 12% of the TIC, too low"; should count the whole isotope envelope; the diagnostics screen is confusing | full-envelope XIC engine (1.4) |
| 08-21 | identity clustering at 70% leaves holes: pairs at 87.5% and 90% identity split | exhaustive all-pairs single-linkage pass (1.2) |
| 08-25 | two acidic PLA2 at 69.4% split (threshold); super cluster joined every "phospholipase" when she typed "acidic phospholipase" (wants AND/OR, exclusions); reclustering with a Search term applied to the whole universe (wants it scoped); wants Undo | all implemented (1.3) |
| 10-23 | "I asked it to compute the XIC, but it seems it did not work" | her RAW arrived renamed `..._01-003.raw`; Compute paired no run and reported success with 0 rows; now refused with a rename hint (commit d4f3154) |
| 10-24 | Divide and Conquer gave about 20% before and about 70% now: what changed? | the exhaustive pass; one family of near-palindromic 11-residue BPPs aligns to its own reversed decoys; the mixed families are now named (commit 5f92607) |
| 10-24 | Search accepts one word only ("metalloprotease, SVMP" lists nothing) | OPEN |
| 10-24 | super cluster keywords cannot express phrases ("ribosomal subunit" OR "ribosomal protein") | OPEN (note: "protein" is a stop word, `FamilyQuant/WordCluster.cs:27`) |
| 10-24 | join proteins #x, #y, #z by id, in batch, not row by row | batch edit of selected rows (5f92607) |
| 10-24 | the Viperidae database has thousands of proteins that will rarely be identified; she does not want to hide them, she wants them to appear as one "cellular proteins" slice in her pie chart | batch edit plus annotation (5f92607); her pie chart is made OUTSIDE FamilyQuant from the CSV export |

Paulo, 2026-08-17, on the workflow: "the workflow is confusing, because we handle two separate worlds that we join at the end; ... a total revamp where we point at one folder with the raw files, the PEAKS results, the SEPro and whatever else there is, and the program understands it and works from there."

## Today's measurements (Bothrops jararaca T-R, 584 entries: 292 targets, 292 `Reverse_` decoys, 242 identifiers containing "contaminant")

Database: `C:/Users/paulo/Desktop/FamilyQuant/uniprotkb_taxonomy_id_8724_2026_06_25_ORDENADO_CONT123_NO-Redundancy.T-R` (byte-identical to Ana's copy).

Divide and Conquer (score 0): fast-only 24% (252 families); exhaustive 73% (339 families). Just below the exhaustive answer, at 72%, exactly one family mixes: 5 targets + 5 decoys, BPP 11h, 11g, 11j and two more, 11 residues each. BPP 11d (`QGRPPGPPIPP`) aligns to its reverse (`PPIPPGPPRGQ`) at 75% identity over 8 of 11 residues.

Identity scan (families / with targets / non-contaminant target families / mixed):

| Identity | Fast only | Exhaustive |
|---|---|---|
| 95% | 514 / 257 / 143 / 0 | 512 / 256 / 143 / 0 |
| 85% | 452 / 226 / 122 / 0 | 441 / 221 / 117 / 0 |
| 75% | 399 / 199 / 104 / 0 | 354 / 177 / 84 / 0 |
| 70% | 365 / 181 / 90 / 0 | 312 / 154 / 67 / 1 |
| 60% | 311 / 155 / 75 / 0 | 264 / 131 / 58 / 1 |
| 50% | 274 / 138 / 73 / 0 | 233 / 117 / 56 / 1 |
| 40% | 259 / 130 / 72 / 0 | 187 / 93 / 44 / 1 |
| 35% | 254 / 128 / 71 / 0 | 167 / 85 / 43 / 3 |
| 30% | 252 / 126 / 70 / 0 | 141 / 72 / 38 / 3 |
| 25% | 252 / 126 / 70 / 0 | 123 / 63 / 36 / 3 |
| 20% | 250 / 126 / 70 / 2 | 20 / 10 / 6 / 1 |
| 15% | 245 / 126 / 70 / 8 | 1 / 1 / 1 / 1 |

In exhaustive mode the mixing from 72% down to 45% is the BPP family alone; other families first mix at 35% (FosB, L-amino-acid oxidase / amine oxidase), and at 20% single linkage chains almost everything into one component.

## Historical quantitation numbers (quoted from CLAUDE.md, 1.4 implementation history, run _01; NOT the 1.5 talk numbers)

Identified envelope 27.84% of TIC, de novo envelope 22.82%, recovered 50.67%; identified M+0 11.89%; resolved isotope-series detector 60.90% of TIC (z >= 2: 57.70%, z = 1: 3.20%). `SCIENTIFIC_METHOD.md` now defines three distinct readings (primary reference-calibrated envelope, M+0 sensitivity, unadjusted six-window diagnostic); use its language.

## Operational facts learned today

- Users judge whether an update arrived by the version in the title bar; the title shows the assembly version, which can lag the ClickOnce version.
- Users rename and copy RAW files; runs are paired by exact file name.
- The quick test pass: 1387 checks, 0 failed, 16 skipped (real-data sections).
