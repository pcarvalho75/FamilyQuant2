# FamilyQuant 2.0 roadmap (remaining work only)

Version 10, 2026-11-04 11:25, release 2.1.5: a guided order for the Families screen. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file and go to `docs/ROADMAP-archive.md`; the needle restarts at zero with each version.

Version 9 delivered 22 steps (in the archive): the dot plot's database-entries axis, published as 2.1.4 at 10:05, the README for 2.1.4, and Ana's new page delivered at 10:09. Version 10 is what Ana's first session showed: she pressed Group by evidence before Fetch evidence, got an empty scope with no reason given, and could not tell what Fetch evidence and Fetch family world are for.

## The goal, in Paulo's and Ana's words

- Ana, 11-04 11:10: "melhor colocar os botões na ordem: Fetch evidence, Group by evidence, Apply grouping, Fetch family world e por fim o Compute"
- Paulo, 11-04 11:12: "what can we do to let the software more intuitive in the sense so other users dont make the same mistakes"
- Paulo, 11-04 11:18: "can you get a worer on this asap, i guess these should be simple changes, mostly gui"

## Rules every worker follows

1. **Only the program makes results.** Python may investigate or check a number, never make one that reaches a person.
2. **No exception lists.** No family, protein, Pfam or keyword literal in a rule; evidence and data decide, and what a user chooses is stored in the project. Nothing the user sees names an organism, a venom, a toxin or a sample (D21; the suite tests it).
3. **No function is lost.** The feature ledger is the contract: a release cannot pass with a row open, and only Paulo can drop a row. Ledger rows and receipts are never edited to make a check pass.
4. **Numbers stay equal unless a step says otherwise.** A speed change must leave every stored result byte-identical; a step that means to change a number shows before and after on real data and passes a science review.
5. **Deployable at every merge.** One step per commit, with its test; tests go into DefaultSuite. Builders and reviewers run focused checks; the gate runs the whole suite.
6. **A step counts as done only after a reviewer approves it, it is merged and the gate is green.**
7. **A family panel is user data.** Ana's families, domains, subclasses and decisions live in her file and the project, never in code.
8. **Never write inside `C:/Users/paulo/Desktop/AnaG`** except a delivery to `AnaG/Bjararaca/Relatorio` on Paulo's word.
9. **The code repository stays private.** Releases go out as a Setup.exe and a zip on the public releases-only repository (D31, D32).
10. **No em dashes or en dashes** in anything written, UI text included.
11. **Steps stay small.** A step that hides several pieces of work is split, so the needle shows real progress (Paulo, 11-02 and 11-03).

## The team

| Name | Engine | Role in version 10 | Worktree |
|---|---|---|---|
| Orchestrator | Claude, main session | Assigns, gates, merges, publishes, keeps this file and the board true. Writes no product code. | |
| FQPanel (PN) | Sol 6.1 xhigh | W1: the Grouping panel's numbered order, next-step highlight, step status and tooltips | `D:/FamilyQuant-wt/panel` |
| FQGuard (GD) | Sol 6.1 xhigh | W2: the missing-input offers, the empty-scope explanation, counted progress, Help | `D:/FamilyQuant-wt/guard` |
| FQRelease (RL) | Sol 6.1 xhigh | W3 and W4: version 2.1.5, notes, README, package, package check, update check | `D:/FamilyQuant-wt/release` |
| FQReviewDS (DS) | DeepSeek V4.1 Flash | Every review (D59); code and synthetic data only, never Ana's data. Never edits product code. | `D:/FamilyQuant-wt/review-ds` |

Flow of one step: the owner posts `READY <step> <commit>`; FQReviewDS posts `APPROVED` or `REJECTED`; a rejection is the owner's next commit; the orchestrator gates, merges and closes the row.

## Design rules for the look (hard requirements)

1. **No yellow or gold anywhere,** the focus ring included. One accent colour, for the primary action and selection only.
2. **One surface family.** Every colour comes from a token.
3. **Colour means status or nothing.**
4. **One primary button per panel,** the others quiet.
5. **One line of explanation at most on a screen.** The rest goes to Help (F1) and tooltips.
6. **Standard Windows order:** File first, then Edit, View, Analysis, Help.

## Workstreams

Columns: Diff = L, M, H. Hrs = worker hours at measured speed. Blocked by: 🟢 = open now. The graph follows the Blocked by column. A step lands when its owner posts DONE with a commit; it is done when a reviewer approves it and it is merged.

### W1 The Grouping panel in Ana's order (FQPanel)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W2 Guards, messages and Help (FQGuard)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W3 Reviews, version and the gate

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W4 Release 2.1.5

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| U4c | README: the step-by-step guide follows the numbered order; a new synthetic dark screenshot of the Grouping panel; reads 2.1.5 | L | 0.3 | 🟢 | U4d | Synthetic data only; links to releases/latest | RL | | 0 | 0 | |
| U4d | Review of the README | L | 0.1 | U4c | U4e | APPROVED | DS | | 0 | 0 | |
| U4f | Update check on the public files: an installed 2.1.4 (test root) shows the banner for 2.1.5 and Install update runs the Setup in test mode | L | 0.3 | 🟢 | | PASS on every stage | RL | | 0 | 0 | |

## Critical path

W1 and W2 run in parallel from now, U3c beside them; their reviews U3a and U3b; the full gate U3d; the package U4a and its check U4b; the publish U4e; the update check U4f. Rough wall-clock estimate: 2 to 3 hours from 11:25, a range, never a promise.

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
| D58 | DeepSeek on the team | DeepSeek V4.1 Flash joins as a second reviewer while Grok is out, on code and synthetic data only (its API is hosted in China; Ana's data never goes to it). Paulo, 11-04 09:20 ("grok is out but deepseek 4.1 is back as a reviewer, bring him to the team") |
| D59 | Who reviews | Every review goes exclusively through the DeepSeek reviewer FQReviewDS; Juliana's Opus reviewer is not used for now. Paulo, 11-04 09:56 ("lets not use julianas reviewer yet, all reviews go exlcusively through the deepseek worker") |
| D60 | A guided order for the Families screen | Built now as 2.1.5 with Ana's order (Fetch evidence, Group by evidence, Apply grouping, Fetch family world, Compute), a moving next-step highlight, step status, offers for a missing input, an explanation for an empty scope and counted progress; published when every check is green, like D56. Paulo, 11-04 11:18 ("can you get a worer on this asap"), after Ana 11:10 |
| D61 | Releasing 2.1.5 without the ledger re-proof | The desk's cleanup deleted the evidence files the feature-ledger producers read, so every receipt went stale (desk mistake, 11:40). 2.1.5 ships on the full functional suite (every check, real data included, receipts deferred) and FQReviewDS's review; the ledger stays as proved for 2.1.4 at 9c03db8 the same morning; the release notes say so. Rebuilding the ledger evidence inside the repository follows as its own step. Paulo, 11-04 11:44 ("a") |

## Risks and triggers

| Risk | Trigger | Response |
|---|---|---|
| A moved button loses its handler or a test pins the old layout | The screen harness or a test fails after U1a | The owner fixes it as the next commit; no behaviour change is accepted in a layout step |
| The highlight rule reads a state the project does not store | U1b's test cannot set a state from a saved project | Rule from stored state only; anything else stays "not yet" |
| UI text names an organism, venom or toxin | The D21 test fails | No merge; the text is rewritten |
| A feature-ledger row pins an old label or tooltip | The full gate loses a ledger row | The owner re-proves the row, or the desk rules it an intended wording change before the gate |
| The DeepSeek balance runs low | The launcher refuses below USD 2 | Paulo tops it up; no other reviewer is used without his word (D59) |
