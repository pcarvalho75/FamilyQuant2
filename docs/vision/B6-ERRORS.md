# B6: the error beside every family-world number

FQOpus, 2026-11-02. Every number below was printed by the program: `FamilyQuant.Tests.exe
--family-world-check` (sources `FamilyQuant.Core/Quantitation/FamilyWorldPass.cs` and
`FamilyWorldErrors.cs`), on `scientific_results.fqproj` and `e16-ana-searched.fcf` (UniProt 2026_03), under
D11: tags of at least 9 residues, only the 40 families in scope credited. Outputs:
`D:/FamilyQuant-data/out-opus/b6/run1` and `run2`. The by-hand reading at the end used a Python probe
to look at sequences; it produced no number for the product.

## What is stored

`FamilyWorldErrors.Summarize` writes one table, `QuantManifest.FamilyWorldResult`, which is left out of
`manifest.json` when the pass never ran. Per family in scope:

- its own share of the unattributed de novo envelope (and the split and count-in-every-family readings);
- the decoy rate: envelope won by the reversed copies of its proteins, over the envelope credited to it;
- the right rate: of the known answers credited to the family, how many truly were it, shown only from
  10 such credits (ruling M31), always with the counts beside it; below that the row says
  "too little evidence: n known credits", and with none "no known answer";
- the world proteins its credited tags hit, for the suggested additions.

The rows close on the unattributed envelope (family parts, the shared row and every other outcome), and
every row of the export (`family_world_errors.csv`) states the rules.

## The table on the Bothrops jararaca project

| Family | Own share of the residual | Decoy rate | Known credits, truly it | Right rate |
|---|---:|---:|---:|---|
| snaclec | 8.60% | 0.0008% | 385, 368 | 95.58% |
| venom metalloproteinase (M12B) | 5.43% | 0.02% | 547, 528 | 96.53% |
| phospholipase A2 | 3.48% | 0.0004% | 35, 34 | 97.14% |
| peptidase S1 | 1.33% | 0 | 511, 507 | 99.22% |
| nucleotide pyrophosphatase/phosphodiesterase | 1.32% | 0 | 1, 0 | too little evidence |
| true venom lectin | 0.48% | 0.01% | 75, 69 | 92.00% |
| PDGF/VEGF growth factor | 0.10% | 0 | 42, 41 | 97.62% |
| flavin monoamine oxidase | 0.03% | 0 | 170, 170 | 100% |
| CRISP | 0.02% | 0 | 93, 92 | 98.92% |
| NGF-beta | 0.01% | 0 | 27, 27 | 100% |
| 5'-nucleotidase | under 0.01% | 0 | 49, 48 | 97.96% |
| glutaminyl-peptide cyclotransferase | none credited | none | 25, 25 | 100% |
| peptidase A1, phospholipase B-like, peroxiredoxin, cystatin, calmodulin, Lipase | 0.02% or less each | 0 | 0 | no known answer |
| glycosyl hydrolase 27 | under 0.01% | 0 | 5, 0 | too little evidence |
| disintegrin | none credited | none | 9, 0 | too little evidence |
| glycosyl hydrolase 56 | none credited | none | 9, 9 | too little evidence |

Overall under D11: 20.83% of the residual is credited to a family in scope, decoys win 0.16% of the
residual (0.77% of the credited), 8.15% is in tags under 9 residues, 0.75% lands only on families out of
scope, 0.95% only on proteins with no family, 69.16% is not found. Known answers: 1,898 right of 1,963
landed (96.69%).

Two rows to read with care. The phosphodiesterase family carries 1.32% of the residual with a single
known credit, which was wrong: its share is real signal but unvalidated, and the table says so. And six
families have no known answer at all; their rows show a share and a decoy rate, never a right rate.

## The result table B7 reads

`FamilyWorldErrors.Compose` completes the stored table with the first pass (ruling M33): each curated
group of the project maps to the UniProt families its members carry, and a first-pass peptide is the own
peptide of a family when all its curated groups map to that one family; otherwise it goes to a shared
row. A curated group whose members carry no UniProt family keeps its own row, named by its members (a
contaminant with no description by its identifier). The table closes on FamilyEnvelopeQuant's measured
total, which the checker asserts bit for bit. Own parts as shares of the measured total, on this project
(58 rows, 29 of them curated groups without a UniProt family):

| Family | Identified | De novo in the database | De novo only in the family world | World right rate |
|---|---:|---:|---:|---|
| venom metalloproteinase (M12B) | 24.41% | 5.45% | 1.25% | 96.53% |
| peptidase S1 | 15.30% | 2.43% | 0.30% | 99.22% |
| snaclec | 10.34% | 2.60% | 1.97% | 95.58% |
| flavin monoamine oxidase | 5.12% | 0.50% | 0.01% | 100% |
| true venom lectin | 2.57% | 0.19% | 0.11% | 92.00% |
| CRISP | 2.56% | 0.16% | 0.01% | 98.92% |
| phospholipase A2 | 1.24% | 0.44% | 0.80% | 97.14% |
| PDGF/VEGF growth factor | 1.12% | 0.10% | 0.02% | 97.62% |
| nucleotide pyrophosphatase/phosphodiesterase | 0 | 0 | 0.30% | too little evidence |

Global rows: shared between families 0.02% identified, 0.01% de novo in the database, under 0.01% in the
family world; identified in no family 0.55%; de novo unexplained 18.18%, one bucket for the whole sample.

## Conserved families, checked by hand

The 65 wrong credits among the 1,963 landed known answers, read one by one
(`family_world_known_answers_d11.csv`):

- 20 credit the right family AND a second one. All three causes are proteins or domains that two families
  share, not a motif hitting an unrelated family: phospholipase A2 + glycosyl hydrolase 27 (5), every one
  through the same unreviewed Crotalus adamanteus entry A0AAW1ATK0 (967 residues, "Alpha-galactosidase"),
  which UniProt annotates with both families and which contains a phospholipase A2 calcium-binding loop
  (YGCYCGWGGRGWPK); snaclec + true venom lectin (6), through the C-type lectin motif (WSPYxxHCYK) both
  families carry; metalloproteinase + disintegrin (9), through the disintegrin domain of metalloproteinase
  precursors. In the composition such a peptide goes to the shared row, never into either family's own
  share, so these do not inflate a family.
- 45 are tags unrelated to the identified peptide (under 50% gapless identity between them, e.g. snaclec
  scans whose accepted tag YALFGADVYEAEDSCFK reads like a metalloproteinase peptide). The de novo engine
  read a different peptide from the same precursor window, and the tag went to the family of what it
  reads. Only 11 of the 1,898 right answers have such an unrelated tag.

So on this project the conserved families are not inflated by short motifs once tags have 9 residues:
the wrong credits are two-family proteins (which the shared row absorbs) and de novo calls of another
peptide. What this does not cover: a second sample, families with no known answer, and B4's own world.

## The computed project (through Compute)

`D:/FamilyQuant-data/out-opus/b6/bothrops-jararaca-family-world.fqproj` is the Bothrops jararaca project as
a person would make it: `--family-world-project` fetched the curation's evidence (171 of 171), read the
task from its headers (clade Viperidae, proposed scope Toxin, confirmed), and B4 fetched and stored the
family world (12 families in scope, 4,203 entries, 2,842 of them reviewed, UniProt 2026_03, content hash
3C1F01CA...). Compute (`--fullxic-pipeline-snapshot`) then attached the table. `--family-world-report`
reads it back: 19.48% of the unattributed de novo envelope is credited to a family in scope, decoys 0.00%,
tags too short 8.15%, not found 72.37%; known answers 1,832 right of 1,881 landed (97.40%). Two Compute
runs are byte-identical, and against a Compute run with no stored world the quant and spectrum outputs
are identical and the manifest differs only by the added table. With this sample's own scope the world
holds 12 families instead of Ana's 40, so families such as the phosphodiesterase are not credited here.

## At the default length 8 (D22, B8a)

The default minimum tag length is now 8. These numbers are program output at 8. Decoys are given over both
denominators: the unattributed de novo envelope (the residual) and the credited envelope.

- 12-family world. The project was recomputed through Compute as
  `D:/FamilyQuant-data/out-opus/b10b/computed12/computed.fqproj` (SHA256 0f39525a3b25fc22...). Its stored
  table credits 20.54% of the residual to a family in scope (55,264,469,830.69 of 269,071,929,058.21).
  Decoys are 4,615,782.77: 0.0017% of the residual, 0.0084% of the credited. Known answers are 1,902 right
  of 1,954 landed (97.34%): the table half 935 of 970 (96.39%), the held-out half 967 of 984 (98.27%).
  Choosing 9 in that project gives the table of the previous section, byte for byte
  (`--family-world-lengths` with the B6 project as reference: 7 passed, 0 failed; output in
  `D:/FamilyQuant-data/out-opus/b10b/lengths12`).
- 40-family world (FQEvidence's `b4-ana-world.fcf`, hash EF95269818318019...). It was fetched for another
  source database, so the program cannot attach it to this project and no Compute of it exists. The
  numbers come from the same analysis Compute runs, given the world file (`--family-world-lengths` with
  the world file: 5 passed, 0 failed; output in `D:/FamilyQuant-data/out-opus/b10b/lengths40`). It credits
  22.44% of the residual (60,385,069,310.34). Decoys are 5,940,885.19: 0.0022% of the residual, 0.0098% of
  the credited. Known answers are 1,977 right of 2,042 landed (96.82%): the table half 963 of 1,001
  (96.20%), the held-out half 1,014 of 1,041 (97.41%).
