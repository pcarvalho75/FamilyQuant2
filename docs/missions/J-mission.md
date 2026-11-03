# Mission J: Ana's complete venom analysis, made by the program

Paulo, 2026-11-02 19:40: "here is the ultimate test for this program, i want you to use it to make a very cool and
complete analysis ... you will use all available features and generate a complete word document. if you need a
feature that this program does not do you implement it in the program, the program needs to be able to accomplish the
full analysis. she wants a quantitative description of the families".

## The deliverable

One Word document (`.docx`) WRITTEN BY FamilyQuant (File > Export report), from one saved `.fqproj`, plus that
project. Every number, table and figure in it comes out of the program. A person can open the project in FamilyQuant
and export the same document again. The written interpretation is the only human text; it lives in the project's
notes and the report prints it as a section. It quotes only numbers the report prints.

## Inputs (read only; never write inside these folders)

| What | Path |
|---|---|
| Three RAW acquisitions (technical replicates, 1 ug, 240 min) | `C:/Users/paulo/Desktop/AnaG/Bjararaca/PatternLab/20220624_Bjararaca_1ug_3step-240min_0{1,2,3}.raw` |
| PatternLab search result (Comet 2021.01, semi-tryptic, 2 missed cleavages, 35 ppm, I=L) | `C:/Users/paulo/Desktop/AnaG/Bjararaca/PatternLab/Bjararaca.sepr2`, params in `comet.params` beside it |
| Database used by the search: Ana's Viperidae file, 31,777 targets (381 contaminants) plus 31,777 decoys | `C:/Users/paulo/Desktop/AnaG/audios/uniprotkb_taxonomy_id_8689_2026_10_23_ORDENADO_NO-Redundancy_CONT381.fcf` (SourceSha256 d73d9810...ac76). The `.T-R` itself is not on this machine; the `.fcf` holds every identifier and sequence |
| PEAKS de novo, all three runs, ALC >= 50 | `C:/Users/paulo/Desktop/AnaG/DeNovo_ALC-50percent/` (source "PEAKS de novo all", `.denovo.csv`) |
| PEAKS de novo, run 01 only, ALC >= 80 | `C:/Users/paulo/Desktop/AnaG/Bjararaca/DeNovoPeaks_ALL/` |
| Ana's family panel (the consensus of 14 papers) | `C:/Users/paulo/Desktop/AnaG/Bjararaca/FamilyQuant_Viperidae_consenso_final_Claude.xlsx` |
| Ana's note | `C:/Users/paulo/Desktop/AnaG/Bjararaca/AnaNotes.txt` and the image beside it (BPP-CNP is one precursor) |
| My earlier family draft (made by Python, so not a result; use it only as a comparison) | `C:/Users/paulo/Desktop/AnaG/Familias_veneno_rascunho/Viperidae_familias_veneno_rascunho.fcf` (27 families, 2,753 proteins) |

These RAW files are the same acquisitions as the BrProt project; only the database changed. Copy small inputs
(`.sepr2`, `.fcf`, `.csv`) into your own output folder when a command needs a writable copy. Never copy the RAW files.

## What Ana asked for (her spreadsheet, tabs 1 to 6, in Portuguese)

Tab 1, 41 families with a decision each: Núcleo (always quantify), Padrão (standard, expect under 1%), Condicional
(depends on the genus), Opcional (scan only), Excluir (not a family), and "Fundir em CTL" (merge into another family).
Her priorities if not everything fits: 1) aminopeptidase, 2) PLA2 inhibitor, 3) Kazal inhibitor. Each row lists Pfam
domains (her note: the PF accessions were not checked against InterPro), subclasses to report, typical abundance in
the literature, notes and sources.

Tab 2, SVMP classes by domain architecture (Fox and Serrano 2005): P-I = Reprolysin without Disintegrin and without
ADAM_CR; P-II = Reprolysin + Disintegrin, no ADAM_CR; P-III = Reprolysin + Disintegrin + ADAM_CR; the DC fragment =
ADAM_CR and Disintegrin without Reprolysin, counted inside SVMP P-III, never as P-I or free disintegrin.

Tab 4, quantification rules:
1. SVMP class by architecture; when only catalytic-domain peptides are seen, report "SVMP, class not resolved"
   instead of forcing a class.
2. The DC fragment as above.
3. Free disintegrin = Disintegrin without Reprolysin; always say whether disintegrins were counted inside SVMP.
4. BPP, CNP and svMP-i come from one precursor (BPP-CNP): one NP total, with the parts detailed; never two families.
5. Shared domains (Lectin_C: CTL, snaclec, galactose lectin, alpha PLI; Endonuclease_NS: PDE and nucleases;
   Disintegrin: SVMP, DIS, DC): decide by the accompanying domains before assigning.
6. PLA2: separate D49 (catalytic) from K49 (homologue).
7. Report unit: % of the venom per family, then the number of toxins of that family in parentheses; keep abundance
   and diversity apart.
8. Different genes are different toxins; variants of one gene are proteoforms.
9. Declare the database (public, transcriptome or both).
10. Declare the workflow (here: crude venom, no prior fractionation, shotgun).
11. Families under 0.1% are reported only as "others", with the list in a supplement.
12. Some families depend on the genus (cystatin, crotamine, waglerin, AChE, Kunitz in pit vipers).

## Rules every worker follows (unchanged from 2.0)

- Only the program makes results. Python may investigate; nothing it computes reaches a person.
- No exception lists: no family, protein-name, Pfam or keyword literal inside an automatic rule. The panel is USER
  DATA: Ana's family names, domains and decisions live in her file and in the project, never in code.
- The program is general (D21): nothing the user sees names an organism, venom, toxin or a sample's numbers. The
  report prints the user's own panel text and the project's own numbers; its fixed wording is general.
- No function is lost; frozen numbers stay equal unless a step says otherwise and shows before and after.
- InvariantCulture for files, current culture for display. No em or en dashes anywhere.
- Unsupported cases are reported plainly ("not measurable with this search"), never guessed. Example: the svMP-i
  tripeptides are shorter than the search's minimum peptide length of 5.
- Work in your own worktree and branch; merge master before READY; post `## READY <step> <commit>` in your OUTBOX;
  workers never push.
