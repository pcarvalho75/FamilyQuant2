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
| K7 | Second reader: every number in the program-written Summary of findings (J44) on Ana's project matched to its cell in the program's exports | M | 0.4 | 🟢 | K17 | A table of number, source cell and match, with no mismatch | GR2 | | 0 | 0 | |

### W3 The report export works and is fast

The export failed on Ana's project (a figure read the on-screen palette from the report's thread), repeated 15 minutes of work, and drew figures too large to save.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W4 Release 2.1.0 on GitHub

Ana installs it from the public releases page (D31); the desk publishes on Paulo's word, given 11-03.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| K8c | The cheap ungated tests come into the gates (2.1.1): from K8b's exclusion list, every fixture that needs no RAW and runs in under a minute moves into DefaultSuite, one commit per group; any that fails gets its own fix or an owner | M | 1 | 🟢 | | Exclusion list shorter by every cheap fixture; quick suite still under its time budget; each moved fixture green | FL | | 0 | 0 | |
| K9 | Full gate on the release candidate re-cut after J41 (and J32, J34 if merged): every ledger receipt current (quick gates defer them) | M | 0.5 | 🟢 | K10 | Full gate green, ledger 5,460 of 5,460 | Orchestrator | | 0 | 0 | |
| K10 | The unpacked candidate exports the final project's report through the real menu | L | 0.3 | K9 | K11 | Files and times recorded | EV | | 0 | 0 | |
| K11 | Published: v2.1.0 on pcarvalho75/FamilyQuant2-releases with the zip, its .sha256 and user release notes | L | 0.1 | K10 | K13, K17 | The release page is live | Orchestrator | | 0 | 0 | |
| K13 | Install check on 2.1.0 from GitHub; 2.0.0's Check for updates finds 2.1.0 | L | 0.2 | K11 | | Both pass | EV | | 0 | 0 | |

### W5 Ana's report delivered

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J34f | A Word table that continues on the next page repeats its header row there, and its caption stays with its last row (Ana's report p18-p20, results/product-parts: no header on p19, the caption alone at the top of p20; FQLook's K16, 12:14) | L | 0.3 | 🟢 | K14 | Red then green on a fixture that spans a page; the page check shows p19 with the header and the caption with the table; no cell changes | SH | | 0 | 0 | |
| K14 | Final export of the interpreted project through the real command, on master with J30, J24, J16, J32 and J34 | L | 0.5 | J34f | K15, K16 | Word report and supplement written | OP | | 0 | 0 | |
| K15 | Number trace on the final report: every number found, or each one not found ruled | L | 0.2 | K14 | K17 | `trace.csv` with nothing left open | T1 | | 0 | 0 | |
| K16 | The final report's pages read for look and sense | L | 0.3 | K14 | K17 | No blocking item | LK | | 0 | 0 | |
| K17 | Delivered: the Word report, supplement, project, family panel, a read-me with the release link, and the AI key file (D42) written to `AnaG/Bjararaca/Relatorio`; nothing else in Ana's folders touched | L | 0.2 | K7, K15, K16, K11 | | The five items with hashes; the rest of AnaG unchanged | OP | | 0 | 0 | |

### W6 Compute fast and visible

Compute took 37 to 54 minutes on Ana's project with the RAW cache warm, silent for most of it, and opening a project searched for 10 to 22 minutes (now cached, J28).

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J38d | New HomologySearch method (Paulo, 11-03 09:20) from Fable's plan (docs/ideas/HOMOLOGY-PERFORMANCE.md): the mismatch-budget early exit, the exact-containment shortcut for best-score identified queries with the matrix guard, and the diagonal q-gram prefilter; every caller switches to it; the old HomologyMapper stays unchanged, marked [Obsolete] ("use HomologySearch"), as the reference the equivalence tests compare against | M | 1 | 🟢 | J38e | Every stored result byte-identical to the old method on fixtures and on the full-run copy; old and new compared hit for hit on every search call | AS | | 0 | 0 | |
| J38e | Compute before and after on the full-run copy | L | 0.3 | J38d | J38f | Both times; stored results byte-identical | AS | | 0 | 0 | |
| J38f | J38 reviewed and merged | L | 0.2 | J38e | | Approved; gate green | AS | | 0 | 0 | |
| J47a | Fable's science options in HomologySearch, each off by default: a decoy-calibrated identity gate per tag length; isobaric-swap folding for de novo tags (N/GG, Q/AG, equal-mass pair order, K/Q) weighted by PEAKS per-residue confidence; crediting families by score tier with the multiplicity reported | M | 1.5 | J38d | J47b | Options off: byte-identical; options on: their own tests | AS | | 0 | 0 | |
| J47b | Validation of each science option on the full-run project: target-decoy rates at the gate, the Database gap error rates, the leave-one-out family check (J42: 966 right, 12 wrong, 95 abstained), and what changes in the headline; Paulo decides which become default (Paulo, 11-03 13:15: no recomputing everything: validate by replaying FQAssay's captured five search inputs and the cached attribution, one full Compute only for the options chosen as defaults) | M | 1 | J47a | | A before/after table per option; science review | AS | | 0 | 0 | |

### W7 Clear reasons and totals

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J43 | With a panel in use, the headline and composition figures use the per-panel-family totals (D40, B): before and after shown on a real project with several families in one panel family, science review, the old unit kept in the supplement | M | 1 | 🟢 | | Red then green on a multi-family fixture; before/after table on a real project; reviewed | FL | | 0 | 0 | |
| J49 | The number trace cites the right cell (2.1.1): a printed number is matched first to cells of the sentence's own subject (the family, run or option it names), and a number that equals several cells with no subject match is reported AMBIGUOUS instead of the first equal cell (K15, 11-03: the 14-protein estimate of one family was cited to the '14 families above threshold' cell; the cover's 2.0 to a protein count) | M | 1 | 🟢 | | On Ana's export every K15 wrong-cell example cites its subject cell; ambiguous cases listed; NOT FOUND unchanged | RS | | 0 | 0 | |

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
