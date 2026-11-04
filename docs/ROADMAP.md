# FamilyQuant 2.0 roadmap (remaining work only)

Version 7, 2026-11-03 19:55, the interactive HTML report. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file and go to `docs/ROADMAP-archive.md`; the needle restarts at zero with each version.

Version 6 delivered 85 steps (in the archive): Ana's report with the corrected scope, releases 2.1.0 and 2.1.1 on GitHub, the new HomologySearch (Compute about 80 s instead of about 600 s), score tier on by default, and the last clarity fixes. Version 7 is one new piece Paulo asked for after reading the Word report: a simplified, graphical, interactive report.html written beside it from the same stored results. The release and the delivery to Ana wait on Paulo's word after he has seen the page.

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

Columns: Diff = L, M, H. Hrs = worker hours at measured speed. Blocked by: 🟢 = open now. The graph follows the Blocked by column. A small step lands when its owner posts DONE with a commit; it counts as done when a reviewer approves it.

### W1 Data and export (FQResults, FQShell and FQAssay)

The program side (FQShell joined at 20:30 on Paulo's "hire 1 more sol worker so we can speed things", FQAssay at 20:40 on "hire an additional sol worker"): what the page is fed, how it is written and exported, and the proof that every number is the stored one.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| M1a | Baseline: fq/results on current master; build and quick suite green before any change | L | 0.2 | 🟢 | M1b, M1c | 0 warnings; quick suite green | RS | | 0 | 0 | |
| M1d | Producer: the headline composition and family rows (names, shares, SD, CV, proteins, peptides); each share also gets a short label (for example 38.6 %) rounded by the program from the stored value, with the exact Word string in its pop-up | M | 0.2 | 🟢 | M1e, M1f, M1g, M1h | Builds; sample regenerated by the program, not by hand | RS | | 0 | 0 | |
| M1e | Producer: per-run shares and the parts within each family | L | 0.2 | M1d | M2e, M1m | Runs and parts filled for every family that has them | RS | | 0 | 0 | |
| M1f | Producer: highlights, measured signal by run and the Database gap (no methods or quality text: the page is plots) | L | 0.2 | M1d | M2c, M2g, M1m | Each printed value is a field bound to its stored value or Word cell | RS | | 0 | 0 | |
| M1g | Producer: coverage, with live positions when the stored results hold them, else the native figure | M | 0.2 | M1d | M2f, M1m | Coverage filled; the commit says which form and why | AS | | 0 | 0 | |
| M1h | Writer: fills the template once, with safe JSON escaping and byte-identical output | L | 0.2 | M1d | M1i | report.html written from a test project | SH | | 0 | 0 | |
| M1i | Export: report.html staged and published with the Word report and supplement, all or nothing | M | 0.2 | M1h | M1j, M1k | Three files appear together; cancel keeps the old ones | SH | | 0 | 0 | |
| M1j | Export: the section choices and the others threshold reach the page | L | 0.1 | M1i | M1l | An unchecked section is absent from the page | SH | | 0 | 0 | |
| M1k | Delivery rehearsal writes the page; the status message, Help text and docs name it | L | 0.1 | M1i | M1o | Rehearsal output holds report.html | SH | | 0 | 0 | |
| M1l | Tests: identical bytes for the same project; names and </script> escaped; excluded sections absent; three-file publication and cancel | M | 0.2 | M1j | M1o | Tests in the suite and green | SH | | 0 | 0 | |
| M1m | Tests: every printed number, pop-ups included, traces to a stored value or a Word cell | M | 0.2 | M1e, M1f, M1g | M1o | Trace test green; 0 NOT FOUND | AS | | 0 | 0 | |
| M1o | Proof on Ana's delivered project copy, no Compute: export the page with FQLook's template | M | 0.2 | M1k, M1l, M1m, M2j | M1p | SVMP 38.5988% and every share match her report; 0 NOT FOUND | RS | | 0 | 0 | |
| M1p | Build 0 warnings and quick suite green on the merged branch; READY M1 | L | 0.1 | M1o | M3b | READY M1 posted with the counts | RS | | 0 | 0 | |

### W2 The page (FQLook)

Plots, not text (Paulo, 20:15: "this html page is basically plots, this is not supposed to be a bible text like the report"). One self-contained template: inline CSS, JS and SVG, offline, no library; it prints only the display strings it is given.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| M2k | Design critique by FQOpus (Opus 5.5, Paulo's Fiocruz account): renders on Ana's data, a ranked punch list, top five marked do now; FQLook applies them | L | 0.3 | 🟢 | M2j | PUNCHLIST posted; FQOpus approves FQLook's next render | OP | | 0 | 0 | |
| M2j | Design pass on Ana's full sample: renders at 1440 and 390 px, light and dark, pop-ups open; READY M2 | M | 0.3 | 🟢 | M1o, M3b | Screenshots posted with the changes listed | LK | | 0 | 0 | |

### W3 Review and merge

Nobody reviews their own work; a finding is fixed by its owner and checked again.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| M3a | Code review of FQResults' lane as it lands: contract, producers, proof | M | 0.2 | 🟢 | M3d | APPROVED, or findings sent to the owner | GR | | 0 | 0 | |
| M3b | Code review of FQShell's and FQAssay's lanes as they land: writer, export, tests, coverage, trace tests | M | 0.2 | M1h | M3d | APPROVED, or findings sent to the owner | GR3 | | 0 | 0 | |
| M3c | Code review of FQLook's template as it lands: no client number formatting, no network, no innerHTML with data, keyboard reach (Paulo judges the design himself at M3f) | M | 0.2 | 🟢 | M3d | APPROVED, or findings sent to the owner | GR2 | | 0 | 0 | |
| M3g | Opus review (FQOpus, Paulo's Fiocruz account), batched: the final page exported from Ana's project, every number against the Word report it wrote (K14), plus the design check of FQLook's render | M | 0.3 | M1p, M2j | M3d | APPROVED, or findings sent to the owners | OP | | 0 | 0 | |
| M3d | Owners fix the review findings; the reviewer checks again | L | 0.2 | M3a, M3b, M3c, M3g | M3e | Every finding closed | RS | | 0 | 0 | |
| M3e | Desk gate and merge to master | L | 0.2 | M3d | M3f | Gate green; master pushed | Orchestrator | | 0 | 0 | |
| M3f | Paulo sees the page and the screenshots | L | 0.1 | M3e | M4a, M5a | Paulo's OK or his changes | Paulo | | 0 | 0 | |

### W4 Release 2.1.2 (only on Paulo's word)

The same chain as 2.1.1.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| M4a | Version 2.1.2 and release notes | L | 0.3 | M3f | M4b | Version in every place K19 changed; notes claim only what is on master | EV | | 0 | 0 | |
| M4b | Full release gate on the 2.1.2 commit | M | 0.3 | M4a | M4c | Ledger 5,460 of 5,460, full suite green | Orchestrator | | 0 | 0 | |
| M4c | The 2.1.2 package and its real-menu export of a copy of Ana's project | L | 0.3 | M4b | M4d | Zip and SHA256; export writes all three files | EV | | 0 | 0 | |
| M4d | v2.1.2 published on FamilyQuant2-releases | L | 0.1 | M4c | M4e | Release page live and Latest; downloaded zip hash equals M4c | Orchestrator | | 0 | 0 | |
| M4e | Install check from GitHub; 2.1.1 offers 2.1.2 | L | 0.2 | M4d |  | Both pass | EV | | 0 | 0 | |

### W5 Delivery to Ana (only on Paulo's word)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| M5a | report.html into Ana's Relatorio folder, exported from her delivered project with no Compute | L | 0.2 | M3f |  | File delivered; her other files unchanged by hash | Orchestrator | | 0 | 0 | |

## Critical path

M1b, M1d, the producers M1e to M1g, the writer M1h and export M1i, the tests, then the proof M1o (which also needs FQLook's page, M2a to M2j), the reviews M3a and M3b, the fixes M3d, the gate M3e and Paulo's look M3f. Reviewers: FQReview, FQReview2 and FQReview3 (hired 20:45 on Paulo's "hire a new grok reviewer"), and FQOpus for the final page (20:52, "put the opus carvalho paulo as a reviewer"). The release (W4) and the delivery to Ana (W5) start only on his word.

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
