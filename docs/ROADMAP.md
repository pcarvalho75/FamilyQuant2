# FamilyQuant 2.0 roadmap (remaining work only)

Version 5, 2026-11-02 evening, mission J. Owner: Paulo. Orchestrator: the Claude session "Family Quant codebase review".
Finished rows leave this file at the next refresh and go to `docs/ROADMAP-archive.md`.

Version 4 closed with FamilyQuant 2.0.0 published (49 of 49 steps, in `docs/ROADMAP-archive.md`). Version 5 is mission J: Ana's complete analysis of her new data, made and written up by the program. The needle starts at zero; each box is one thing a person can see finished.

## The goal, in Paulo's words

- "lets call this familyquant 2.0", "make it a completely new git repo", "ideally, i think a new project, so we dont lose reference"
- "most of the code will be copy paste, but knowing ana, i know she will sometimes want to resort to the old version"
- "this is also a chance to do a complete re-write and tweak code as well without missing any function and improving on things"
- "using python scripts was cheating, every result passed to ana needs to be through the software"
- "things need to look slick and clean", "the colors look terrible, we have white with yellow in some parts"
- "the file menu usually comes first"
- "i want something revolutionary to come out of this, where this family idea realy shines and somethign very witty and automatic is done"
- Mission J, 11-02: "the ultimate test for this progrm ... make a very cool and complete analysis ... generate a complet word document. if you need a feature that this program does not do you implement it in the program", "she wants a qauntitative description of the families", "and do some nice plots and these plots should be able to be done by the software", "and if you want to test the program, make tests in parallel", "how about 2 gpt terra workers for more simpe things", "by the end of this project i would like a report in the directory where anas files are. there should be a simple way to input the families", "maybe the program could make this very complete as a docx exit and generate this so many plots with explanations automaticlay ... maybe with checkboxes for sections of the report so one may check or uncheck the parts to be generated"

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
11. **A family panel is user data.** Ana's families, domains, subclasses and decisions are read from her file and stored in the project. Code holds the general rule, never her list (mission J).
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
| FQOpus (OP) | Opus 5.5 xhigh | Runs the analysis through the program: the baseline, the full run, the interpretation and the final number trace | `D:/FamilyQuant-wt/opus` | `fq/opus` |
| FQTerra1 (T1), FQTerra2 (T2) | GPT-6.1 Terra xhigh | Simpler steps, added by Paulo 11-02 | `C:/FamilyQuant-wt2/terra1`, `terra2` | `fq/terra1`, `fq/terra2` |
| FQFloater (FL) | Sol 6.1 xhigh | Floater: quantitation pipeline, project formats, then whatever waits longest | `C:/FamilyQuant-wt2/floater` | `fq/floater` |
| FQReview (GR) | Grok 4.7 xhigh | Main reviewer, keeps every B, A and N step, and owns B2, A2, B8 and R4. Never edits product code. | `D:/FamilyQuant-wt/review` | detached |
| FQKimi (KM) | Kimi | Second reviewer of everything else, in batches, one instance, no subagents, at most 15 calls in 5 hours and at most 5 reviews an hour (Paulo, 11-02). Never edits product code. | `D:/FamilyQuant-wt/kimi` | detached |
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

Mission J (Paulo, 2026-11-02 19:40): "use it to make a very cool and complete analysis ... generate a complete word document. if you need a feature that this program does not do you implement it in the program". Ana wants a quantitative description of the families. The shared brief is `D:/FamilyQuant-wt/desk/briefs/J-mission.md`.

Columns: Diff = L, M, H, VH. Hrs = worker hours at measured speed. Blocked by: 🟢 = open now. ETA = hours after the start in that worker's queue, never a promise. Prog = my estimate of work done. Cred = counted only after approval and merge. EV, LK, AS, SH, RS, OP, FL, GR and KM are the workers above.

### W1 Ana's family panel, as user data

Her consensus spreadsheet becomes the project's family panel. Family names, domains and decisions live in her file and the project, never in code.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J1d | File > Import family panel: the panel table, the decision mapping and the Pfam check on one screen, with Help in general words | M | 1.5 | 🟢 | J7a | Native captures in both themes; a choice changes the stored panel and Undo restores it | T1 | | 0 | 0 | |
| J1e | Simple family input: on the same screen, a table where a person adds, edits and removes families (name, decision from a list, Pfam codes, subclasses) with Undo, plus Save panel as spreadsheet, a template that imports back unchanged | M | 1.5 | 🟢 | J7a | Native test: type a family, save, reopen, export the spreadsheet and import it back with identical panel | T1 | | 0 | 0 | |

### W2 Families by evidence

The program, not a script, puts every database protein in a panel family.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J2a | Assign every target protein to a panel family by evidence: the UniProt family of a toxin-annotated protein, else the nearest reviewed toxin, and where two panel families share a domain the panel's own Pfam lists decide by the accompanying domains; shown as proposals, applied as one undoable locked change | H | 3 | 🟢 | J2b, J2c, J3a | The rule in Help in general words; fixtures; the guard test finds no family, name or Pfam literal | EV | | 0 | 0 | |
| J2b | Measure that rule: leave-one-out on the reviewed entries, and every difference from the 11-01 draft listed with its reason, both written by the program | M | 1 | J2a | J7a | Program CSV with the right rate; each draft difference has a reason | EV | | 0 | 0 | |
| J2c | Per-protein audit export: accession, family, subclass, evidence used and why, for Ana to check in Excel | L | 0.5 | J2a | J6b | CSV opens in Excel under pt-BR and en-US with the same columns | T1 | | 0 | 0 | |

### W3 Subclasses

SVMP P-I, P-II, P-III and "class not resolved"; PLA2 D49 and K49; the parts of a precursor.

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J3a | Subclass of each protein: by domain architecture (the user enters required and absent Pfam per subclass) or by the UniProt subfamily text, carried to unreviewed proteins by the nearest reviewed; definitions stored in the project | M | 2 | J2a | J3b | Fixtures; Ana's SVMP and PLA2 subclasses entered once and replayed from the recipe | T2 | | 0 | 0 | |

### W4 Where the peptides fall

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J4c | Coverage map figure: a protein drawn as a bar with its domains and the identified and de novo peptides on it, for the top proteins of each family | M | 2 | 🟢 | J6b | Native PNG in both themes and the Light export | LK | | 0 | 0 | |

### W5 The numbers Ana asked for

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J5e | Sensitivity table: family shares under the three shared-peptide policies and two de novo cutoffs | M | 1.5 | 🟢 | J6b | Program table; stored results reused, none invented | AS | | 0 | 0 | |
| J5g | Family and subclass ring chart: families on the inner ring, their subclasses and "class not resolved" on the outer ring, small families grouped as others | M | 1.5 | 🟢 | J6b | Native PNG in both themes; rings sum to 100% | LK | | 0 | 0 | |

### W6 The Word report, written by the program

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J6j | Supplement workbook: the program writes `<report>_supplement.xlsx` beside the Word document, one sheet per long table (per protein, per peptide, per run, others list, audit), each sheet named where the document cites it, following the section checkboxes | M | 1.5 | 🟢 | J6b | Workbook opens in Excel; sheets equal the program's CSV exports cell for cell; same bytes under pt-BR and en-US | SH | | 0 | 0 | |
| J6k | The new figures in the report: composition with run error bars, the family and subclass ring, abundance against diversity, the signal sources, and coverage maps of the top proteins of each family, each with its automatic caption, as a figures builder FQResults' J6b calls | M | 1.5 | 🟢 | J6b | Each figure in the document is byte-equal to the program's own PNG export and its caption's numbers equal the figure's data | LK | | 0 | 0 | |
| J6b | Results in the report: composition with toxin counts, per-run table, subclass and domain tables, figures, Database gap, quality rates, sensitivity and the others list | H | 3 | J2c, J4c, J5e, J5g, J6g | J6e | Every number in the document equals the program's own export; the document reads in about 30 to 40 pages and every long table goes to a companion `_supplement.xlsx` the program writes beside it | RS | | 0 | 0 | |
| J6f | Report sections with checkboxes: the Export report screen lists every section and figure, each with a checkbox, all ticked by default; the choice is stored in the project and the document holds exactly the ticked parts | M | 1 | 🟢 | J6e | Native test: unticking a section removes it and only it; the choice survives reopen | SH | | 0 | 0 | |
| J6g | Automatic explanations: every figure and table gets a caption saying what is drawn and how to read it, plus sentences written from that project's own numbers, in general words | M | 2 | 🟢 | J6b | Each caption's numbers equal the figure's data; no fixed organism text | LK | | 0 | 0 | |

### W7 The analysis

| ID | Step | Diff | Hrs | Blocked by | Opens | Done-check | Worker | Status | Prog | Cred | ETA |
|---|---|---:|---:|---|---|---|---|---|---:|---:|---:|
| J0 | Baseline run of today's program on the new data, with timings and a gap list against Ana's rules | M | 1.5 | 🟢 | J7a | RESULT J0 posted with every output path | OP | | 0 | 0 | |
| J7a | The full run through the program: panel import, evidence, families, subclasses, Compute on three runs with de novo, family world, Database gap; project saved | H | 2 | J0, J1d, J2b | J7b | The project reopens; a second Compute repeats every number | OP | | 0 | 0 | |
| J7b | Interpretation, written into the project notes, quoting only the report's numbers, read against Ana's panel | M | 1.5 | J7a | J7c | A second reader checks every number against the report | OP | | 0 | 0 | |
| J7c | Final export and delivery: every number in the document traced to a program export, then the Word report, the project and the family panel written to `C:/Users/paulo/Desktop/AnaG/Bjararaca/Relatorio`, nothing else in Ana's folders touched | M | 1 | J7b | J7d | Reviewer's trace table with 0 mismatches | OP | | 0 | 0 | |
| J7d | Release 2.1.0, so Ana can make the same report, on Paulo's word | L | 0.5 | J7c | | The release names the version the report prints | EV | | 0 | 0 | |
| J8 | Parallel suite: the console suite split into independent shards run as separate processes at once, each with its own temp folder, with one summary that adds them up; gates and quick suites use it | M | 1.5 | 🟢 | J7a | The sharded run reports exactly the serial run's passed, failed and skipped counts on the same commit, in a fraction of the time | OP | | 0 | 0 | |
| J9 | Compute writes its cache and spectrum CSV to a per-user cache folder (or one the user chooses), never beside the RAW files, so a read-only or shared data folder works; old caches still found | M | 1 | 🟢 | J7a | Native Compute on a read-only RAW folder succeeds; the same numbers as before; the RAW folder unchanged | FL | | 0 | 0 | |
| J10 | The de novo source picker reads which exports the chosen folder holds and proposes the matching source, so a folder of .denovo.csv files proposes "de novo all" | L | 0.5 | 🟢 | J7a | Fixture folders for each source; the person can still change it | FL | | 0 | 0 | |
| J11 | Family-world decoy rates never print above 100%: when a family's credited envelope is too small to give a rate, the row says so, with the counts beside it | L | 0.5 | 🟢 | J6b | The snaclec-like case (decoy envelope over a near-zero credit) prints "too little credited signal" with its counts | FL | | 0 | 0 | |
| J12 | Protein names in the new report and its supplement stop at the header's organism field (" OS="), as the screens do; the frozen 1.5-compatible exports keep their bytes | L | 0.5 | 🟢 | J6b | One shared name helper used by the report, supplement and audit CSV; golden stays 4/0/0 | AS | | 0 | 0 | |

## Critical path

J1a, J1b, J1d and J2a, J2b open the full run J7a. Beside them run the quantitation steps J3b, J5a, J5b and J4a, J4b, and the report J6a. The charts start at once on the quantitation lane's published result types, with stand-in data until those land. Everything meets at J6b and J7a, then J7b, J6e, J7c.

## Paulo's decisions

| ID | Decision | Default if no answer | Needed by |
|---|---|---|---|

### Decisions taken

Paulo agreed these at the restart, 2026-11-01.

| ID | Question | Taken |
|---|---|---|
| D38 | The 13 ambiguous family links in J2a | Confirmed by the desk from Ana's written rules (tab 1 merges, tab 4 rules 2, 3 and 5): disintegrin family and M12B Disintegrin-only to DIS, the DC architecture stays SVMP, the non-specific endonuclease family to NUCL, snaclec and true venom lectin to CTL, M12B to SVMP; source-less single-domain contexts stay outside the link table. Stored as user choices in the project, attributed to her consensus. Desk, 11-02 23:02 |
| D33 | Which de novo data | Ana's three-run export (`AnaG/DeNovo_ALC-50percent`, PEAKS de novo all), cut by the program at ALC 80. Paulo, 11-02 |
| D35 | Which families | All 41 of Ana's consensus families with her decisions: Excluir is not a family, Fundir em CTL joins CTL, the rest are measured, and those under 0.1% are listed as others with the names in a supplement. Paulo, 11-02 |
| D36 | Report language | English, with Ana's own family names from her file. Paulo, 11-02 |
| D37 | What "% of the venom" means | The share of the signal credited to families (identified plus de novo matched to the database), summing to 100%; beside it the share of the whole measured signal, and the de novo found only through Database gap shown apart, never added. Paulo, 11-02 |
| D34 | Who writes the Word document | The program writes the complete report (File > Export report) from the project, with the input it needs: the family panel from Ana's file and a Report details form in the project (title, authors, sample, workflow, kind of database, written interpretation). Paulo, 11-02: "maybe the software could export a complete report too, its just that it would need some input" |
| D14 | Should identity clustering change its coverage rule, merge on shared peptides, or use a reviewed-family veto? | No. Clustering stays as it is, and the measurements ship as a diagnostic the user can see |
| D32 | Publishing | FamilyQuant 2.0.0 published 2026-11-02 19:16 at https://github.com/pcarvalho75/FamilyQuant2-releases/releases/tag/v2.0.0 from commit bc7209f, zip SHA256 02df797c...056c8f, by the desk at Paulo's request. The desk may publish when Paulo asks. Paulo, 11-02 |
| D31 | How 2.0 reaches users | From a new public repo, pcarvalho75/FamilyQuant2-releases, as a release zip, with Help > Check for updates (Y10). This repo stays private, since its history holds unpublished work. No install on this machine; R3c runs the program from the unpacked zip, so a clean machine stays untested until the first real install. ClickOnce at the old address is not the route. Paulo, 11-02 |
| D30 | The last six rows | Accepted: two refusals that can no longer appear, the old cannot-interrupt line, two sample-specific examples, and for 2.0 the status of an old .plp with no per-run totals, which no longer gives the reason. That last one goes to a later version. The PatternLab generator equivalence stays out, since current code cannot confirm it. Paulo, 11-02 |
| D29 | Rows that remove something a user could do, and sentences that mention a file | Accepted as lost from 2.0, all still in 1.5: running extraction or partition without a project, the three-way partition bar and its CSV, the RAW preview before computing. Also accepted: the wait during family attribution, and the contaminant rule of D15. Sentences tagged as file rows only because they mention a file are wording rows (Rule 5). Five dropped messages are restored in R4c, among them that the project is not saved yet. Paulo, 11-02 |
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
| Ana's rule needs a family literal | A step can only meet a rule by naming a family, a domain or a keyword in code | The rule becomes a user choice stored in the project, entered once from her file |
| A subclass cannot be told apart | Peptides shared by every subclass, as in the SVMP catalytic domain | They are reported as "class not resolved", never forced into a class |
| The full run is too slow for tonight | J0 measures Compute on three RAW files with de novo above two hours | Cache reuse first; report the time to Paulo the same hour |
