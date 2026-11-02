# FamilyQuant 2.0 roadmap (remaining work only)

Version 3, 2026-11-01. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file at the next refresh and go to `docs/ROADMAP-archive.md`.

Version 2 closed 56 steps and the H1 cooldown: the evidence engine, the new shell, the rebuilt Build, Quantify, Results and De novo screens, the Core split and housekeeping. Master `7a54be1` passed a clean clone at 2,525 passed, 0 failed, 3 skipped. This version starts the count again from zero. Paulo restarted the team on 2026-11-01.

## The goal, in Paulo's words

- "lets call this familyquant 2.0", "make it a completely new git repo", "ideally, i think a new project, so we dont lose reference"
- "most of the code will be copy paste, but knowing ana, i know she will sometimes want to resort to the old version"
- "this is also a chance to do a complete re-write and tweak code as well without missing any function and improving on things"
- "using python scripts was cheating, every result passed to ana needs to be through the software"
- "things need to look slick and clean", "the colors look terrible, we have white with yellow in some parts"
- "the file menu usually comes first"
- "i want something revolutionary to come out of this, where this family idea realy shines and somethign very witty and automatic is done"

## Rules every worker follows

1. **Only the program makes results.** Python may be used to investigate or to check a number, never to make one that reaches a person. Scratch probes count as scripts: nothing built from a probe is delivered.
2. **No exception lists.** No table of families, no protein names, no Pfam, PANTHER or keyword literal in the rule. Evidence and data decide; what a user chooses is stored in the project. For anything automatic, in this order: public databases alone, then the program suggests how to proceed, then an LLM, only if it does what data could not (Paulo, 2026-11-01). The family world (W8) needs no LLM.
3. **No function is lost.** The feature ledger (N3) lists everything 1.5.0.6 does. It is the contract: a release cannot pass with a row open, and only Paulo can drop a row. Rows close by kind: one executed check may close every row of a kind when each row names what was compared, and a reviewer samples the result (D13). 1.5 stays installed as the safety net.
4. **Numbers stay equal to 1.5.0.6 unless a step says otherwise.** Any module may be rewritten. The golden check (F2) must stay byte-identical against the frozen reference. A step that means to change a number says so in its row, shows before and after on real data, and passes a science review.
5. **Copy what is good, rewrite what is better.** Every file is read as it moves. Simpler, faster or clearer code replaces it when the tests pin the behaviour first. Rewriting for its own sake is not a goal.
6. **1.5 stays available.** 2.0 is a separate project that installs beside 1.5 and never touches it. 2.0 opens every old file, and it can export a `.fcf` that 1.5 opens, so Ana can go back at any moment. Old repository: `pcarvalho75/FamilyQuant`, frozen as the 1.x reference. The 2.0 repository `pcarvalho75/FamilyQuant2` is private, like the old one, and stays private: it carries third-party binaries, collaborator names and working notes.
7. **Deployable at every merge.** One step per commit, with its test. Paulo publishes every release. The desk pushes master to the private repository after a merge, as a backup (D9).
8. **A step counts as done only after the reviewer approves it** and it is merged and green.
9. **The deck stays untouched:** `BrProt_FamilyQuant_FourPart_Source_Decomposition.pptx` and its sources, until Paulo says otherwise.
10. **No em dashes or en dashes** in anything written, UI text included.

## The team

This team is exclusive to FamilyQuant: six Sol workers, one Opus worker, Grok and Kimi as reviewers, and Gemini back when its quota returns, sharing no workers, folders or tools with Project Orion.

| Name | Engine | Role | Worktree | Branch |
|---|---|---|---|---|
| Orchestrator | Claude, main session | Assigns, merges, runs gates, keeps this file true. Writes no product code. | | master |
| FQEvidence (EV) | Sol 6.1 xhigh | Ana's file, the program reading the task from the file, family-world fetch | `D:/FamilyQuant-wt/evidence` | `fq/evidence` |
| FQLook (LK) | Sol 6.1 xhigh | Look, curation and family screens | `D:/FamilyQuant-wt/look` | `fq/look` |
| FQShell (SH) | Sol 6.1 xhigh | Busy overlay, shell messages, closing the feature ledger | `D:/FamilyQuant-wt/shell` | `fq/shell` |
| FQAssay (AS) | Sol 6.1 xhigh | Dependencies, clustering, de novo tidy, improvements, documents | `D:/FamilyQuant-wt/assay` | `fq/assay` |
| FQResults (RS) | Sol 6.1 xhigh | Charts, quantitation panel, figures, family-world results | `C:/FamilyQuant-wt2/results` | `fq/results` |
| FQOpus (OP) | Opus 5.5 xhigh | The gate experiment and the second attribution pass | `D:/FamilyQuant-wt/opus` | `fq/opus` |
| FQFloater (FL) | Sol 6.1 xhigh | Floater: quantitation pipeline, project formats, then whatever waits longest | `C:/FamilyQuant-wt2/floater` | `fq/floater` |
| FQReview (GR) | Grok 4.7 xhigh | Main reviewer, keeps every B, A and N step, and owns B2, A2, B8 and R4. Never edits product code. | `D:/FamilyQuant-wt/review` | detached |
| FQKimi (KM) | Kimi | Second reviewer of everything else, in batches of at most 8, one instance, no subagents, at most 15 calls in 5 hours. Never edits product code. | `D:/FamilyQuant-wt/kimi` | detached |
| FQArgon (AR) | Gemini 4 Argon high | Reviews steps in turn with FQReview. Out of quota until about 2026-11-07. | `D:/FamilyQuant-wt/argon` | detached |
| FQFable | Fable 5.1 xhigh | Architect, only on Paulo's request, in short sessions. Not in the queues. | `C:/FamilyQuant-wt2/fable` | `fable/vision` |

Flow of one step: owner posts `READY <step> <commit>`; the orchestrator assigns it to a reviewer in `D:/FamilyQuant-data/reviews/CLAIMS.md`; that reviewer posts `APPROVED <step> <commit> <reason>` or `REJECTED <step> <commit> <findings>`; a rejection is the owner's next commit, red then green, and another reviewer checks it; the orchestrator merges, builds, runs the suite, and only then the row closes.

## Target layout

| Project | Holds | Rule |
|---|---|---|
| `FamilyQuant.Core` | Everything without WPF: families, clustering, grouping, evidence, quantitation, de novo, homology, project files | No WPF, no message boxes, reports progress and accepts cancel |
| `FamilyQuant.App` | WPF screens, each a thin view over a tested view model; assembly `FamilyQuant2` | No science in the UI |
| `FamilyQuant.Reports` | Paper and talk figure code no screen calls, a separate tool reading a `.fqproj`. Only if X9 finds enough to move | Never in the installed app |
| `FamilyQuant.Tests` | The suite, split by area | Runs headless |

## Design rules for the new look (hard requirements)

1. **No yellow or gold anywhere,** the focus ring included. One accent colour, used for the primary action and selection only.
2. **One surface family.** Dark panels never hold white boxes or a white table, and light panels never hold dark ones. Every colour comes from a token.
3. **Colour means status or nothing.** Green, amber and red appear only as small status marks, never on plain values.
4. **No outlines around groups.** Grouping comes from spacing and one quiet surface step.
5. **One primary button per panel,** the others quiet.
6. **No emoji.** Icons come from Segoe Fluent Icons, a Windows font, so no new dependency.
7. **One line of explanation at most on a screen.** The rest goes to Help (F1) and tooltips.
8. **Tables:** soft or no grid lines, readable header, right-aligned numbers, decoys hidden by default, accession visible.
9. **Standard Windows order:** File first, then Edit, View, Analysis, Help, with the usual shortcuts.
10. **Base:** the Windows 11 Fluent theme built into .NET 10 (`ThemeMode`), no third-party theme.

## Workstreams

Columns: Diff = L, M, H, VH. Hrs = worker hours at measured speed: the 56 steps of version 2 were estimated at 155 hours and took at most about 41 worker hours (6.8 hours on the clock, at most six builders), so every estimate here is divided by 4. Blocked by: 🟢 = open now. ETA = hours after the restart in that worker's queue, never a promise. Prog = my estimate of work done. Cred = counted only after approval and merge. EV, LK, AS, SH, RS, OP, FL, GR, KM and AR are the workers above.

### W0 Foundation and the new project

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| N6 | Close the ledger by kind of row (D13): one executed check per kind, such as frozen 1.5.0.6 text compared with 2.0, with each row naming what was compared. A row no kind covers gets its own proof or stays open | H | 1.5 | C2, C13 | C14 | `--feature-ledger-release-check` passes; a reviewer samples 200 rows at random across all kinds and agrees; any sample failure reopens that whole kind | SH | not started | 0% | 0% | T+6 |

### W2 Ana's result, through the software

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| A1 | Make her file with 2.0 preview 1 on her original `.fcf`: Fetch Evidence, fetch the panel again so it keeps tissue text, review, Create, Save, Export. Provenance shows version, recipe and release | M | 0.5 | 🟢 | A2 | Recipe reapplied gives the same file; file check passed; no script touched it | EV | not started | 0% | 0% | T+0 |
| A2 | Adversarial science review of the file: concepts, denominators, labels, red flags, comparison with what Ana already holds | H | 1 | A1 | Ana's file ready | GR's written verdict; every finding closed | GR | not started | 0% | 0% | T+2 |
| A6 | The coverage gate counts the query letters actually aligned (D12). The evidence window, the audit CSV and Help state the rule in plain words, and no value reads over 100%. Ana's file is made again with it | M | 0.5 | 🟢 | A2 | Same families, tiers and default selection on her file; the neighbours that change are listed; recipe replay exact; the rule is visible on the screen and in the export | EV | not started | 0% | 0% | T+2 |
| A5 | Measure the shared-peptide effect on her data, through Compute full XIC. It needs a `.sepr2` on this database: if none exists when you reach it, post that and leave the step open | M | 0.5 | A2 | Paper numbers | Numbers exported by the program | EV | not started | 0% | 0% | her pace |

### W3 Look

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| V3 | Zero hard-coded colours: a suite check fails on any colour outside tokens and the chart palette. Each rebuilt screen must pass it; this row closes when the last one does | H | 0.5 | C1, C5 | C14 | Guard red on a planted colour; 0 sites left; golden equal | LK | not started | 0% | 0% | T+4 |

### W4 Shell

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| S9 | Evidence fetch stages the whole fetch on a copy and attaches it at the end. Today the first fetch is written before the panel, so a Cancel at the panel keeps half of it | M | 0.5 | A1 | R2 | Cancel at any point leaves the file as it was; red test first | EV | not started | 0% | 0% | T+1 |

### W5 Screens: each rebuilt as a thin view over a tested view model

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| C1 | Curation rebuilt: table always in view, actions in a side panel, no Controls and Data Browser tabs, no "Layer" words. USearch ID column hidden by default, data kept. Decoys hidden by default; Locus shows the accession. Long operations use the S6 host. `ClusterGenerationControl` (1,978 lines) replaced by a view model and a thin view | VH | 2 | 🟢 | C2, C5, V3, C13 | Pressing any action shows its result without changing view; every ledger row of the screen checked; golden equal | LK | not started | 0% | 0% | T+2 |
| C2 | Family view: a table of families (name, proteins, shares, lock) with the proteins of the selected family beside it | H | 1.5 | C1 | N6, R2 | Selection, edit, lock and undo work on both tables; 39,996 families smooth | LK | not started | 0% | 0% | T+4 |
| C5 | Quantitation panel: Layers 2 and 3 merged; Full XIC first, PatternLab as a secondary source, options shown only when they apply | M | 1 | C1 | V3, C13, R2 | Every option kept; golden equal | RS | not started | 0% | 0% | T+3 |
| C13 | Wording pass: labels, tooltips (one or two sentences) and Help match the new screens | M | 1 | C1, C5 | N6, C14 | Suite's tooltip checks green; no old screen named anywhere | LK | not started | 0% | 0% | T+5 |
| C14 | Finish: 100, 125 and 150% scaling, smallest window, Tab order, focus visible, both themes | M | 0.5 | C13, V3, N6 | R2 | Harness renders at each scale; Paulo's final look sign-off | LK | not started | 0% | 0% | T+7 |

### W6 Core rewrite: copy what is good, rewrite what is better

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| X6 | De novo engine tidy: readers, reconciler and table builders, with the logic left in `DeNovoControl` moved into Core | H | 1 | 🟢 | R3 | Golden equal; real-data de novo sections green | FL | not started | 0% | 0% | T+1 |
| X7 | Project and file formats: `ProjectStore` (1,083) split, schema versioned, and an `.fcf` export the frozen 1.5.0.6 opens | H | 1 | 🟢 | R2 | A file written by 2.0 opens in the reference build with the same families; old `.fqproj` rules kept | FL | not started | 0% | 0% | T+2 |
| X12 | Improvements found on the way, listed in `docs/IMPROVEMENTS.md` with before and after. Known candidates: the fast clustering pass has no coverage gate (20.1% of its joins, 2,329 of 11,584, fail the second pass's gate); the decoy mixing score cannot see over-merging, so use identified peptides to test the grouping (a peptide identical in two identity families is evidence they are one); the evidence family as a veto on single-linkage merges; one contaminant rule (D5). Anything that changes a number goes to Paulo first | H | 1 | 🟢 | X13, R3 | Each change measured on real data; science review; golden snapshot updated only by Paulo's word | AS | not started | 0% | 0% | T+1 |
| X13 | Documents: `CLAUDE.md` from 50 KB to a working guide, history to `docs/HISTORY.md`, talk documents out of the root, release notes for 2.0. `SCIENTIFIC_METHOD.md` gains the rule that an unannotated protein joins only through an in-scope neighbour, which the code has and the document lacks. The three test files over 1,000 lines split with no check lost; `dotnet format` differences cleared | L | 1 | X12 | R3 | No fact lost, no line over 300 characters; suite count unchanged; `dotnet format --verify-no-changes` clean | AS | not started | 0% | 0% | T+2 |

### W7 Releases (Paulo publishes each one)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| R2 | 2.0 preview 2: new look and shell | M | 0.5 | C2, C5, X7, C14 | R3 | Same, plus Paulo's sign-off on renders, and a published update reaches an installed 2.0 | Orchestrator | not started | 0% | 0% | T+8 |
| R3 | 2.0 release candidate: rewrite finished, every ledger row closed, the family world in if B1 passed | M | 0.5 | R2, B8, X6, X12, X13 | R4 | Same, plus the ledger check | Orchestrator | not started | 0% | 0% | T+8 |
| R4 | Final adversarial pass on the whole product | H | 1 | R3 | R5 | GR's verdict; all findings closed | GR | not started | 0% | 0% | T+9 |
| R5 | FamilyQuant 2.0 final, then the archive of this roadmap | L | 0.5 | R4 | Done | Paulo publishes | Orchestrator | not started | 0% | 0% | T+10 |

### W8 Breakthrough: the family world

The bet, from `docs/vision/VISION.md`: a de novo peptide your database cannot place is searched against every reviewed member of the family, from all species, plus the unreviewed entries of the sample's clade, and is credited to the family, never to a protein. The family becomes the unit that reaches past the database. The result says, per family: identified, de novo in your database, de novo only in the family world (the database gap), and still unexplained, and the program ranks the families by that gap and offers the proteins the tags hit as suggested additions to the database. B1 decides it. If B1 is killed, B4 to B8 leave the roadmap and R3 stops waiting for them; B2 and B3 stand either way.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| B4 | Fetch the family world: the reviewed panel plus one clade query per family, unreviewed included, all or nothing, release recorded, stored with the project, offline afterwards | M | 1 | 🟢 | B5 | An interrupted fetch leaves the file as it was; offline rerun identical; size growth measured on Ana's file | EV | not started | 0% | 0% | T+2 |
| B5 | Second attribution pass: de novo peptides left unattributed go to `HomologyMapper.Search` against the family world with reversed decoys. A tag needs at least 9 residues and only families in scope can be credited (D11). A hit credits the UniProt family, never a protein | H | 1.5 | B4 | B6 | Existing exports byte-identical (golden 4/0/0), new columns in a new export only; two runs byte-identical; same numbers as B1's checker | OP | not started | 0% | 0% | T+4 |
| B6 | The error beside every claim: for each family, the decoy rate and the known-answer right rate, stored in the project. A family with too little evidence shows that instead of a number | M | 1 | B5 | B7 | Every family row has both; reproducible; conserved families checked by hand | OP | not started | 0% | 0% | T+5 |
| B7 | Result: the four-part bar per family and the "Database gap" ranking: the families with the most signal missing from your database. An export writes a FASTA of the family-world proteins the tags hit, as suggested additions for the person to add and search again in PatternLab. The program never changes the database itself. The screen, the CSV and Help state the minimum tag length and the in-scope rule beside the result | M | 1 | B6 | B8 | Screen and CSV from one table; every number from the program; the rules in force are visible on the screen and in the export, and stored in the project; both themes | RS | not started | 0% | 0% | T+6 |
| B8 | Science review on a real sample: labels, denominators, inflation of conserved families, and the wording of the claim | H | 1 | B7 | R3 | GR's verdict; findings closed; Paulo accepts the wording | GR | not started | 0% | 0% | T+7 |

## Cooldown

H1 ran at the middle of the project and is finished: 56 steps landed, housekeeping merged, and `docs/housekeeping/H1-NOTES.md` carries what it left. No further cooldown is planned.

## Critical path

Three lines run side by side from the restart. For Ana's file: A1, A2. For the breakthrough: B1, then B4, X5, B5, B6, B7, B8. For the look: V5, C3, C1, then C2, C5, C13, N6, C14, R2. R3 waits for all of them. X5 is the seam: it opens both B5 and X12.

## Paulo's decisions

| ID | Decision | Default if no answer | Needed by |
|---|---|---|---|

### Decisions taken

Paulo agreed these at the restart, 2026-11-01.

| ID | Question | Taken |
|---|---|---|
| D14 | Should identity clustering change its coverage rule, merge on shared peptides, or use a reviewed-family veto? | No. Clustering stays as it is, and the measurements ship as a diagnostic the user can see |
| D15 | Does the contaminant rule look past `Reverse_`? | Yes, one exact unwrap, then the database prefix in exact case. Target classes do not change; golden stays 4/0/0 |
| D13 | How the ledger is proven | By kind of row, one executed check per kind, plus a reviewer's random sample, instead of hand work per row |
| D11 | Minimum tag length for the family world | 9 residues, and only families in scope can be credited. Paulo: the program must show this to the user |
| D12 | How the 70% coverage gate is counted | Query letters actually aligned, so gaps earn no credit. Paulo: the program must show this to the user |
| D7 | `LICENSE.txt` | Changed from the blank MIT template to all rights reserved, 2026, holder Fiocruz |
| D8 | Test 1.5 and 2.0 side by side before Ana gets 2.0? | Not needed. Paulo: it is safe, she already uses 1.5 and will have 2.0 beside it |
| D1 | Is a family-level claim acceptable for a peptide absent from the species database? | Yes, with the decoy rate and right rate printed beside it. |
| D2 | Clade of the family world on Ana's file: Viperidae or all snakes? | The clade the program reads from the headers, Viperidae on her file |
| D3 | Scope proposed by the program or typed by the person? | Proposed, the person confirms |
| D4 | Does the database gap ranking belong in the product or only in the paper? | In the product, named "Database gap", with a FASTA of suggested additions |
| D5 | One contaminant rule: three places differ today (prefix or anywhere, exact case or not) | The database's own label, prefix and exact case, measured before and after |
| D6 | Replace the bundled USEARCH with the upstream CC0 file after a parity check? | Yes |
| D9 | Does the desk keep pushing master to the private repository? | Yes, as a backup |
| D10 | Retire the deprecated `build_scientific_deck.mjs`? | Keep it untouched as history |

## Risks and triggers

| Risk | Trigger | Response |
|---|---|---|
| The family world credits the wrong family | B1 or B6 shows a right rate under the bar, above all on conserved families | Tighten the gate or drop B4 to B8. No claim is made without its error beside it |
| The claim is not new | B2 finds the same method published | Say so plainly, cite it, and keep what is still ours: the automatic flow and the gap ranking |
| The clade fetch is too large | B4 measures a project size or fetch time Ana cannot use | Fetch only the families found in scope. Report the size |
| Two steps collide in `FamilyEnvelopeQuant` | B5 starts before X5 merges | B5 waits. X5 leaves the seam |
| Kimi runs out of quota | A Kimi call returns 403 or a limit | FQOpus takes reviews again until a short test call succeeds. The orchestrator tells Paulo the same hour. A step never goes to its own author |
| A rewrite loses a function | A ledger row has no check, or a user finds a gap | No release passes; the row goes back to the step that dropped it |
| A rewrite moves a number | Golden check red | No merge; the step goes back to its owner, or follows rule 4 if the change is meant |
| Ana needs 1.5 while 2.0 is unfinished | She asks | 1.5 stays installed and untouched, and `.fcf` export (X7) carries her work back |
| UniProt changes or is down | A fetch fails live | Stored evidence keeps working; fetch stays all or nothing |
| Non-UniProt headers | B3 or the evidence fetch cannot read them | Report it plainly in the review window; proteins stay unassigned, never guessed |
