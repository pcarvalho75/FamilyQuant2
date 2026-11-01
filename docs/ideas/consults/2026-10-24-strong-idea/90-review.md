# Convener's red-flag review of Fable's round-1 paper

Reviewer: Claude (Opus 5.5), 2026-10-24. Every number below was recomputed from the files named; every code claim was read at the cited lines in the worktree at `7614742`.

## Verdict

**Endorsed, with three amendments.** The toxin-class layer is the right "very strong idea": it is the figure Ana currently builds by hand outside the program, it keeps the eight invariants by construction (a class is a label, never a grouping), and it closes the two open requests (multi-term Search, phrase keywords) as a by-product. No booking was found inflated; two statements of fact need small corrections; one real gap must be planned before work starts.

## Numbers recomputed

| Claim | Recomputed | Holds |
|---|---|---|
| 87 quantified families, primary sum 9.22e11 (`family_attribution.csv`) | 87, 9.220e11 | yes |
| SVMP 39%, SVSP 22%, CTL 17%, LAAO 7%, CRISP 3.7%, PLA2 3.0% by keyword | 39.1%, 22.4%, 16.6%, 7.1%, 3.7%, 3.0% | yes (estimates by keyword, as labelled) |
| 22 blank-description families, about 6.1e9 | 22, 6.064e9 | yes |
| Jararafibrase-3 (family 4) unclassified, 3.2e10 | 3.23e10, the largest unclassified family | yes |
| 35 target headers with no description in the `.T-R` | 35 | yes |
| Identified family sum 7.90e11 vs 7.62e11 assigned in `signal_by_run.csv` | 7.901e11 vs 7.618e11 | yes (different bases, as labelled) |
| Ana's `.fcf`: 584 entries, 302 families, identity 75, word similarity 0.8, super cluster, 2 locks, 1 diverging row, 0 annotations | identical | yes |
| On-disk pack predates the current family CSV header | current header has `family_label` (`PresentationEvidencePack.cs:576`), the file does not | yes |

Additional fact from the recount: 59 of the 87 families match no toxin keyword, but together they carry only 4.38e10 (4.8% of the primary sum). Classification is a long tail by count and a short one by signal, which makes the "unclassified" counter of P3 the right UI: few families matter.

## Code claims checked

Held: CSV columns (`CsvReport.cs:26-35`); edges memoised in memory and the TSV deleted (`USearchTools.cs:389-412`, `:458`); count-in-every-family in the native table (`FamilyEnvelopeQuant.cs:236-239`, `:311-314`); single-substring Search (`FamilyFilter.cs:44-51`); "protein" a stop word (`WordCluster.cs:27`); undo, batch and lock references.

Corrections (no effect on the plan):

1. **Blank labels.** `FamilyEnvelopeQuant.cs:460-466` keeps the first NON-EMPTY member description (an empty one is overwritten by the next member). A blank label therefore means no target member of that family has a description, not that the first one lacks it. The 22 cases are still the description-less contaminant headers.
2. **"Up to 4x on the Viperidae database."** `SharedPeptidePolicy.cs:6-8` says "on the most redundant project measured here"; it does not name Viperidae. Keep the argument, drop the attribution.

## Amendment 1 (a gap): the composition must exist on the PLP path too

Ana's pie chart today is built from the Layer 1 CSV, whose share column is `XICUser` from the PLP quantitation (`CsvReport.cs:65`, `FamilyClusterFile.AttributeXICToGroups`, `FamilyQuantFile.cs:259`). P4 and P5 build the composition only on the native full-XIC path (`FamilyEnvelopeQuant`, used by Analytics at `AnalyticsControl.xaml.cs:136`), which needs RAW files and a Compute run. As written, the plan would give her the class column in the CSV (P3) but the class composition only after a full XIC compute.

Add **P4b, class rollup on the PLP path (S, pure, blocked by P1 and P2):** group by class instead of by `UserID` in a class-level twin of `AttributeXICToGroups`. Grouping by class makes the existing `FamilyUnique` rule mean "class-unique" automatically (a peptide shared only within one class is credited in full), `SplitEqually` stays additive, and `CountInEveryFamily` is refused for a composition. Output: a "Class" CSV (class, share, peptide count, families) beside the Layer 1 export, with the denominator named exactly as the family CSV names it. Proof: a fixture where two families of one class share a peptide (dropped at family level under `FamilyUnique`, credited at class level) and closure of the class shares to 100% under `IdentifiedSignal`. Files owned: a new `ClassRollup.cs`, its tests, and the export call in `ClusterGenerationControl.xaml.cs` (coordinate with P3, which owns that file: do P4b's UI hook inside P3).

## Amendment 2: ship in two milestones

- **Milestone 1, classification (P1, P2, P3, P4b, P7):** the class column, the dictionary window, the batch class box, the class CSV on the PLP path, multi-term Search and phrase keywords. This is what Ana can use on her next dataset without running Compute; estimate three to four worker-days.
- **Milestone 2, the composition figure (P4, P5, P6):** the native-path composition with the spread across runs, the chart, the evidence pack. Estimate three to five worker-days.

## Amendment 3: two product decisions belong to Paulo and Ana, not to the plan

1. **The dictionary content is a scientific choice.** Mapping "amine oxidase" to LAAO, "Jararafibrase" (a fibrinogenolytic SVMP by name, not by keyword), trypsinogen homologs, and every rule in `DefaultToxinClasses.json` must be validated by Ana before any figure is quoted (invariant 6). Ship it marked draft, with its version and hash on every output.
2. **Pie or bars.** Ana asked for a pie; Fable rejects a program-drawn pie (fifteen slices, adjacent colours) and gives bars plus the CSV. That is defensible data-visualisation practice, but it is her figure: ask her whether a top-N pie with an "other classes" slice is wanted inside the program.

## Traps confirmed

All of Fable's traps stand; the most important are "unclassified is never cellular" (only the user writes "cellular proteins") and "a class is never a grouping". One more: the class column must refresh after Undo and after a batch (both call `UpdateGUI`), and a class derived from a stale dictionary must show the dictionary hash, so two exports from different dictionaries cannot be confused.
