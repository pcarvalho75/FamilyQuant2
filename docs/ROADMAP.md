# FamilyQuant 2.0 roadmap (remaining work only)

Version 12, 2026-11-05 12:30, releases 2.1.7 and 2.1.8: Ana's suggestions of 11-05. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file and go to `docs/ROADMAP-archive.md`; the needle restarts at zero with each version.

Version 11 delivered 10 steps (in the archive): the clean Recent projects list, published as 2.1.6 at 14:21 on 11-04, and its update check. Version 12 builds every suggestion in Ana's deck `FamilyQuant_2_1_6_0_Sugestoes.pptx` (6 slides, 11-05), as the desk proposed and Paulo accepted.

## The goal, in Paulo's words

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

Columns: Diff = L, M, H. Hrs = worker hours at measured speed. Blocked by: 🟢 = open now. A step is done when a reviewer approves it; the release's full suite gates the release.

### W1 Fetch evidence that keeps its work, 2.1.7 (FQFetch)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z1a | Protein evidence survives a panel failure: when the reference panel cannot finish, the fetched protein evidence (domains, families, source) is still applied and saved, the panel is stored as missing with its reason, and the release guard between evidence and panel still holds | H | 0.5 | 🟢 | Z1d, Z1e, Z7c | A test whose handler fails one panel batch leaves the domains applied and the panel marked missing; the file-changed and cancel guards still apply nothing | FE | | 0 | 0 | |
| Z1b | Patient retries: server errors (5xx), 429 and timeouts retried with growing waits (about 5, 15, 45 and 120 s); the progress window says "UniProt busy, retrying in N s"; other 4xx fail at once; Cancel stops the wait | M | 0.3 | 🟢 | Z1d, Z7c | A handler that fails 3 times then answers completes; a 400 fails at once; Cancel during a wait cancels | FE | | 0 | 0 | |
| Z1c | Resume: finished evidence and panel batches are kept on disk, keyed by query and UniProt release, so the next Fetch evidence starts from the first unfinished batch; a release change discards them | M | 0.5 | 🟢 | Z1e, Z7c | Fail at batch 25, fetch again: batches 1 to 24 are not asked again and the result equals an uninterrupted fetch byte for byte | FE | | 0 | 0 | |
| Z1d | A batch that still fails is split in halves down to single families; a family UniProt refuses is named, and the panel is stored as incomplete with that family listed. An incomplete panel is never used as complete: the steps that read it say so and offer Retry panel | M | 0.4 | Z1a, Z1b | Z1e, Z7c | One poisoned family among 100 leaves 99 fetched, the one named; Fetch family world reports the incomplete panel instead of using it | FE | | 0 | 0 | |
| Z1e | Plain words on failure: the status line and the result say what was kept and what is missing, for example "Protein evidence saved for 40,250 proteins. The reference panel stopped at 1 of 64 batches (UniProt server error, HTTP 500). Retry panel"; a Retry panel button on the Families screen | L | 0.2 | Z1a, Z1c, Z1d | Z7c | Message rendered dark and light with --screens; Retry panel completes a stored incomplete panel | FE | | 0 | 0 | |

### W2 Update and Setup, 2.1.7 (FQHome)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z2a | After Help > Install update, Setup reopens FamilyQuant when it finishes (the `/silent /waitpid` update path only; a plain silent install does not launch); a failed install does not launch | M | 0.3 | 🟢 | Z2b, Z7c | Test-mode Setup records exactly one launch after an update and none after a failed one | HM | | 0 | 0 | |
| Z2b | The reopened app says once "FamilyQuant was updated to 2.1.7." (a marker Setup writes, read and cleared on start) | L | 0.2 | Z2a | Z7c | Shown once after a test-mode update, not on the next start | HM | | 0 | 0 | |
| Z2c | Setup compares versions. Installed newer than this Setup: "FamilyQuant 2.1.7 is already installed. This setup holds 2.1.4.", main button Open FamilyQuant, a quiet Install this older version behind a confirmation. Same version: "already installed" with Open FamilyQuant and Repair. The title never reads "Upgrade" to an older version. Silent mode refuses a downgrade with an exit code and a log line | M | 0.4 | 🟢 | Z7c | Test mode: older over newer shows the page and installs nothing on Open; silent downgrade exits non-zero; equal and newer cases render dark and light | HM | | 0 | 0 | |
| Z2d | Setup has its own icon (the app mark with a download badge, from `FamilyQuant/Assets/Brand`) and its window says Setup, so it cannot be taken for the app on a desktop | L | 0.2 | 🟢 | Z7c | Setup.exe icon differs from FamilyQuant2.exe; no yellow or gold | HM | | 0 | 0 | |

### W3 Close project, 2.1.7 (FQHome)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z3a | File > Close project closes the open project with the usual Save, Discard, Cancel prompt and returns to Home; disabled with no project open; the sidebar steps go back to "Open a project first" | L | 0.3 | 🟢 | Z7c | Close with unsaved changes asks; Cancel keeps the project; after close another project opens without restarting | HM | | 0 | 0 | |

### W4 Home and one name per thing, 2.1.8 (FQOpus)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z4a | The names table (in `docs/` and the code): Home, 1 Build families, 2 Curate families (a Group by evidence, b Group by words), 3 Quantify, 4 Results, 5 De novo review; a suite check fails when an old name (Start page, New from database, Compute full XIC, Analytics / report, De novo evidence) appears in UI text | M | 0.3 | Z3a | Z4b to Z4h | The check goes red on a planted old name | OP | | 0 | 0 | |
| Z4b | Home replaces Start everywhere (sidebar button, View > Home); five numbered boxes in Ana's order, each with one line, its state (ready, needs a project, done) and one button; De novo review marked optional; Open project and Recent projects stay below | M | 0.5 | Z4a | Z8c | Rendered dark and light at both sizes; each box opens its screen | OP | | 0 | 0 | |
| Z4c | Build families replaces New from database (button, menu, Help, window title); it opens the same database clustering (Group by sequence identity, USearch) | L | 0.2 | Z4a | Z8c | No "New from database" left in UI text | OP | | 0 | 0 | |
| Z4d | Curate families replaces Families and Open project as the step name; with no project open it asks for a project or offers Build families; inside, the 2.1.5 guided steps are part a, Group by evidence, and the description and keyword grouping is part b, Group by words | M | 0.4 | Z4a | Z8c | Both parts reachable and labelled; the 2.1.5 step order and highlight unchanged | OP | | 0 | 0 | |
| Z4e | Quantify is the one name of the Compute full XIC screen (sidebar, menu, Help, window title, messages) | L | 0.2 | Z4a | Z8c | No "Compute full XIC" left in UI text | OP | | 0 | 0 | |
| Z4f | Results and De novo review are the one names of their screens (sidebar, menu, Help, window titles) | L | 0.2 | Z4a | Z8c | No "De novo evidence" or bare "De novo" step name left in UI text | OP | | 0 | 0 | |
| Z4g | The Analysis menu becomes Tools and keeps only the extra tools (Spectrum comparison, Clustering diagnostic, Reannotate SEPro); the five steps live on Home and the sidebar | L | 0.2 | Z4e, Z4f | Z8c | Menu order File, Edit, View, Tools, Help; every former Analysis command still reachable | OP | | 0 | 0 | |
| Z4h | Help follows the five steps and the new names (`HelpContent`, `HelpWorkflowFacts`), with the flow drawn as on Home | L | 0.2 | Z4b to Z4g | Z8c | Help names match the names table check | OP | | 0 | 0 | |

### W5 Families screen layout, 2.1.8 (FQHome)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z5a | The two grids' headers sit at the same height; a title above each grid: "Families" on the left, "Proteins in this family" on the right, with the selected family's name | L | 0.2 | Z1e | Z8c | Rendered dark and light at both sizes; headers aligned to the pixel | HM | | 0 | 0 | |
| Z5b | All proteins moves to the right, above the protein grid | L | 0.1 | Z5a | Z8c | Rendered; the button keeps its behaviour | HM | | 0 | 0 | |
| Z5c | Family numbers centred in the ID column; the USearch ID column shown by default in the protein grid | L | 0.1 | Z5a | Z8c | Rendered; a stored column choice of the user still wins | HM | | 0 | 0 | |

### W6 De novo: measure, decide, explain (FQFetch, Paulo)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z6a | Measure same-scan conflicts on the real-data set (`C:/Users/paulo/Desktop/FamilyQuant`, the three-run PEAKS export at ALC 80, D33) through the program's Spectrum comparison: scans where PatternLab and the de novo call give different sequences for the same precursor while the de novo peptide is still credited in Quantify; count, credited de novo signal involved, and its share of all de novo and of all signal, per run. No product change | M | 0.5 | Z1e | Z6b | Counts reproduced by a second run of the same command; method reviewed by FQKimi on code only | FE | | 0 | 0 | |
| Z6b | Paulo decides what Quantify does with a same-scan conflict, from Z6a's numbers (D65): a) the PatternLab identification wins the scan, b) keep both and state it, c) another rule | L | 0 | Z6a | Z6c, Z6d | Decision logged | Paulo | | 0 | 0 | |
| Z6c | D65 a): on a scan PatternLab identified, a de novo call with a different sequence for the same precursor gets no credit in Quantify; an option, ON by default, stored with the project and shown in the report's rules; before and after family shares on real data; ships in 2.1.8 if green, else the next release | H | 1 | Z6b | Z6d | Synthetic known answers; before and after shares on real data; code review APPROVED; science review APPROVED by FQOpus | FE | | 0 | 0 | |
| Z6d | Help page "De novo in FamilyQuant": Ana's seven steps mapped to what the program does (what Quantify reads, de novo peptides dropped when PatternLab found the same peptide, the same-scan rule as decided, XIC of de novo peptides, homology by PepExplorer's method at 80% identity with I = L and the decoy gate) and why De novo review is optional | L | 0.3 | Z6b | Z8c | Every claim cites the code; no organism, venom or sample words | FE | | 0 | 0 | |

### W7 Release 2.1.7 (FQHome, Orchestrator)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z7a | Version 2.1.7 in every place 2.1.6 was set, and the 2.1.7 notes (claim only what landed) | L | 0.1 | Z3a | Z7c | Versions read 2.1.7.0 | HM | | 0 | 0 | |
| Z7b | README reads 2.1.7 with a short what's new | L | 0.1 | Z7a | Z7d | Synthetic only; reviewed | HM | | 0 | 0 | |
| Z7c | The full functional suite on the integrated 2.1.7 commit (receipts deferred, D61), after every W1, W2 and W3 step is approved | M | 0.2 | Z1a to Z1e, Z2a to Z2d, Z3a, Z7a | Z7d | Every check green | Orchestrator | | 0 | 0 | |
| Z7d | The 2.1.7 Setup.exe and zip with SHA256 from the integrated commit, Setup test mode, package check; then v2.1.7 published as Latest and the README pushed | L | 0.2 | Z7b, Z7c | Z7e | GitHub digests equal the package | HM, Orchestrator | | 0 | 0 | |
| Z7e | Update check on the public files: 2.1.7 reports current; an installed 2.1.6 test root shows the banner, Install update runs the public Setup in test mode and the app is reopened (Z2a) | L | 0.2 | Z7d | | PASS | HM | | 0 | 0 | |

### W8 Release 2.1.8 (FQFetch, FQHome, Orchestrator)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z8a | Version 2.1.8 and the 2.1.8 notes | L | 0.1 | Z7e | Z8c | Versions read 2.1.8.0 | FE | | 0 | 0 | |
| Z8b | README for 2.1.8: the new Home screenshot from synthetic data, the new names | L | 0.2 | Z4b, Z8a | Z8d | Synthetic only; reviewed | FE | | 0 | 0 | |
| Z8c | The full functional suite on the integrated 2.1.8 commit (D61 mode), after every W4 and W5 step and Z6d are approved | M | 0.2 | Z4a to Z4h, Z5a to Z5c, Z6d, Z8a | Z8d | Every check green | Orchestrator | | 0 | 0 | |
| Z8d | The 2.1.8 Setup.exe and zip, package check, published as Latest, README pushed | L | 0.2 | Z8b, Z8c | Z8e | GitHub digests equal the package | HM, Orchestrator | | 0 | 0 | |
| Z8e | Update check on the public files: an installed 2.1.7 updates to 2.1.8 and reopens | L | 0.2 | Z8d | | PASS | FE | HM | 0 | |

### W9 The FamilyQuant website (FQSite)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| Z9a | Deep study before any page: the science documents, Help, the docs index, the key code paths (database clustering, evidence grouping, family world, full XIC quantitation, de novo and homology, the report) and every screen rendered on synthetic data; written up in `UNDERSTANDING.md` in the site folder, every claim with file:line | M | 1 | 🟢 | Z9c, Z9d, Z9e | A reviewer checks the write-up against the code: APPROVED | ST | | 0 | 0 | |
| Z9b | Art direction, design system and the hero, fun, friendly and animated, never a research or review site (Paulo, 14:13): brand palette, type, motion, light and dark; the download button reads the latest release from GitHub (falls back to the releases page) | M | 1 | 🟢 | Z9c to Z9f | Rendered at desktop and phone width in both themes; the download link resolves | ST | | 0 | 0 | |
| Z9c | How it works: the five steps (Build families, Curate families, Quantify, Results, De novo review), instructional, with what goes in, what comes out and what to check, illustrated and with app screenshots on synthetic data | M | 1 | Z9a, Z9b | Z9g | Every statement matches UNDERSTANDING.md | ST | | 0 | 0 | |
| Z9d | The ideas, light and intuitive (the science stays for the paper, Paulo 14:14): why quantify by family, why read spectra two ways (database match and de novo), how de novo peptides find their family; interactive, animated explainers | H | 1.5 | Z9a, Z9b | Z9g | Every claim cites the program's behaviour | ST | | 0 | 0 | |
| Z9d2 | How it works inside, shown and animated, light on jargon (Paulo, 14:10 and 14:14): sequences aligned and grouped into families, words grouping them, evidence from UniProt (Pfam domains and families) sorting them, peptides landing on families and their signal adding up | H | 1.5 | Z9a, Z9b | Z9g | Every statement matches UNDERSTANDING.md | ST | | 0 | 0 | |
| Z9i | What makes FamilyQuant different, as a fun visual side-by-side ("other tools see this, FamilyQuant sees this"): protein groups, spectral counting, de novo alone, homology alone versus what FamilyQuant combines; a line or two of sources at most; no claim anyone could knock down | M | 0.5 | Z9a | Z9g | Every comparison cites a source | ST | | 0 | 0 | |
| Z9j | A separate rigorous science page (Paulo, 14:16): every idea on the main page has a small "the science behind this" button that opens its own section of science.html, written precisely (alignment and identity, evidence and Pfam, the family world, full isotope envelopes and normalisation, PSM plus de novo, homology at 80% identity with I = L, decoys and FDR, the PatternLab-wins rule, what the program does not do), every claim from UNDERSTANDING.md, a few references | H | 1.5 | Z9a, Z9b | Z9g | Every claim cites the program; each button lands on its section | ST | | 0 | 0 | |
| Z9e | An illustrative example, a fictitious snake venom, labelled fictitious and illustrative on every view: families, an interactive dot plot in the style of report.html, composition, the de novo contribution, the database gap | M | 1 | Z9a, Z9b | Z9g | No real sample or collaborator number anywhere | ST | | 0 | 0 | |
| Z9f | Install and update guide, FAQ, glossary, how to cite, credits; footer with the Fiocruz logo and "All rights reserved, 2026, Fundação Oswaldo Cruz (Fiocruz)" | L | 0.5 | Z9b | Z9g | Rendered; links resolve | ST | | 0 | 0 | |
| Z9g | Review: claims against the code (FQKimi), accessibility, phone width, both themes, speed, no real data, no em or en dashes | M | 0.5 | Z9c to Z9f, Z9d2, Z9i, Z9j | Z9h | APPROVED | KM | | 0 | 0 | |
| Z9h | Paulo looks at it and decides where it is deployed (for example next to SCPBrowser on patternlabforproteomics.org) | L | 0 | Z9g | | Paulo's word | Paulo | | 0 | 0 | |

## Critical path

2.1.7: W1 (FQFetch) and W2 with W3 (FQHome) run in parallel now; Kimi reviews in batches as steps land; Z7a and Z7b; one suite Z7c; package and publish Z7d; update check Z7e. Rough wall-clock: 3 to 4.5 hours from 12:30, so about 15:30 to 17:00, a range, never a promise.

2.1.8: W4 (FQHome after Z7e's package is built) and W5 with Z6a (FQFetch after W1); Z6d waits for Paulo's D65; then Z8a to Z8e. Rough wall-clock: 3 to 5 hours after 2.1.7, so this evening. Z6c ships in 2.1.8 only if it is green in time; otherwise in the next release.

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

## Risks and triggers

| Risk | Trigger | Response |
|---|---|---|
| A partial panel is used as if complete | A family is missing after Z1d | The panel is stored as incomplete with the missing families named; every step that reads it says so and offers Retry panel |
| Resume mixes two UniProt releases | The release changes between two fetches | Kept batches are keyed by release and discarded when it changes (Z1c); the evidence and panel release guard stays |
| A rename loses a function or breaks a ledger row | The full suite fails on a ledger or text check | The owner maps the old name to the new one in the ledger's Added contracts section; nothing is dropped or edited to pass |
| Setup reopens the app wrongly | Two windows, or a launch after a failed install | Launch only on the update path, once, as the user (Z2a test) |
| The reviewer is busy elsewhere | Another project's Kimi worker is running when a batch is ready | The batch waits in the queue; if a 2.1.7 batch waits more than 30 minutes the desk tells Paulo |
| Ana's data leaves this PC | Any review or brief | Kimi sees code and synthetic data only; Z6a reports counts and shares, never her rows |
