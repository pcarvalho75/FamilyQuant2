# FamilyQuant 2.0 roadmap (remaining work only)

Version 13, 2026-11-05 14:31, release 2.1.8 and the FamilyQuant website, in small steps (Paulo, 14:25: "a way more detailed roadmap with many small steps ... remove all that was already accomplished and bring the needle back to zero"). Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file and go to `docs/ROADMAP-archive.md`; the needle restarts at zero with each version.

Version 12 delivered 30 steps (in the archive): 2.1.7 published at 13:54 on 11-05 (fetch keeps its work, the update reopens the app, Setup checks versions, Close project) and every 2.1.8 build step (Home and one name per thing, the Families layout, the PatternLab-wins rule, the de novo Help page). Version 13 finishes 2.1.8 and builds the website, both split into small steps.

## The goal, in Paulo's words

- 11-05 14:25: "i wold like to see a way more detailed roadmap with many small steps ... remove all that was already accomplished and bring the needle back to zero"
- 11-05 14:03 to 14:22: the website: "very very very gorgeous", "very instructional so everyone can understand", "fun and intuitive", "let the science for the paper", a separate rigorous science page, an in-depth study first
- 11-05 13:57: "lets publish the next version asap... full throttle ahead!"

- 11-05 12:21: "lets ready every suggestion from ana as you suggested also, prepare the roadmap and begin, 2 sol workers and 1 kimi reviewer"

## What Ana reported, and what the desk found in the code

| Slide | Ana | Found in the code (11-05) | Steps |
|---|---|---|---|
| 1 | Fetch evidence stops ("UniProt refused a panel page (HTTP 500)") and the domain column stays empty, all morning | The fetch is all or nothing: one failed reference-panel batch (25 of 64) discards the protein evidence already downloaded (`EvidenceFetchSession.FetchStagedAsync`). Three tries, 1 s and 2 s apart (`UniProtHttp.GetWithRetryAsync`). UniProt published release 2026_04 on 11-04; it answered normally at 12:15 on 11-05 | W1 |
| 2 | The Start screen is confusing; use Home; name the flow as in 1.5 (Build families, Curate families with Group by evidence and Group by words); add File > Close project | One thing carries up to three names; no Close project exists | W3, W4 |
| 3 | One name per step: Quantify (not Compute full XIC), Results (not Analytics / report), De novo review (not De novo or De novo evidence); Home shows the five numbered steps | Confirmed: sidebar, Analysis menu and Help use different names for the same screens | W4 |
| 4 | Does Quantify follow her de novo flow (PatternLab and PEAKS, same scan, keep the reliable call, XIC of de novo only, PepExplorer)? Is 80% identity used? | Quantify does the de novo work; De novo review is inspection only. A de novo peptide is dropped when PatternLab identified the same peptide (run, sequence with I = L, charge, mass). Homology is PepExplorer's method, minimum identity 0.80 (`HomologyOptions.DefaultMinIdentity`), raised by the decoy gate when it is on. A scan where PatternLab and the de novo call disagree on the sequence has no rule that picks one: possible double count, to be measured | W6 |
| 5 | Install update closes the program with no message and does not reopen it; the next start shows "Upgrade FamilyQuant 2.1.6.0 to 2.1.4.0" | The silent update never relaunches the app (`MainWindow.UpdateInstall.cs`, Setup `/silent /waitpid`). The 2.1.4 page is an old Setup.exe (VERSION 2.1.4.0) the user opened from an icon; Setup does not compare versions (`InstallerWindow.IsUpgrade`), so Continue would downgrade | W2 |
| 6 | Families screen: align the two grid headers, title each grid, move All proteins above the protein grid, centre the family numbers | Layout only. Her earlier question (USearch ID not shown) is answered here too | W5 |

## Rules every worker follows

1. **Only the program makes results.** Python may investigate or check a number, never make one that reaches a person.
2. **No exception lists.** No family, protein, Pfam or keyword literal in a rule; evidence and data decide, and what a user chooses is stored in the project. Nothing the user sees names an organism, a venom, a toxin or a sample (D21; the suite tests it).
3. **No function is lost.** The feature ledger is the contract: only Paulo can drop a row. A rename keeps the function; a ledger row that pins an old name is mapped in the ledger's "Added contracts after the frozen source" section, never edited to make a check pass.
4. **Numbers stay equal unless a step says otherwise.** Only Z6c may change a number, and only after Paulo's decision, a before and after on real data and a science review.
5. **Deployable at every merge.** One step per commit, with its test; tests go into DefaultSuite. Builders run focused checks; the desk runs the whole suite once per release (D61 mode).
6. **A step counts as done when a reviewer approves it** (Paulo, 11-05 13:41: "many approvals and no green boxes?"). Each release still ships only after its own full suite (Z7c, Z8c) is green on the merged commit; a suite failure reopens the step it points to until the fix is approved.
7. **A family panel is user data.** Ana's families, domains, subclasses and decisions live in her file and the project, never in code.
8. **Never write inside `C:/Users/paulo/Desktop/AnaG`** except a delivery to `AnaG/Bjararaca/Relatorio` on Paulo's word.
9. **The code repository stays private.** Releases go out as a Setup.exe and a zip on the public releases-only repository (D31, D32).
10. **No em dashes or en dashes** in anything written, UI text included.
11. **Steps stay small.** A step that hides several pieces of work is split, so the needle shows real progress.
12. **Never install on this PC** (D31): Setup runs in test mode only.

## The team

| Name | Engine | Role in version 12 | Worktree |
|---|---|---|---|
| Orchestrator | Claude, main session | Assigns, merges, routes reviews, runs the suite once per release, publishes, keeps this file and the board true. Writes no product code. | |
| FQFetch (FE) | Sol 6.1 xhigh | W1 Fetch evidence that keeps its work (2.1.7); then W6 de novo | `D:/FamilyQuant-wt/fetch` |
| FQHome (HM) | Sol 6.1 xhigh | W2 Update and Setup and W3 Close project (2.1.7); W7 release 2.1.7; then W5 Families layout and W8 release 2.1.8 | `D:/FamilyQuant-wt/home` |
| FQOpus (OP) | Opus 5.5, this session's Fiocruz account (D67) | W4 Home and one name per thing (2.1.8) | `C:/FamilyQuant-wt2/opus` |
| FQSite (ST) | Opus 5.5, an agent of this session on the Fiocruz account (D69) | W9 the FamilyQuant website | `C:/FamilyQuant-wt2/website` |
| FQKimi (KM) | Kimi Code (K3.1 Pro) | Reviewer (D64), with FQReviewDS; the desk gives each finished step to whichever reviewer is free, in batches of at most 8; code and synthetic data only. Never edits product code. | `D:/FamilyQuant-wt/review-kimi` |
| FQReviewDS (DS) | DeepSeek V4.1 Flash | Second reviewer (D67); code and synthetic data only. Never edits product code. | `D:/FamilyQuant-wt/review-ds` |

Kimi limits: one Kimi instance at a time across every project, at most 15 calls in any rolling 5 hours (Paulo, 11-01) and 15 reviews in any rolling hour (Paulo, 11-05 13:24: "raise kimi to 15 reviews"; it was 5 from 11-02), no subagents. Orion runs its own Kimi worker at times; the desk launches FQKimi only when no other Kimi worker runs.

## Design rules for the look (hard requirements)

1. **No yellow or gold anywhere,** the focus ring included. One accent colour, for the primary action and selection only.
2. **One surface family.** Every colour comes from a token.
3. **Colour means status or nothing.**
4. **One primary button per panel,** the others quiet.
5. **One line of explanation at most on a screen.** The rest goes to Help (F1) and tooltips.
6. **Standard Windows order:** File first, then Edit, View, Tools, Help (Tools replaces Analysis in Z4g).

## Workstreams

Columns: Diff = L, M, H. Hrs = worker hours at measured speed. Blocked by: 🟢 = open now. A step is done when a reviewer approves it (desk steps: when their check passes); each release ships only after its own full suite.

### W8 Release 2.1.8 (FQFetch, FQOpus, FQHome, Orchestrator)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z8a | Version 2.1.8.0 in every place 2.1.7.0 is set, packaging defaults included | L | 0.1 | 🟢 | Z8c1 | Versions read 2.1.8.0; reviewed | FE | | 0 | 0 | |
| Z8a2 | The 2.1.8 release notes, claiming only what landed | L | 0.1 | 🟢 | Z8c1 | Reviewed | FE | | 0 | 0 | |
| Z8b | The public README reads 2.1.8, with the new Home screenshot and the new names | L | 0.1 | 🟢 | Z8d5 | Reviewed; synthetic only | OP | | 0 | 0 | |
| Z8c1 | The final 2.1.8 integration: master plus every approved 2.1.8 commit | L | 0.1 | Z8a, Z8a2 | Z8c2, Z8d1 | Merged clean; build 0 warnings | Orchestrator | | 0 | 0 | |
| Z8c2 | The full functional suite on the final integration (receipts deferred, D61) | M | 0.2 | Z8c1 | Z8d3 | Every check green | Orchestrator | | 0 | 0 | |
| Z8d1 | The 2.1.8 Setup.exe and zip from the integration commit, Setup in test mode | L | 0.1 | Z8c1 | Z8d2 | Built with 0 warnings | HM | | 0 | 0 | |
| Z8d2 | The package check | L | 0.1 | Z8d1 | Z8d3 | PASS | HM | | 0 | 0 | |
| Z8d3 | Tag v2.1.8 at the tested commit, merge into master, push | L | 0.05 | Z8c2, Z8d2 | Z8d4 | Tag and master on GitHub | Orchestrator | | 0 | 0 | |
| Z8d4 | The GitHub release draft with its four files; digests checked against the package | L | 0.1 | Z8d3 | Z8d5 | Digests equal | Orchestrator | | 0 | 0 | |
| Z8d5 | v2.1.8 published as Latest; README pushed | L | 0.05 | Z8d4, Z8b | Z8e1 | Latest is v2.1.8 | Orchestrator | | 0 | 0 | |
| Z8e1 | Fresh public downloads equal the published digests; 2.1.8 reports current | L | 0.1 | Z8d5 | Z8e2 | PASS | HM | | 0 | 0 | |
| Z8e2 | An installed 2.1.7 test root shows the 2.1.8 banner and updates in test mode | L | 0.1 | Z8e1 | Z8e3 | PASS | HM | | 0 | 0 | |
| Z8e3 | The app reopens once and says it was updated to 2.1.8 | L | 0.05 | Z8e2 | | PASS | HM | | 0 | 0 | |

### W1b Follow-ups for the next release (FQFetch, FQHome)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z1f | The resume cache uses short file names, so its paths stay well under 260 characters on any user profile; tests clean their own temporary files | L | 0.3 | Z8a2 | | Longest cache path measured; reviewed | FE | | 0 | 0 | |
| Z1g | Two exported presentation figures still name the organism and its venom in their captions (`FamilyQuant/PresentationFigures.cs:1854` and `:2020`, found by FQOpus); neutral wording, and the D21 check stops skipping that file for caption text | L | 0.2 | 🟢 | Z1j | No organism, venom or sample word in any exported figure text; the check goes red on a planted word | FE | | 0 | 0 | |
| Z1h | A flaky suite check: the Full XIC column attach expects PENDING before READY and failed 2 of about 10 runs on a timing race (FQOpus); make it wait for the state instead of racing | L | 0.2 | 🟢 | Z1j | 20 runs in a row green | FE | | 0 | 0 | |
| Z1i | The public README's Families, Quantify and Results screenshots re-captured from 2.1.8 (synthetic data), so every picture shows the new names | L | 0.2 | Z8e3 | Z1j | Every README image matches 2.1.8 | HM | | 0 | 0 | |
| Z1k | The program's Help says the de novo reconciliation allows for isotope selection errors (`FamilyQuant.Core/HelpContent.cs:514-515`), but the code uses a direct 20 ppm m/z match (`DeNovoReconciler.cs:68-87`); correct the Help to match the code (found by FQSite, confirmed by FQReviewDS on the science page) | L | 0.1 | 🟢 | Z1j | Help matches the code; reviewed | FE | | 0 | 0 | |
| Z1j | The follow-ups ship in the next release when Paulo asks for one | L | 0 | Z1f, Z1g, Z1h, Z1i, Z1k | | Paulo's word | Paulo | | 0 | 0 | |

### W9a The website: study and design (FQSite)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z9a | The study: how FamilyQuant works end to end, every claim with file:line (`UNDERSTANDING.md`) | M | 1 | 🟢 | Z9a2 | FQKimi checks it against the code: APPROVED | ST | | 0 | 0 | |
| Z9a2 | The algorithm walk-through added to the study: what each step computes, in order, and how the pieces connect (Paulo, 14:22) | M | 0.5 | Z9a | every page | Reviewed | ST | | 0 | 0 | |
| Z9a3 | `INSPIRATION.md`: what works on the SCPBrowser and APEX pages, and how this site goes beyond them | L | 0.2 | 🟢 | Z9b1 | Written | ST | | 0 | 0 | |
| Z9b1 | Art direction: palette from the brand (no yellow or gold), type, motion rules, light and dark; fun and friendly, never a research site | M | 0.3 | Z9a3 | Z9b2 | A one-page style sheet rendered | ST | | 0 | 0 | |
| Z9b2 | Page skeleton: sections, sticky navigation, phone layout without sideways scroll | M | 0.3 | Z9b1 | Z9b3, Z9c1 | Rendered at desktop and phone width | ST | | 0 | 0 | |
| Z9b3 | The hero: the animated family motif (sequences gathering into families) and one line on what FamilyQuant does | M | 0.4 | Z9b2 | Z9b4 | Rendered; respects reduced motion | ST | | 0 | 0 | |
| Z9b4 | The download button: reads the latest release from GitHub, shows the version, falls back to the releases page | L | 0.2 | Z9b3 | Z9f1 | The link resolves; the fallback works offline | ST | | 0 | 0 | |

### W9b The website: the basics, one animated explainer each (FQSite)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z9c1 | What a protein, a peptide and a spectrum are: a protein cut into peptides, peptides flying through the instrument | M | 0.4 | Z9b2, Z9a2 | Z9c2 | Animation with one-line captions | ST | | 0 | 0 | |
| Z9c2 | What homology is: relatives sharing an ancestor, shown as sequences that mostly match | M | 0.3 | Z9c1 | Z9c4 | Animation with captions | ST | | 0 | 0 | |
| Z9c3 | What Pfam is: a catalogue of protein building blocks, coloured blocks on a protein | M | 0.3 | Z9c1 | Z9d2 | Animation with captions | ST | | 0 | 0 | |
| Z9c4 | What alignment and sequence identity are: two sequences side by side, matches lighting up, a percentage | M | 0.3 | Z9c2 | Z9d1 | Interactive toy | ST | | 0 | 0 | |
| Z9c5 | What de novo means, next to a database match: one spectrum read two ways | M | 0.3 | Z9c1 | Z9d7 | Animation with captions | ST | | 0 | 0 | |
| Z9c6 | What a protein family is, and why counting by family is steadier than counting proteins | M | 0.3 | Z9c2 | Z9d5 | Animation with captions | ST | | 0 | 0 | |
| Z9c7 | The pop-up glossary: any term on the page explains itself on hover or tap | L | 0.2 | Z9c1 | Z9g1 | Every glossary term reachable by keyboard and touch | ST | | 0 | 0 | |

### W9c The website: how FamilyQuant works, step by step (FQSite)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z9d1 | Step 1, Build families: proteins grouped by sequence identity (USearch), animated | M | 0.4 | Z9c4 | Z9d8 | True to the study | ST | | 0 | 0 | |
| Z9d2 | Step 2a, Group by evidence: Pfam and family evidence from UniProt sorting proteins, animated | M | 0.4 | Z9c3 | Z9d8 | True to the study | ST | | 0 | 0 | |
| Z9d3 | Step 2b, Group by words: descriptions bringing names together | L | 0.2 | Z9d2 | Z9d8 | True to the study | ST | | 0 | 0 | |
| Z9d4 | The family world, answering Ana's question of 11-05 (why reach for relatives from other species?): the reference panel as a ruler that labels unreviewed proteins, never quantified; the optional family world for leftover de novo peptides only, shown as the Database gap, never changing the main totals | M | 0.3 | Z9d2 | Z9d8 | True to the study | ST | | 0 | 0 | |
| Z9d5 | Step 3, Quantify: peptides landing on families, isotope envelopes rising, signal adding up | M | 0.4 | Z9c6 | Z9d8 | True to the study | ST | | 0 | 0 | |
| Z9d6 | Step 4, Results: the report and its dot plot, what to look at | L | 0.3 | Z9d5 | Z9d8 | True to the study | ST | | 0 | 0 | |
| Z9d7 | Step 5 and de novo: how a de novo peptide finds its family by homology, and why the database match wins a shared spectrum | M | 0.4 | Z9c5 | Z9d8 | True to the study | ST | | 0 | 0 | |
| Z9d8 | The whole journey: one animation from a sample to a family pie | H | 0.6 | Z9d1 to Z9d7 | Z9g1 | Plays end to end; captions on each stage | ST | | 0 | 0 | |
| Z9d9 | Real app screenshots (synthetic data) placed next to each step | L | 0.2 | Z9d1 to Z9d7 | Z9g1 | Synthetic only | ST | | 0 | 0 | |

### W9d The website: example, difference, science page, practical (FQSite)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z9e1 | A fictitious snake venom: an invented dataset, labelled fictitious and illustrative on every view | L | 0.2 | Z9b2 | Z9e2 | No real number anywhere | ST | | 0 | 0 | |
| Z9e2 | Its interactive dot plot, in the style of the program's report | M | 0.4 | Z9e1 | Z9e3 | Hover and axes work at phone width | ST | | 0 | 0 | |
| Z9e3 | Its composition and the de novo contribution, animated | M | 0.3 | Z9e2 | Z9g1 | Labelled fictitious | ST | | 0 | 0 | |
| Z9i | What makes FamilyQuant different, as a fun side-by-side ("other tools see this, FamilyQuant sees this"); a line or two of sources | M | 0.4 | Z9d8 | Z9g1 | No claim anyone could knock down | ST | | 0 | 0 | |
| Z9j1 | The science page (science.html): its sections and anchors; a "the science behind this" button beside each idea on the main page | M | 0.3 | Z9a2 | Z9j2 | Each button lands on its section | ST | | 0 | 0 | |
| Z9j2 | Science page, part 1, rigorous: alignment and identity, evidence and Pfam, the family world | M | 0.5 | Z9j1 | Z9g1 | Every claim from the study | ST | | 0 | 0 | |
| Z9j3 | Science page, part 2, rigorous: quantitation and normalisation, de novo and homology, decoys and FDR, the PatternLab-wins rule, what the program does not do | M | 0.5 | Z9j2 | Z9g1 | Every claim from the study | ST | | 0 | 0 | |
| Z9f1 | Install and update guide, with the Windows "More info, Run anyway" step pictured | L | 0.2 | Z9b4 | Z9g1 | Matches the release notes | ST | | 0 | 0 | |
| Z9f2 | FAQ | L | 0.2 | Z9d8 | Z9g1 | Short answers | ST | | 0 | 0 | |
| Z9f3 | Authors (Ana Gisele Neves Ferreira, Richard Hemi Valente, Paulo Costa Carvalho, Luis Miguel Muñoz; Paulo, 11-05), credits, how to cite, and the footer with the Fiocruz logo and "All rights reserved, 2026, Fundação Oswaldo Cruz (Fiocruz)" | L | 0.1 | Z9b2 | Z9g1 | Rendered in both themes | ST | | 0 | 0 | |

### W9e The website: checks and Paulo's look (FQKimi, FQSite, Paulo)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z9g1 | Claims review: every statement on both pages against the study and the code | M | 0.4 | Z9d8, Z9e3, Z9i, Z9j3, Z9f2 | Z9h | APPROVED | KM | | 0 | 0 | |
| Z9g2 | Phone width, both themes, keyboard and screen reader basics, reduced motion | L | 0.2 | Z9d8 | Z9h | Checked with screenshots | ST | | 0 | 0 | |
| Z9g3 | Speed, no real data, no em or en dashes, every link resolves | L | 0.1 | Z9g1 | Z9h | Checked | ST | | 0 | 0 | |
| Z9k | Paulo's one fix after his look (11-05 16:08): peptides in the hero overview, and anywhere a dot stands for a peptide, drawn as short words of 4 to 6 linked amino acids instead of single dots | L | 0.3 | Z9g1 | Z9h | Rendered desktop and phone; nothing else changed | ST | | 0 | 0 | |
| Z9h | Paulo looks at it and decides where it is deployed | L | 0 | Z9g1, Z9g2, Z9g3, Z9k | | Paulo's word | Paulo | | 0 | 0 | |

## Critical path

2.1.8: Z8a and Z8a2 (FQFetch), the README review Z8b, then the final integration Z8c1, the suite Z8c2 with the package Z8d1 and Z8d2 in parallel, publish Z8d3 to Z8d5, update check Z8e1 to Z8e3. Rough wall-clock: 30 to 50 minutes from 14:30, a range, never a promise.

The website: the study Z9a and its walk-through Z9a2, design Z9b1 to Z9b4, then the explainers and steps (W9b, W9c) in order, the example, the difference, the science page and the practical parts, then the checks and Paulo's look. Rough wall-clock: 4 to 7 hours; one Opus builder, so the steps run in sequence.

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
| D62 | A clean Recent projects list | Built now as 2.1.6: unavailable files hidden, removal by right-click, a small x and Delete, and tests isolated from the user's list; same fast mode and test approach as 2.1.5 (D61); published when green. Paulo, 11-04 13:19 ("put that feature in and redeploy") |
| D63 | Ana's suggestions of 11-05 | All built as the desk proposed, in two releases, each published when every check is green (like D56 and D62): 2.1.7 = Fetch evidence that keeps its work and resumes, the update reopens the app, Setup compares versions, File > Close project; 2.1.8 = Home with five numbered steps and one name per thing (Ana's names, Analysis becomes Tools), the Families layout, USearch ID shown, the de novo Help page. Paulo, 11-05 12:21 ("lets ready every suggestion from ana as you suggested") |
| D64 | Who builds and reviews | Two Sol 6.1 builders, FQFetch and FQHome, and one Kimi reviewer, FQKimi, in batches; this replaces D59 (DeepSeek only). Kimi, like DeepSeek (D58), sees code and synthetic data only. Paulo, 11-05 12:21 ("2 sol workers and 1 kimi reviewer") |
| D66 | The same-scan conflict | Measured first on the real-data set (Z6a), then Paulo decides (D65); nothing changes before. Paulo, 11-05 12:21, accepting the desk's pick |
| D65 | A scan where PatternLab and the de novo call give different sequences for the same precursor | a) The PatternLab identification wins the scan: its FDR-controlled identification is kept and the de novo call on that scan gets no credit (Ana's step 4; it removes a double count, since both XICs measure the same precursor's ions). Z6a measured 305 to 431 such scans per run, 2.6 to 3.7% of the de novo signal and 0.35 to 0.55% of all signal at most. Built in Z6c as an option, ON by default, with before and after on real data and a science review. Paulo, 11-05 13:24 |
| D67 | More hands | Paulo, 11-05 12:38: "hire one more worker, an opus worker from this account, and also hire deepseek as a second reviewer". DeepSeek FQReviewDS joined at 12:41 (code and synthetic data only, as D58). The Opus CLI worker hit the account's weekly CLI limit at 12:42 (resets 11-06 08:00), so FQOpus runs at 12:48 as an Opus agent of this session, on the same account (Paulo, 12:47: "an opus from this account"); it takes W4, FQHome takes W5 and W8 from FQFetch |
| D68 | Kimi's review rate | 15 reviews in any rolling hour (was 5 from 11-02); 15 calls per rolling 5 hours and one instance unchanged. Paulo, 11-05 13:24 ("raise kimi to 15 reviews, and update that law") |
| D69 | A FamilyQuant website | A gorgeous, instructional public page in the spirit of the SCPBrowser page but better: how it works, why families, why peptide spectrum matches plus de novo, a fictitious and clearly labelled snake venom example, the download link, the Fiocruz logo in the footer; built by one more Opus from this account, after a deep study of the program; ChatGPT may make art. The program itself stays general (D21); the site uses fictitious data only. Paulo, 11-05 14:05 |
| D70 | Where the website goes | On patternlabforproteomics.org beside SCPBrowser, as a folder Paulo uploads; relative paths only, so any folder name works. The desk suggests /familyquant2/, because /familyquant/ may hold the 1.5 update files that 2.0 must never touch. Paulo, 11-05 14:47 ("yes, it will go here") |

## Risks and triggers

| Risk | Trigger | Response |
|---|---|---|
| A partial panel is used as if complete | A family is missing after Z1d | The panel is stored as incomplete with the missing families named; every step that reads it says so and offers Retry panel |
| Resume mixes two UniProt releases | The release changes between two fetches | Kept batches are keyed by release and discarded when it changes (Z1c); the evidence and panel release guard stays |
| A rename loses a function or breaks a ledger row | The full suite fails on a ledger or text check | The owner maps the old name to the new one in the ledger's Added contracts section; nothing is dropped or edited to pass |
| Setup reopens the app wrongly | Two windows, or a launch after a failed install | Launch only on the update path, once, as the user (Z2a test) |
| The reviewer is busy elsewhere | Another project's Kimi worker is running when a batch is ready | The batch waits in the queue; if a 2.1.7 batch waits more than 30 minutes the desk tells Paulo |
| Ana's data leaves this PC | Any review or brief | Kimi sees code and synthetic data only; Z6a reports counts and shares, never her rows |
