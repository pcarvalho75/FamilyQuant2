# FamilyQuant 2.0 roadmap (remaining work only)

Version 8, 2026-11-04 00:05, the two releases of the night. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file and go to `docs/ROADMAP-archive.md`; the needle restarts at zero with each version.

Version 7 delivered 42 steps (in the archive): the interactive report.html written beside the Word report (data contract, producers with short labels, writer, three-file export, trace with 0 NOT FOUND on Ana's real export, the page with FQOpus' design critique, a dot plot with selectable axes, all reviews and gates), the 2.1.2 version and release gate, and the public README with manual and screenshots. Version 8 is what remains: publish 2.1.2 tonight, then 2.1.3 with a real installer and automatic updates, both without questions while Paulo sleeps (D51, D52).

## The goal, in Paulo's words

- Mission J, 11-02: "the ultimate test for this progrm ... make a very cool and complete analysis ... generate a complet word document. if you need a feature that this program does not do you implement it in the program", "she wants a qauntitative description of the families", "by the end of this project i would like a report in the directory where anas files are. there should be a simple way to input the families"
- 11-03: "i would like to hand her the software as well so she can experiment", "we publish it in github and she installs it from there"
- 11-03: "there needs to be a detiled progress bar for generating this report or else we cant tell if the software stalled", "yes, improve on reporting"
- 11-03: "can you think of a way to optimize the compute ... what is consuming it so much", "we need this to speed up things"
- 11-03: "i would also like to see the steps more frgmented (small steps) so I can actually sense the progress and make sure we are not hitting a wall"
- 11-03 19:20: "i went through the report and its something very complicated and extensive to read... we could also generate side by side a simplified report which is an html, its mostly graphical related, interactive and awesomely cool! not alot of text, maybe pop-ups, but mostly graphical oriented and needs to be awesome and very nicely crafted and appealing with professional design"
- Standing: "using python scripts was cheating, every result passed to ana needs to be through the software", "things need to look slick and clean"

## Rules every worker follows

1. **Only the program makes results.** Python may investigate or check a number, never make one that reaches a person.
2. **No exception lists.** No family, protein, Pfam or keyword literal in a rule; evidence and data decide, and what a user chooses is stored in the project. Nothing the user sees names an organism, a venom, a toxin or a sample (D21; the suite tests it).
3. **No function is lost.** The feature ledger is the contract: a release cannot pass with a row open, and only Paulo can drop a row. Ledger rows and receipts are never edited to make a check pass.
4. **Numbers stay equal unless a step says otherwise.** A speed change must leave every stored result byte-identical; a step that means to change a number shows before and after on real data and passes a science review.
5. **Deployable at every merge.** One step per commit, with its test; tests go into DefaultSuite. Builders and reviewers run focused checks; the gate runs the whole suite.
6. **A step counts as done only after a reviewer approves it, it is merged and the gate is green.**
7. **A family panel is user data.** Ana's families, domains, subclasses and decisions live in her file and the project, never in code.
8. **Never write inside `C:/Users/paulo/Desktop/AnaG`** except the final delivery to `AnaG/Bjararaca/Relatorio` (step K17).
9. **The code repository stays private.** Releases go out as a zip on the public releases-only repository (D31, D32).
10. **No em dashes or en dashes** in anything written, UI text included.
11. **Steps stay small.** A step that hides several pieces of work is split, so the needle shows real progress (Paulo, 11-02 and 11-03).

## The team

| Name | Engine | Role | Worktree |
|---|---|---|---|
| Orchestrator | Claude, main session | Assigns, gates, merges, publishes on Paulo's word, keeps this file true. Writes no product code. | |
| FQOpus (OP) | Opus 5.5 xhigh | Runs Ana's analysis through the program, the interpretation and the delivery | `D:/FamilyQuant-wt/opus` |
| FQEvidence (EV) | Sol 6.1 xhigh | The 2.1.0 release: candidate, unpacked checks, install check from GitHub | `D:/FamilyQuant-wt/evidence` |
| FQResults (RS) | Sol 6.1 xhigh | The report export fix and the delivery command | `C:/FamilyQuant-wt2/results` |
| FQShell (SH) | Sol 6.1 xhigh | Progress for Export report, Compute and opening | `D:/FamilyQuant-wt/shell` |
| FQLook (LK) | Sol 6.1 xhigh | Figures on large projects and the report's look | `D:/FamilyQuant-wt/look` |
| FQAssay (AS) | Sol 6.1 xhigh | The faster fetch and Compute speed | `D:/FamilyQuant-wt/assay` |
| FQFloater (FL) | Sol 6.1 xhigh | One total per panel family | `C:/FamilyQuant-wt2/floater` |
| FQTerra1 (T1), FQTerra2 (T2) | GPT-6.1 Terra xhigh | Export speed, number trace; scope clarity, link notes, Help | `C:/FamilyQuant-wt2/terra1`, `terra2` |
| FQReview (GR) | Grok 4.7 xhigh | Main and science reviewer. Never edits product code. | `D:/FamilyQuant-wt/review` |
| FQReview2 (GR2) | Grok 4.7 xhigh | Second reviewer. Never edits product code. | `D:/FamilyQuant-wt/review2` |
| FQKimi (KM) | Kimi | Batch reviewer, at most 15 calls in 5 hours and 5 reviews an hour. Never edits product code. | `D:/FamilyQuant-wt/kimi` |

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

### W1 Release 2.1.2 tonight

Paulo's word: publish when done, no questions (D49, D51). Ana should find it when she wakes.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W2 Release 2.1.3: the installer and automatic updates

Paulo, 23:50 to 23:57 (D52): a real installer, Help > Install update, and a check at every start with a notice on screen; published when every check is green.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| M7i | The 2.1.3 zip and Setup.exe from that commit; Setup.exe in test mode extracts and the app reports 2.1.3 | L | 0.2 | 🟢 | M7e | Both files with SHA256; test-mode install passes | AS | | 0 | 0 | |
| M7e | v2.1.3 published with the Setup.exe and the zip; README reads 2.1.3 throughout (Paulo 00:11), with the installer section, pushed | L | 0.2 | M7i |  | Release live and Latest; hashes equal M7i; README live | Orchestrator | | 0 | 0 | |

### W3 Held for Paulo's word

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| M5a | report.html into Ana's Relatorio folder (the desk never writes there without his word) | L | 0.1 | 🟢 |  | File delivered; her other files unchanged by hash | Orchestrator | | 0 | 0 | |

## Critical path

2.1.2: M8a, M4c, M4d (publish), M4e; M5b and the README screenshots (M6f, M6g) run beside it. 2.1.3: M7a, M7b and M7c in parallel now, their three reviews, the full gate M7h after 2.1.2 is out, the package M7i and the publish M7e. M5a stays held.

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

## Risks and triggers

| Risk | Trigger | Response |
|---|---|---|
| The export fails on the real project in another way | J30b or K14 stops with an error | The export log now carries the full stack (J24); the owner fixes it as the next commit; Paulo hears the new time at once |
| The corrected scope pulls in a protein that is not a venom protein | K3 lists one | The desk rules before K6; the report's method names the scope |
| A speed change moves a number | Any stored result differs from before | No merge; the change goes back (rule 4) |
| A quick gate hides a stale ledger receipt | K9's full gate fails on receipts | The owner of the row re-proves it before the candidate is published |
| The release does not install on Ana's machine | K12 or K13 fails | Fix before K11 or publish 2.1.1; 2.0.0 and 1.5 stay installable |
| A test leaves files that fail the next gate | A failure names output the commit cannot make | The suite empties each shard's folder before a run (11-03); suspect leftovers first |
| The page shows a number the Word report does not | M1m or M1o finds a printed number with no stored source | No merge; the page prints only payload display strings (M1b) |
| Ana's folder is touched | Any file under AnaG changes outside Relatorio | Stop and restore; checked before and after K17 |
| The machine is shared by several large runs | A run's time grows past its estimate | The desk reorders unstarted work; never stops a running run |
