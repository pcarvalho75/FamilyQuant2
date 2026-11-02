# B1: the family-world gate, written verdict

FQOpus, 2026-11-01. Every number below was printed by the program:
`FamilyQuant.Tests.exe --family-world-check` (source `FamilyQuant.Tests/FamilyWorldCheck.cs`), run on
`scientific_results.fqproj` (the Bothrops jararaca project) and `e16-ana-searched.fcf` (Ana's Viperidae file
with its stored reference panel, UniProt 2026_03). Outputs: `D:/FamilyQuant-data/out-opus/b1/run2` and `run3`.

## Verdict: BETWEEN

| Test | Measured | PASS needs | KILL at | Falls in |
|---|---:|---:|---:|---|
| Known answers, right family among landed (sample species out) | 92.46% (1,975 of 2,136) | at least 95% | under 85% | between |
| Coverage, unattributed de novo envelope landing on a family | 26.34% | at least 20% | under 5% | pass |
| Coverage, decoys as a share of the landed envelope | 7.14% | at most 5% | over 20% | between |

The bars are the roadmap's, fixed before the run. By the roadmap, BETWEEN means: study the gate before
building. Nothing here was tuned, and the verdict above is the only one the checker computes.

## What was measured

**Known answers.** Every scan PatternLab identified on which PEAKS or DeepNovo also made an ACCEPTED call at
a compatible precursor (the stored spectrum table: same file, scan, charge and 20 ppm precursor). Each
distinct pair (de novo tag, identified peptide) counts once: 5,519 pairs from 29,295 calls. The known family
is the UniProt family of the database proteins that hold the identified peptide (the production identified
rule). Excluded: 1,025 pairs whose holders carry no UniProt family (mostly unreviewed metalloproteinases),
114 held by a contaminant, 4 held by no target. 4,376 pairs remain.

The family world is every entry of Ana's file (31,777, contaminants included) plus the 4,715 panel entries
the file does not already hold, each with a reversed copy as decoy. The sample's species (OX 8724) is
removed three ways: OX= in the header, UniProt's taxonomy list (6 panel entries carry no header and are
B. jararaca) and the sample's own database: 176 proteins out, 36,316 left. Each tag is searched with the
program's mapper at its shipped 80% gate and the production de novo rule (every target past the gate after
the target-decoy competition). A hit credits the UniProt families of the winning proteins.

Right means every credited family is a known one. A tag credited to its own family and another one is
counted wrong.

| Outcome, species out | Pairs |
|---|---:|
| Credited to a family (landed) | 2,136 |
| Not landed | 2,180 |
| Won by a decoy | 29 |
| Landed only on proteins with no UniProt family | 31 |

**Genus out, ungated** (asked by the orchestrator, M1): the genus Bothrops (8721, from UniProt's lineage) is
removed, 726 proteins. Landed 1,623, right 1,488: **91.68%**.

**Coverage.** The unattributed de novo reference-calibrated envelope of the project's own first pass, walked
exactly as `BuildFromPeptides` walks it (the checker asserts the total equals
`UnattributedDeNovoEnvelopeReferenceCalibrated` bit for bit): 2.690719E+11, 65.68% of the de novo envelope,
18,837 peptides. Searched against the whole world (no species removed, as the second pass would run):

| Outcome | Share of the residual | Peptides |
|---|---:|---:|
| Credited to a family | 26.34% | 2,139 |
| of which to exactly one family | 23.56% | |
| Won by a decoy | 1.88% | 259 |
| Landed only on proteins with no UniProt family | 1.23% | 204 |
| Not landed | 70.55% | 16,235 |

## Where the errors are (diagnostics, never part of the verdict)

By tag length, species out, right among landed:

| Tag length | Pairs | Landed | Right |
|---|---:|---:|---:|
| 7 or fewer | 95 | 76 | 31.58% |
| 8 | 153 | 89 | 80.90% |
| 9 | 203 | 111 | 97.30% |
| 10 | 267 | 203 | 85.22% |
| 11 to 12 | 633 | 378 | 96.03% |
| 13 to 15 | 1,197 | 512 | 96.88% |
| 16 to 20 | 1,119 | 450 | 94.22% |
| 21 or more | 709 | 317 | 99.37% |

The 80% gate is a fraction, so a 7 residue tag needs only 6 identities and a 10 residue tag allows 2
mismatches. In a world of 36,000 proteins, short tags find unrelated proteins: 170 families with no known
answer of their own received 232 credits, all wrong (receptors, kinases and other proteins of Ana's file).

By family, species out (known: landed, right; credited: of them truly that family):

| Family | Known pairs | Landed | Right | Credited | Truly that family |
|---|---:|---:|---:|---:|---:|
| venom metalloproteinase (M12B) | 1,210 | 580 | 92.41% | 581 | 96.73% |
| snaclec | 1,078 | 405 | 92.10% | 406 | 95.07% |
| peptidase S1 | 948 | 554 | 94.04% | 552 | 98.55% |
| flavin monoamine oxidase | 380 | 198 | 91.41% | 191 | 98.43% |
| CRISP | 175 | 98 | 96.94% | 98 | 98.98% |
| true venom lectin | 158 | 79 | 91.14% | 83 | 91.57% |
| PDGF/VEGF growth factor | 116 | 43 | 97.67% | 44 | 97.73% |
| phospholipase A2 | 104 | 44 | 79.55% | 42 | 97.62% |
| 5'-nucleotidase | 87 | 57 | 94.74% | 56 | 98.21% |

Phospholipase A2 is the family below the KILL bar on its own known answers (35 of 44); 6 of its 9
misses credit phospholipase A2 and a second family beside it, so its credited precision stays 97.62%.
Over all families, of the 161 wrong landings 86 credit the right family plus another one, and 102 come
from tags of 10 residues or fewer.

Other views: weighted by calls the right rate is 94.64%. A tag equal to the identified peptide is right in
90.61% of landings and a different tag in 93.50%: a correct read is no safer, so the errors come from
the sequence meeting other proteins, not from misreads.

## Limits

- The truth is UniProt's family line on the B. jararaca proteins holding the identified peptide. A holder
  with no family line contributes nothing, so a known set can be incomplete.
- The family world here is Ana's file plus the panel, standing in for B4's clade fetch (ruling M1).
- The coverage decoy rate measures chance hits only; the wrong-family risk is what the known-answer test
  measures.
- I did not run the gate on a second sample.

## The gate study, and Paulo's decision (D11)

The roadmap's answer to BETWEEN is to study the gate before building. The study (same checker, commit
b0c6e42, output `D:/FamilyQuant-data/out-opus/b1/study2`) measures one candidate rule without applying it:
a minimum tag length for the cross-species pass. To keep the choice from being made on the numbers it then
reports, the known answers were split in two BEFORE anything was looked at, by SHA-256 of the identified
peptide (every tag of one peptide stays in one half; the outcome plays no part): a table half of 2,304
pairs (559 peptides) and a held-out half of 2,072 pairs (574 peptides). The bars did not move.

| Minimum tag length | Table half: landed, right | Held-out half: landed, right | Residual landed | Decoys of landed | Credits to families with no known answer |
|---:|---:|---:|---:|---:|---:|
| 7 (none) | 1,044, 92.24% | 1,092, 92.67% | 26.34% | 7.14% | 232 to 170 families |
| 8 | 1,010, 94.36% | 1,050, 95.05% | 23.34% | 1.49% | 68 to 46 families |
| 9 | 965, 94.61% | 1,006, 96.02% | 21.58% | 0.74% | 44 to 23 families |
| 10 | 918, 94.44% | 942, 95.97% | 19.52% | 0.82% | 43 to 22 families |
| 11 | 821, 95.37% | 836, 97.49% | 18.23% | 0.02% | 22 to 10 families |
| 12 | 739, 95.26% | 716, 97.77% | 16.41% | 0.02% | 16 to 7 families |
| 13 | 653, 95.10% | 626, 98.08% | 12.41% | 0.01% | 12 to 4 families |
| 14 | 560, 95.18% | 534, 98.13% | 9.09% | 0.01% | 9 to 4 families |
| 15 | 496, 94.56% | 471, 98.09% | 7.72% | 0.00% | 8 to 4 families |

**Paulo decided (D11): a tag needs at least 9 residues, and only families in scope can be credited.** At 9
residues the table half is right in 94.61% of landings (913 of 965) and the held-out half in 96.02% (966 of
1,006); pooled, 1,879 of 1,971, 95.33%. Coverage lands 21.58% of the residual with decoys at 0.74% of it.
Against the unchanged bars, that is a pass on the held-out half and pooled, NOT on both halves: the table
half stays between, 0.39 points under 95%. The landed share and the decoy rate pass.

**Later, D22 (Paulo, 2026-11-02): the default moved to 8, and a person can choose any length from 7 to 15.**
This page stays the record of the study at 9. Paulo chose 8 after the table by length on both worlds (B10,
known answers searched in the world without the sample's species). Going from 9 to 8 added 1.06% of the
residual on the 12-family world and 1.57% on the 40-family world. Decoys were 0.04% and 0.06% of what it
added, and the known answers it added were 95.89% and 95.06% right. Going from 8 to 7 added more, but decoys
were 8.81% and 24.87% of it, and its known answers were 88.24% and 73.24% right.

What the study did not measure: the in-scope rule. It counted only how many of the credits to families with
no known answer fall outside the panel's families (29 of the 44 at 9 residues); the effect of crediting
in-scope families only is B5's to measure, on the same known answers.

By family at 9 residues, right among landed, table half then held-out half (from
`family_world_gate_study_by_family.csv`):

| Family | Table half | Held-out half |
|---|---:|---:|
| venom metalloproteinase (M12B) | 243 of 256 (95%) | 273 of 286 (95%) |
| peptidase S1 | 223 of 231 (97%) | 271 of 283 (96%) |
| snaclec | 179 of 196 (91%) | 184 of 189 (97%) |
| flavin monoamine oxidase | 97 of 101 (96%) | 71 of 75 (95%) |
| CRISP | 44 of 45 (98%) | 47 of 48 (98%) |
| true venom lectin | 47 of 47 (100%) | 20 of 22 (91%) |
| PDGF/VEGF growth factor | 20 of 20 (100%) | 20 of 21 (95%) |
| phospholipase A2 | 22 of 26 (85%) | 7 of 8 (88%) |
| 5'-nucleotidase | 19 of 19 (100%) | 29 of 29 (100%) |
| glutaminyl-peptide cyclotransferase | 11 of 11 (100%) | 14 of 14 (100%) |
| NGF-beta | 4 of 5 (80%) | 23 of 24 (96%) |
| glycosyl hydrolase 56 | 3 of 3 | 6 of 6 |
| sorting nexin | 0 of 4 | none landed |
| fetuin, CNF-like-inhibitor, ERCC1/RAD10/SWI10 | 1 pair or none | 1 pair or none |

Among the families with at least 20 landings in a half, phospholipase A2 is the weakest: 67% to 85% in
the table half at every length, 77% to 88% in the held-out half up to 14 residues. It must be reported
with its own rate, never inside a mean. The small families are worse on paper and say little: NGF-beta
is 4 of 5 in the table half, and sorting nexin is wrong in all 4 of its landings (each credited to
snaclec instead) and has none in the held-out half. On 5 or fewer pairs a rate means little either way;
those families need more known answers before any rate is shown for them (B6).

**Families with no known answer.** Without a length rule 170 such families took 232 credits, all wrong by
construction, because no identified peptide of the sample vouches for them; at 9 residues 23 families take
44. This test cannot tell whether a credit to such a family is right. What I would do: (1) credit only
in-scope families, as D11 now says, which removes most of them (29 of the 44 at 9 residues fall outside the
panel's families); (2) in B6, show a family with no known answer as "no known-answer evidence", never with a
rate borrowed from the mean; (3) as a later proposal, measure those families on known answers from another
sample, or from a project whose database search does identify them.

## What the study was for (written before it ran)

The errors sit in tags of 10 residues or fewer, where a fixed 80% fraction allows few identities. A study
would measure, on these same known answers and decoys, a length or minimum-identities rule for the
cross-species pass only, and report the landed fraction it costs. That choice is Paulo's; this checker
does not make it.
