# FamilyQuant 2.0 roadmap (remaining work only)

Version 20, 2026-11-11 12:00 (revised 13:50: one release, D109), release 2.1.16, ready by early morning 11-12. Theme: unblock Ana first, then make Import family list simpler and Review grouping clearer. 2.1.16 fixes the Quantify crash she hit (slide 34) and carries everything else from her 11-10 deck and her 11-11 answers.
Finished rows leave this file and go to `docs/ROADMAP-archive.md`; the needle restarts at zero with each version.

Version 19 delivered 34 of 34 steps, now in the archive: releases 2.1.14 (simpler) and 2.1.15 (every protein's coverage, native HTML charts).

The inputs for this version:
- Ana's deck `FamilyQuant_v15_20261110_V2.pptx` (34 slides on 2.1.15).
- The desk's ten questions, `Perguntas_Ana_FamilyQuant_2.1.15.docx`.
- Her nine voice notes of 11-11, transcribed by the desk and kept out of the repo.
- UniProt Q9DGI0, which she sent.

## The goal, in Paulo's and Ana's words

- Paulo, 11-11: "most importantly, we need to fix this bug" (Quantify: "The computation did not finish. The calling thread cannot access this object because a different thread owns it.")
- Paulo, 11-11: "based on these questions, the answers, the slides, do a roadmap to solve all these issues to the next version"
- Paulo, 11-11 13:48: "the goaals need to be very clear based on the slides and all the requests ana did through audions" (AP0)
- Paulo, 11-11 13:50: "you need to simplify execution as much as possible, only test when really necessary, etc... because this should be ready by early morning" (D109, D116)
- Paulo, 11-11 13:56: "bundle reviewes and merges and dont do major tests for every step, so we can progress swuiftly" (D116)
- Ana, slide 21: "achei que o Import family list poderia ser mais simples ... Às vezes, menos é mais."
- Ana, slide 29: "Parei aqui, não consigo avançar com o Apply grouping desabilitado"
- Ana, 11-11: "a melhor coisa é quantificar todo mundo e tirar essa coisa do color, que eu acho que só complica sem necessidade"
- Ana, 11-11, on SVMPI (BJ46a, Q9DGI0): "não pode juntar, não. Nem com os inibidores tripeptídeos, nem com a cistatina canônica"
- Ana, 11-11: "eu ainda estou confusa com essa coisa das famílias que o escopo propõe, as famílias que eu proponho ... para onde vai cada coisa"

## Rules every worker follows

1. **Only the program makes results.** Python may investigate or check a number, never make one that reaches a person.
2. **No exception lists.** No family, protein, Pfam or keyword literal in a rule; evidence and data decide, and what a user chooses is stored in the project. Nothing the user sees names an organism, a venom, a toxin or a sample's numbers (D21; the suite tests it). Protein family and domain names (for example serum albumin, whey acidic protein domain) are protein names, not samples (desk ruling 11-06 on Z12p2c).
3. **No function is lost.** The feature ledger is the contract: only Paulo can drop a row. A rename keeps the function; a ledger row that pins an old name is mapped in the ledger's "Added contracts after the frozen source" section, never edited to make a check pass.
4. **Numbers stay equal unless a step says otherwise.** In this version no step changes a stored quantity. RG2 (D111) and RG5 (D113) change which family some proteins get, with the desk's before and after counts on Ana's panel.
5. **Deployable at every merge.** One step per commit, with its test; tests go into DefaultSuite. Builders run focused checks; the desk runs the whole suite once per release (D61 mode).
6. **A step counts as done when a reviewer approves it** (Paulo, 11-05 13:41: "many approvals and no green boxes?"). Each release still ships only after its own full suite (RL1 in this version) is green on the merged commit; a suite failure reopens the step it points to until the fix is approved.
7. **A family panel is user data.** Ana's families, domains, subclasses and decisions live in her file and the project, never in code.
8. **Never write inside `C:/Users/paulo/Desktop/AnaG`** except a delivery to `AnaG/Bjararaca/Relatorio` on Paulo's word.
9. **The code repository stays private.** Releases go out as a Setup.exe and a zip on the public releases-only repository (D31, D32).
10. **No em dashes or en dashes** in anything written, UI text included.
11. **Steps stay small.** A step that hides several pieces of work is split, so the needle shows real progress.
12. **Never install on this PC** (D31): Setup runs in test mode only.

## The team

Proposed; staffed on Paulo's word (D115). The engines are the same as in version 19.

| Name | Engine | Role in version 20 | Worktree |
|---|---|---|---|
| Orchestrator | Claude, main session | Assigns work, routes reviews, merges, runs both releases and keeps this file and the board true. It runs every check on Ana's data (QX4, AP1, AP2, AP6) and her panel sheet (AP3), drafts the message to her (AP4) and drops the ledger rows Paulo removes | |
| FQDeNovo (DN) | Sol 6.1 xhigh | QX1 to QX3 (the crash), then SF1 | `D:/FamilyQuant-wt/denovo` |
| FQImport (IM) | Sol 6.1 xhigh | IM1 to IM9: Import family list | `D:/FamilyQuant-wt/import` |
| FQPanel (PN) | Sol 6.1 xhigh | RG2, RG4, RG5: the panel rules in Core; RG8 and SF4 on the Curate families screen (moved from FQSimple 11-11 13:58, while RG4 waited on a seam) | `D:/FamilyQuant-wt/panel` |
| FQSimple (SM) | Sol 6.1 xhigh | RG1, RG3, RG6, RG7, SF2, SF3: the Review grouping window | `D:/FamilyQuant-wt/simple` |
| FQRev (RV) | Grok 4.7 xhigh | Reviews every READY under D89 and D91 and fixes small defects itself. A fix over 20 lines is checked by a builder of another lane | `D:/FamilyQuant-wt/review` |

The builders and the reviewer see code and synthetic data only. Ana's project, panel sheet and RAW files are read only by the desk. The desk runs the check commands the builders write and passes back counts, never rows.

File ownership, so the lanes never touch the same file at the same time:
- DN owns `AppOperationHost.cs`, `ComputeXicViewModel.cs`, `MainWindow.Quantify.cs`, the Quantify screen and the Build to Curate families hand-off.
- IM owns `FamilyListImport*`, `FamilyQuant.Core/Panel/Import` and `FamilyQuant.Core/PanelTemplates`.
- PN owns `FamilyQuant.Core/Evidence/PanelEvidenceClassifier*` and `FamilyQuant.Core/Panel` (Import excepted).
- SM owns `SuperClusterPreviewWindow.xaml(.cs)`, `GroupingReviewViewModel*`, `EvidenceReviewViewModel.cs` and the Families screen in `ClusterGenerationControl`.
- RG4 writes a column into the sheet that IM8 shows. PN asks IM for that part through the desk.

## Design rules for the look (hard requirements)

1. **No yellow or gold anywhere,** the focus ring included. One accent colour, for the primary action and selection only.
2. **One surface family.** Every colour comes from a token.
3. **Colour means status or nothing.**
4. **One primary button per panel,** the others quiet.
5. **One line of explanation at most on a screen.** The rest goes to Help (F1) and tooltips.
6. **Standard Windows order:** File first, then Edit, View, Tools, Help (Tools replaces Analysis in Z4g).
7. **Every control explains itself on hover** (Paulo, 11-07). Every button, menu item, check box, option, input box, list, tab, expander and column header has a tooltip of one or two plain sentences: what it does, and what changes (the project, a file on disk, or nothing; whether Undo reverts it). An input box also says its meaning, unit, default and allowed range. A disabled control says why and what enables it. A tooltip never only repeats its label, uses the same terms as the Help and the website, and follows D21.

## Workstreams

Every row is a small step with its own done-check. A step is done when a reviewer other than its builder approves it (rule 6); each lane merges into master at T19. Ids never change; a step split here keeps its id as a prefix (T7 would become T7a and T7b).

### W26 The Quantify crash, first in the release (FQDeNovo, FQRev, Orchestrator)

Paulo, 11-11: "most importantly, we need to fix this bug".

**The cause, found by the desk in the code (QX1 confirms it):**
1. Since 2.1.14 (T5), Quantify warns when a RAW run has no de novo calls.
2. The warning is reported from the computing thread (`FullXicInputStage.cs:108`). `AppOperationHost.Report` raises `ProgressReported` on that same thread (`AppOperationHost.cs:173`).
3. The Quantify screen's handler sets `DeNovoRunWarning` there (`ComputeXicViewModel.cs:101-106`).
4. The main window's PropertyChanged handler then calls `UpdateGuide()` (`MainWindow.Quantify.cs:27-33`), which reads `GridApp.Children` from the wrong thread (`MainWindow.Status.cs:70`).
5. WPF throws, the engine stops, and the screen shows "The computation did not finish".

Ana's `DeNovoPeaks_ALL` holds a CSV for run 01 only, so runs 02 and 03 raise the warning. Any project with a RAW run missing from the de novo source hits this. T5's test (`--test-denovo-run-warnings`) checks the engine alone, without the Quantify screen and the main window, so the suite never caught it.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| QX1 | A failing test first. The Quantify screen runs a compute that reports "denovo-run-warning" from a worker thread, with the main window's handler attached, as on Ana's runs 02 and 03. Today this ends with the cross-thread error | L | 0.3 |  | QX2 | The test fails on master 1b5083af with "The calling thread cannot access this object"; reviewed | DN | FQDeNovo | 0 | 0 |  |
| QX2 | The fix at the source. When Report comes from another thread, AppOperationHost raises ProgressReported on the UI thread, as it already does for its own display (`AppOperationHost.cs:171-172`). The test subscribers that rely on event order are checked, including the ones that cancel on a progress item | M | 0.8 | QX1 | QX3, QX4 | QX1 passes. The ComputeProgress, HtmlReportExport.Native, GroupingDiscoverySafety and FamilyComposition tests are green; reviewed | DN | FQDeNovo | 0 | 0 |  |
| QX3 | The same hazard elsewhere. List every progress event or PropertyChanged that a background task raises and a window handler hears, in Build, Fetch evidence, Fetch family world, Quantify and Export. Each is either marshalled or proved safe by a test that raises it from a worker thread | M | 0.8 | QX2 | RL1 | The list is in the step note, with one test per path; reviewed | DN | FQDeNovo | 0 | 0 |  |
| QX4 | The desk's real run. Use a local copy of Ana's project and her three RAW files, with her settings from slide 33: her de novo source and ALC 90, isotope ceiling 6, at least 5 points per peak. Run Quantify from the screen: it finishes, and the warning names runs 02 and 03 | M | 0.5 | QX2 | RL1 | Run summary counts posted, no rows | Orchestrator | not staffed | 0 | 0 |  |

### W27 Ana's answers on her panel (Orchestrator; her data, never code)

Her 11-11 answers, against the desk's ten questions. She answered the Decision question first, so she counts it as her first answer. The numbers below are the docx's.

| Question | Ana's answer | Where it goes |
|---|---|---|
| 6, Decision and Colour categories (slide 13) | Quantify every family; remove the colours, "só complica" | IM3, IM5, AP3 |
| 1, the 19 ties (slides 9, 29) | Follow the consensus sheet; she agrees the desk builds the list | AP1, RG2, RG3 |
| 2, SVMPI (slide 9) | BJ46a (Q9DGI0), the protein Richard isolated. It is a large metalloprotease inhibitor with a cystatin-like domain, not a canonical cystatin. Never join it with the tripeptides or with the cystatins. "não sei como fazer" | AP2, RG5 |
| 3, svMP-i and pHpG (slide 9) | Agrees: svMP-i joins NP, pHpG is not a family. She asks what "measured in its own sequence family" means | AP3, RG7, AP4 |
| 4, Other (rares) (slides 9, 15 to 21) | These will not show in jararaca, or will fall below the threshold and go to "Outras". Ignore AMP and endopeptidase; Pfam per name is optional | AP3 (the row goes) |
| 5, subclasses (slide 14) | Only two matter: true lectins against snaclecs (CTL), and PLI alpha, beta and gamma. The other subclasses would split the families into crumbs | AP3, IM9 |
| 7, 8, 9, 10 | Not answered yet. Her question in row 3 is really question 8, her list against the scope. Question 10's project file is no longer needed, since the cause is in the code | RG6, RG7, AP4 |

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| AP0 | The goals traced (Paulo, 11-11: "the goals need to be very clear based on the slides and all the requests ana did through audios"). Every comment on her 34 slides and every request in her nine voice notes is mapped to a step, or marked answered, or added as a new step. Every step's premise about today's program is checked in the code: the desk's own traces, the builders' P0 posts and the reviewer's R0 | M | 0.8 |  | AP1, AP3, AP4 | The trace table in this file, each line with its slide or voice note and its step; every P0 and R0 line answered | Orchestrator | WORKING | 0 | 0 |  |
| AP7 | A deck for Ana, in Portuguese (Paulo, 11-11: "I was considering a powerpoint showing how we addressed each and every point raised by her"). One slide per point: her comment and slide number or voice note, what changed, and before (her screenshot) and after (the 2.1.16 screen on her data); opening slides on the crash and her revised sheet; closing slides on her subclass results and what is left for Paulo's session (slide 25, questions 7 to 10). Screens from the final build; built by the desk only (her data); red-flag review before Paulo sees it. AP4 becomes a three-line cover note sent with it after RL2 | M | 2.0 | RL1 | AP5 | The .pptx staged for Paulo, every point of the AP0 trace on a slide | Orchestrator | not started | 0 | 0 |  |
| AP1 | The 19 ties listed for Ana, as promised in question 1. Run Fetch evidence and Group by evidence on a copy of her Viperidae project with the template she imported (42 rows plus SVMPI). One row per tie: the UniProt family, the domains, the tied families, one example protein (accession and name), what the consensus sheet said, the desk's proposal, and whether RG2 would settle it | M | 0.8 |  | AP3, AP4 | A Portuguese xlsx in the desk's staging folder; counts posted | Orchestrator | not staffed | 0 | 0 |  |
| AP2 | Where BJ46a lands. It is in her database as Q9DGI0 (FTE46_BOTJA). Its UniProt keywords say metalloprotease inhibitor and secreted, and its tissue is liver, with no scope word, so it is probably out of scope. On her project, check whether it is in scope and which family it gets today. Count the entries of her database that share its UniProt family (fetuin family) and its Pfam (Cystatin, PF00031). Snake cystatins belong to the cystatin family | L | 0.4 |  | AP3, RG5, D113 | Counts posted | Orchestrator | not staffed | 0 | 0 |  |
| AP3 | Her panel sheet revised as she answered. Out: svMP-i, pHpG and Other (rares). Gal-CTL becomes the "true lectins" subclass of CTL beside "snaclecs". PLI gets its alpha, beta and gamma subclasses. SVMPI stays its own row. Decision column and colours out. The tie answers from AP1 go in | M | 0.6 | AP1, AP2 | AP4, AP6 | The sheet is staged, with a row diff against her template posted | Orchestrator | not staffed | 0 | 0 |  |
| AP4 | The message to Ana in Portuguese, drafted in chat; Paulo sends it. It covers what changed in her sheet and why, and the tie list. It explains "measured in its own sequence family" with RG7's picture. It tells her the de novo folder has calls for run 01 only, so runs 02 and 03 are measured from identified peptides alone, and asks whether that was intended. Questions 7 to 10 shrink to one line each | L | 0.5 | AP1, AP3, RG7 | AP5 | The draft, in chat, for Paulo | Orchestrator | not staffed | 0 | 0 |  |
| AP5 | Delivery into `AnaG/Bjararaca/Relatorio`, on Paulo's word (rule 8): the revised sheet and the tie list | L | 0.2 | AP4, RL2 |  | Her other files unchanged by hash | Orchestrator | not staffed | 0 | 0 |  |
| AP6 | The desk's walk on her data with the 2.1.16 candidate, before her session. Import the revised sheet. Every tie is explained or settled, Apply grouping is enabled, Quantify finishes and the report opens | M | 1.0 | AP3, RL1 | RL2 | Findings posted; each one fixed, or moved on Paulo's word | Orchestrator | not staffed | 0 | 0 |  |

### W28 Import family list, simpler (FQImport, FQRev)

Ana, slide 21: "o sistema ficou sofisiticado demais, para possibilitar o reconhecimento de diversos formatos e colunas ... acabou complicando para o usuário".

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| IM1 | The program's own template imports (slides 19, 20). Today the review stops on a workbook holding the Families sheet plus the Instructions sheet that Save a template writes (`PanelImportTemplate.InstructionsSheet`); Ana got through only by deleting Instructions. After the fix it goes through, and any sheet without family headers is ignored and named once | L | 0.5 |  | IM8, IM9, RG4 | A test imports the program's own template unchanged; reviewed | IM | FQImport | 0 | 0 |  |
| IM2 | "Save a template" becomes "Use template", Ana's word (slide 7, D114). The tooltip says it writes an empty sheet to fill. The rename is mapped in the ledger | L | 0.2 |  | IM8 | Screen render and ledger mapping; reviewed | IM | FQImport | 0 | 0 |  |
| IM3 | The template the program writes drops the Notes column, the Decision column and the colour legend (slide 7; Ana 11-11; D110). A sheet that still has those columns imports as before | L | 0.5 |  | IM8 | Test: an old sheet with Decision and Notes imports unchanged, and the new template has neither column; reviewed | IM | FQImport | 0 | 0 |  |
| IM4 | "Try with the example" leaves the import window (slide 6, D110) | L | 0.2 |  | IM8 | Screen render, and the ledger row dropped on Paulo's word; reviewed | IM | FQImport | 0 | 0 |  |
| IM5 | Colour categories leave the import window (slide 13, D110). The Decision labels page appears only when the sheet has a Decision column with words of its own | L | 0.5 |  | IM8 | A test each way, plus a screen render; reviewed | IM | FQImport | 0 | 0 |  |
| IM6 | No second Pfam confirmation (slide 17). PF codes that exist are taken as given. Only cells written as names, or codes not found, ask, one line per row. A row with no Pfam code is named once: it gets no proteins until it has one. Use selected matches, Skip this family and Continue without matches are no longer asked | M | 1.0 |  | IM8 | A synthetic sheet with PF codes reaches the summary with no Pfam question; tests; reviewed | IM | FQImport | 0 | 0 |  |
| IM7 | Saving right after the import (slide 21). Accept closes the window and puts the list in the project. The status line says File > Save keeps it, and the title marks the project modified. Closing before Accept says what is lost | L | 0.5 |  | IM8 | Test and screen render; reviewed | IM | FQImport | 0 | 0 |  |
| IM8 | Fewer pages. A sheet with the program's headers goes through three pages: file, columns, summary. Other pages appear only when something needs a choice | M | 1.0 | IM1 to IM7 | RL1 | Screen renders before and after on a synthetic sheet; tests; reviewed | IM | FQImport | 0 | 0 |  |
| IM9 | Subclasses get a visible home (slide 14). The template carries a Subclasses sheet with these columns: Family, Subclass, UniProt family or subfamily, Pfam required, Pfam absent, and a note per header. The Help says in general terms how a subclass is recognized, including that a subclass written as a UniProt family name (for example "serpin family") matches the proteins of that family | M | 0.8 | IM1 | AP3, RL1 | Test: a subclass written as a UniProt family name labels a protein of that family; screen render; reviewed | SM | FQSimple | 0 | 0 |  |

### W29 Review grouping, clearer (FQPanel, FQSimple, FQRev)

Ana, slide 26: "fiquei confusa sobre como agir em relação a cada uma das diversas opções ... acho que vou precisar fazer esta etapa com você".

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| RG1 | Apply grouping says why it waits (slide 29). Beside the disabled button, a line reads "Waiting for 19 choices in Panel links", with a button that opens Panel links on Needs a choice | L | 0.5 |  | RL1 | Test and screen render; reviewed | SM | FQSimple | 0 | 0 |  |
| RG2 | Ties the domains already settle (D111). Among tied families, the one whose own Pfam list the protein covers most completely wins. For example, a Disintegrin-only protein covers all of a one-domain list but a third of a three-domain list. Only ties that stay equal are asked, and a choice the user already made wins | M | 1.0 |  | AP1, RL1 | A synthetic test, plus the desk's before and after on Ana's panel: ties asked and proteins per family, counts only; reviewed | PN | FQPanel | 0 | 0 |  |
| RG3 | Each Panel links row shows one example protein (accession and name) and its UniProt family, so the user chooses for a protein they recognize (slide 26) | L | 0.5 |  | RL1 | Test and screen render; reviewed | SM | FQSimple | 0 | 0 |  |
| RG4 | The choices travel with the list (D112). Saving the panel as a sheet writes each chosen link into a "UniProt family" column on the chosen row. Importing that sheet into a new project restores the choices with no question | M | 1.2 | IM1 | RG5, RL1 | Round-trip test: choose, save, import into a new project, and no tie is asked; reviewed | PN | FQPanel | 0 | 0 |  |
| RG5 | Rows told apart by UniProt family when Pfam cannot (Ana on SVMPI). A row with Pfam Cystatin and UniProt family "fetuin family" takes those proteins; the other Cystatin row takes the rest. If AP2 shows them out of scope, D113 decides how they come in | M | 1.0 | AP2, RG4 | RL1 | A synthetic test with two rows sharing one Pfam code, plus the desk's count on Ana's project; reviewed | PN | FQPanel | 0 | 0 |  |
| RG6 | The Family checklist answers "which wins, 23 or 42?" (slide 24). Every row of the user's list shows its state: has N proteins, waits for a choice, has no Pfam code, or matches no database protein. The top line reads "Your list: 42 families, 23 with proteins, ..." | L | 0.6 |  | RL1 | Screen render on a synthetic panel, plus a test; reviewed | SM | FQSimple | 0 | 0 |  |
| RG7 | Where every protein goes (Ana 11-11). A short panel gives three counts, one sentence each: proteins in a family of the list; in-scope proteins in no family of the list, which keep their sequence family (named after its proteins) and are measured as one; and proteins outside the scope. Help shows the same picture in general terms (D21) | M | 0.8 |  | AP4, RL1 | Screen render and a Help test; reviewed | SM | FQSimple | 0 | 0 |  |
| RG8 | "Fetch family world" gets a plain name (slide 32; Ana read it as "family word"; D114). The label and the Help say what it counts: the UniProt families of the in-scope proteins. The 42 in her project without a list is that count, not her list. The rename is mapped in the ledger | L | 0.4 |  | RL1 | Screen render and ledger mapping; reviewed | PN | FQPanel | 0 | 0 |  |
| RG9 | "Show all options" says what each tab is for (slide 25: "continua difícil de entender"). Each tab it opens (Keywords, Architecture, Left out, Unassigned, Markers, Motif counts and the others) shows one line at its top: what the tab is for and when to use it; the Help page lists the same lines. No control moves or goes | L | 0.5 |  | RL1 | Test that every tab has its line; reviewed | SM | FQSimple | 0 | 0 |  |

### W30 Small fixes from the deck (FQDeNovo, FQSimple, FQRev)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| SF1 | After Build writes the .fcf, Curate families opens on it (slide 3) | L | 0.4 | QX3 | RL1 | Test; reviewed | DN | FQDeNovo | 0 | 0 |  |
| SF2 | The text under "Scope words found: yes" is no longer cut off (slide 27): the details panel wraps | L | 0.3 |  | RL1 | Screen renders at 1366x820 and 1024x768; reviewed | SM | FQSimple | 0 | 0 |  |
| SF3 | The alignment says what it aligns (slide 28: 2 selected, 9 shown). Find why. Then either align the selection only, or title it "2 selected and 7 reviewed references" | L | 0.4 |  | RL1 | Test; reviewed | SM | FQSimple | 0 | 0 |  |
| SF4 | The Families count names what is hidden (slide 4: "19,948 visible · 40,407 in file"). It becomes "19,948 shown; 20,459 decoy and contaminant families hidden (View)" | L | 0.2 |  | RL1 | Screen render; reviewed | PN | FQPanel | 0 | 0 |  |
| SF5 | The HTML report shows the subclasses (found by the desk's AP6 walk on her data: the computed project and the supplement hold PLI alfa and gama, true lectins and snaclecs, but report.html shows no subclass) | M | 0.8 |  | RL2 | Test: the exported HTML holds every subclass label; reviewed | DN | FQDeNovo | 0 | 0 |  |

### W31 Release 2.1.16 (Orchestrator)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---|---:|---|---|---|---|---|---:|---:|---:|
| RL1 | The 2.1.16 candidate: the lanes merged, the full suite once, the ledger refreshed only for the rows the steps touched (D116), and the rows dropped under D110 named | M | 1.0 | QX3, QX4, IM8, IM9, RG1 to RG9, SF1 to SF4 | AP6 | Suite line and ledger line posted | Orchestrator | not staffed | 0 | 0 |  |
| RL2 | Release 2.1.16 after AP6: version bump, package, notes, README, and the update check from 2.1.15 | M | 1.0 | AP6 | AP5, RL3 | Published as Latest; an installed 2.1.15 offers 2.1.16 | Orchestrator | not staffed | 0 | 0 |  |
| RL3 | The website follows 2.1.16 (new import and Review grouping screens), on Paulo's word, as D105 | L | 0.5 | RL2 |  | Upload folder ready | SM | FQSimple | 0 | 0 |  |

## Every request, traced (AP0)

Each comment on Ana's 34 slides and each request in her nine voice notes of 11-11, with its step. "Desk" means the desk traced the premise in the code; "P0" means the builder confirms it in the code before building and puts the line in its READY; FQRev's R0 checks every brief against the code.

| Source | Ana's request, in short | Step | Premise checked |
|---|---|---|---|
| Slide 3 | After Build writes the .fcf, open it in Curate families | SF1 | P0 |
| Slide 4 | "19,948 visible, 40,407 in file": what is hidden? | SF4 | P0 (slide 32 shows the same 20,459 hidden) |
| Slide 6 | "Try with the example" confuses more than it helps | IM4 | P0 |
| Slide 7 | The consensus sheet's extra columns pollute; Notes is optional, so drop it; say "Use Template"; simplify to the maximum | IM2, IM3, IM8 | P0 |
| Slide 9 | The SVMPI row was added by hand; the rest is the consensus of 14 venomics papers | AP1, AP2, RG5 | Desk: BJ46a (Q9DGI0) is in her database; its Pfam is Cystatin, its UniProt family fetuin; the panel row has Pfam only (`FamilyPanel.cs`) |
| Slide 13 | How the template's Decision relates to the window's options; are Colour categories needed? | IM3, IM5, AP3 | P0 |
| Slide 14 | Where subclasses go in the sheet, and what the window's subclass options mean | IM9, AP3 | Desk: `SubclassAssignment.ClassifyDirect` matches subfamily names; `WithoutRankSuffix` strips only " subfamily" and " sub-subfamily" |
| Slide 17 | Why confirm Pfam again when the sheet gives it; what "Continue without matches" does | IM6 | P0 |
| Slides 19, 20 | The template with an Instructions sheet blocks the review; deleting that sheet works | IM1 | Desk: the program's own template writes the Instructions sheet (`PanelImportTemplate.InstructionsSheet`); why the review stops is P0 |
| Slide 21 | Had to close without saving to reach File > Save; is it lost?; the import is too sophisticated ("menos é mais") | IM7, IM8 | P0 |
| Slide 24 | The program proposes 23 families, her sheet has 42: which wins? | RG6, RG7 | P0 |
| Slide 25 | "Show all options" is still hard to understand | RG9, and Paulo's session with Ana | P0 |
| Slide 26 | Confused about how to act in Proteins, Panel links, Family checklist and Unassigned | RG1, RG3, RG6, RG7, and the session | P0 |
| Slide 27 | The lines under "Scope words found: yes" cannot be read | SF2 | P0 |
| Slide 28 | Selected 2 proteins, the alignment shows 9 | SF3 | P0 |
| Slide 29 | Apply grouping stays disabled; the 19 pending choices; how they relate to her 42-row sheet | RG1, RG2, RG3, AP1 | P0 |
| Slide 30 | Her workaround: reopened the .fcf without the import and grouped by evidence | No step; AP6 walks the import route | |
| Slide 32 | A project without her sheet still shows 42 families for the "family word": how? | RG8 | Desk: the 42 is the count of distinct UniProt families among the in-scope proteins of her database (`FamilyWorldSnapshot.cs:70-73`, `CurationViewModel.FamilyWorld.cs:27`); 7,719 UniProt entries were fetched for them. It does not come from her sheet; the same number is a coincidence |
| Slide 33 | Her Quantify settings | QX4 uses them | |
| Slide 34 | Quantify fails: "The calling thread cannot access this object" | QX1, QX2, QX3, QX4 | Desk: traced to `AppOperationHost.cs:173` and `MainWindow.Status.cs:70` (W26) |
| Voice note 11.38 | Decision and Colour categories were made up; quantify every family and drop the colours | IM3, IM5, AP3 | as slide 13 |
| Voice note 11.38 | She is free from 11-12 afternoon | D109: ready by early morning | |
| Voice note 11.40 | The ties: follow the consensus; the desk builds the list | AP1, RG2 | |
| Voice note 11.42 | BJ46a must join neither the tripeptides nor the canonical cystatins; "não sei como fazer" | AP2, RG4, RG5, D113 | as slide 9 |
| Voice notes 11.43, 11.45 | What "measured in its own sequence family" means; "para onde vai cada coisa" | RG7, AP4 | P0 |
| Voice notes 11.46.01 and 11.46.36 | The rare families will not appear or fall under the threshold into "Outras"; Pfam per name is optional | AP3 (the row goes) | |
| Voice note 11.47 | Ignore AMP and the endopeptidase inhibitor | AP3 | |
| Voice note 11.48 | Subclasses: only true lectins against snaclecs, and PLI alpha, beta and gamma (different families, all phospholipase inhibitors); not SVMP P-I to P-III, PLA2 D49/K49 or short and long disintegrins | IM9, AP3 | IM9 proves both shapes: a split by UniProt family name and a split by Pfam |

## Critical path

**Today.** The crash goes first: QX1, QX2, then QX3 and the desk's real run QX4. Meanwhile the desk builds AP0, AP1 and AP2 on 2.1.15, since ties and scope do not depend on Quantify.

**In parallel.** IM runs IM1 to IM9. PN does RG2, then RG4 once IM1 lands, then RG5 once AP2 is posted. SM does RG1, RG3 and RG6 to RG8, then SF2 to SF4. DN does SF1 after QX3. FQRev reviews each step as it lands.

**By early morning 11-12.** RL1, then AP6, then RL2 and AP5 (Paulo, 11-11 13:50: "ready by early morning"). Ana is free from 11-12 afternoon ("só vou conseguir estar liberada para voltar nesse assunto amanhã à tarde"). About 21 hours of steps, about 6 hours of wall time with four builders.

**After this version.** Paulo's session with Ana on Review grouping (slide 26: "vou precisar fazer esta etapa com você"). "Show all options" is still hard for her (slide 25), so the next simplification comes from that session. The items in the desk's notes/next-roadmap.md remain.

## Paulo's decisions

None open: D109 to D116 are taken (below). Still on Paulo's word: sending the message and deck to Ana (AP4, AP7) and the delivery into her Relatorio folder (AP5). The website (RL3) started on his word, 11-11: "we might need to upddate our webiste as well".

### Decisions in force

The full history, D1 to D39, is in `docs/ROADMAP-archive.md`.

| ID | Question | Taken |
|---|---|---|
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
| The fix changes when a subscriber hears progress | QX2 turns a synchronous event into a posted one | QX2 checks the tests that cancel on a progress item; QX3 adds a worker-thread test per path |
| Another cross-thread path ships | Any background event with a window handler | QX3 lists them all; QX4 runs Quantify from the real screen on real data before 2.1.16 ships |
| RG2 moves a protein a user expected elsewhere | A tied protein gets a family without a question | Only ties change and a stored choice wins; the desk posts before and after counts on Ana's panel |
| Old sheets stop importing | A sheet with Decision, Notes or colours | IM3 keeps reading them, with a test |
| BJ46a never enters the scope | AP2's count | D113 |
| A function is lost without Paulo's word | The ledger check fails at RL1 | Only D110 drops rows; the renames (D114) are mapped in the ledger's Added contracts section |
| Ana's data leaves this PC | Any brief or review | Only the desk (Anthropic) reads her project and sheet; the builders and the reviewer see code and synthetic data only |
| The day runs long | 2.1.16 not ready by 11-12 noon | The late steps move to the next roadmap and 2.1.16 ships with what is reviewed; 2.1.16 has already unblocked her Quantify |
