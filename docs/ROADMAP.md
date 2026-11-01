# FamilyQuant roadmap (remaining work only)

Version 1, 2026-11-01. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file at the next refresh and go to `docs/ROADMAP-archive.md`.

## The goal, in Paulo's words

- "using python scripts was cheating, every result passed to ana needs to be through the software"
- "things need to look slick and clean", "the colors look terrible, we have white with yellow in some parts"
- "the file menu usually comes first"
- Simpler and better, with no feature or value lost, unless a feature is useless.

## Rules every worker follows

1. **Only the program makes results.** Python may be used to investigate or to check a number, never to make one that reaches a person. Scratch probes count as scripts: nothing built from a probe is delivered.
2. **No exception lists.** No table of families, no protein names, no Pfam, PANTHER or keyword literal in the rule. Evidence and data decide; what a user chooses is stored in the project. No LLM in the product.
3. **No number moves by accident.** The golden check (F2) must stay byte-identical through all GUI and cleanup work. A step that means to change a number says so in its row.
4. **The quantitation engines are not in scope:** extraction, attribution and homology search stay as they are. Their screens may change, their numbers may not.
5. **Deployable at every merge.** One step per commit, with its test. Paulo publishes and pushes; nobody else pushes.
6. **A step counts as done only after the reviewer approves it** and it is merged and green.
7. **The deck stays untouched:** `BrProt_FamilyQuant_FourPart_Source_Decomposition.pptx` and its sources, until Paulo says otherwise.
8. **No em dashes or en dashes** in anything written, UI text included.

## The team

This team is exclusive to FamilyQuant: three Sol workers and one Grok reviewer, sharing no workers, folders or tools with Project Orion.

| Name | Engine | Role | Worktree | Branch |
|---|---|---|---|---|
| Orchestrator | Claude, main session | Assigns, merges, runs gates, keeps this file true. Writes no product code. | | master |
| FQEvidence (EV) | Sol 6.1 xhigh | Golden check, evidence model, fetch, classifier, apply, Ana's file | `D:/FamilyQuant-wt/evidence` | `fq/evidence` |
| FQLook (LK) | Sol 6.1 xhigh | Screen harness, look, shell, curation and review screens | `D:/FamilyQuant-wt/look` | `fq/look` |
| FQAssay (AS) | Sol 6.1 xhigh | Foundation cleanup, neighbour search, coherence, validation, then Build, Quantify, Results, De novo screens and cleanup | `D:/FamilyQuant-wt/assay` | `fq/assay` |
| FQReview (GR) | Grok 4.7 xhigh | Reviews every step, code and science. Never edits product code. | `D:/FamilyQuant-wt/review` | detached |

Flow of one step: owner posts `READY <step> <commit>`; GR posts `APPROVED <step> <commit> <reason>` or `REJECTED <step> <commit> <findings>`; a rejection is the owner's next commit, red then green, and GR checks it again; the orchestrator merges, builds, runs the suite, and only then the row closes.

## Design rules for the new look (hard requirements)

Today's problems, seen on renders of every real screen on 2026-11-01:
- Gold text on the banner, on every value and on every title number, beside white input boxes and a white table on dark panels.
- Thick white outlines around every group box, emoji used as icons, and five button colours with no meaning.
- 80 hard-coded colours in XAML and 63 in code, outside the chart palette.
- The Home cards turn white with white titles when disabled, so steps 3 and 4 cannot be read.
- The results table is on another tab from the controls, so you cannot see what a button did.
- The table opens on decoys with empty descriptions, and the Locus column cuts off the accession.
- Long banners: up to three lines on De Novo and a long paragraph above the Analytics charts.
- The menu starts with Home and Run USearch; File is third.

The rules:
1. **No yellow or gold anywhere,** the focus ring included. One accent colour, used for the primary action and selection only.
2. **One surface family.** Dark panels never hold white boxes or a white table, and light panels never hold dark ones. Every colour comes from a token.
3. **Colour means status or nothing.** Green, amber and red appear only as small status marks, never on plain values.
4. **No outlines around groups.** Grouping comes from spacing and one quiet surface step.
5. **One primary button per panel,** the others quiet.
6. **No emoji.** Icons come from Segoe Fluent Icons, a Windows font, so no new dependency.
7. **One line of explanation at most on a screen.** The rest goes to Help (F1) and tooltips.
8. **Tables:** soft or no grid lines, readable header, right-aligned numbers, decoys hidden by default, accession visible.
9. **Standard Windows order:** File first, then Edit, View, Analysis, Help, with the usual shortcuts.
10. **Base:** the Windows 11 Fluent theme built into .NET 10 (`ThemeMode`), no third-party theme. A trial on 2026-11-01 restyled menus, tabs, inputs and combo boxes with no other change. It also left the table header white on white, so the theme only works once the hard-coded colours are gone: V2 and V3 go together. The API is marked experimental, so V2 records the warning suppression and tests Windows 10 and 11.

## Workstreams

Columns: Diff = L, M, H, VH. Hrs = worker hours, my estimate. Blocked by: 🟢 = open now. ETA = hours after launch in that worker's queue, a rough estimate and never a promise. Prog = my estimate of work done. Cred = counted only after approval and merge. EV, LK, AS and GR are the workers above.

### W0 Foundation

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| F1 | Split `FamilyQuant.Tests/Program.cs` (13,809 lines) into one file per area. Move only. | L | 2 | 🟢 | F2, F4, F5, E1, every test added later | Same PASS and SKIP counts as master on quick and full; no test body changed | EV | working | 5% | 0% | T+2 |
| F2 | Golden numbers guard: `--golden` exports family CSV, composition CSV, analytics summary and evidence-pack numbers from one stored project and compares them byte for byte with a snapshot kept outside the repo | M | 3 | F1 | Every GUI and cleanup merge | Passes on master; a deliberate one-share mutation turns it red | EV | not started | 0% | 0% | T+5 |
| F3 | Screen harness: `--screens <dir>` renders every screen and window, light and dark, at 1366x820 and 1024x768. Starts from the probe that made the 2026-11-01 renders | M | 3 | 🟢 | V1, every look review | Home, Build, Curation, De novo, Analytics, Compute, Super Cluster preview, Lock manager, Spectrum table, Identity scan and Text report all written | LK | working | 5% | 0% | T+3 |
| F4 | One definition of the decoy prefix (`Reverse_`, 13 sites in 7 files) and the contaminant prefix (6 sites) | L | 1 | F1 | E1 | Suite check fails on a literal outside the one class; golden equal | EV | AS | 0% | 0% | T+6 |
| F5 | Remove dead paths: `GroupByKeywords` (no screen calls it, 22 checks move to `MatchKeywords`), overloads kept for older callers, `GroupingType` where the product only ever passes User | M | 3 | F1 | Simpler E11 | Same rule pinned by the moved checks; golden equal | EV | AS | 0% | 0% | T+9 |

### W1 Evidence engine: what the Python scripts did, now inside the program

What moves, script by script: `grouping_fetch.py` to E2. `exp1` and `exp2` (nearest labelled neighbour, never itself) to E3 and E4. `exp3` (domain composition, subclass by length and cysteines, name against evidence) to E6 and E7. `exp4` (domains that mark a family, read from the data) to E5. `exp5` and `exp6` (the rules, the reason for each protein, the route comparison, leave-one-out) to E5, E6 and E9. `exp7` and `exp8` (biology checks, rescues, left-out list) to E7, E8 and E10. `make_draft_fcf.cs` (numbers, annotations, locks, audit CSV, reopen-and-compare) to E11, E12 and E13. `words_route_probe.cs` to E9. New code goes in `FamilyQuant/Evidence/`, free of WPF like `DeNovo/` and `Homology/`.

Where the Python broke rule 2 and the program must not: the 27-row family table, the Pfam code list, the ADAM name regex and `TM and over 700 aa`. Their replacements: family names come from UniProt and the user ticks them (E5, C4), architecture is learned (E6), and outliers are learned from reviewed members (E7).

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| E1 | Evidence model and storage: per protein reviewed, UniProt family and subfamily, keywords, tissue, Pfam accession and name, PANTHER, transmembrane count, fragment flag; file level source and release. Stored in the file; old files still load | M | 3 | F1, F4 | E2, E3 | Round trip test offline; old `.fcf` opens unchanged; size growth measured on Ana's file | EV | not started | 0% | 0% | T+12 |
| E2 | Fetch Evidence replaces Fetch Domains: same all-or-nothing batches and retries, more fields, progress and cancel. Domains column unchanged | M | 3 | E1 | E3, E5 | Live on Ana's file: asked 31,775, found 31,775, every field count agrees with `viperidae_rich.tsv`; canned tests for a refused batch and a missing entry | EV | not started | 0% | 0% | T+15 |
| E3 | Nearest reviewed protein inside the app: `usearch_global` of all targets against the reviewed ones, id at least 50%, alignment at least 70% of the query, never the protein itself (accession compared), deterministic ties, memoised, stored as evidence | M | 3 | E2 | E4, E5 | Best hit agrees with `vs_revtox2.tsv` on all pairs or each difference explained; two runs byte-identical | EV | AS | 0% | 0% | T+18 |
| E4 | Outside reference panel: reviewed proteins of the families found, from all taxa, so entries with few reviewed relatives or no accession still get a neighbour. Panel saved with the evidence, offline afterwards | H | 4 | E3 | E5, E14 | Ana's file with accessions stripped: leave-one-out on 1,092 reviewed still scores; panel release recorded; offline rerun identical | EV | AS | 0% | 0% | T+22 |
| E5 | The classifier, pure and general. Scope is words the user gives, matched on keywords and tissue. Order: annotated with a UniProt family; annotated with a neighbour at 60%; annotated with a marker domain learned at 80% vote; unannotated with a neighbour at 70% and compatible domains; else unassigned. Each protein gets family, tier and reason. Publishes the `EvidenceProposal` contract for C4 | H | 5 | E3 | E6, E9, E10, E11, C4 | Known-answer tests; a suite check fails on any family name, Pfam or PANTHER code, or venom word in `Evidence/`; PLA2 and SVSP unannotated members recovered as in `exp5` | EV | not started | 0% | 0% | T+27 |
| E6 | Architecture split, general: inside a family, group proteins by domain architecture learned from the data, offer each group as a split with a name from its dominant domain. SVMP against free disintegrin becomes one user choice, stored in the recipe. No domain code in the rule | H | 4 | E5 | E11, E14, C4 | On Ana's file the split separates free disintegrins with no literal; `Disintegrin` count within the differences E14 explains; a second family (Snaclec or PLA2) splits or stays whole by the same code | EV | not started | 0% | 0% | T+31 |
| E7 | Coherence check, learned from reviewed members of each family: length range, cysteine range, transmembrane share, domain share. A protein outside what reviewed members ever show is flagged with the reason, unticked by default. Replaces `TM and over 700 aa` and the ADAM regex | H | 3 | E5 | E14, C4 | ADAM proteins in the SVMP set flagged without a name rule, or the remainder listed for E14; flags reproducible | EV | AS | 0% | 0% | T+34 |
| E8 | Motif check: the user types a pattern, the program counts carriers per family on complete entries, Pfam unused. The motifs are user input, stored in the project, none in code | L | 1 | E5 | C4 | Typing the `exp8` patterns reproduces its biology table | EV | AS | 0% | 0% | T+35 |
| E9 | Validation report inside the app: leave-one-out on reviewed proteins (right, wrong, no neighbour, confusion list) and the route comparison (words, words with exclude, recluster, evidence) against reviewed labels | M | 3 | E5 | E14, A2 | Reproduces 977, 15, 100 on 1,092 or each difference explained; screen and CSV | EV | AS | 0% | 0% | T+38 |
| E10 | Left-out list: proteins carrying a family's learned marker domain that were not assigned, with why | M | 2 | E5 | E13, C4 | Agrees with the 359 of `fora_com_dominio_de_veneno.csv` or differences explained, with no domain table in code | EV | AS | 0% | 0% | T+40 |
| E11 | Apply: new numbers past every number in use, annotations, locks, decoys and unassigned proteins untouched, one undo step. Writes a recipe into the project (scope, thresholds, ticked families, names, splits, single-protein overrides, release, program version) | H | 4 | E5, E6, F5 | E12, E13, A1, C4 | Applying the recipe twice gives byte-identical assignments; undo restores everything; recipe survives save and reopen | EV | not started | 0% | 0% | T+44 |
| E12 | File check after every save: reopen the saved file and compare sequences, decoys, unassigned proteins, locks, source hash. Result shown on screen. Replaces the checks in `make_draft_fcf.cs` | M | 2 | E11 | A1 | A corrupted fixture is caught; a good file reports passed | EV | not started | 0% | 0% | T+46 |
| E13 | Exports from the program: audit CSV (family, number, previous family, accession, locus, description, reason, domains, length, cysteines, reviewed, fragment, annotated, UniProt family) and left-out CSV. RFC-4180, InvariantCulture | L | 2 | E10, E11 | A1 | Columns match the delivered CSVs; suite pins them | EV | not started | 0% | 0% | T+48 |
| E14 | Science gate: the whole rule against the frozen Python classes, as a checker only. Every disagreement listed and judged on biology and literature. The rule is never tuned to copy Python; a real error found in Python is recorded as one | VH | 5 | E4, E6, E7, E9, C4 | A1, E15 | All 256 reviewed venom entries consistent; every difference has a written verdict approved by GR | EV | not started | 0% | 0% | T+53 |
| E15 | Method written up: Help page, `SCIENTIFIC_METHOD.md`, what is new compared with word and keyword grouping | L | 2 | E14 | R1 | Help opens; every number in it comes from the validation report | EV | not started | 0% | 0% | T+55 |

### W2 Ana's result, through the software

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| A0 | Hold the Python-made files in the Gmail draft to Ana. Nothing more goes to her until A3 | L | 0 | 🟢 | A3 | Paulo confirms the draft stays unsent | Paulo | waiting: Paulo | 0% | 0% | now |
| A1 | Make her file with the released program on her original `.fcf`: Fetch Evidence, review, Create, Save, Export. Provenance shows version, recipe and release | M | 2 | E12, E13, E14, C4, R1 | A2 | Recipe reapplied gives the same file; file check passed; no script touched it | EV | not started | 0% | 0% | T+58 |
| A2 | Adversarial science review of the file: concepts, denominators, labels, red flags, comparison with what Ana already holds | H | 3 | A1, E9 | A3 | GR's written verdict; every finding closed | GR | not started | 0% | 0% | T+61 |
| A3 | Paulo sends Ana the program's file with a short note that it replaces the draft | L | 0 | A0, A2 | A4 | Sent by Paulo | Paulo | not started | 0% | 0% | T+62 |
| A4 | Ana's answers: is the family list right; keep disintegrins separate (default yes, one click to reverse); a `.sepr2` on this database | L | 0 | A3 | A5 | Answers recorded in the recipe | Ana | not started | 0% | 0% | her pace |
| A5 | Measure the shared-peptide effect on her data, through Compute full XIC | M | 2 | A4 | Paper numbers | Numbers exported by the program | EV | not started | 0% | 0% | her pace |

### W3 Look

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| V1 | Two directions on the real curation screen, dark and light, on the Fluent base. Paulo picks the default; the other stays as a View option | M | 3 | F3 | V2 | Renders delivered; Paulo's pick logged. Work continues on the recommended one meanwhile | LK | not started | 0% | 0% | T+6 |
| V2 | Tokens file (colour, spacing, type, radius) and Fluent base through `ThemeMode`; drop the custom Button template and the gold focus ring; light, dark and system | H | 5 | V1 | V3, V4, V5, V7, C4 | Windows 11 and 10 tested; suite green; no gold left in `App.xaml` | LK | not started | 0% | 0% | T+11 |
| V7 | Type scale, spacing and copy rules applied to shared styles; sentence case; one primary button per panel | M | 2 | V2 | V3, all screens | Renders checked against design rules 1 to 7 | LK | not started | 0% | 0% | T+13 |
| V3 | Zero hard-coded colours: 80 in XAML, 63 in code outside `ChartPalette`. A suite check fails on any new one | H | 6 | V2 | V4, all screens | Guard red on a planted colour; 0 sites left; golden equal | LK | not started | 0% | 0% | T+19 |
| V4 | Table style for all grids: rows, header, hover, selection, numbers right-aligned, cut-off text shown in a tooltip | M | 3 | V3 | C1, C2, C4 | Renders in both themes at both sizes; header readable | LK | not started | 0% | 0% | T+22 |
| V5 | Emoji out, Segoe Fluent Icons in | L | 2 | V3 | S1, all screens | Suite check fails on an emoji in UI text | LK | not started | 0% | 0% | T+24 |
| V6 | Charts follow the tokens in both themes; `ChartPalette` stays the one source; pinned contrast checks kept | M | 3 | V3, C9 | C14 | Contrast check passes; exported PNGs unchanged in the Light palette | LK | not started | 0% | 0% | T+81 |

### W4 Shell

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| S1 | Menu in standard order: File (New from database, Open, Save, Save as, Export, Recent, Exit), Edit (Undo, Copy), View (theme, hide decoys, hide contaminants), Analysis (Compute, Results, De novo, Reannotate SEPro), Help. Ctrl+N, O, S, Z, F, F1 | M | 3 | V5 | S2, S3, S5 | File is first; each old command reachable; shortcut tests; "Reanotate" typo gone | LK | not started | 0% | 0% | T+27 |
| S2 | Left navigation rail in place of the Home card grid: Families, Quantify, Results, De novo, with state marks | H | 4 | S1, S4 | C1, C8, C9, C10 | Every screen reachable by keyboard; disabled steps readable with the reason | LK | not started | 0% | 0% | T+55 |
| S3 | Replace both yellow banners and the "showing" line with one quiet status bar; long text goes to Help | M | 2 | S1 | S2 | No banner over one line at the smallest size; no fact lost, each moved to Help | LK | not started | 0% | 0% | T+48 |
| S4 | Start page: open, new, recent projects, last result; fixes the white disabled cards | M | 3 | S1 | S2 | Disabled state readable in both themes; recents survive restart | LK | not started | 0% | 0% | T+51 |
| S5 | One Save: `.fqproj` is the file, `.fcf` stays as Export and still opens. Unsaved mark, prompt on close. Ana's file goes from 70 MB to about 16 MB | H | 4 | S1, E12 | R2 | Old `.fcf` opens; project round trip byte-equal in curation; a curation-only project opens with no quant | LK | not started | 0% | 0% | T+59 |
| S6 | One busy overlay with progress and Cancel for every long operation | M | 3 | S2 | C8 | Cancel leaves the file as it was; suite covers each operation | LK | not started | 0% | 0% | T+62 |
| S7 | Window size and last folder remembered; drag a file to open | L | 1 | S1 | R2 | Survives restart | LK | not started | 0% | 0% | T+63 |

### W5 Screens

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| C4 | Group by evidence window: families on the left with ticks and names, proteins on the right with reason, tier, flags, split, motif box, left-out tab. Same window as the Super Cluster preview, with a keyword mode, so the ticks, red rows and copy all stay | VH | 7 | E5, V4, S1 | E14, A1 | Whole flow by keyboard and mouse in the suite; 31,777 proteins scroll smoothly; Cancel changes nothing | LK | not started | 0% | 0% | T+34 |
| C3 | Keyword boxes only in that window: one "Group by keywords" command on the screen | L | 1 | C4 | C1 | Three duplicate inputs gone; suite re-pointed | LK | not started | 0% | 0% | T+35 |
| C1 | Curation layout: table always in view, actions in a side panel, no Controls and Data Browser tabs, no "Layer" words. USearch ID column hidden by default, data kept. Decoys hidden by default; Locus shows the accession | H | 6 | C3, V4 | C2, C5 | Pressing any action shows its result without changing view; suite green; golden equal | LK | not started | 0% | 0% | T+41 |
| C2 | Family view: a table of families (name, proteins, shares, lock) with the proteins of the selected family beside it | H | 5 | C1 | R2 | Selection, edit, lock and undo work on both tables; 39,996 families smooth | LK | not started | 0% | 0% | T+46 |
| C5 | Quantitation panel: Layers 2 and 3 merged; Full XIC first, PatternLab as a secondary source, options shown only when they apply | M | 3 | C1, S2 | R2 | Every option kept; golden equal | LK | not started | 0% | 0% | T+66 |
| C6 | Build families screen: one flow, one explanation line, button labels that say what they do ("Input a FASTA file" and "Run USearch" go); Remove Redundancy kept | M | 3 | S2 | C7 | All functions kept; renders pass | LK | AS | 0% | 0% | T+69 |
| C7 | One "find best identity" flow, if the check passes: first prove Scan gives Divide and Conquer's answer on the Bjararaca and Viperidae files, otherwise both stay | M | 2 | C6 | R2 | Equivalence measured and written down; `FDR` still reported | LK | AS | 0% | 0% | T+71 |
| C8 | Quantify screen instead of the Compute dialog; one run summary | M | 3 | S2, S6 | C10 | Same inputs, same outputs; golden equal; run summary kept | EV | AS | 0% | 0% | T+61 |
| C9 | Results screen: Analytics paragraph becomes short summary cards with details on demand; one primary export | M | 4 | S2 | V6, K1 | Every fact in the paragraph still reachable; golden equal | EV | AS | 0% | 0% | T+65 |
| C10 | De novo uses the filters Compute uses, so the screen can no longer disagree with the run | M | 3 | C8 | C11 | One source of filters; test proves it | EV | AS | 0% | 0% | T+68 |
| C11 | Retire Extract areas and Partition ion current from the De novo screen (superseded by Full XIC, and a second recovery on another basis). Homology CSV moves to Results. Only if Ana and Richard do not use them | M | 2 | C10, P2 | K2 | Answer recorded; screen reduced; homology CSV still exported | EV | AS | 0% | 0% | T+70 |
| C12 | Secondary windows restyled: Lock manager, Spectrum table, Identity scan, Text report, Help | M | 4 | V4 | C14 | All render clean in both themes | LK | AS | 0% | 0% | T+75 |
| C13 | Wording pass: labels, tooltips (one or two sentences) and Help match the new screens | M | 3 | C1, C5, C6, C8, C9 | C14 | Suite's tooltip checks green; no old screen named anywhere | LK | not started | 0% | 0% | T+78 |
| C14 | Finish: 100, 125 and 150% scaling, smallest window, Tab order, focus visible, both themes | M | 2 | C12, C13, V6 | R2 | Harness renders at each scale; Paulo's final look sign-off | LK | not started | 0% | 0% | T+84 |

### W6 Cleanup

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| K1 | Paper and talk figures (`PresentationFigures`, `PresentationEvidencePack`, 3,703 lines) move to a separate tool that reads a `.fqproj`. The studies Compute calls stay | H | 4 | C9 | R3 | Same evidence pack, byte for byte, from the tool; not in the ClickOnce payload | EV | AS | 0% | 0% | T+74 |
| K2 | Dependencies: `Extended.Wpf.Toolkit` (6 uses), `Newtonsoft.Json` (3 files, the rest is `System.Text.Json`), `OpenMcdf` (no reference in this repo), `LiteDB` (after C11). Each goes only if a real RAW and `.xic2` still open | M | 3 | C11, C12 | R3 | Publish rehearsal runs; real files open | EV | AS | 0% | 0% | T+77 |
| K3 | Finish pulling logic out of `ClusterGenerationControl` and `DeNovoControl` (4,307 lines) into pure classes, only what C1, C2 and C10 left behind | M | 3 | C2, C10 | R3 | Line counts reported; golden equal | EV | AS | 0% | 0% | T+80 |
| K4 | `CLAUDE.md` from 50 KB to a working guide, history to `docs/HISTORY.md`, talk documents out of the repo root | L | 2 | K3 | R3 | No fact lost, no line over 300 characters | EV | AS | 0% | 0% | T+82 |

### W7 Releases (Paulo publishes each one)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| R1 | Release 1.6: evidence engine and review window; version bumped in both files | M | 1 | E15, C4 | A1 | Full suite, golden, publish rehearsal, release notes | Orchestrator | not started | 0% | 0% | T+56 |
| R2 | Release 1.7: new look and shell | M | 1 | C2, C5, C7, S5, S7, C14 | R3 | Same, plus Paulo's sign-off on renders | Orchestrator | not started | 0% | 0% | T+85 |
| R3 | Release 1.8: cleanup | M | 1 | K1, K2, K3, K4 | R4 | Same | Orchestrator | not started | 0% | 0% | T+86 |
| R4 | Final adversarial pass on the whole product, then the archive of this roadmap | H | 3 | R3 | Done | GR's verdict; all findings closed | GR | not started | 0% | 0% | T+89 |

## Critical path

F1, E1, E2, E3, E5, then both E6 and C4 (C4 also needs V2, V3, V4, S1), then E11, E12, E13, E14, R1, A1, A2, A3. That is about T+62 of worker time for Ana's new file, a rough estimate. Sol is thorough and slow, so expect a range of T+55 to T+90.

## Paulo's decisions

| ID | Decision | Default if no answer | Needed by |
|---|---|---|---|
| P1 | Light or dark as the default look (V1) | Work continues on my recommendation; the other stays as a View option | Before C14 |
| P2 | Do Ana and Richard use the De Novo sections Extract areas and Partition ion current? | They stay until someone says they can go | Before C11 |
| P3 | Hold the Python-made draft to Ana (A0) | Held | Now |

## Risks and triggers

| Risk | Trigger | Response |
|---|---|---|
| The general rule cannot separate ADAM from SVMP | E14 shows reviewed ADAMs left in SVMP after E4, E6, E7 | Show them as flagged conflicts for the user. Never add a name rule. Tell Paulo |
| Fluent theme fails on a user's Windows | V2 test fails on Windows 10 | Keep tokens, replace the base with our own control styles |
| UniProt changes or is down | E2 fails live | Stored evidence keeps working; fetch stays all or nothing |
| Non-UniProt headers | E4 test scores poorly | Report it plainly in the review window; proteins stay unassigned, never guessed |
| A GUI step moves a number | Golden check red | No merge; the step goes back to its owner |
