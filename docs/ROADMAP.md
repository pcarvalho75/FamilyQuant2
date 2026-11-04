# FamilyQuant 2.0 roadmap (remaining work only)

Version 9, 2026-11-04 06:45, release 2.1.4. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file and go to `docs/ROADMAP-archive.md`; the needle restarts at zero with each version.

Version 8 delivered 18 steps (in the archive): 2.1.2 (the interactive report.html with the dot plot) and 2.1.3 (the Setup.exe installer, Help > Install update, the start-up update banner) published on GitHub overnight, the README for 2.1.3, and Ana's new page with a 2.1.3 READ-ME delivered into her Relatorio folder at 06:29. Version 9 is what remains: the dot plot's database-entries axis, which D51 moved to the next version, shipped as 2.1.4 (D54).

## The goal, in Paulo's words

- 11-03 22:23 to 22:32: "a cool report is not a cool report without a dotplot", "entries in the db versus ion current ... the dot size could also represent something and different families could have different colors ... mouse over could display a tool tip", "allow users to select what is in each axis or size"
- 11-04 06:38: "include that feature in the html, cleand the roadmap, geenerate the new one with detailed steps and get to work"
- Standing: "using python scripts was cheating, every result passed to ana needs to be through the software", "things need to look slick and clean"

## Rules every worker follows

1. **Only the program makes results.** Python may investigate or check a number, never make one that reaches a person.
2. **No exception lists.** No family, protein, Pfam or keyword literal in a rule; evidence and data decide, and what a user chooses is stored in the project. Nothing the user sees names an organism, a venom, a toxin or a sample (D21; the suite tests it).
3. **No function is lost.** The feature ledger is the contract: a release cannot pass with a row open, and only Paulo can drop a row. Ledger rows and receipts are never edited to make a check pass.
4. **Numbers stay equal unless a step says otherwise.** A speed change must leave every stored result byte-identical; a step that means to change a number shows before and after on real data and passes a science review.
5. **Deployable at every merge.** One step per commit, with its test; tests go into DefaultSuite. Builders and reviewers run focused checks; the gate runs the whole suite.
6. **A step counts as done only after a reviewer approves it, it is merged and the gate is green.**
7. **A family panel is user data.** Ana's families, domains, subclasses and decisions live in her file and the project, never in code.
8. **Never write inside `C:/Users/paulo/Desktop/AnaG`** except a delivery to `AnaG/Bjararaca/Relatorio` on Paulo's word (step G5b).
9. **The code repository stays private.** Releases go out as a Setup.exe and a zip on the public releases-only repository (D31, D32).
10. **No em dashes or en dashes** in anything written, UI text included.
11. **Steps stay small.** A step that hides several pieces of work is split, so the needle shows real progress (Paulo, 11-02 and 11-03).

## The team

| Name | Engine | Role in version 9 | Worktree |
|---|---|---|---|
| Orchestrator | Claude, main session | Assigns, gates, merges, publishes, keeps this file and the board true. Writes no product code. | |
| FQResults (RS) | Sol 6.1 xhigh | The family database-entry count, its payload fields and number trace; Ana's new page | `C:/FamilyQuant-wt2/results` |
| FQLook (LK) | Sol 6.1 xhigh | The dot plot defaults and the page checks | `D:/FamilyQuant-wt/look` |
| FQAssay (AS) | Sol 6.1 xhigh | Version 2.1.4, release notes, the Setup.exe and zip | `D:/FamilyQuant-wt/assay` |
| FQEvidence (EV) | Sol 6.1 xhigh | The old-project check, the package check, the update check from GitHub | `D:/FamilyQuant-wt/evidence` |
| FQTerra2 (T2) | GPT-6.1 Terra xhigh | The README for 2.1.4 | `C:/FamilyQuant-wt2/terra2` |
| FQReview (GR) | Grok 4.7 xhigh | Science and code review of the count. Never edits product code. | `D:/FamilyQuant-wt/review` |
| FQReview2 (GR2) | Grok 4.7 xhigh | Code review of the payload, trace and page. Never edits product code. | `D:/FamilyQuant-wt/review2` |
| FQReview3 (GR3) | Grok 4.7 xhigh | Review of the version, notes and README. Never edits product code. | `D:/FamilyQuant-wt/review3` |

If a Grok reviewer runs out of credit, D53 holds: an Opus on Juliana's account reviews Sol work, a Sol reviews any Opus work.

Flow of one step: the owner posts `READY <step> <commit>`; the orchestrator assigns a reviewer in `D:/FamilyQuant-data/reviews/CLAIMS.md`; the reviewer posts `APPROVED` or `REJECTED`; a rejection is the owner's next commit, checked by another reviewer; the orchestrator gates, merges and closes the row.

## Design rules for the look (hard requirements)

1. **No yellow or gold anywhere,** the focus ring included. One accent colour, for the primary action and selection only.
2. **One surface family.** Every colour comes from a token.
3. **Colour means status or nothing.**
4. **One primary button per panel,** the others quiet.
5. **One line of explanation at most on a screen.** The rest goes to Help (F1) and tooltips.
6. **Standard Windows order:** File first, then Edit, View, Analysis, Help.

## Workstreams

Columns: Diff = L, M, H. Hrs = worker hours at measured speed. Blocked by: 🟢 = open now. The graph follows the Blocked by column. A step lands when its owner posts DONE with a commit; it is done when a reviewer approves it and it is merged.

### W1 The count (FQResults, FQEvidence)

What a family's database entries are is ruled in D55; G1a checks the ruling against the code before G1b merges.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| G1b | Core: a read-only, family-keyed count of database entries from the stored curation, collapsed to effective panel keys the way EffectivePanelFamilyStudy collapses UserIDs; no Compute, no search, saved studies untouched | M | 0.5 | 🟢 | G1c, G3a | KnownAnswers test in DefaultSuite, red then green: decoy, contaminant, unassigned entry, a panel family of two families; rule written in docs/HTML-REPORT.md | RS | | 0 | 0 | |
| G1c | Payload: `databaseEntries` offered in dotMeasures whenever the count exists, log scale with program-made ticks, every value a bound field | L | 0.3 | G1b | G1d, G1e, G2b | Synthetic payload lists the measure; existing payload tests unchanged | RS | | 0 | 0 | |
| G1d | Number trace and ledger: the new fields resolve to their stored source; one ledger row for the measure | M | 0.3 | G1c | G3b | Trace of Ana's copied project: 0 NOT FOUND; ledger row proved | RS | | 0 | 0 | |
| G1e | Old projects: a copy of Ana's project, outside her folder, opened in the new build exports the axis with no Compute, and the Word report and supplement numbers equal the 2.1.3 export | L | 0.3 | G1c | G3d | Axis present; compare output attached; every number equal | EV | | 0 | 0 | |

### W2 The page (FQLook)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| G2b | Page checks on the merged payload: the synthetic page and Ana's real page, light and dark, phone width; the selector lists the axis; no console errors; README screenshot taken | L | 0.2 | G1c | G3d, G4c | Screenshots in `D:/FamilyQuant-data/out-look/G2b` | LK | | 0 | 0 | |

### W3 Reviews and the gate

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| G3a | Review of the count (G1b): the rule against D55 and the code | M | 0.2 | G1b | G3d | APPROVED, or fixes by the owner checked by another reviewer | GR | | 0 | 0 | |
| G3b | Review of the payload, trace and page defaults (G1c, G1d, G2a) | M | 0.2 | G1d | G3d | APPROVED, or fixes by the owner checked by another reviewer | GR2 | | 0 | 0 | |
| G3d | Full gate on the integrated 2.1.4 commit: ledger refresh and the full suite | M | 0.4 | G1e, G2b, G3a, G3b | G4a | Every ledger row proved; full suite green | Orchestrator | | 0 | 0 | |

### W4 Release 2.1.4

Published when every check is green, like 2.1.2 and 2.1.3 (D56).

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| G4a | The 2.1.4 Setup.exe and zip, each with its SHA256, from the gate commit | L | 0.2 | G3d | G4b, G5a | Setup.exe in test mode extracts and the app reports 2.1.4.0 | AS | | 0 | 0 | |
| G4b | Package check before publishing: every file equal to the build; EXE and DLL versions 2.1.4.0 | L | 0.2 | G4a | G4e | PASS | EV | | 0 | 0 | |
| G4e | v2.1.4 published as Latest with the Setup.exe, the zip and their .sha256 files; README pushed | L | 0.2 | G4b | G4f, G5b | Downloaded assets match their SHA256; README live and read | Orchestrator | | 0 | 0 | |
| G4f | Update check on the public files: an installed 2.1.3 (test root) shows the banner for 2.1.4 and Install update runs the Setup in test mode | L | 0.3 | G4e | | PASS on every stage | EV | | 0 | 0 | |

### W5 Ana's page

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| G5a | Ana's page with the new axis: export with the 2.1.4 build from a copy of her project outside her folder, traced; staged in `D:/FamilyQuant-data/out-desk/G5-for-Ana` with a READ-ME for 2.1.4 | L | 0.3 | G4a | G5b | Trace 0 NOT FOUND; her Word and supplement numbers equal the delivered ones; SHA256SUMS written | RS | | 0 | 0 | |
| G5b | Deliver report.html and the READ-ME into her Relatorio folder (Paulo's word given, D57) | L | 0.1 | G5a, G4e | | Delivered; her other files unchanged by hash | Orchestrator | | 0 | 0 | |

## Critical path

G1b, G1c, G1d, the review G3b, the full gate G3d, the package G4a and its check G4b, the publish G4e, then G4f and Ana's page G5a. G1a, G2a, G3c and G4c start now beside it. Rough wall-clock estimate: 3 to 4 hours from 06:45, a range from the 11-04 night's pace, never a promise.

## Paulo's decisions

| ID | Decision | Default if no answer | Needed by |
|---|---|---|---|

### Decisions in force

The full history, D1 to D39, is in `docs/ROADMAP-archive.md`.

| ID | Question | Taken |
|---|---|---|
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

## Risks and triggers

| Risk | Trigger | Response |
|---|---|---|
| The rule counts the wrong entries | G1a or G3a finds a decoy, a contaminant or another family's entry in a count | The desk corrects D55 before G1b merges; the owner fixes it as the next commit |
| The new axis moves another number | G1e finds any Word or supplement number different from the 2.1.3 export | No merge; the change goes back (rule 4) |
| The page shows a number with no stored source | G1d's trace finds a NOT FOUND | No merge; the page prints only payload display strings |
| A quick gate hides a stale ledger receipt | G3d fails on receipts | The row's owner re-proves it; refresh_ledger.py reruns a lost register once |
| GitHub fails during the upload | A 404 or 500 while the release is a draft | Retry the failed asset; never force; publish only when every asset matches |
| A reviewer runs out of credit | A Grok call fails on balance | D53: Opus on Juliana's account reviews Sol work |
| Ana's folder is touched | Any file under AnaG changes before Paulo's word | Stop and restore; checked before and after G5b |
