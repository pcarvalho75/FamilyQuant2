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

## What studying the gate would look at (for Paulo)

The errors sit in tags of 10 residues or fewer, where a fixed 80% fraction allows few identities. A study
would measure, on these same known answers and decoys, a length or minimum-identities rule for the
cross-species pass only, and report the landed fraction it costs. That choice is Paulo's; this checker
does not make it.
