# FamilyQuant 2.0 roadmap (remaining work only)

Version 4, 2026-11-02. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file at the next refresh and go to `docs/ROADMAP-archive.md`.

Version 3 closed 35 steps; they are in `docs/ROADMAP-archive.md`. At Paulo's order the needle went back to zero and every remaining step was cut small, so that no step hides other work: each box is one thing a person can see finished.

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
- Steps stay small. A step that hides several pieces of work is split, so that the needle shows real progress. Paulo, 2026-11-02.

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

Columns: Diff = L, M, H, VH. Hrs = worker hours at measured speed: estimates are short because these steps are small. Blocked by: 🟢 = open now. ETA = hours after the restart in that worker's queue, never a promise. Prog = my estimate of work done. Cred = counted only after approval and merge. EV, LK, AS, SH, RS, OP, FL, GR, KM and AR are the workers above.

### W1 Family world

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W2 Port gaps found by the proofs

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W3 Ledger, screen by screen

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| N6q | Paulo rules on the wording list in one pass | L | 0 | 🟢 | N6r | Paulo's ruling recorded | Paulo | not started | 0% | 0% | T+0 |
| N6r | Reviewer's 200-row random sample across the whole ledger | M | 0.3 | N6q | N6s | GR's verdict; one failure reopens the kind | GR | not started | 0% | 0% | T+0 |
| N6s | Release check passes | L | 0.1 | N6r | R3b | `--feature-ledger-release-check` passes | Orchestrator | not started | 0% | 0% | T+0 |

### W4 Look

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

### W5 Release (Paulo publishes)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| R3b | Final proof refresh and full suite on the frozen code | L | 0.2 | R4c, N6s | R3c | Every proof current; suite green | Orchestrator | not started | 0% | 0% | T+2 |
| R3c | Release candidate: versions, build and a clean install rehearsal | M | 0.3 | R3b | R5a | Rehearsal installs beside 1.5 and opens her file | Orchestrator | not started | 0% | 0% | T+2 |
| R4a | Final adversarial pass: code and behaviour | H | 1 | 🟢 | R4c | GR's verdict | GR | not started | 0% | 0% | T+1 |
| R4c | Every finding fixed and gated | M | 0.5 | R4a | R3b | No open finding | Orchestrator | not started | 0% | 0% | T+2 |
| R5a | FamilyQuant 2.0 final published | L | 0 | R3c | R5b | Paulo publishes | Paulo | not started | 0% | 0% | T+2 |
| R5b | Archive this roadmap | L | 0.1 | R5a | Done | Archive committed | Orchestrator | not started | 0% | 0% | T+2 |

### W6 Paper numbers (no release depends on these)

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|

## Cooldown

H1 ran at the middle of the project and is finished: 56 steps landed, housekeeping merged, and `docs/housekeeping/H1-NOTES.md` carries what it left. No further cooldown is planned.

## Critical path

Everything meets at R3a, the code freeze. The family world runs B10a, B10b, then the screen B10c, B10d, B10e. The ledger runs side by side, screen by screen, N6a to N6o, then Paulo's one ruling N6q, the sample N6r and the release check N6s. The look runs C14a to C14e. After the freeze the chain is R3b, R3c, R4a and R4b, R4c, R5a.

## Paulo's decisions

| ID | Decision | Default if no answer | Needed by |
|---|---|---|---|

### Decisions taken

Paulo agreed these at the restart, 2026-11-01.

| ID | Question | Taken |
|---|---|---|
| D14 | Should identity clustering change its coverage rule, merge on shared peptides, or use a reviewed-family veto? | No. Clustering stays as it is, and the measurements ship as a diagnostic the user can see |
| D28 | Order of the end game | Freeze when Y9 lands, since the ledger work after it touches no product code. The two final passes then run at once beside the ledger work, and the final refresh and release candidate come after their fixes, so they run only once. Desk, 11-02, to finish today |
| D27 | Rows that hold no fact of their own, number formats, and superseded rows | Rule 4: they close with the reason recorded, and none is a DROP. Fragments and formats with the same precision today close as a group. A superseded row closes only if it concerns wording or layout; one that removes something the user could do goes to Paulo one by one. The Recalculate colour cue, the Compute XIC sentence, the keyboard focus style, four main-window formats and two unreachable warnings are accepted. Paulo, 11-02 |
| D26 | Failure guidance that got shorter in the port | Restored in Y9 before the freeze. Independent fact checks found 46 rows a user would miss. Desk, 11-02 |
| D25 | Wording rules | Rule 1: the old fact in new wording is accepted. Rule 2: a row that only describes the old look closes under the new theme. Rule 3: the current screen name replaces Layer N and a clause about a retired screen goes. Paulo, 11-02 |
| D24 | Is the claim sentence roadmap work? | No. It is Paulo's own message to Ana, so its boxes left the roadmap. Paulo, 11-02 |
| D23 | Finish today | Yes. C14 runs beside the ledger, preview 2 folds into the release candidate, and rows that only changed wording get one ruling from Paulo at the end. Paulo, 11-02 |
| D22 | Default minimum tag length | 8, and the user can choose 7 to 15. Replaces the 9 in D11. Paulo, 11-02 |
| D21 | Is the program general? | Yes. It must work with any database, so nothing the user sees names an organism, a venom or a sample's numbers. Paulo, 11-02 |
| D20 | Minimum tag length | The user can change it and see the error by length on their own project; 9 stays the default. Paulo, 11-02. Step B10 |
| D19 | A joined header with different taxa | The target keeps all its taxa and the file is not refused. Paulo, 11-02. Step B11 |
| D18 | Does the family world ship on one sample? | Yes. Every project shows its own measured rates, and it stays optional. Paulo, 11-02 |
| D17 | Row FQ15-0061551CF9D6 | The clause on Layer 5 goes with the retired partition screen; the assignment fact stays. Paulo, 11-02 |
| D16 | Where the claim sentence goes | To Ana, papers and project records only, never into the program. It also states that the program has been tested only on venom. Paulo, 11-02 |
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
