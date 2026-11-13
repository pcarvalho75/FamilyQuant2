# FamilyQuant 2.0 roadmap (remaining work only)

Version 21, 2026-11-12 17:50 (Pacific), release 2.1.18, ready by early morning 11-13 in Brazil. Theme: keep each of Ana's families together with the program's default clicks, and let her export the report. Two workers only (D118).
Finished rows leave this file and go to `docs/ROADMAP-archive.md`; the needle restarts at zero with each version.

Version 20 delivered 40 of 41 steps, now in the archive: 2.1.16 (the Quantify crash, simpler Import family list, clearer Review grouping) and 2.1.17 (Review grouping layout). Its last open step, AP5, is carried here.

The inputs for this version:
- Ana's deck `FamilyQuant_v17_20261112.pptx` (9 slides on 2.1.17, built from her revised sheet).
- Her voice note of 11-12 (`AnaNew.ogg`, 60 s), transcribed by the desk and kept out of the repo.
- Her comment of 11-12: "Outra coisa é sobre a opção de 'Word clustering', ela não deveria vir antes da etapa de quantificação?"

## The goal, in Paulo's and Ana's words

- Paulo, 11-12 17:40: "study the slides and come up with a new roadmap to address all the issues, we will need to have the results by early tomorrow so it will need to be a very direct approach"
- Paulo, 11-12 17:40: "we will be able to count only with 2 workers for this, 1 sol and one opus from the juliana account" (D118)
- Ana, slide 4: "Após o agrupamento por evidência, muitas 'metallo' ainda ficaram separadas do grupo principal (#40430, com 232 entradas / o banco tem mais de 800 entradas com metalloproteinase, metalloprotease, SVMP)"
- Ana, slide 8: "O agrupamento das metallo não está legal, o gráfico não corresponde à realidade do Veneno. Já conseguimos resultado muito melhor anteriormente, mesmo sem usar palavras para clusterizar."
- Ana, 11-12 voice note: "eu já fiz isso nas versões anteriores sem mexer em nada manualmente, aceitando tudo que o programa trazia, e as metálogas ficaram mais bem agrupadas"
- Ana, slide 9: "Quando mando exportar o report html, dá este erro"
- Ana, slide 2: "Acho que o default deveria ser tudo confirmado, com a possibilidade de desfazer a confirmação depois"

The bar for 2.1.18 (GA5 checks it on her data): with her revised sheet and only the default clicks (Import family list, Fetch evidence, Group by evidence, Apply grouping, Quantify, Export), the import asks nothing, the metalloproteinase entries of her database sit in one family unless a stated reason keeps one out, and the HTML report exports.

## Rules every worker follows

1. **Only the program makes results.** Python may investigate or check a number, never make one that reaches a person.
2. **No exception lists.** No family, protein, Pfam or keyword literal in a rule; evidence and data decide, and what a user chooses is stored in the project. Nothing the user sees names an organism, a venom, a toxin or a sample's numbers (D21; the suite tests it). Protein family and domain names (for example serum albumin, whey acidic protein domain) are protein names, not samples (desk ruling 11-06 on Z12p2c).
3. **No function is lost.** The feature ledger is the contract: only Paulo can drop a row. A rename keeps the function; a ledger row that pins an old name is mapped in the ledger's "Added contracts after the frozen source" section, never edited to make a check pass.
4. **Numbers stay equal unless a step says otherwise.** In this version no step changes a stored quantity. GA2 (D119), GA3 (D120) and GA4 change which family some proteins get, with the desk's before and after counts on Ana's data (GA5).
5. **Deployable at every merge.** One step per commit, with its test; tests go into DefaultSuite. Builders run focused checks; the desk runs the whole suite once per release (D61 mode).
6. **A step counts as done when the other worker approves it** (Paulo, 11-05 13:41: "many approvals and no green boxes?"). Each release still ships only after its own full suite (RL7 in this version) is green on the merged commit; a suite failure reopens the step it points to until the fix is approved.
7. **A family panel is user data.** Ana's families, domains, subclasses and decisions live in her file and the project, never in code.
8. **Never write inside `C:/Users/paulo/Desktop/AnaG`** except a delivery to `AnaG/Bjararaca/Relatorio` on Paulo's word.
9. **The code repository stays private.** Releases go out as a Setup.exe and a zip on the public releases-only repository (D31, D32).
10. **No em dashes or en dashes** in anything written, UI text included.
11. **Steps stay small.** A step that hides several pieces of work is split, so the needle shows real progress.
12. **Never install on this PC** (D31): Setup runs in test mode only.
13. **Ana's data stays with Anthropic models.** The desk and FQGroup read only the desk's copies under `D:/FamilyQuant-data/out-desk`; counts leave, rows do not; nothing of hers is committed (D121).

## The team

Staffed on Paulo's word, 11-12 17:40 (D118).

| Name | Engine | Role in version 21 | Worktree |
|---|---|---|---|
| Orchestrator | Claude, main session | Routes reviews, merges, runs the release, keeps this file and the board true. Runs the real-data walk (GA5), answers slide 7 (AQ1) and builds the deck for Ana (AQ2) | |
| FQGroup (OJ) | Opus 5.5 xhigh, Juliana's account | GA1 to GA4 (her families together), EX1 and EX2 (export after curation); reviews FQDeNovo's steps | `D:/FamilyQuant-wt/group` |
| FQDeNovo (DN) | Sol 6.1 xhigh | IM10, IM11 (import as written), PO1, PO2 (panel order), NM1 (names in the report), PI1, PI2 (pie chart), the package (RL8); reviews FQGroup's steps | `D:/FamilyQuant-wt/denovo` |

FQGroup may read the desk's copies of Ana's data under `D:/FamilyQuant-data/out-desk` (D121); it posts counts, never rows, and commits none of her data. FQDeNovo sees code and synthetic data only. Nobody reads or writes `C:/Users/paulo/Desktop/AnaG`.

File ownership, so the two never touch the same file:
- OJ owns `FamilyQuant.Core/Evidence`, `FamilyQuant.Core/Panel` (Import excepted), `FamilyQuant.Core/Quantitation/PanelAuditCsv.cs`, `EvidenceReviewViewModel.cs`, `GroupingReviewViewModel*` and the protein edit paths of Curate families (Edit selected proteins, Group by keywords, Recluster by words, Undo).
- DN owns `FamilyListImport*`, `FamilyListStoredEvidenceMatches.cs`, `FamilyQuant.Core/Panel/Import`, the Grouping panel of `ClusterGenerationControl` (order and labels only), the Results screen, the Export window and the HTML report.
- A change one needs in the other's file goes through the desk.

## Design rules for the look (hard requirements)

1. **No yellow or gold anywhere,** the focus ring included. One accent colour, for the primary action and selection only.
2. **One surface family.** Every colour comes from a token.
3. **Colour means status or nothing.**
4. **One primary button per panel,** the others quiet.
5. **One line of explanation at most on a screen.** The rest goes to Help (F1) and tooltips.
6. **Standard Windows order:** File first, then Edit, View, Tools, Help (Tools replaces Analysis in Z4g).
7. **Every control explains itself on hover** (Paulo, 11-07). Every button, menu item, check box, option, input box, list, tab, expander and column header has a tooltip of one or two plain sentences: what it does, and what changes (the project, a file on disk, or nothing; whether Undo reverts it). An input box also says its meaning, unit, default and allowed range. A disabled control says why and what enables it. A tooltip never only repeats its label, uses the same terms as the Help and the website, and follows D21.

## Workstreams

Every row is a small step with its own done-check. A step is done when the other worker approves it (rule 6, D118). Ids never change; a step split here keeps its id as a prefix.

### W33 Her families stay together (FQGroup, Orchestrator)

Ana, slides 4, 5 and 8 and her voice note: the SVMP row placed 232 of its 821 matches, many metalloproteinase families stayed apart, and the C-type lectin became the largest family in her chart.

**What the desk found in the code before this roadmap (GA1 measures it):**
1. Her Family checklist row "Metaloproteinase de veneno": 821 matches, 258 reviewed, 232 placed, 589 outside. Every "Why outside" reads "The protein's keywords and tissue annotations do not match the user scope phrases" (`PanelAuditCsv.cs:109-116`).
2. The scope test reads each protein's own keywords and tissue (`EvidenceClassifier.cs:21-27`). Unreviewed entries rarely carry "Toxin" or "Venom gland", so they stay outside even when their own source family maps to her row; the audit text says so ("The protein's own source family maps to panel family ..., but its keywords and tissue annotations do not match").
3. Ties use D111: the row whose Pfam list the protein covers most completely wins. A protein with the metalloproteinase, propeptide and disintegrin domains covers the one-domain Desintegrina row completely and the four-domain SVMP row by three quarters, so it goes to Desintegrina (her slide 5 shows one such entry, "Proposed in Desintegrina").
4. The proteins of her database that match no row stay in their own sequence families: the five "Snake venom metalloproteinase" families in her top 15 (F20761, F491, F2410, F4159, F32981). Their peptides shared with the main SVMP family count as "shared between families" (25.8% + 2.0% of all peptide features in her chart).
5. The desk's 2.1.17 walk on its copy of her project reproduces it: 231 placed, 821 matches, 590 outside.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| GA1 | Decode on her data (D121). On the desk's walked copy of her project (her revised sheet, scope "Toxin", "Venom gland", 2.1.17), sort the SVMP row's 821 matches, and every in-scope protein whose description names a metalloproteinase, into: placed in SVMP; outside by the scope words; placed in another row (and which); left in its own sequence family. For each class give reviewed or not, whether its own UniProt family is the one her row names, whether it has a Pfam set, and which scope fields it lacks. Also measure whether running Fetch UniProt families before Group by evidence changes any placement. Descriptions may be counted here; they never enter a rule (rule 2) | M | 1.0 |  | GA2, GA3, GA4, PO2 | The class table, with the command that makes it, in the READY; counts only | OJ | FQGroup | 0 | 0 |  |
| GA2 | Her list wins over the scope words (D119). A protein whose own evidence maps to exactly one row of the user's list (its own UniProt family named in the row, or its Pfam set by the tie rule) joins that row even when its keywords and tissue lack the scope words. The scope words decide only for proteins that map to no row. A failing synthetic test first: a reviewed and an unreviewed member of one UniProt family, only the reviewed one carrying the scope keyword. The panel audit stops giving the scope reason for a protein whose source family maps to a row | M | 1.0 | GA1 | GA5, RL6 | The test fails on master ce442337 and passes on the branch; GA1's class "outside by the scope words" recounted on the branch, with the before and after in the READY | OJ | FQGroup | 0 | 0 |  |
| GA3 | Domain ties by overlap both ways (D120). Replace D111's measure with the overlap of the protein's Pfam set and the row's list counted over both (shared domains divided by all domains in either), so a protein with three of a four-domain row's domains goes to that row and a protein with only the disintegrin-type domain goes to the one-domain row. A stored user choice still wins; only real ties are asked. Synthetic test with both shapes, generic domain ids | M | 0.7 | GA1 | GA5, RL6 | The test fails on master and passes on the branch; the number of tie questions on her panel recounted (D111 gave 4, AP1 made it 0; still 0) | OJ | FQGroup | 0 | 0 |  |
| GA4 | Only if GA1 finds the class: in-scope proteins with no Pfam set and no UniProt family (fragments) join the row of their nearest placed relative when the identity reaches the grouping identity (70% in her project); otherwise they keep their own family, as today | M | 1.0 | GA1 | GA5, RL6 | A synthetic test, and GA1's "own sequence family" class recounted; or GA1's table shows the class is empty and the step closes with that line | OJ | FQGroup | 0 | 0 |  |
| GA5 | The desk's walk with default clicks only, as Ana works ("aceitando tudo que o programa trazia"). On a fresh copy of her project before grouping: Import family list (her revised sheet), Fetch evidence, Group by evidence, Apply grouping, Quantify with her slide 33 settings, Export HTML. Run it on 2.1.17 and on the 2.1.18 candidate | M | 1.0 | GA2, GA3, GA4, EX2, IM10, PO1 | RL8, AQ2 | Before and after in the LOG: import questions, SVMP placed and outside, the top 15 of the chart, the shared share, the HTML exported. Every metalloproteinase-described entry left outside has a stated reason | Orchestrator | Orchestrator | 0 | 0 |  |

### W34 The report exports after any curation (FQGroup)

Ana, slide 9: Export says "Family assignment evidence is missing or out of date". The check fails as soon as any protein's family differs from the one recorded at Apply grouping (`PanelAssignmentEvidenceStore.cs:88-91`, `row.FamilyUserId != target.Protein.UserID`). A protein moved with Edit selected proteins, a Group by keywords run or a Recluster by words run is enough, and her slide 4 shows Edit selected proteins open.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| EX1 | A failing test first: synthetic project, Apply grouping, then move one protein with Edit selected proteins, and separately run Group by keywords; Quantify; Export report. Today both end with the error | L | 0.3 |  | EX2 | The test fails on master ce442337 with that message | OJ | FQGroup | 0 | 0 |  |
| EX2 | The fix. A family change after Apply grouping updates the assignment facts of the proteins it moved, with the reason "Moved by you" or "Regrouped by words", so the evidence stays current and the report exports; Undo restores both. A family whose proteins all keep one list row keeps its link; a mixed family is reported without one, as the report does today for conflicts. The refusal stays for a really stale project (a changed database or panel) | M | 1.0 | EX1 | GA5, RL6 | EX1 passes; the existing refusal tests still pass; the moved protein's reason shows in the protein audit | OJ | FQGroup | 0 | 0 |  |

### W35 Her sheet is taken as written (FQDeNovo)

Ana, slide 2: the import still asks her to confirm, family by family, the evidence she wrote in her own sheet; all 38 rows "need a matching choice" and Next stays off. The desk's cause: a code from the sheet counts as known only when stored evidence lists it (`FamilyListImportViewModel.Commit.cs:85, 93-104`). She imports the list first (step 0) and fetches evidence after (step 1), so nothing is stored yet and every row asks. The desk's AP6 walk used a project that already had evidence, which is why it saw no question. Ana, slide 1: she mapped "Family name" to the English column to get an English report, found the column choices confusing, and saw Next stay off for some choices.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| IM10 | Codes as written (D122). A well-formed Pfam accession from the sheet is accepted as written whether or not evidence is stored yet; Evidence matches appears only for a cell that names a domain without a code, and never blocks Next for a row with codes. Each row can be un-confirmed later, in the import's summary and in Family checklist. A failing test first: a project with no stored evidence imports the program's template with codes and today asks for every row | M | 1.0 |  | GA5, RL6 | The test fails on master and passes on the branch: no question, the matches page skipped | DN | FQDeNovo | 0 | 0 |  |
| IM10b | The other half of IM10, in FQGroup's file (FQDeNovo's SEAM NEED, 11-12 17:54): a per-row "Un-confirm" action in Family checklist, also for rows accepted from sheet codes. It removes the family's confirmed evidence match in the review draft, updates its proposal through the existing guards and Undo, and is kept only on Apply grouping; the family's Pfam definitions stay | L | 0.4 | IM10 | RL6 | A synthetic test: un-confirm a sheet-code row, the proposal drops its matches, Undo restores, Cancel keeps nothing | OJ | FQGroup | 0 | 0 |  |
| IM11 | The Columns page shows only what the sheet has. The rows for Notes, Decision and Subclass names appear only when the sheet has such a column; when Next is off, one line under the buttons says why (a required field without a column, or two fields on one column) | L | 0.5 | IM10 | RL6 | A test per reason; a render of the page with her template's columns | DN | FQDeNovo | 0 | 0 |  |

### W36 Names in the report (FQDeNovo)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| NM1 | Ana, slide 1: "Eu quero gerar o report em inglês". The Export window gets one choice, "Family names: Name or Other-language name" (D125), stored in the project; the HTML, Word and workbook reports and the Results chart use it; a family without an other-language name keeps its Name. Her old workaround (the English column mapped as Family name) still imports | M | 1.0 | IM11 | RL6 | A test that exports with each choice; the report's family labels follow it | DN | FQDeNovo | 0 | 0 |  |

### W37 The Grouping panel in working order (FQDeNovo)

Ana's comment: "Word clustering" should come before quantification. Her slide 3 screen shows the panel as "a Group by evidence: 0 Import family list, 1 Fetch evidence, 2 Group by evidence, 3 Fetch UniProt families, 4 Quantify", then "b Group by words". Ana, slide 3: "Por que as UniProt families não vieram no Fetch evidence?"

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| PO1 | Quantify comes last (D123). The panel reads: a Group by evidence (Import family list, Fetch evidence, Group by evidence), b Group by words (optional), then Quantify on its own line under both. Labels and order only; every command keeps its function | L | 0.5 |  | GA5, RL6 | A render of the panel, and the ledger rows that name the steps still pass | DN | FQDeNovo | 0 | 0 |  |
| PO2 | Fetch UniProt families in its right place, as GA1 measured (D123). If fetching it first changes the grouping, Fetch evidence also fetches it, and the separate button goes; if it does not, it moves under b as optional, with a tooltip saying what it downloads and that grouping does not need it | L | 0.5 | GA1, PO1 | RL6 | GA1's line quoted in the READY; a render of the panel | DN | FQDeNovo | 0 | 0 |  |

### W38 A pie chart (FQDeNovo)

Ana, slide 8: "O Calvete sempre mostra o gráfico em forma de pizza, seria bom ter esta opção também".

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| PI1 | The HTML report's family chart gets a "Pie" choice beside the bars (D124): one slice per family shown, then other families, shared between families and unattributed, the same numbers as the bars, summing to 100% of all peptide features; it follows Normalize to and the names choice (NM1) | M | 1.0 | NM1 | RL6 | A test that the slices sum to 100.0 and equal the bar values; a render | DN | FQDeNovo | 0 | 0 |  |
| PI2 | The Results screen's "Family chart" list gets the same "Pie" view | M | 0.8 | PI1 | RL6 | The same test on the screen's data; a render | DN | FQDeNovo | 0 | 0 |  |

### W39 Answers for Ana (Orchestrator)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| AQ1 | Ana, slides 6 and 7: is her reading of the TIC shares right? The desk checks in the code what the resolved isotope-series diagnostic counts (charges, isotope ceiling, points per peak, any origin) and writes the answer: what holds, and what to say instead | L | 0.5 |  | AQ2 | Each statement on her slide 7 marked right or corrected, with the code line behind it, in the desk's notes | Orchestrator | Orchestrator | 0 | 0 |  |
| AQ2 | The deck for Ana in Portuguese, as AP7: one slide per point (her 9 slides, the voice note, the Word clustering comment), her words, what changed, her screen and the 2.1.18 screen on her data, and the before and after numbers of GA5 | M | 1.5 | GA5, AQ1, RL8 | AQ3 | The .pptx staged for Paulo; every row of the trace below on a slide; numbers traced to GA5's outputs | Orchestrator | Orchestrator | 0 | 0 |  |
| AQ3 | The WhatsApp text and the attachments, staged in a pickup folder on the Desktop; Paulo sends | L | 0.2 | AQ2 |  | Folder and text in chat | Orchestrator | Orchestrator | 0 | 0 |  |
| AP5 | Carried from version 20. Delivery into `AnaG/Bjararaca/Relatorio`, on Paulo's word (rule 8): the revised sheet and the tie list | L | 0.2 |  |  | Her other files unchanged by hash | Orchestrator | Paulo | 0 | 0 |  |

### W40 Release 2.1.18 (Orchestrator, FQDeNovo)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| RL6 | Reviews and merges in two batches (D116, D118): FQDeNovo reviews FQGroup's steps and FQGroup reviews FQDeNovo's, each fixing small defects itself (D89); the desk merges approved commits into `fq/v2118-release` | M | 1.0 | GA2, GA3, GA4, EX2, IM10, IM10b, IM11, NM1, PO1, PO2, PI1, PI2 | RL7 | Each batch's verdict file and merge commit in the LOG | Orchestrator | Orchestrator | 0 | 0 |  |
| RL7 | The 2.1.18 candidate: version bump on the 31 lines, release notes, ledger refresh for the rows the steps touched, the full suite once | M | 1.0 | RL6 | RL8, GA5 | Suite line posted: 0 failed | Orchestrator | Orchestrator | 0 | 0 |  |
| RL8 | Package (FQDeNovo), publish as Latest, README, merge into master and push | M | 0.8 | RL7, GA5 | AQ2 | Published; digests match; an installed 2.1.17 offers 2.1.18 | Orchestrator | Orchestrator | 0 | 0 |  |

## Every request, traced (AP0)

Each point of Ana's 11-12 deck, voice note and comment, with its step. "Desk" means the desk traced the premise in the code before this roadmap; "P0" means the builder confirms it first and puts the line in its READY.

| Source | Ana's request, in short | Step | Premise checked |
|---|---|---|---|
| Slide 1 | A report in English: is mapping the English column as "Family name" the way? The column choices confuse; some choices leave Next off | NM1, IM11 | Desk: no report-names option exists; the other-language name is kept as text only (`PanelImportTemplate.cs:25, 71`); why Next is off is not shown on the page (P0) |
| Slide 2 | Still asks her to confirm each family's evidence from her own sheet; default all confirmed, undo later | IM10 | Desk: `FamilyListImportViewModel.Commit.cs:85, 93-104`; nothing is stored before Fetch evidence |
| Slide 3 | Why did the UniProt families not come with Fetch evidence? | PO2, GA1 | Desk: Fetch UniProt families is step 3, after Group by evidence, and asks for the grouping task first (`ClusterGenerationControl.FamilyWorld.cs:8-21`) |
| Slide 4 | Many metallo families stay apart from #40430 (232 entries; the database has over 800) | GA1, GA2, GA3, GA4, GA5 | Desk: W33 points 1, 2 and 5 |
| Slide 5 | The SVMP row: 821 matches, 232 placed, 589 outside; an entry "Proposed in Desintegrina" | GA2, GA3 | Desk: W33 points 1 to 3 |
| Slides 6, 7 | Is her reading of the TIC shares right? | AQ1 | Desk: her numbers match her screen (44.61 + 6.83 = 51.44; diagnostic 59.52 to 59.72) |
| Slide 8 | The metallo grouping does not match venom reality; earlier versions did better without words; still confused in Review grouping | GA1 to GA5, and Paulo's session | Desk: W33 point 4 (shared peptides) |
| Slide 8 | A pie chart, as Calvete shows | PI1, PI2 | P0 |
| Slide 9 | Export HTML fails: "Family assignment evidence is missing or out of date" | EX1, EX2 | Desk: `PanelAssignmentEvidenceStore.cs:88-91` |
| Voice note 11-12 | Earlier versions grouped metallo better with default clicks; the grouping screen has too much to know where to start | GA5 (default clicks only), and Paulo's session | |
| Comment 11-12 | "Word clustering" should come before quantification | PO1 | Desk: her slide 3 screen |

## Critical path

**Tonight, in parallel.** FQGroup: GA1, then GA2 and GA3, then EX1 and EX2, then GA4 if GA1 finds its class. FQDeNovo: IM10, IM11, PO1, NM1, then PO2 once GA1 posts, then PI1 and PI2. The desk answers slide 7 (AQ1) meanwhile.

**Reviews in two batches (RL6).** Batch 1 when GA2, GA3, IM10 and PO1 have landed; batch 2 with the rest. Each worker reviews the other's batch.

**By early morning 11-13 in Brazil.** RL7 (candidate and suite), GA5 (the walk on the candidate), RL8 (publish), then AQ2 and AQ3. About 16 hours of steps on two workers and the desk, about 8 hours of clock.

**After this version.** Paulo's session with Ana on Review grouping ("tem tanta coisa ali que eu fico sempre meio confusa assim por onde começar").

## Paulo's decisions

None open. D118 is Paulo's; D119 to D126 are the desk's defaults (as on 11-11, "defaults"), and Paulo can override any of them. Still on Paulo's word: sending the deck to Ana (AQ3) and the delivery into her Relatorio folder (AP5).

### Decisions in force

The full history, D1 to D39, is in `docs/ROADMAP-archive.md`.

| ID | Question | Taken |
|---|---|---|
| D118 | The team for 2.1.18? | One Sol 6.1 (FQDeNovo) and one Opus 5.5 xhigh on Juliana's account (FQGroup); each reviews the other's steps, cross-family. Paulo, 11-12 17:40: "only with 2 workers for this, 1 sol and one opus from the juliana account" |
| D119 | Should her list win over the scope words? | Yes: a protein whose own evidence maps to exactly one row of her list joins it; the scope words decide only for proteins that map to no row (GA2). Desk default, 11-12 |
| D120 | Domain ties by overlap both ways? | Yes: shared domains divided by all domains in either replaces D111's one-way coverage; a user's choice still wins (GA3). Desk default, 11-12 |
| D121 | May FQGroup read her data? | Yes, the desk's copies under `D:/FamilyQuant-data/out-desk` only, counts out, nothing committed; it is an Anthropic model, as the desk and the 11-07 walks. FQDeNovo stays on code and synthetic data. Desk default, 11-12 |
| D122 | Take the sheet's codes as written? | Yes, before or after Fetch evidence; un-confirm later (IM10). Ana, slide 2 |
| D123 | The panel order? | Quantify last, after Group by words; Fetch UniProt families where GA1's measurement puts it (PO1, PO2). Ana's comment, 11-12 |
| D124 | A pie chart? | Yes, a chart choice in the HTML report and on the Results screen, the same numbers as the bars (PI1, PI2). Ana, slide 8 |
| D125 | A report in English? | A "Family names" choice (Name or Other-language name) in the Export window, stored in the project (NM1). Ana, slide 1 |
| D126 | How much testing? | As D116: one focused test per behaviour change, a render only where the look changed, reviews in two batches, the full suite once on the candidate, GA5 on her data. No website update unless Paulo asks. Desk default, 11-12 |
| D117 | A 2.1.17 tonight for the Review grouping layout defects found in 2.1.16? | Yes, before Ana's session; the website pictures and the deck are taken from 2.1.17. Paulo, 11-11: "yes i agree with a next version" |
| D110 | Remove "Try with the example", the colour categories, and the template Decision and Notes columns and colour legend? | Yes. Older sheets with them still import; no ledger row had to be dropped. Paulo, 11-11 ("defaults") |
| D111 | Settle ties by how completely a protein covers each tied row's Pfam list? | Yes; only real ties are asked and a user's choice wins (on her project 16 questions became 4). Paulo, 11-11 ("defaults") |
| D112 | Write tie choices back into the sheet as a "UniProt family" column? | Yes. Paulo, 11-11 ("defaults") |
| D113 | A row's own UniProt family brings its proteins into scope (BJ46a and the fetuins into SVMPI)? | Yes, exactly that family's proteins. Paulo, 11-11 ("defaults") |
| D114 | Names "Use template" and "Fetch UniProt families"? | Yes. Paulo, 11-11 ("defaults") |
| D115 | The team? | Four Sol builders (FQDeNovo, FQImport, FQPanel, FQSimple) and FQRev (Grok) reviewing, launched 11-11 13:47 on Paulo's "im assuming you are already working on the new roadmap" |
| D109 | One release or two? | One: 2.1.16 with every step, by early morning 11-12. Paulo, 11-11 13:50: "simplify execution as much as possible ... this should be ready by early morning" |
| D116 | How much testing? | Lean gates: one focused test per step that changes behaviour; a screen render only for layout steps (IM8, RG1, RG6, RG7, SF2); the reviewer runs the step's own test; reviews and merges in batches (one build per lane tip, the batch's new tests only); the full suite once at RL1; the ledger refreshed only for touched rows. Paulo, 11-11 13:50: "only test when really necessary"; 13:56: "bundle reviewes and merges and dont do major tests for every step" |
| D21 | Is the program general? | Yes: nothing the user sees names an organism, a venom or a sample's numbers. Paulo, 11-02 |
| D31 | How releases reach users | A zip on the public releases-only repo pcarvalho75/FamilyQuant2-releases; Help > Check for updates reads it. The code repo stays private. Paulo, 11-02 |
| D32 | Publishing | The desk publishes when Paulo asks. 2.1.0 asked for on 11-03 ("we publish it in github and she installs it from there") |
| D33 | Which de novo data | Ana's three-run PEAKS export, cut by the program at ALC 80. Paulo, 11-02 |
| D35 | Which families | All 41 of Ana's consensus families with her decisions; under 0.1% listed as others. Paulo, 11-02 |
| D36 | Report language | English, with Ana's own family names from her file. Paulo, 11-02 |
| D37 | What "% of the venom" means | The share of the signal credited to families, summing to 100%; beside it the share of the whole measured signal. Paulo, 11-02 |
| D38 | The ambiguous family links | Decided from Ana's written rules (disintegrin and M12B Disintegrin-only to DIS, DC stays SVMP, endonuclease to NUCL, snaclec and true venom lectin to CTL, M12B to SVMP). Desk, 11-02 |
| D39 | The scope of families by evidence | "Toxin", "venom gland", so the panel's galactose-binding lectins (Gal-CTL, merge into CTL) join CTL; the report's method states the scope. Desk, 11-03 06:35 |
| D41 | The three new link choices the D39 scope raised | Decided by D38's rule (the source family decides; a source-less domain combination stays outside): venom Kunitz-type family, any combination, to KUN; the same family with Kunitz_BPTI plus WAP to KUN; no source family with Kunitz_BPTI plus WAP stays outside. Checked against the panel's KUN and Waprin rows by FQReview (K3). Desk, 11-03 08:20 |
| D42 | How Ana gets the AI key | Option b: Paulo's key ships only as a small file next to Ana's report, never in the program, the public release or the repository; FamilyQuant offers to import it on first use and stores it per user with Windows protection (J46a). Paulo, 11-03 08:55 ("i will follow your advice") |
| D40 | Headline unit when several families share a panel family | B: with a panel in use, the headline and composition figures use the per-panel-family totals (J27), which credit a peptide shared inside one panel family once. Done in 2.1.1, right after Ana's delivery (step J43); her project has one family per panel family, so her numbers are the same either way. Paulo, 11-03 08:28 ("lets go with B") |
| D43 | Defaults of the homology science options (J47b) | A: score tier ON by default, per-length gate OFF, tag gate OFF until J47c is measured (FQReview's science review agreed). Paulo, 11-03 15:40 ("i agree with you on all") |
| D44 | Publish 2.1.1 today | Yes: after J47d and the version bump, through the same full gate, package, real-menu export and install checks as 2.1.0. Paulo, 11-03 15:40 |
| D45 | The tag-gate option on this roadmap | Finish J47c (fast prefilter, its J47b row) in parallel with 2.1.1; it stays OFF by default whatever it shows. Paulo, 11-03 15:40 |
| D46 | The HTML report | Built now beside the Word report, from the same stored values, offline and self-contained; release and delivery after Paulo sees it. Paulo, 11-03 19:20 ("just get it going"); roadmap asked for at 19:52 |
| D47 | Scope of the HTML page | Plots, not text: no methods or quality text, short pop-ups only, short rounded labels with the exact value in the pop-up; no design review (Paulo judges it at M3f; FQReview2 checks the template's code only); coverage as the native figure unless positions are stored. Paulo, 20:15, after the 9-hour question |
| D48 | Prepare the release and the delivery ahead | Yes: version 2.1.2 and notes on a branch now, the final export for Ana staged outside her folder; publishing and delivery still wait for Paulo's word after he sees the page. Paulo, 21:39 ("yes") |
| D49 | When to publish 2.1.2 | No early freeze: finish every step of W1 to W3, then publish on GitHub (W4) without waiting for another word; delivery to Ana (W5) still waits for his word. Paulo, 21:47 ("we will do the project until the end and then publish it in github") |
| D50 | The public README | FQTerra2 writes a README devoted to 2.1.2 with a manual and screenshots from synthetic data only; the desk pushes it as soon as it is reviewed, before the release, with links to releases/latest. Paulo, 22:10 to 22:13 |
| D51 | The night run | Paulo goes to sleep (23:57: "when you finish this project please confirm you will upload it ... i cant have questions asked"). The desk finishes W2 to W4 and publishes 2.1.2 on GitHub with no further questions. The database-entries axis waits for the next version, so the release is not delayed before the collaborator wakes. W5 (writing into Ana's folder) stays held without his word; the staged page waits outside her folder. Paulo, 23:57 |
| D52 | 2.1.3 tonight | An installer built in .NET (no download, no install on this PC: tested in test mode), Help > Install update and a start-up update notice; published when every check is green, like 2.1.2 (D51). Paulo, 23:50 to 23:57 |
| D53 | Reviewer fallback tonight | If a Grok reviewer runs out of credit, an Opus 5.5 reviewer on Juliana's account (juliana.fischer@fiocruz.br, LPEC team, engine opusjf) reviews the Sol builders' work, read-only and batched; any work an Opus writes is reviewed by a Sol. Paulo's own Fiocruz account (96 percent weekly) only if Juliana's is out too. Paulo, 00:05 and 00:03 |
| D54 | The database-entries axis | Ships in the HTML report as 2.1.4 now, with a new roadmap of small steps. Paulo, 11-04 06:38 ("include that feature in the html ... get to work") |
| D55 | What a family's database entries are | The number of target entries of the project's database that the curation assigns to the family (their UserID); decoys and contaminants left out; a panel family counts the distinct entries of all its member families; Others is not plotted. UserID 0 is a real family; an entry with no UserID is unassigned. A family with no remaining entries counts zero; a panel family in conflict is unavailable and is not added to any union. Read from the stored curation, so an old project shows the axis without Compute. Desk, 11-04 06:45; G1a approved by FQReview at 06:50 with the last two clauses |
| D56 | Publish 2.1.4 | When every check is green, like D49 and D52; the update banner of 2.1.3 then offers it. Desk, 11-04 06:45, following D49 and D52 |
| D57 | Deliver Ana's new page (G5b) | Yes: the desk delivers report.html and the 2.1.4 READ-ME into her Relatorio folder as soon as G5a is staged and its checks pass, backing up the current copies first and checking her other files unchanged by hash. Paulo, 11-04 08:23 ("yes you may") |
| D58 | DeepSeek on the team | DeepSeek V4.1 Flash joins as a second reviewer while Grok is out, on code and synthetic data only (its API is hosted in China; Ana's data never goes to it). Paulo, 11-04 09:20 ("grok is out but deepseek 4.1 is back as a reviewer, bring him to the team") |
| D59 | Who reviews | Every review goes exclusively through the DeepSeek reviewer FQReviewDS; Juliana's Opus reviewer is not used for now. Paulo, 11-04 09:56 ("lets not use julianas reviewer yet, all reviews go exlcusively through the deepseek worker") |
| D60 | A guided order for the Families screen | Built now as 2.1.5 with Ana's order (Fetch evidence, Group by evidence, Apply grouping, Fetch family world, Compute), a moving next-step highlight, step status, offers for a missing input, an explanation for an empty scope and counted progress; published when every check is green, like D56. Paulo, 11-04 11:18 ("can you get a worer on this asap"), after Ana 11:10 |
| D61 | Releasing 2.1.5 without the ledger re-proof | The desk's cleanup deleted the evidence files the feature-ledger producers read, so every receipt went stale (desk mistake, 11:40). 2.1.5 ships on the full functional suite (every check, real data included, receipts deferred) and FQReviewDS's review; the ledger stays as proved for 2.1.4 at 9c03db8 the same morning; the release notes say so. Rebuilding the ledger evidence inside the repository follows as its own step. Paulo, 11-04 11:44 ("a") |
| D62 | A clean Recent projects list | Built now as 2.1.6: unavailable files hidden, removal by right-click, a small x and Delete, and tests isolated from the user's list; same fast mode and test approach as 2.1.5 (D61); published when green. Paulo, 11-04 13:19 ("put that feature in and redeploy") |
| D63 | Ana's suggestions of 11-05 | All built as the desk proposed, in two releases, each published when every check is green (like D56 and D62): 2.1.7 = Fetch evidence that keeps its work and resumes, the update reopens the app, Setup compares versions, File > Close project; 2.1.8 = Home with five numbered steps and one name per thing (Ana's names, Analysis becomes Tools), the Families layout, USearch ID shown, the de novo Help page. Paulo, 11-05 12:21 ("lets ready every suggestion from ana as you suggested") |
| D64 | Who builds and reviews | Two Sol 6.1 builders, FQFetch and FQHome, and one Kimi reviewer, FQKimi, in batches; this replaces D59 (DeepSeek only). Kimi, like DeepSeek (D58), sees code and synthetic data only. Paulo, 11-05 12:21 ("2 sol workers and 1 kimi reviewer") |
| D66 | The same-scan conflict | Measured first on the real-data set (Z6a), then Paulo decides (D65); nothing changes before. Paulo, 11-05 12:21, accepting the desk's pick |
| D65 | A scan where PatternLab and the de novo call give different sequences for the same precursor | a) The PatternLab identification wins the scan: its FDR-controlled identification is kept and the de novo call on that scan gets no credit (Ana's step 4; it removes a double count, since both XICs measure the same precursor's ions). Z6a measured 305 to 431 such scans per run, 2.6 to 3.7% of the de novo signal and 0.35 to 0.55% of all signal at most. Built in Z6c as an option, ON by default, with before and after on real data and a science review. Paulo, 11-05 13:24 |
| D67 | More hands | Paulo, 11-05 12:38: "hire one more worker, an opus worker from this account, and also hire deepseek as a second reviewer". DeepSeek FQReviewDS joined at 12:41 (code and synthetic data only, as D58). The Opus CLI worker hit the account's weekly CLI limit at 12:42 (resets 11-06 08:00), so FQOpus runs at 12:48 as an Opus agent of this session, on the same account (Paulo, 12:47: "an opus from this account"); it takes W4, FQHome takes W5 and W8 from FQFetch |
| D68 | Kimi's review rate | 15 reviews in any rolling hour (was 5 from 11-02); 15 calls per rolling 5 hours and one instance unchanged. Paulo, 11-05 13:24 ("raise kimi to 15 reviews, and update that law") |
| D69 | A FamilyQuant website | A gorgeous, instructional public page in the spirit of the SCPBrowser page but better: how it works, why families, why peptide spectrum matches plus de novo, a fictitious and clearly labelled snake venom example, the download link, the Fiocruz logo in the footer; built by one more Opus from this account, after a deep study of the program; ChatGPT may make art. The program itself stays general (D21); the site uses fictitious data only. Paulo, 11-05 14:05 |
| D70 | Where the website goes | On patternlabforproteomics.org beside SCPBrowser, as a folder Paulo uploads; relative paths only, so any folder name works. The desk suggests /familyquant2/, because /familyquant/ may hold the 1.5 update files that 2.0 must never touch. Paulo, 11-05 14:47 ("yes, it will go here") |
| D71 | Release 2.1.9 | The UniProt 429 fix for Read database and scope (Z1m) ships now with the approved follow-ups Z1f to Z1l; the Fetch evidence speed-up (domains first, smarter waits) is not in it. Paulo, 11-06 ("please fix that and deploy a version with that fixed") |
| D72 | Release 2.1.11 | Ana's 11-06 batch (W12) ships as the next version; work starts now; the desk writes to Ana when it is published. Paulo, 11-06 ("solve all these requests in a next version, please begin working now... and when you are done we write to ana"; "provide me the roadmap too, there is plenty of things to improve"; "well put all this in this batch... we need to wrap all this up in this round", the Fetch evidence speed-up and the later ideas included; the science page text Ana found extremely confusing made much clearer) |
| D73 | The database update and the faster panel | (a) Ana gives a new FASTA made the same way, compared with the project; (b) proteins missing from it are removed and recorded; (c) a changed sequence under the same accession is updated in place, kept in its family and flagged; (d) the full version with the preview window (Z14c3a to Z14c3e); (e) the evidence family number gap fixed in this round (Z14c4); (f) the faster panel not in 2.1.11 (Z15a after it). Paulo, 11-06 14:07 ("i accept all suggestions") |
| D74 | Placement rule in the database update | A second-pass (coverage-gated) link ranks before identity, so a perfect match over a short fragment cannot outrank a full-length relative; "close to several families" counts only second-pass links, the only links that chain families in Build families. FQOpus proposed it in Z14c3b; FQReviewDS asked for a ruling; the desk accepts it (DU3: the update equals a fresh Build families of the new file). 11-06 |
| D75 | Importing the consensus Excel | A wizard with a tutorial, designed first and shown to Paulo, built to a professional standard (Z12p1 to Z12p5, Z12p2a to Z12p2e). Paulo, 11-06 ("enable her to import her excel and handle it gracefully, very gracefully"; "with a tutorial and maybe a wizard ... super very professional") |
| D76 | A floater | Paulo hired an Opus 5.5 worker on juliana.fischer@fiocruz.br as a floater (FQFloat): it builds a lane (the Excel import core Z12p1, Z12p3, Z12p2d) and reviews other workers' code when the desk asks, mostly the Sol builders. Paulo, 11-06 ("hire an additional opus worker in juliana.fischer@fiocruz account"; "this would be a floater... it can review work from sol or work on a lane") |
| D77 | Tooltips in 2.1.11 | Every window gets informative tooltips by design rule 7 in this round, split by window owner (Z12q1 to Z12q7), with a suite check that fails on a control without one; the release moves later by about 3 hours. Paulo, 11-07 ("put informative toolstips as in the program, there are barely tooltips, so put them in and include this step in the roadmap"); Paulo, 11-07: "ok, so hire an additional sol worker": FQTips takes Z12q4 and Z12q8; Paulo, 11-07: "so hire a new sol worker": FQFix takes Z12q5, Z12q6 and Z12h2a; then "actually hire 2": FQPolish takes Z12q3, and Z12q6 stays with FQFloat, which had already built it |
| D78 | Store less in this round | Curation format version 2 (Z15b1 to Z15b5) and the scoped reference panel (Z15a1 to Z15a4) join 2.1.11, with the streaming fix (Z12p5f1 to Z12p5f4) and compact writing (Z12p5j); old files open; an older FamilyQuant refuses a format-2 file with a clear message; supersedes D73 f. Paulo, 11-07 ("no problem in doing a new file version type, we need to push forward to a final version and breaking changes happen"; "yes, include everything") |
| D79 | The storage chain in parallel, and more hands | Contract first (Z12p5f0 for streaming, Z15b1 for the format-2 frame), then each call site and each format part in its own file by a different worker; the proof written first (Z12p5f4) by a new Sol, FQProof; a Grok reviewer from 10:00. Paulo, 11-07 ("there must be a more intelligent way to develop it in such a way that we can hire an additional worker now"; "great, so put this additional sol worker too"; "after 10 am we will be able to hire grok as a reviewer"; "update the roadmap to reflect this new disposition") |
| D80 | No FamilyQuant 1.5 compatibility | The .fcf file is written in curation format 2 like projects; nothing is kept for FamilyQuant 1.5 readers; DU7's frozen 1.5 reader check is retired. Paulo, 11-07 ("forget familyquant 1.5") |
| D81 | Her whole workbook | The wizard reads every sheet of a workbook by structure: the family list, a colour legend, a subclass table and notes sheets (W21, Z16a1 to Z16a8); her Pfam accessions are ticked as her own statement and marked; subclass rules are only proposed from recognized Pfam names and confirmed by her; nothing is set silently. Paulo, 11-07 ("take a look at ... the xlsx, it was prepared by ana, there are many tabs, understand that file and allow the wizard to import it easily"; "put these steps into your roadmap and update it"); then "make the wizard smarter and adaptable, so it can read straight off the bat all the tabs from anas files, understand it!": the wizard reads the whole workbook at once and proposes everything on one page, accepted with one press (Z16a9) |
| D82 | Tooltip census approved | Z12q7d closes on Paulo's word, without his reading of all 1,242 tooltips; the generated census (release c1f34dd9, every control with a tooltip, 0 missing, 0 repeating the label, 0 wording flags) is the record, and any wording Ana questions after release becomes a step on the next roadmap. Paulo, 11-07 09:20: "its impossible for me to read all tooltips and approve them... we will certainly hear feedback from ana, so lets approve that step" |
| D83 | Ship today | Paulo, 11-07 09:25 to 09:29: "we need to get all workers busy, this project needs to ship today"; "i see 2 blue squares... if you need more workers, hire!". FQScope (Sol) hired for Z15a6; Argon joins as a reviewer from a third model family for the wizard polish walk (Z12p2e), so it need not wait for Kimi's budget; the walk is split so the pages her workbook import changes are walked again on their final pictures (Z12p2e2); FQSite restarted on Z16a8 |
| D84 | Decision labels in any language | The legend step (Z16a2) may propose a program decision for a decision label from a small, documented, general vocabulary of action words in English, Portuguese and Spanish (quantify, keep, include, exclude, remove, not a family, merge or join into, optional, conditional), read from the label and its legend meaning; merge maps only to a valid source key; each proposal shows the matched words as its reason and is confirmed by her one press; conditional, optional, conflicting or unmatched meanings stay uncertain. No family, organism or sample names, no per-workbook exceptions, no language model. Same principle as the absence-marker ruling for subclass rules. Desk, 11-07 10:19, so that Z16a9 can read a workbook in another language "straight off the bat" |
| D85 | Report export memory accepted at 25% lower | Z12p5f9 asked for report export peak memory at least a third lower on the 1.08 GB synthetic proof; FQFix measured 24.7% (8.95 to 6.74 GB) with byte-identical reports and the dispatcher deadlock fixed (DeepSeek 2 rejected 71db2646 on the bar alone). The bar was set by the desk, not by Paulo, and her real project already exports its report at 6.8 GB without running out of memory (FQOpus, RESULT Z12p5k-quantify). So 2.1.11 ships the 25%; the rest (the report snapshot still holds a fourth copy of the panel during export, FQFix FINDING Z12p5f9-live-roots) goes to the next roadmap. Desk, 11-07 10:54, under Paulo's "this project needs to ship today" |
| D86 | A shorter tail | Paulo, 11-07 at about 10:53: "what can we do to progress faster and more efficiently". (1) The three walks on her real data (Z16a7 her workbook end to end, Z12p5 the final walk, Z12k5 as she would work) run in one FQOpus session on the W21 integration branch, not three; (2) the re-walk of the new wizard pages (Z12p2e2) moves from Kimi, whose 15 calls in 5 hours ran dry twice today, to Argon, and Kimi has nothing on the tail; (3) Z15b6, a measurement, no longer holds the final merge Z12h2b; its numbers go to the notes; (4) the feature-ledger refresh runs in full as soon as W21 is integrated, so Z12h2d before release reruns only what changed. No extra worker: the pace is set by one chain, not by hands or reviews |
| D87 | Decision labels as she writes them | From FQOpus' probe of her real workbook: an action word in the label itself is proposed; a negation cancels only an action word it directly precedes; a bracketed qualifier keeps the legend match and makes the proposal uncertain, with the qualifier as its reason. Desk, 11-07 12:00 (Z16a11) |
| D88 | Ship today with the ledger made honest | Paulo, 11-07 12:05: "do thi quick fix. we need this project done today and cant afford to wait more time!" The refresh restores what it can; only quick fixes (a wrong or missing input, a plain rerun) are made; every VERIFIED row still not proved moves to OPEN with its reason and is verified again after 2.1.11 (Z17a10, listed in docs/LEDGER-REVERIFY-2.1.11.md). The release check is not changed or weakened; the claims are. Closes Z17a3 |
| D89 | The reviewer fixes, and work is bundled | Paulo, 11-07 14:36: "this process of review and go back is just to slow. maybe the reviewer fixes i?", then "or accumulate to do lest testing so we bundle?" Both, from now on: (1) a reviewer who finds a defect it can fix inside the item's own files fixes it itself on a branch from the item's commit, with a test red on that commit, and posts FIXED; a different reviewer checks the fix (assign_review.py now reads reviewers' FIXED posts); REJECTED only for a design problem or a fix outside the item's files. (2) Small steps of one lane go to one reviewer as one bundle and are checked in one pass at the newest commit; merges run only the focused commands of what changed; the full suite runs once, at Z12h2c | Taken | now |
| D90 | The release check reports deferred rows instead of failing on them | Paulo, 11-07 14:46, between shipping with the check red and changing it: "im inclined on 2". The check accepts the D88 rows only with the exact deferral text, only when listed in docs/LEDGER-REVERIFY-2.1.11.md, and only for version 2.1.11; it prints the verified and deferred counts, so a green check never reads as more proof than there is (Z17a11) | Taken | Z12h2d |
| D91 | A reviewer's small fix is checked by the desk, not reviewed again | Paulo, 11-07 15:05: "why do workers take another 15 min when a fix vomes in if they already know what to fix or things should be coming in already fixed by the reviewer!" A reviewer's FIXED of at most 20 changed lines over the commit it reviewed, inside the item's files and with its tests green, is checked by the desk on that delta (git diff) and ticked at once (assign_review.py prints DESK CHECK); a larger fix goes to a second reviewer, who checks only the delta. First use: Z16a13 c4f613c7, 7 lines | Taken | now |
| D92 | D21 covers the program and its Help, not the website, for 2.1.11 | Paulo, 11-07 15:20, on FQSite's Q Z14a0: "pick whatever you think is more appropriate". The desk keeps the website's examples (venom, Toxin, Venom gland, the invented Lantern viper) as on the live site Paulo approved, so the upload changes no page beyond the release; neutral examples that show "any database" go to the documentation rewrite after the release (notes/next-roadmap.md) | Taken | Z14a |
| D93 | AI Help reads master on GitHub through a read-only token built into the program; the repository stays private | Paulo, 11-08: "i dont want to make th erepo public but i dont mind if they hack in", "the user should be with the newest version always". The token can read only Contents of FamilyQuant2; it is kept scrambled in the binary so automatic scanners do not revoke it, not for security; it is never in the repository or a log. |
| D94 | AI Help stays inside GitHub's limits and says so | Paulo, 11-08: "lets not do it, lets keep in the github limits and you make the user aware in the help window of this limitation". No local copy of the source; the window states the shared limits (10 code searches a minute, 5,000 calls an hour, for all users) and, when one is hit, when it resets. |
| D95 | The per-interval de novo normalization | Removed: de novo peptides use their measured envelope, the same rule as identified peptides. Desk's guard: if the check T1 puts the median measured ratio outside 0.8 to 1.25, the desk stops and asks. Paulo, 11-09 ("im also considering in removing this wierd normalization", "ok, so include the per interval normalization, build a roadmap") |
| D96 | Each peptide counts once | Family charts and shares credit a peptide to at most one family; "count in every family" leaves the program; the denominator is a choice in the HTML report: PSM only, PSM and de novo, detector envelopes (everything in MS1 that matches an isotopic signature, identified or not), TIC; Paulo, 11-09: "i want tic, psm , PSM _ de novo, and detector envelopes tthings that match isotopic signature)". Paulo, 11-09 ("a peptide cannot go to more than one family", his notes) |
| D97 | One day | This version holds only what fits in one day; the rest of the 11-09 plan waits for the next roadmap, planned with Paulo. Paulo, 11-09 ("very straight forward, something to be done in one day") |
| D98 | The figures live in the HTML report | The HTML report gets tabs with every Results figure and plain legends; "Screen figures (PNG)" leaves the program. Paulo, 11-09 ("ideally everything in the html so we get rid of the export figures and simplify") |
| D99 | Simpler | Fewer options on screen, advanced controls behind "Show all options", one Export window. Paulo, 11-09 ("too many options,and simplify the program,.. multiple exports, everything should be significantly simplified!") |
| D100 | The team for 2.1.14 | Three Sol 6.1 builders (FQDeNovo, FQCount, FQHtml) and one Grok 4.7 reviewer (FQRev); the desk runs every check on Ana's data. Paulo, 11-09 ("put 3 sol workers and a grok reviewer, get it going") |
| D101 | Which exports stay (was P2) | The default, taken with Paulo's go: keep the Report (HTML, Word, workbook), the CSV tables, Suggested additions (FASTA), Families (.fcf) and Interpretation input (Markdown); remove Screen figures (PNG) and the Presentation evidence pack. Paulo, 11-09 ("get it going") |
| D102 | A fourth builder | FQSimple (Sol 6.1) takes T17, T12 and T18 from the other three lanes, so the day is shorter. Paulo, 11-09 ("recruit a 4th sol worker") |
| D103 | Bigger review bundles | FQRev reviews one lane at a time at its newest commit, every landed step of that lane in one pass (one build, all focused commands, one read of the diff); after the first pass, one final bundle per lane when its last step lands. Paulo, 11-09 ("bundle many things in a single review") |
| D104 | The HTML replots by normalization | A page-wide radio button selects the normalization and every chart that depends on it redraws (T15b, T11b), before 2.1.14 ships. Paulo, 11-09 ("i would like the html to be able to replot depending on the normalization, so there would be a radio button to select the normalization on the html") |
| D105 | The website follows 2.1.14 | A new upload folder for the website with the science text and screenshots of this release (T21); Paulo uploads it. Paulo, 11-09 ("does this roadmap include and update in the website?") |
| D106 | Ledger rows this release changed on purpose | Rows whose proofs went stale because of the intended 2.1.14 changes and that the refresh cannot bring back are reopened as OPEN, named in docs/LEDGER-REVERIFY-2.1.14.md, to verify again after release (as D88 for 2.1.11); 2.1.14 ships when the full suite is green. Paulo, 11-09 ("well just verify agin"), leaving until 20:00 ("i expect this project to be done by then") |
| D107 | Every protein's coverage in the HTML | The HTML Coverage tab lists every protein with placed peptides in every family, as a 2.1.15 release published when its checks pass. Paulo, 11-09 ("exactly you need to fix that, list every protein like you did") |
| D108 | The HTML report draws its own charts | Every picture in the HTML report becomes a native HTML chart drawn from payload data (hover values, legends, themes), with the same numbers as the program; the Word report keeps its pictures; the HTML is checked on real data before 2.1.15 ships. Paulo, 11-09 ("double check on this html report, if thisgs are working, fix and republish"; "improve on the images in the html report, does not look nice... should be more htmlis"; "fix that too and republish") |

## Risks and triggers

| Risk | Trigger | Response |
|---|---|---|
| GA2 pulls a non-venom relative into a family | A protein mapped to a row only by a shared Pfam set (for example a membrane-anchored relative with the same domains) | GA1 counts that class first; GA2 lets a Pfam-only match through the scope only when it is the protein's single row by D120's overlap; GA5 lists every new member's description class |
| GA3 reverses a tie a user already settled | A stored choice | A stored choice wins (D111 kept that); GA3's test covers it |
| EX2 hides a really stale project | A changed database or panel | The refusal stays for those; EX2's test covers both |
| Sol sees her data | Any brief or review | FQDeNovo gets code and synthetic data only; FQGroup's READYs carry counts, never rows (D121) |
| The Juliana account runs out | FQGroup stops on a usage limit | The desk resumes it when the window resets; GA5 and the release wait for GA2, GA3 and EX2, the rest can ship without GA4 |
| The night runs long | No candidate by 01:00 Pacific | 2.1.18 ships with what is reviewed in batch 1 (GA2, GA3, EX2, IM10, PO1); PI2 and PO2 move to the next version |
| A function is lost without Paulo's word | The ledger check fails at RL7 | No row is dropped in this version; renames are mapped in the ledger's Added contracts section |
