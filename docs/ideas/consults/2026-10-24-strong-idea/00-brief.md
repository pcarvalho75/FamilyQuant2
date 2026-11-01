# Council brief: one very strong idea for FamilyQuant

Date: 2026-10-24. Convener: Claude (Opus 5.5). Members this time: **Fable 5.1 only** (the owner named Fable).

## The owner's ask, verbatim

> i think you could conult fable on some very strong idea we should do to make family quant significantly better

## What we want from you

One idea that would make FamilyQuant **significantly** better for its real users (venom proteomics, Ana's lab at Fiocruz), not a list of polish items. "Significantly better" means at least one of:

- a result the users cannot get today and would put in a paper or a talk;
- a large cut in the hand work a user does on every dataset (Ana curates hundreds to thousands of proteins by hand);
- a class of silent wrong results made impossible.

Brainstorm widely first, then converge on ONE recommendation and plan it so a worker could start.

## Hard constraints

- Read `CLAUDE.md` first. Its eight invariants are absolute (runs matched by file name, ion current over distinct peaks, both sides of the three-way bar M+0, remainder never clamped, curation locks survive reclustering, decode never guess, no MessageBox on the de novo screen, InvariantCulture for parsing and CSV).
- `SCIENTIFIC_METHOD.md` defines the estimands and the language the program may use (for example the detector residual must not be called "unidentified peptide"). An idea that needs a new scientific claim must say how that claim is calibrated or labelled.
- Windows-only WPF on .NET 10, delivered by ClickOnce to non-programmer biologists. The vendored DLLs (`PatternTools.dll`, `SeproPckg2.dll`, `MAligner.dll`) are opaque: no source here.
- Tests are the console runner `FamilyQuant.Tests` (fixture sections run anywhere; real-data sections skip without data).
- Every number you quote must be regenerable from the repository, the data files named in `05-context.md`, or be marked as your estimate.

## What exists today (version 1.5.0.1, deployed 2026-10-24)

Two-stage identity clustering (USEARCH fast pass plus exhaustive all-pairs single linkage), word reclustering (TF-IDF), keyword super cluster with AND/OR and exclusions, search-scoped reclustering, curation locks, 50-step undo, batch User ID / annotation edit of selected rows (new today), target/decoy mixing score with the mixed families named (new today), identity scan and Divide and Conquer, PLP-based family quantitation with four denominators and three shared-peptide policies, a full-envelope XIC engine re-extracting identified and de novo precursors from RAW files, PEAKS / DeepNovo source-aware de novo evidence with reconciliation against SEPro PSMs, homology mapping of de novo peptides to families (a PepExplorer port), the `.fqproj` project, the Analytics screen and a reproducible 1920x1080 evidence pack, and SEPro reannotation with the curated family numbers.

## Material

- Repository (read-only worktree at the current master): `C:/tmp/familyquant-idea-plan`
- `CLAUDE.md`, `SCIENTIFIC_METHOD.md`, `RELEASE_NOTES.md`, `SCIENTIFIC_REVISION_REPORT.md`, `SCIENTIFIC_TALK_SCRIPT.md` in the repository root
- `docs/ideas/consults/2026-10-24-strong-idea/05-context.md`: the users, their feedback history, today's measurements
- Real data (read-only; do NOT run anything): `C:/Users/paulo/Desktop/FamilyQuant` (Bothrops jararaca target-decoy database, `PLVI.sepr2`, three RAW files, PEAKS and DeepNovo exports, the `BrProt_Talk` evidence pack with its CSVs) and `C:/Users/paulo/Desktop/AnaG` (Ana's own copy of the inputs)

## Protocol

One member, one round (a second round only if the convener's review leaves open points). You write the paper; the convener then does the red-flag review (`90-review.md`), checking every claim against the code and the data, and cuts what does not hold.

## The spirit (verbatim)

**Members: strongly collaborative, lightly adversarial. The convener: the strongest adversary.** Members are on the same side and build on each other ("what a nice idea, and we could improve it further by considering this", "and I can also fix this thing you left behind"); their main job is to make the plan bigger, better and safer, and a clear error, an overbooked saving or an invariant risk is noted briefly, with evidence and the fix. The one who calls the council (the orchestrator, you) is the strongest adversarial reviewer: after the rounds, you check every booking against the data and every invariant argument against the code (`90-review.md`), and cut what does not hold.

## Paper format

1. **Brainstorm** (6 to 10 candidates, one short paragraph each): the idea, who it helps and how much, the evidence (file:line or data), effort S/M/L.
2. **The recommendation** (one idea), as a plan of packages. Per package: what; where (files, methods, file:line); mechanism; why every invariant still holds; the value, with evidence; difficulty S/M/L; blocked by; what it opens; the proof (fixture tests and the real-data check); worker type; files owned (disjoint across packages).
3. **Why this one** over the runners-up, in a few lines.
4. **Rejected ideas** (one line each) and **traps** (what would make this go wrong).

About 3,000 words. Cite file:line. Every estimate says it is an estimate. No em or en dashes.
