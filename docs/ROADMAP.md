# FamilyQuant 2.0 roadmap (remaining work only)

Version 6, 2026-11-03 07:35, the end of mission J. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file and go to `docs/ROADMAP-archive.md`; the needle restarts at zero with each version.

Version 5 delivered 52 steps (in the archive): the family panel, families by evidence, subclasses, the numbers Ana asked for, the Word report and its supplement, the number trace, faster reopen and the first full run on her data. Version 6 is what remains, cut into small steps so the needle moves with real progress: Ana's report with the corrected CTL scope (D39), the 2.1.0 release on GitHub for her to install, a report export that works and is fast on her project, a visible and faster Compute, and the last clarity fixes. Each code piece ends with its own "reviewed and merged" step.

## The goal, in Paulo's words

- Mission J, 11-02: "the ultimate test for this progrm ... make a very cool and complete analysis ... generate a complet word document. if you need a feature that this program does not do you implement it in the program", "she wants a qauntitative description of the families", "by the end of this project i would like a report in the directory where anas files are. there should be a simple way to input the families"
- 11-03: "i would like to hand her the software as well so she can experiment", "we publish it in github and she installs it from there"
- 11-03: "there needs to be a detiled progress bar for generating this report or else we cant tell if the software stalled", "yes, improve on reporting"
- 11-03: "can you think of a way to optimize the compute ... what is consuming it so much", "we need this to speed up things"
- 11-03: "i would also like to see the steps more frgmented (small steps) so I can actually sense the progress and make sure we are not hitting a wall"
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

Columns: Diff = L, M, H. Hrs = worker hours at measured speed. Blocked by: 🟢 = open now. The graph follows the Blocked by column.

### W1 Ana's analysis with the corrected scope

The rerun of the full analysis with the scope "Toxin", "venom gland" (D39), so the panel's galactose-binding lectins join CTL, and its science check.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W2 The interpretation

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W3 The report export works and is fast

The export failed on Ana's project (a figure read the on-screen palette from the report's thread), repeated 15 minutes of work, and drew figures too large to save.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W4 Release 2.1.0 on GitHub

Ana installs it from the public releases page (D31); the desk publishes on Paulo's word, given 11-03.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W5 Ana's report delivered

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W6 Compute fast and visible

Compute took 37 to 54 minutes on Ana's project with the RAW cache warm, silent for most of it, and opening a project searched for 10 to 22 minutes (now cached, J28).

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J47c | The tag-gate option made fast enough to validate: a conservative weighted seed prefilter (the replay of one search ran over 30 minutes because weighted profiles fell back to full window scans), proven equal to the brute path, then its J47b row (known answers, decoy rates, headline); it stays OFF until Paulo chooses | M | 1 | J47a | | Prefilter equals brute on fixtures; OFF replay still equal; the tag row added to the J47b table | AS | | 0 | 0 | |

### W7 Clear reasons and totals

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W8 Release 2.1.1

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

## Critical path

Ana's report: K1, K2, K4, K6, then K14 (which also needs J30c, J24c, J16b, J32d, J34c), K15 and K16, then K17. The release runs beside it: K8, K9, K10, K11, and K17 links the published release. Compute speed (W6) and the clarity steps (W7) ship in 2.1.0 if they land before the candidate (K8), otherwise in 2.1.1.

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

## Risks and triggers

| Risk | Trigger | Response |
|---|---|---|
| The export fails on the real project in another way | J30b or K14 stops with an error | The export log now carries the full stack (J24); the owner fixes it as the next commit; Paulo hears the new time at once |
| The corrected scope pulls in a protein that is not a venom protein | K3 lists one | The desk rules before K6; the report's method names the scope |
| A speed change moves a number | Any stored result differs from before | No merge; the change goes back (rule 4) |
| A quick gate hides a stale ledger receipt | K9's full gate fails on receipts | The owner of the row re-proves it before the candidate is published |
| The release does not install on Ana's machine | K12 or K13 fails | Fix before K11 or publish 2.1.1; 2.0.0 and 1.5 stay installable |
| A test leaves files that fail the next gate | A failure names output the commit cannot make | The suite empties each shard's folder before a run (11-03); suspect leftovers first |
| Ana's folder is touched | Any file under AnaG changes outside Relatorio | Stop and restore; checked before and after K17 |
| The machine is shared by several large runs | A run's time grows past its estimate | The desk reorders unstarted work; never stops a running run |
