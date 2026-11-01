# FamilyQuant 2.0 roadmap (remaining work only)

Version 2, 2026-11-01. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file at the next refresh and go to `docs/ROADMAP-archive.md`.

## The goal, in Paulo's words

- "lets call this familyquant 2.0", "make it a completely new git repo", "ideally, i think a new project, so we dont lose reference"
- "most of the code will be copy paste, but knowing ana, i know she will sometimes want to resort to the old version"
- "this is also a chance to do a complete re-write and tweak code as well without missing any function and improving on things"
- "using python scripts was cheating, every result passed to ana needs to be through the software"
- "things need to look slick and clean", "the colors look terrible, we have white with yellow in some parts"
- "the file menu usually comes first"

## Rules every worker follows

1. **Only the program makes results.** Python may be used to investigate or to check a number, never to make one that reaches a person. Scratch probes count as scripts: nothing built from a probe is delivered.
2. **No exception lists.** No table of families, no protein names, no Pfam, PANTHER or keyword literal in the rule. Evidence and data decide; what a user chooses is stored in the project. No LLM in the product.
3. **No function is lost.** The feature ledger (N3) lists everything 1.5.0.6 does. It is the contract: a release cannot pass with a row open, and only Paulo can drop a row.
4. **Numbers stay equal to 1.5.0.6 unless a step says otherwise.** Any module may be rewritten. The golden check (F2) must stay byte-identical against the frozen reference. A step that means to change a number says so in its row, shows before and after on real data, and passes a science review.
5. **Copy what is good, rewrite what is better.** Every file is read as it moves. Simpler, faster or clearer code replaces it when the tests pin the behaviour first. Rewriting for its own sake is not a goal.
6. **1.5 stays available.** 2.0 is a separate project that installs beside 1.5 and never touches it. 2.0 opens every old file, and it can export a `.fcf` that 1.5 opens, so Ana can go back at any moment. Old repository: `pcarvalho75/FamilyQuant`, frozen as the 1.x reference. The 2.0 repository `pcarvalho75/FamilyQuant2` is private, like the old one, and stays private: it carries third-party binaries, collaborator names and working notes.
7. **Deployable at every merge.** One step per commit, with its test. Paulo publishes and pushes; nobody else pushes.
8. **A step counts as done only after the reviewer approves it** and it is merged and green.
9. **The deck stays untouched:** `BrProt_FamilyQuant_FourPart_Source_Decomposition.pptx` and its sources, until Paulo says otherwise.
10. **No em dashes or en dashes** in anything written, UI text included.

## The team

This team is exclusive to FamilyQuant: five Sol workers, one Opus worker and two reviewers, Grok and Gemini, sharing no workers, folders or tools with Project Orion.

| Name | Engine | Role | Worktree | Branch |
|---|---|---|---|---|
| Orchestrator | Claude, main session | Assigns, merges, runs gates, keeps this file true. Writes no product code. | | master |
| FQEvidence (EV) | Sol 6.1 xhigh | Golden check, evidence model, fetch, classifier, apply, Ana's file, then the quantitation and project rewrite | `D:/FamilyQuant-wt/evidence` | `fq/evidence` |
| FQLook (LK) | Sol 6.1 xhigh | Look, curation and review screens | `D:/FamilyQuant-wt/look` | `fq/look` |
| FQShell (SH) | Sol 6.1 xhigh | Feature ledger, shell, and the Build, Quantify, Results, De novo and secondary screens | `D:/FamilyQuant-wt/shell` | `fq/shell` |
| FQAssay (AS) | Sol 6.1 xhigh | New project, ledger, architecture, neighbour search, coherence, validation, then core rewrite | `D:/FamilyQuant-wt/assay` | `fq/assay` |
| FQResults (RS) | Sol 6.1 xhigh | Word grouping and chart rewrites, then Quantify, Results and De novo screens | `C:/FamilyQuant-wt2/results` | `fq/results` |
| FQOpus (OP) | Opus 5.5 xhigh | Evidence checks: nearest neighbour, reference panel, coherence, motifs, validation, left-out list | `D:/FamilyQuant-wt/opus` | `fq/opus` |
| FQReview (GR) | Grok 4.7 xhigh | Reviews steps in turn with FQArgon, code and science. Never edits product code. | `D:/FamilyQuant-wt/review` | detached |
| FQArgon (AR) | Gemini 4 Argon high | Reviews steps in turn with FQReview, code and science. Never edits product code. | `D:/FamilyQuant-wt/argon` | detached |

Flow of one step: owner posts `READY <step> <commit>`; the orchestrator assigns it to GR or AR in turn in `D:/FamilyQuant-data/reviews/CLAIMS.md`; that reviewer posts `APPROVED <step> <commit> <reason>` or `REJECTED <step> <commit> <findings>`; a rejection is the owner's next commit, red then green, and the other reviewer checks it; the orchestrator merges, builds, runs the suite, and only then the row closes.

## Target layout

| Project | Holds | Rule |
|---|---|---|
| `FamilyQuant.Core` | Everything without WPF: families, clustering, grouping, evidence, quantitation, de novo, homology, project files. 69 of today's files already qualify | No WPF, no message boxes, reports progress and accepts cancel |
| `FamilyQuant.App` | WPF screens, each a thin view over a tested view model; assembly `FamilyQuant2` | No science in the UI |
| `FamilyQuant.Reports` | Paper and talk figures, a separate tool reading a `.fqproj` | Never in the installed app |
| `FamilyQuant.Tests` | The suite, split by area | Runs headless |

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
10. **Base:** the Windows 11 Fluent theme built into .NET 10 (`ThemeMode`), no third-party theme. A trial on 2026-11-01 restyled menus, tabs, inputs and combo boxes with no other change. It also left the table header white on white, so the theme only works once hard-coded colours are gone. The API is marked experimental, so V2 records the warning suppression. Ana and Richard run Windows 11 with .NET 10 (Paulo, 2026-11-01), so Windows 11 is the target.

## Workstreams

Columns: Diff = L, M, H, VH. Hrs = worker hours, my estimate; the first steps ran several times faster than estimated. Blocked by: 🟢 = open now. ETA = hours after launch in that worker's queue, a rough estimate and never a promise. Prog = my estimate of work done. Cred = counted only after approval and merge. EV, LK, AS, SH, RS, OP, GR and AR are the workers above.

### W0 Foundation and the new project

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W1 Evidence engine: what the Python scripts did, now inside the program

What moves, script by script: `grouping_fetch.py` to E2. `exp1` and `exp2` (nearest labelled neighbour, never itself) to E3 and E4. `exp3` (domain composition, subclass by length and cysteines, name against evidence) to E6 and E7. `exp4` (domains that mark a family, read from the data) to E5. `exp5` and `exp6` (the rules, the reason for each protein, the route comparison, leave-one-out) to E5, E6 and E9. `exp7` and `exp8` (biology checks, rescues, left-out list) to E7, E8 and E10. `make_draft_fcf.cs` (numbers, annotations, locks, audit CSV, reopen-and-compare) to E11, E12 and E13. `words_route_probe.cs` to E9. New code goes in `FamilyQuant.Core/Evidence/`.

Where the Python broke rule 2 and the program must not: the 27-row family table, the Pfam code list, the ADAM name regex and `TM and over 700 aa`. Their replacements: family names come from UniProt and the user ticks them (E5, C4), architecture is learned (E6), and outliers are learned from reviewed members (E7).

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| E6 | Architecture split, general: inside a family, group proteins by domain architecture learned from the data, offer each group as a split with a name from its dominant domain. SVMP against free disintegrin becomes one user choice, stored in the recipe. No domain code in the rule | H | 4 | E5 | E11, E14, C4 | On Ana's file the split separates free disintegrins with no literal; `Disintegrin` count within the differences E14 explains; a second family splits or stays whole by the same code | EV | not started | 0% | 0% | T+4 |
| E11 | Apply: new numbers past every number in use, annotations, locks, decoys and unassigned proteins untouched, one undo step. Writes a recipe into the project (scope, thresholds, ticked families, names, splits, single-protein overrides, release, program version) | H | 4 | E5, E6, F5 | E12, E13, A1, C4 | Applying the recipe twice gives byte-identical assignments; undo restores everything; recipe survives save and reopen | EV | not started | 0% | 0% | T+8 |
| E12 | File check after every save: reopen the saved file and compare sequences, decoys, unassigned proteins, locks, source hash. Result shown on screen. Replaces the checks in `make_draft_fcf.cs` | M | 2 | E11 | A1, S5 | A corrupted fixture is caught; a good file reports passed | EV | not started | 0% | 0% | T+10 |
| E13 | Exports from the program: audit CSV (family, number, previous family, accession, locus, description, reason, domains, length, cysteines, reviewed, fragment, annotated, UniProt family) and left-out CSV. RFC-4180, InvariantCulture | L | 2 | E10, E11 | A1 | Columns match the delivered CSVs; suite pins them | EV | OP | 0% | 0% | T+19 |
| E14 | Science gate: the whole rule against the frozen Python classes, as a checker only. Every disagreement listed and judged on biology and literature. The rule is never tuned to copy Python; a real error found in Python is recorded as one | VH | 5 | E4, E6, E7, E9, C4 | A1, E15 | All 256 reviewed venom entries consistent; every difference has a written verdict approved by GR | EV | OP | 0% | 0% | T+17 |
| E15 | Method written up: Help page, `SCIENTIFIC_METHOD.md`, what is new compared with word and keyword grouping | L | 2 | E14 | R1 | Help opens; every number in it comes from the validation report | EV | OP | 0% | 0% | T+21 |
| OP1 | Long evidence steps on the operation path: the neighbour search and the panel fetch get progress and Cancel. Ana's file takes 398 s with neither | M | 1 | X10 | S6, A1 | Cancel leaves the file as it was; progress shown on her real file | OP | not started | 0% | 0% | T+22 |

### W2 Ana's result, through the software

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| A1 | Make her file with 2.0 preview 1 on her original `.fcf`: Fetch Evidence, review, Create, Save, Export. Provenance shows version, recipe and release | M | 2 | E12, E13, E14, C4, R1 | A2 | Recipe reapplied gives the same file; file check passed; no script touched it | EV | not started | 0% | 0% | T+24 |
| A2 | Adversarial science review of the file: concepts, denominators, labels, red flags, comparison with what Ana already holds | H | 3 | A1, E9 | A3 | GR's written verdict; every finding closed | GR | not started | 0% | 0% | T+27 |
| A3 | Paulo sends Ana a second email, the same day if the team gets there: FamilyQuant 2.0 preview 1 and the program's file, with a short note that it replaces the file she received and that 1.5 stays installed | L | 0 | A2 | A4 | Sent by Paulo | Paulo | not started | 0% | 0% | T+27 |
| A4 | Ana's answers: is the family list right; keep disintegrins separate (default yes, one click to reverse); a `.sepr2` on this database | L | 0 | A3 | A5 | Answers recorded in the recipe | Ana | not started | 0% | 0% | her pace |
| A5 | Measure the shared-peptide effect on her data, through Compute full XIC | M | 2 | A4 | Paper numbers | Numbers exported by the program | EV | not started | 0% | 0% | her pace |

### W3 Look

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| V7 | Type scale, spacing and copy rules applied to shared styles; sentence case; one primary button per panel | M | 2 | V2 | V4, all screens | Renders checked against design rules 1 to 7 | LK | not started | 0% | 0% | T+2 |
| V4 | Table style for all grids: rows, header, hover, selection, numbers right-aligned, cut-off text shown in a tooltip | M | 3 | V7 | C1, C2, C4 | Renders in both themes at both sizes; header readable | LK | not started | 0% | 0% | T+5 |
| V5 | Emoji out, Segoe Fluent Icons in | L | 2 | V7 | S1, all screens | Suite check fails on an emoji in UI text | LK | not started | 0% | 0% | T+14 |
| V6 | Charts follow the tokens in both themes; `ChartPalette` stays the one source; pinned contrast checks kept | M | 3 | X8, C9 | V3 | Contrast check passes; exported PNGs unchanged in the Light palette | LK | not started | 0% | 0% | T+33 |
| V3 | Zero hard-coded colours: a suite check fails on any colour outside tokens and the chart palette. Each rebuilt screen must pass it; this row closes when the last one does | H | 2 | C1, C5, C6, C8, C9, C10, C12, S4 | C14 | Guard red on a planted colour; 0 sites left; golden equal | LK | not started | 0% | 0% | T+35 |

### W4 Shell

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| S6 | One busy overlay with progress and Cancel for every long operation, on the path from X10 | M | 3 | S2, X10 | C8, R2 | Cancel leaves the file as it was; suite covers each operation | LK | SH | 0% | 0% | T+6 |

### W5 Screens: each rebuilt as a thin view over a tested view model

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| C4 | Group by evidence window: families on the left with ticks and names, proteins on the right with reason, tier, flags, split, motif box, left-out tab. Same window as the Super Cluster preview, with a keyword mode, so the ticks, red rows and copy all stay | VH | 7 | E5, V4, S1 | E14, A1 | Whole flow by keyboard and mouse in the suite; 31,777 proteins scroll smoothly; Cancel changes nothing | LK | not started | 0% | 0% | T+12 |
| C3 | Keyword boxes only in that window: one "Group by keywords" command on the screen | L | 1 | C4 | C1 | Three duplicate inputs gone; suite re-pointed | LK | not started | 0% | 0% | T+15 |
| C1 | Curation rebuilt: table always in view, actions in a side panel, no Controls and Data Browser tabs, no "Layer" words. USearch ID column hidden by default, data kept. Decoys hidden by default; Locus shows the accession. `ClusterGenerationControl` (1,978 lines) replaced by a view model and a thin view | VH | 7 | C3, V4, F6 | C2, C5, V3 | Pressing any action shows its result without changing view; every ledger row of the screen checked; golden equal | LK | not started | 0% | 0% | T+22 |
| C2 | Family view: a table of families (name, proteins, shares, lock) with the proteins of the selected family beside it | H | 5 | C1 | R2 | Selection, edit, lock and undo work on both tables; 39,996 families smooth | LK | not started | 0% | 0% | T+27 |
| C5 | Quantitation panel: Layers 2 and 3 merged; Full XIC first, PatternLab as a secondary source, options shown only when they apply | M | 3 | C1, S2 | V3, C13 | Every option kept; golden equal | LK | not started | 0% | 0% | T+30 |
| C6 | Build families screen: one flow, one explanation line, button labels that say what they do ("Input a FASTA file" and "Run USearch" go); Remove Redundancy kept | M | 3 | S2, X3 | C7, V3 | All ledger rows kept; renders pass | AS | SH | 0% | 0% | T+9 |
| C7 | One "find best identity" flow, if the check passes: first prove Scan gives Divide and Conquer's answer on the Bjararaca and Viperidae files, otherwise both stay | M | 2 | C6 | R2 | Equivalence measured and written down; `FDR` still reported | AS | SH | 0% | 0% | T+11 |
| C10 | De novo uses the filters Compute uses, so the screen can no longer disagree with the run. `DeNovoControl` (2,329 lines) replaced by a view model and a thin view | H | 5 | C8 | C11, V3 | One source of filters; test proves it; ledger rows checked | EV | RS | 0% | 0% | T+5 |
| C11 | Retire Extract areas and Partition ion current from the De novo screen (superseded by Full XIC, and a second recovery on another basis). Homology CSV moves to Results. Paulo decided on 2026-11-01 to retire them; 1.5 keeps them | M | 2 | C10 | X11 | Ledger rows marked DROP by Paulo's word; homology CSV still exported | EV | RS | 0% | 0% | T+7 |
| C12 | Secondary windows rebuilt: Lock manager, Spectrum table, Identity scan, Text report, Help | M | 4 | V4 | V3, C14 | All render clean in both themes | AS | SH | 0% | 0% | T+15 |
| C13 | Wording pass: labels, tooltips (one or two sentences) and Help match the new screens | M | 3 | C1, C5, C6, C8, C9 | C14 | Suite's tooltip checks green; no old screen named anywhere | LK | not started | 0% | 0% | T+38 |
| C14 | Finish: 100, 125 and 150% scaling, smallest window, Tab order, focus visible, both themes | M | 2 | C13, V3, V6 | R2 | Harness renders at each scale; Paulo's final look sign-off | LK | not started | 0% | 0% | T+40 |

### W6 Core rewrite: copy what is good, rewrite what is better

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| X3 | Clustering: `USearchTools` and `RedundancyTools` with clear result types, cancel and progress | H | 3 | X2, X10 | C6 | Same families frozen against current: Bjararaca exhaustive, Viperidae fast at identity 85 (exhaustive is about 2 billion pairs, ruled out on 2026-11-01); memo and gate tests kept | AS | not started | 0% | 0% | T+6 |
| X5 | Quantitation pipeline: `FullXicComputer` (997) and `FamilyEnvelopeQuant` (968) restructured into stages with clear inputs and outputs. The studies Compute calls stay and get their own folder | VH | 6 | F6, X10, C8 | R3 | Golden equal on the full project; every invariant in the guide has a test; run time not worse | EV | OP | 0% | 0% | T+28 |
| X6 | De novo engine tidy: readers, reconciler and table builders, with the logic left in `DeNovoControl` moved into Core | H | 3 | C10 | R3 | Golden equal; real-data de novo sections green | EV | not started | 0% | 0% | T+31 |
| X7 | Project and file formats: `ProjectStore` (1,083) split, schema versioned, and an `.fcf` export the frozen 1.5.0.6 opens | H | 4 | X2, E12 | S5, R2 | A file written by 2.0 opens in the reference build with the same families; old `.fqproj` rules kept | EV | not started | 0% | 0% | T+28 |
| X9 | Paper and talk figures (`PresentationFigures`, `PresentationEvidencePack`, 3,703 lines) move to `FamilyQuant.Reports`, a separate tool reading a `.fqproj` | H | 4 | C9, X8 | R3 | Same evidence pack, byte for byte, from the tool; not in the ClickOnce payload | EV | not started | 0% | 0% | T+35 |
| X10 | One path for errors, progress and cancel in Core; no message box below the App layer | M | 3 | F6 | X3, X5, S6 | A suite check fails on `MessageBox` in Core; each long operation cancels cleanly | AS | not started | 0% | 0% | T+3 |
| X11 | Dependencies: `Extended.Wpf.Toolkit` (6 uses), `Newtonsoft.Json` (3 files, the rest is `System.Text.Json`), `OpenMcdf` (no reference in this repo), `LiteDB` (after C11). Each goes only if a real RAW and `.xic2` still open | M | 3 | C11, C12 | R3 | Publish rehearsal runs; real files open | AS | not started | 0% | 0% | T+18 |
| X12 | Improvements found on the way, listed in `docs/IMPROVEMENTS.md` with before and after. Known candidates: the fast clustering pass has no coverage gate (20.1% of its joins, 2,329 of 11,584, fail the second pass's gate); the decoy mixing score cannot see over-merging; whether the embedded USEARCH 11 binary can be replaced by open-source VSEARCH, because the USEARCH licence restricts sharing copies and a swap may change families. Anything that changes a number goes to Paulo first | H | 4 | X3, X4, X5 | R3 | Each change measured on real data; science review; golden snapshot updated only by Paulo's word | AS | not started | 0% | 0% | T+32 |
| X13 | Documents: `CLAUDE.md` from 50 KB to a working guide, history to `docs/HISTORY.md`, talk documents out of the root, release notes for 2.0 | L | 2 | X12 | R3 | No fact lost, no line over 300 characters | AS | not started | 0% | 0% | T+34 |

### W7 Releases (Paulo publishes each one)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| R1 | 2.0 preview 1: evidence engine and review window, for Ana's file; installs beside 1.5 | M | 1 | E15, C4, N1 | A1 | Full suite, golden, publish rehearsal, release notes | Orchestrator | not started | 0% | 0% | T+22 |
| H1 | Cooldown at the middle, by the `halt-team-for-regroup` skill. It starts when preview 1 (R1) is merged, or at 40 of 80 steps, whichever comes first. Everyone finishes the step in hand and stops, all branches merge, housekeeping, duplicate-code review, the roadmap is readjusted from measured speed. The team comes back only on Paulo's word | H | 2 | R1 | R2, R3 | All worker branches merged; full suite and golden green; ledger status reported; roadmap version 3 written; Paulo signs off | Orchestrator | not started | 0% | 0% | T+24 |
| R2 | 2.0 preview 2: new look and shell | M | 1 | H1, C2, C5, C7, S5, S6, S7, C14 | R3 | Same, plus Paulo's sign-off on renders | Orchestrator | not started | 0% | 0% | T+41 |
| R3 | 2.0 release candidate: rewrite finished, every ledger row closed | M | 1 | R2, X4, X5, X6, X9, X11, X12, X13 | R4 | Same, plus the ledger check | Orchestrator | not started | 0% | 0% | T+42 |
| R4 | Final adversarial pass on the whole product | H | 3 | R3 | R5 | GR's verdict; all findings closed | GR | not started | 0% | 0% | T+45 |
| R5 | FamilyQuant 2.0 final, then the archive of this roadmap | L | 1 | R4 | Done | Paulo publishes | Orchestrator | not started | 0% | 0% | T+46 |

## Cooldown

One cooldown sits in the middle of the project (H1). Work does not wait for it: every worker keeps taking its next step until it starts, so nobody sits idle beforehand.

## Critical path

F4, F5, N1, N3, N4, F6 for the structure. E1, E2, E3, E5, then both E6 and C4 (C4 also needs V2, V7, V4, S1), then E11, E12, E13, E14, E15, R1, A1, A2, A3 for Ana's file. That is about T+50 of worker time, a rough estimate. The first steps ran several times faster than estimated, so the real figure may be well below that; do not plan on it yet.

## Paulo's decisions

| ID | Decision | Default if no answer | Needed by |
|---|---|---|---|

## Risks and triggers

| Risk | Trigger | Response |
|---|---|---|
| The general rule cannot separate ADAM from SVMP | E14 shows reviewed ADAMs left in SVMP after E4, E6, E7 | Show them as flagged conflicts for the user. Never add a name rule. Tell Paulo |
| A rewrite loses a function | A ledger row has no check, or a user finds a gap | No release passes; the row goes back to the step that dropped it |
| A rewrite moves a number | Golden check red | No merge; the step goes back to its owner, or follows rule 4 if the change is meant |
| Ana needs 1.5 while 2.0 is unfinished | She asks | 1.5 stays installed and untouched, and `.fcf` export (X7) carries her work back |
| UniProt changes or is down | E2 fails live | Stored evidence keeps working; fetch stays all or nothing |
| Non-UniProt headers | E4 test scores poorly | Report it plainly in the review window; proteins stay unassigned, never guessed |
