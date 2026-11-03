# Repository and test-command catalog

This is the navigation index for the current checkout. The roadmap owns unfinished work. Recorded check counts in milestone notes describe those runs, not a live suite total.

## Documents

| Document | Purpose |
|---|---|
| [CLAUDE.md](../CLAUDE.md) | Current projects, build/test setup, scientific invariants and implementation guidance |
| [Working history](HISTORY.md) | Complete archived working notes, measurements, publishing details and retained limitations |
| [X13 document/test audit](X13-DOCUMENTS.md) | Fact retention, protected-path rulings, exact method splits and receipt validity |
| [Architecture](ARCHITECTURE.md) | Current boundaries, planned Reports/operation host, budgets and historical destination inventory |
| [Core operations](CORE-OPERATIONS.md) | Progress, cancellation, outcomes and atomic publication |
| [Verified Save memory](STREAMING-SAVE.md) | Streaming JSON and fingerprints, exact-entry byte proof and measured memory |
| [Compute and open progress](COMPUTE-PROGRESS.md) | Counted phases, live clocks, timestamped logs and native parity check |
| [App operations](APP-OPERATIONS.md) | Busy host, overlay and publication adapters |
| [Shell dialogs](SHELL-DIALOGS.md) | Headless messages, browse preferences and protected C1 adoption |
| [Quantitation stages](QUANTITATION-PIPELINE.md) | Full-XIC stage contracts, second-pass residual seam and focused parity checker |
| [Panel subclasses](PANEL-SUBCLASSES.md) | User-defined subclass rules, currentness, reviewed transfer and leave-one-out measurement |
| [De novo engine](DENOVO-ENGINE.md) | Reader/result boundaries, Core selections and review tables, focused real-data parity |
| [Project formats](PROJECT-FORMATS.md) | Versioned schema/codecs, legacy container rules and frozen-reader FCF proof |
| [Clustering refactor](CLUSTERING-REFACTOR.md) | Result contracts and bounded frozen/current comparison |
| [Dependencies](DEPENDENCIES.md) | Package decisions, cache compatibility and real-file opening checks |
| [USEARCH provenance](USEARCH-PROVENANCE.md) | Pinned author artifact, license and full assignment parity |
| [Test commands](TEST-COMMANDS.md) | Feature-owned command tables and adding a flag without editing Main |
| [Identity study](IDENTITY-SEARCH-STUDY.md) | Why Scan and Divide remain separate, exact measured scope |
| [Identity/deployment](N1-IDENTITY.md) | Separate application identity and the installed-profile check still owned by Paulo |
| [Science gate](SCIENCE-GATE.md) | Recorded evidence-rule comparison, verdicts and limitations |
| [Task discovery](EVIDENCE-TASK.md) | Header-derived taxonomy, functional keyword proposal and confirmed recipe contract |
| [Quantitation panel](QUANTITATION-PANEL.md) | Source visibility, retained options and native checks |
| [Study ownership](X9-STUDIES.md) | Small X9 move and retained screen dependencies |
| [Native evidence delivery](EVIDENCE-DELIVERY.md) | Program-driven fetch, default review, explicit split, save, export and replay |
| [A2 coverage audit](A2-COVERAGE.md) | Native counterfactual measurement and the counts-only alignment audit label |
| [Query residue coverage](QUERY-COVERAGE.md) | Recorded query letters, bounded coverage, current cache provenance and historical replay |
| [Family-world storage](FAMILY-WORLD-STORAGE.md) | Atomic reviewed/clade fetch, taxonomy, persisted evidence and offline integrity |
| [Reviewed panel bulk retrieval](REFERENCE-PANEL-STREAM.md) | Streamed family queries, completeness checks, retained paged comparison and native parity timing |
| [H1 findings](housekeeping/H1-NOTES.md) | Behavior decisions, protected work, ledger migration and remaining reuse scope |
| [Vision](vision/VISION.md) | Fable's study: the family world proposal behind workstream W8, with the desk's corrections |
| [Code map](vision/CODE-MAP.md) | How a FASTA and a RAW file become a family number, and what that read did not cover |
| [B1 gate](vision/B1-GATE.md) | The family-world gate's verdict, the length study on two blind halves, and D11 |
| [B6 errors](vision/B6-ERRORS.md) | The decoy and right rate beside every family-world number, and the conserved families read by hand |
| [Feature ledger](FEATURE-LEDGER.md) | Frozen preservation contract, dispositions and acceptance state |
| [Feature evidence](FEATURE-EVIDENCE.md) | Per-observation receipts, native proof runner, audit and review sample |
| [AS ledger receipt](ASSAY-LEDGER.md) | Exact M22 cohort, executed C# declaration comparisons and remaining proof requirements |
| [Measured improvement candidates](IMPROVEMENTS.md) | X12 clustering coverage, peptide ambiguity, evidence veto and contaminant measurements |
| [Ledger scan](feature-ledger-scan.json) | Exact frozen observations and source hashes |
| [Roadmap](ROADMAP.md) | Current work and acceptance gates; orchestrator owned |
| [Roadmap archive](ROADMAP-archive.md) | Historical step records; orchestrator owned |
| [Release notes](../RELEASE_NOTES.md) | Historical release descriptions, not current publication status |
| [Security](../SECURITY.md) | Trusted SEPro-input boundary and existing native-process policy |
| [Scientific method](../SCIENTIFIC_METHOD.md) | Protected scientific reference; not edited by housekeeping |
| [Scientific revision report](history/SCIENTIFIC_REVISION_REPORT.md) | Historical validation account and limitations, moved by X13 |
| [Talk script](../SCIENTIFIC_TALK_SCRIPT.md) | Protected presentation input |
| [Screen harness](../FamilyQuant.Tests/SCREENS.md) | Capture matrix, isolation and what screenshots do not prove |
| [Appearance](../FamilyQuant/Themes/README.md) | Theme/token behavior and historical migration context |
| [Screen rules](../FamilyQuant/Themes/STYLE-GUIDE.md) | Shared typography, tables and action roles |
| [Brand assets](../FamilyQuant/Assets/Brand/README.md) | Geometry, generated assets and regeneration command |
| [License](../LICENSE.txt) | All rights reserved, Fiocruz |

Worker mission instructions and consult history are intentionally outside this maintained index.

## Build and ordinary checks

Run from the worktree root in PowerShell. Tests are a console program, not a `dotnet test` suite. This setup follows the gate's per-lane SSD TEMP convention:

```powershell
$env:DOTNET_PROCESSOR_COUNT = '4'
$lane = Split-Path (Get-Location) -Leaf
$env:TEMP = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('Temp/fq2/' + $lane + '-quick')
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet build FamilyQuant2.sln -c Release -v q
$testExe = Join-Path (Get-Location) 'FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe'
& $testExe
```

On the team machine, the equivalent launcher is `powershell -NoProfile -ExecutionPolicy Bypass -File
D:/FamilyQuant-wt/desk/quick-suite.ps1`. It sets the same processor limit and isolated TEMP, runs at normal priority and
reports the exit status. Its `-Full` mode belongs to the gate and supplies the real-data arguments.

The ordinary data slots are `[plp] [database] [deNovoRoot] [acquisitionRoot]`. Use `.` for an absent slot; do not pass empty strings from PowerShell. No-data sections use fixtures; data-dependent sections report skips. A quick green result is not a fresh RAW or installed-product validation.

The syntax tables below use `<name>` for a required supplied path/value and `[name]` for an optional argument. Replace
those markers before execution. Paths containing spaces must be quoted. Use explicit output paths outside the repository
and outside read-only inputs; many native checkers have shared-machine defaults. The tables are source-audited syntax,
not a claim that every expensive or data-specific command was run during housekeeping.

## Fixture and contract commands

Invoke each flag with `& $testExe <flag>` after building. Dispatch and arity are defined by [Program.cs](../FamilyQuant.Tests/Program.cs).

| Flag | Coverage |
|---|---|
| `--test-core-boundary` | Core/App ownership, references and resource contract |
| `--test-dependencies` | Cache compatibility, package removal and shared numeric control contracts |
| `--test-usearch-binary [published-Core-assembly]` | Pinned USEARCH bytes in the checkout, running Core and optional publish payload |
| `--test-dispatch` | Flag inventory, routing, precedence, argument forwarding and merge guards |
| `--list-test-commands` | Deterministic command table; executes no test or scientific handler |
| `--test-document` | Document round trip, scoring and denominator attribution fixtures |
| `--test-evidence-task` | File-derived taxonomy, keyword counts, confirmation and recipe replay fixtures |
| `--source-caption-check <fresh-output>` | Compiled source picker: all five frozen filename/Union facts, selected-source screenshots and native whole-control checks in both themes |
| `--shared-peptide-effect <copied-project> <copied-T-R> <copied-sepr2> <copied-RAW-folder> <new-output> <cache> <dataset-label> <date> [--prepare-only]` | One-run native Compute full XIC and all three shared-peptide policies; exact database/curation check, scoped CSV/JSON outputs and no de novo input |
| `--test-evidence-delivery` | Native default review, save, exports, exact app-session replay and app Open fixture |
| `--test-evidence-fetch-atomic` | Combined evidence/panel staging, cancellation boundaries, failures and one final commit |
| `--test-reference-panel-stream` | Bulk query batching, exact paged-route entry parity, release/count failures, Cancel and panel operation checks |
| `--reference-panel-parity-live <input.fcf> <new-output>` | Full-source live evidence fetch and both reviewed-panel routes, timings, per-entry hashes and offline project reopening |
| `--test-evidence-coverage` | Native alignment-row interpretation, counterfactual chooser and honest audit labels |
| `--test-query-residue-coverage` | Actual query-letter gate, native facts, legacy serialization and evidence-session contracts |
| `--test-family-world-fetch` | Staged fetch, paging, release/taxonomy validation, offline persistence and recipe compatibility |
| `--test-desktop-world` | Actual curation fetch button, scope confirmation, shared busy host, Cancel/failure byte preservation, stored state and offline project reopen |
| `--test-task-order <fresh-output> [<copied-legacy-project> <copied-plp>]` | Native project open, PLP load, User ID edit, Undo, batch annotation and Results; legacy task migration, unchanged world/result provenance and section-local refusal |
| `--test-feedback-facts <output>` | Y9 normal curation/load/export/alignment failure and success routes, stale-share warnings, visible refusal/setup text and deterministic 39-row factcheck TSV on a private desktop |
| `--test-feedback-facts <output>` | Y9 normal curation/load/export/alignment routes and 39-row factcheck TSV; R4c Quantify completion, Build running guidance and redundancy tooltip with a separate five-row TSV, on a private desktop |
| `--desktop-world-walk <copied-project> <copied-raw-folder> <copied-sepr2> <copied-denovo-root> <output> <confirmed-scope>` | Private desktop real-data fetch/scope dialog, production Compute, Database gap and native CSV/FASTA exports; uses only copies |
| `--test-quantitation-stages` | Prepared-peptide assignments, cross-side suppression, residual calibration, rollup and deterministic CSV |
| `--test-family-panel` | Panel and subclass fixtures, safe publication, native runner and report sections |
| `--subclass-leave-one-out <project.fqproj> <new-output-directory>` | Current-assignment reviewed-subfamily measurement, per-target CSV and summary JSON; input project unchanged |
| `--test-denovo-engine` | Core retained selection/review-table contracts and existing reader/reconciliation/source-aware fixtures |
| `--test-project-formats` | Schema contract and existing versioned project round-trip/refusal fixtures |
| `--test-inputs` | Real-data argument-selection contract, without choosing Desktop fallbacks |
| `--test-operations` | Core outcomes, progress, cancellation and the owned native-child fixture |
| `--test-busy` | App operation host, running-command cancellation, prior file bytes and guarded publication |
| `--test-shell-dialogs [--after-busy]` | Browse callers, folder preference, accept/cancel, owned windows and headless messages; optional busy-group setup first |
| `--test-clustering-diagnostic-shell [fresh-output-dir]` | Analysis action, progress/Cancel guards, actual native reports on read-only saved FCF/FQPROJ fixtures and repeat packet parity |
| `--test-clustering-results` | Typed clustering/redundancy results and native synthetic fixtures |
| `--test-identity-study` | Probe parser and grid-versus-probe interpretation |
| `--test-results` | Results view-model readiness and export choices |
| `--test-family-world` | The family world's crediting rule, species removal, the B1 verdict bars, D11 and the B5 pass with its export, on a hand-made world |
| `--test-ui-icons` | No emoji in product source, escaped/entity regressions and Fluent glyphs in both themes |
| `--test-ui-colors` | Literal/fixed-factory color guard with Tokens/ChartPalette sources and the explicit protected export-only PresentationFigures exception |
| `--test-keyword-command` | Single keyword command, empty review, Cancel and accepted-setting retention |
| `--test-review-wait` | Dispatcher completion beyond the former load timeout, original fault/cancellation and synchronization context retention |
| `--test-ui-wording` | Current curation/shell Help paths, family overview and lock instructions, retired-screen names, tooltip length and retained report/Help facts |
| `--test-look-copy-preservation` | Scoped published-Help comparison negative controls, exact case and numeric boundaries |
| `--prove-look-style-roles <frozen-source> <role-map> <new-output> <date-timestamp>` | Twenty-four explicit De novo role mappings, old/current values, native resolved properties and actual consumers in both themes |
| `--probe-look-home-styles <new-output> [--negative-control]` | Native Home action/state checks, resolved style values and off-screen captures in both themes; optional planted lost-enablement regression; no focus or ledger closure by screenshot |
| `--look-finish-focus <new-output> <date-timestamp>` | Native focus-state/template paint checks for four input types, both themes, three DPIs, without taking focus; creates a synthetic keyboard-check project |
| `--look-finish-gallery <existing-native-capture-dir>` | Native review sheets assembled from unchanged C14 source PNGs; originals stay intact |
| `--prove-look-app-tokens <frozen-source> <new-output> <date-timestamp>` | Explicit frozen/current application palette roles and named native Home consumers in both themes; unsupported roles stay OPEN |
| `--prove-look-copy <frozen-source> <native-audit> <new-output> <date-timestamp> [LK-prefix]` | Exact OPEN help/tooltip cohort, native published Help and named-tooltip comparisons, 200-row LK register batches |
| `--refresh-look-registers <frozen-source> <new-output> <date-timestamp>` | Regenerates all five existing LK copy/style registers from their fixed ordered IDs, with no audit or hand preparation; leaves unsupported rows unproved |
| `--test-look-refresh-cohort` | Fixed-cohort selection and refusal of duplicate, unknown, empty or differently identified inputs |
| `--test-chart-themes [output-dir]` | Seven hosted chart types, live token refresh, contrast, subscription lifetime and frozen Light palette pins; labelled synthetic chart captures |
| `--test-evidence-task-scope [output-dir]` | Supplied task facts, editable scope, cancellation, native confirmation and both-theme synthetic captures |
| `--test-report-study-boundary` | Report-only operation ownership, retained Core models and screen dependencies |
| `--report-study-snapshot <new-output-dir>` | Six native synthetic text/PNG fixtures for source-move parity |
| `--test-architecture-group-split [output-dir]` | Native per-key split choices, retained bulk choice, unsplit members staying together, exact recipe replay and optional synthetic captures |
| `--test-curation-view` | Persistent table, accession/full identifier, hidden defaults and native annotation edit/Undo |
| `--test-curation-layout` | Retained chrome, tooltips and ancestor-clipping regression |
| `--test-evidence-coverage-ui [output-dir]` | Recorded query-residue coverage, missing legacy facts, bounded display and native rule line |
| `--test-database-gap [output-dir]` | Stored Core family parts/global buckets, sparse evidence, exact CSV/FASTA, offline reopen and native Results export routes |
| `--test-results-tooltips [fresh-output date-timestamp]` | Two-sentence shared/normalization/chart tips retain every fact and dynamic default; optional semantic preservation receipts |
| `--database-gap-screens <project.fqproj> <fresh-output> [--negative-fingerprint]` | Capture/export the stored table; a stored-world project must enable FASTA, and refusal fails the command |
| `--test-database-gap-refusal <project.fqproj> <fresh-output>` | Native child plants the old fingerprint and must fail the availability gate without writing FASTA or changing input bytes |
| `--test-database-gap-lengths <fresh-output>` | Whole native length chooser, result/CSV/FASTA consistency, repeated bytes, project roundtrip and shell Save/Cancel |
| `--database-gap-lengths-screens <project.fqproj> <fresh-output> [--require-default]` | Real stored length table in both themes, native CSV/FASTA choices and byte repeats; input unchanged. Default proof requires an unselected Core default8 and stored table8, exact FASTA sequences and a native values snapshot |
| `--prove-results-interactions <fresh-output> <date-timestamp>` | Native Results controls, displaced handlers, deterministic exports and observation receipts |
| `--prove-results-members <frozen-root> <fresh-output> <date-timestamp>` | Shared scoped member comparison for assigned Results and quantitation declarations; changed members stay open |
| `--prove-results-runtime <frozen-root> <own-frozen-host> <fresh-output> <date-timestamp> [--negative-control]` | Native frozen/current summary, CSV, User-header and chart-byte comparisons with repeated outputs and per-row receipts |
| `--prove-results-export-facts <frozen-root> <fresh-output> <date-timestamp>` | Native execution receipts for 57 empty/export facts, cancelled choosers, CSV/PNG IO failures and complete/incomplete evidence packs |
| `--results-runtime-snapshot <fresh-json-path>` | Shared native frozen/current synthetic presentation values; not scientific result generation |
| `--test-quantitation-panel [output-dir]` | Full XIC first, conditional source options, retained selectors and both-theme native captures |
| `--test-curation-quantitation` | Full-XIC column, denominator changes, shared policy and unavailable-TIC fallback |
| `--test-curation-operations` | Shared host queue/cancel paths, staged PatternLab publication, stale membership, browse preferences and headless refusal |
| `--test-family-view` | Native family selection, name editing, accessible locks, Undo, production-share projection and 39,996-family virtualization |
| `--test-architecture-group-split [output-dir]` | Native per-key split choices, retained bulk choice, unsplit members staying together, exact recipe replay and optional synthetic captures |
| `--test-report-study-boundary` | Report-only operation ownership, retained Core models and screen dependencies |
| `--report-study-snapshot <new-output-dir>` | Six native synthetic text/PNG fixtures for source-move parity |
| `--shell-checks` | Shell navigation, saving, preferences, Build/Quantify integration and secondary views |
| `--test-shell-workflow` | Current workflow Help routes and retained quantitation facts through the shell status checks |
| `--test-shell-evidence-cancel` | Shared Cancel reaches active protein/panel HTTP requests; staged publication, failure and late Cancel |
| `--feature-ledger-check` | Inventory, row coverage, dispositions and guard regressions |
| `--test-clustering-improvements` | X12 paired-letter, component-veto, native parse and contaminant diagnostic guards |
| `--clustering-improvements <document> <FASTA> <output> <identity> [--small-exhaustive]` | Real-data diagnostic candidates; no product numerical change; bounded all-pairs only |
| `--clustering-improvements-replay <document> <FASTA> <packet> <output> <identity> [--small-exhaustive]` | Recompute a report from recorded native alignments after exact input/executable fingerprint checks |
| `--feature-ledger-release-check` | Same inventory with all retained rows required VERIFIED; OPEN rows intentionally fail |
| `--test-ledger-evidence` | Adversarial receipt, source/artifact hash, assertion and render checks |
| `--test-ledger-declarations` | Exact named-XAML and C# member comparison checks |
| `--test-shell-metadata` | Compiled shell-metadata and isolated Home-event comparison guards; included in the ordinary ledger guard |
| `--test-shell-message-frames [--negative-control]` | Exact frozen handler/message/dialog frames, contributing literals and complete file-filter groups; optional planted wrong-path binding |
| `--test-shell-sepro-fixture <fresh-output.sepr2>` | Owned synthetic package round trip through the actual vendor serializer; no collaborator data or spectra |
| `--test-shell-proof-windows` | Before-Show geometry plus actual off-screen HWND, nonactivation and no-taskbar proof |
| `--test-grid-copy` | Existing native GridCopy context-menu and row/column Copy routines with fake clipboard/browser seams |

## Screens and UI metadata

These commands create native WPF client captures or labelled synthetic fixtures, not scientific deliverables. The broad harness accepts read-only real inputs; focused producers state their fixture scope in their logs.

| Syntax after `$testExe` | Source |
|---|---|
| `--screens <output-dir>` | [ScreenHarness.cs](../FamilyQuant.Tests/ScreenHarness.cs) |
| `--curation-screens <new-output-dir> <input.fcf>` | [Program.CurationScreens.cs](../FamilyQuant.Tests/Program.CurationScreens.cs); four native client captures with input hash |
| `--family-view-screens <new-output-dir> <input.fcf>` | Same producer with a family selected; selection and visible member count recorded |
| `--busy-screens <output-dir>` | [Program.BusyOperations.cs](../FamilyQuant.Tests/Program.BusyOperations.cs); six synthetic overlay states in both themes |
| `--test-busy-logo <fresh-output>` | [Y7 shared wait screen](Y7-BUSY-SCREEN.md); native storyboard start/stop/Cancel, reduced motion, three DPIs, secondary-window routes, bounded cost probe and captured animation preview |
| `--shell-menu-screens <output-dir>` | [Program.StandardMenu.cs](../FamilyQuant.Tests/Program.StandardMenu.cs) |
| `--start-page-screens <output-dir>` | [Program.HomeStart.cs](../FamilyQuant.Tests/Program.HomeStart.cs) |
| `--shell-save-screens <output-dir>` | [Program.ShellSaving.cs](../FamilyQuant.Tests/Program.ShellSaving.cs) |
| `--shell-window-screens <output-dir>` | [Program.ShellWindowPreferences.cs](../FamilyQuant.Tests/Program.ShellWindowPreferences.cs) |
| `--build-family-screens <output-dir>` | [Program.BuildFamiliesScreen.cs](../FamilyQuant.Tests/Program.BuildFamiliesScreen.cs) |
| `--quantify-screens <output-dir>` | [Program.QuantifyViewModel.cs](../FamilyQuant.Tests/Program.QuantifyViewModel.cs) |
| `--quantify-shell-screens <output-dir>` | [Program.QuantifyShell.cs](../FamilyQuant.Tests/Program.QuantifyShell.cs) |
| `--results-screens <output-dir> [project.fqproj]` | [Program.ResultsViewModel.cs](../FamilyQuant.Tests/Program.ResultsViewModel.cs) |
| `--denovo-screens <output-dir>` | [Program.DeNovoScreens.cs](../FamilyQuant.Tests/Program.DeNovoScreens.cs) |
| `--homology-results-screens <output-dir>` | [Program.HomologyResultScreens.cs](../FamilyQuant.Tests/Program.HomologyResultScreens.cs) |
| `--secondary-screens <output-dir>` | [Program.SecondaryWindows.cs](../FamilyQuant.Tests/Program.SecondaryWindows.cs) |
| `--grouping-review-screens <output-dir> [evidence.fcf]` | [GroupingReviewScreens.cs](../FamilyQuant.Tests/GroupingReviewScreens.cs); merged keyword/evidence/architecture review captures |
| `--logo-assets <output-dir> [--preview=<preview.png>]` | [LogoAssets.cs](../FamilyQuant.Tests/LogoAssets.cs); target scratch before replacing tracked assets |
| `--shell-save-smoke <input.fcf> <output.fqproj>` | [Program.ShellSaving.cs](../FamilyQuant.Tests/Program.ShellSaving.cs); verified curation-only persistence, no RAW reproduction |
| `--evidence-recipe-replay-check <recipe-bearing.fcf>` | [Program.ProteinDomains.cs](../FamilyQuant.Tests/Program.ProteinDomains.cs) |

Companion options for `--screens` are `--screen-fcf <input.fcf>`, `--screen-project <input.fqproj>`, `--screen-system` and `--screen-shell-status`. They are not independent commands. See the [capture guide](../FamilyQuant.Tests/SCREENS.md) for the matrix and provenance.

## Frozen parity and generated inventory

| Syntax after the selected executable | Scope/source |
|---|---|
| `--golden <stored.fqproj> <frozen-snapshot-dir> <output-dir>` | Four production export byte comparisons; [GoldenNumbers.cs](../FamilyQuant.Tests/GoldenNumbers.cs) |
| `--golden-verify <stored.fqproj> <frozen-snapshot-dir> <existing-output-dir>` | Verify source and compare existing exports without generating a new pack |
| `--golden-record <stored.fqproj> <empty-snapshot-dir> <output-dir>` | Snapshot-owner maintenance only, through [ReferenceGoldenHost](../FamilyQuant.Tests/ReferenceGoldenHost/ReferenceGoldenHost.csproj) with frozen assemblies; current App is rejected and existing snapshots are never replaced |
| `--feature-ledger-generate <frozen-source-root> <scratch-root>` | [FeatureLedger.cs](../FamilyQuant.Tests/FeatureLedger.cs); writes `scratch-root/docs`, never scan moving product source |
| `--feature-ledger-prove <fresh-output> <date-timestamp>` | Native observation-specific storage/source proofs and register, no ledger edit |
| `--feature-ledger-evidence-import <register> <output> [--replace-existing]` | Validate receipts, preserve an explicitly replaced register and apply evidenced states |
| `--feature-ledger-evidence-import-batch <output> <register1> [<register2> ...] [--replace-existing]` | Import in order with the same guards, atomic refusal and one final report |
| `--test-ledger-import-batch` | Check ordered replacement and atomic batch refusal with synthetic validator inputs |
| `--ledger-apply-rulings <fresh-output> <date>` | Apply the five pinned Paulo rules (wording rules, the rule-4 group ruling and rule-5 message rows) to listed TSV rows; compiled App/Core/Help text witnesses, visual Style/Setter anchors, adversarial guards and remaining review list; [scope](LEDGER-RULINGS.md) |
| `--feature-ledger-evidence <output>` | Read-only counts by kind, source mappings and reproducible 50-row sample |
| `--feature-ledger-kind-review <kind> REOPENED/ACCEPTED <reviewer> <reason> <date> <output>` | Independent D13 kind-review decision and whole-kind reopening |
| `--ledger-declaration-compare <frozen-root> <current-root> <fresh-output> <step> <source-prefix> <date>` | Frozen/current explicit XAML property comparisons and kind receipts |
| `--ledger-assay-compare <frozen-root> <owner-map> <output> <step> <date>` | Shared C# member comparisons for the exact M22 OPEN cohorts; all nonmatches retained |
| `--ledger-assay-cohort <frozen-root> <selection.json> <output> <step> <date>` | Scoped shared declaration comparisons; [receipt scope](ASSAY-LEDGER.md) |
| `--ledger-assay-cache <frozen-root> <output> <date> [--negative-control]` | Frozen/current native cache wire and staging checks; frozen host build required |
| `--assay-cache-snapshot <output>` | Shared synthetic native cache fixture; fresh output required |
| `--test-assay-spectrum-properties`, `--test-assay-build-workflow`, `--test-assay-figure-gaps` | Frozen property, handler and native producer receipt guards |
| `--assay-view-snapshot spectrum/build <output>`, `--assay-build-output-snapshot <output>`, `--assay-figure-gap-snapshot <output>` | Native view, clustering/redundancy and synthetic figure fixtures; fresh output |
| `--ledger-assay-spectrum-properties <frozen-root> <output> <date> [--negative-control]`, `--ledger-assay-build-workflow`, `--ledger-assay-figure-gaps` (same arguments) | Native columns, [Build workflow](N6H-USEARCH.md) and [producer gaps](N6J-PRODUCERS.md) against frozen reference |
| `--ledger-core-relocation <frozen-root> <fresh-output> X5/X7 <date> [member-mappings.json]` | Scoped complete Core member comparisons, exact frozen hashes, retained source/checker snapshots and per-row gaps; [guide](CORE-RELOCATION-LEDGER.md) |
| `--ledger-shell-compare <frozen-root> <fresh-output> <date>` | SH and C1 exact declarations/member tokens; separate candidate registers, unproven rows kept OPEN |
| `--ledger-storage-runtime <frozen-root> <fresh-output> <date> [--negative-control]` | Public project reader/writer schema, codec and refusal observations; [runtime guide](CORE-RUNTIME-LEDGER.md) |
| `--ledger-xic-runtime <frozen-root> <fresh-output> <date> <stored-project> <validated-cache> [--negative-control]` | Native XIC refusal/source/completion facts plus bounded validated cache replay; [runtime guide](CORE-RUNTIME-LEDGER.md) |
| `--ledger-xic-messages-runtime <frozen-root> <fresh-output> <date> <stored-project> [--negative-control]` | J15 detector refusal/cache/live progress, native CSV outcomes and empty-run filename receipts; [guide](XIC-MESSAGES-LEDGER.md) |
| `--ledger-secondary-compare <frozen-root> <native-master-audit> <fresh-output> <date> [--negative-control]` | Shared exact secondary declarations from native OPEN master IDs; [guide](SECONDARY-LEDGER.md) |
| `--ledger-secondary-runtime <frozen-root> <native-master-audit> <frozen-app> <fresh-output> <date> [--negative-control]` | Native frozen/current secondary values and interactions; [guide](SECONDARY-LEDGER.md) |
| `--ledger-secondary-interactions <frozen-root> <native-master-audit> <frozen-app> <fresh-output> <date> [--negative-control]` | Native Quantify/close, identity chooser and injected GridCopy IO/modifier contracts; [guide](SECONDARY-LEDGER.md) |
| `--ledger-file-contracts <native-audit> <frozen-app> <fresh-output> <date> [--negative-control]` | Actual frozen/current FCF and CSV writes, partition/homology export routes, and explicit unproved format rows; [guide](FILE-CONTRACT-LEDGER.md) |
| `--test-wording-tsv <fresh-output>` | Leading quotes, embedded quotes and escaped multiline fields roundtrip through both shared-list readers |
| `--ledger-curation-file-contracts <native-audit> <frozen-app> <fresh-output> <date> [--negative-control]` | Native curation CSV choosers/files and real no-decoy engine refusal; [guide](CURATION-FILE-CONTRACTS.md) |
| `--ledger-input-file-contracts <frozen-app> <trusted-SEPro> <fresh-output> <date>` | Paired PLP, SEPro and FCF input choosers/readers on a private native desktop; [guide](INPUT-FILE-CONTRACTS.md) |
| `--ledger-floater-behaviour <frozen-source> <frozen-app> <scope-json> <fresh-output> <date> [--negative-control]` | Paired native prerequisite, build, lock, PLP and project behavior for the fixed 22-row cohort; [guide](FLOATER-BEHAVIOUR.md) |
| `--test-quantify-close` | Native styled Close button, modal result, retained result and no-focus checks in both themes; included in shell/quick checks |
| `--secondary-wording-append <native-proof-output> <wording.tsv>` | Append native changed-wording candidates for Paulo; no semantic proof or ledger-state change |
| `--secondary-box-audit <native-ledger-audit> <wording.tsv> N6k/N6l <output>` | Verify each retained box row has native proof or a lane wording entry; never changes ledger state |
| `--prove-denovo-ledger <frozen-source-root> <fresh-output> <date>` | Scoped exact De novo declarations, retained snapshots and ledger receipts in batches of 250; [guide](DENOVO-PRESERVATION.md) |
| `--prove-denovo-runtime <frozen-source-root> <frozen-app-folder> <fresh-output> <date>` | Native frozen/current controls and resolved values across both themes and three legacy sources |
| `--prove-denovo-workflows <frozen-source-root> <frozen-app-folder> <fresh-output> <date>` | Native frozen/current projection, reconciliation, binding and legacy export fixtures; C13 style handoff |
| `--prove-denovo-refusals <frozen-source-root> <frozen-app-folder> <fresh-output> <date>` | Native parser/refusal callback feedback, homology setting restoration, formatting and file-name lookup comparisons |
| `--prove-denovo-commands <frozen-source> <frozen-app> <trusted-sepr2> <fresh-output> <date>` | Actual old/current command/data/status and native chooser/export success/cancel/failure receipts; 41-row semantic fact inventory |
| `--prove-denovo-results-export <frozen-source> <frozen-app> <fresh-output> <date> [--negative-control]` | Whole-screen native source/Help and Results assignment export interactions; private desktop, selected CSV bytes, state retention and cancellation |
| `--isolated-native-proof <native-proof-command> <arguments...>` | Private, never-selected Windows desktop for native-dialog proof commands; the dialog proof entry points isolate automatically |
| `--prove-threeway <frozen-source> <frozen-app> <fresh-output> <date>` | Frozen/current three-way numeric/CSV and actual Save/cancel, empty/positive window states in two cultures on the private desktop |
| `--denovo-command-probe <frozen-app> <trusted-sepr2> <fresh-output>` | Actual command trace without ledger import |
| `--native-dialog-probe <fresh-output>` | Owned-process native Open/Folder/Save selection driver check |
| `--ledger-shell-compare <frozen-root> <fresh-output> <date>` | SH and C1 exact declarations/member tokens; separate candidate registers, unproven rows kept OPEN |
| `--ledger-core-relocation <frozen-root> <fresh-output> X5/X7 <date> [member-mappings.json]` | Scoped complete Core member comparisons, exact frozen hashes, retained source/checker snapshots and per-row gaps; [guide](CORE-RELOCATION-LEDGER.md) |
| `--ledger-text-preserved <frozen-root> <fresh-output> <date>` | N6a: closes OPEN rows whose frozen literal is compiled into 2.0 (IL and BAML), in the same feature (PDB file, relocation, reviewed mappings, class), reachable from the entry point; never export/storage or format kinds; dead-code control; one kind receipt per kind; [guide](TEXT-PRESERVED-LEDGER.md) |
| `--ledger-xic-runtime <frozen-root> <fresh-output> <date> <stored-project> <validated-cache> [--negative-control]` | Native XIC refusal/source/completion facts plus bounded validated cache replay; [runtime guide](CORE-RUNTIME-LEDGER.md) |
| `--shell-metadata-snapshot <frozen-root> <inventory> <fresh-output.json>` | Same native metadata helper in current and frozen hosts; assigned views remain unshown |
| `--ledger-shell-native <frozen-root> <frozen-host.exe> <fresh-output> <date> [C1-cohort-register]` | Execute compiled SH/C1 metadata and isolated Home clicks; own candidate registers and complete comparison gaps |
| `--prove-shell-main <frozen-root> <fresh-output> <date>` | Native MainWindow routes, synthetic file writes/refusals and captured dialog/report frames; own SH-main-routes candidate register |
| `--prove-curation-actions <frozen-root> <own-frozen-host> <fixed-audit.json> <fresh-output> <date> [--negative-control]` | Native curation handlers, annotation FCF/CSV roundtrip with unchanged shares, executed chooser filters/defaults and CSV writes/refusals; complete frozen/current CSV comparison in two cultures; own SH-curation-actions candidate, fixed OPEN cohort only |
| `--prove-shell-execution3 <frozen-root> <frozen-app> <trusted-sepr2> <fixed-execution.json> <fresh-output> <date> [--negative-control]` | Own SH-execution3 twenty-row packet: real frozen/current De novo button actions and native choosers/CSV exports, contaminant/decoy accession handling, and actual no-decoy identity-search refusal; private desktop never selected |
| `--prove-shell-behaviour <frozen-root> <frozen-app> <behaviour.json> <fresh-output> <date>` | Own SH-behaviour scoped native UI/contract comparisons with per-row supported or explicit OPEN outcomes; private desktop never selected |
| `--prove-shell-behaviour2 <frozen-root> <frozen-app> <behaviour2.json> <fresh-output> <date>` | Same native route restricted to the final sixteen-row scope, with actual menu command activation, Apply gating, export guards, cache artifacts and redundancy completion |
| `--prove-shell-behaviour3 <frozen-root> <frozen-app> <scope.json> <fresh-output> <date>` | Own SH-behaviour3 packet for the empty-import reconciliation guard; fresh native controls, exact complete refusal and unchanged empty state, isolated from earlier import cases |
| `--curation-export-snapshot <fresh-json>` | Shared current/frozen synthetic CSV and retained User sorting/counter values; no RAW computation |
| `--shell-review-wording <review-gaps.tsv> <shared-wording.tsv> <matching-native-main-packet> <fresh-output> <date>` | Generate every requested/previous own list row from compiled/native feature text or the exact `no current equivalent` decision with a reason; never changes dispositions |
| `--check-shell-review-list <review-gaps.tsv> <wording.tsv>` | Reject missing rows, empty values/reasons and unresolved angle-bracket placeholders in the reviewed cohort |
| `--word-grouping-snapshot <input.fcf-or-fqproj> <output.txt>` | [WordGroupingSnapshot.cs](../FamilyQuant.Tests/WordGroupingSnapshot.cs), also linked into frozen host |
| `--chart-export-snapshot <output-dir>` | [ChartExportSnapshot.cs](../FamilyQuant.Tests/ChartExportSnapshot.cs), synthetic chart parity fixtures |
| `--clustering-snapshot <input.fasta> <output.tsv> <integer-identity> <fast-or-exhaustive>` | [ClusteringSnapshot.cs](../FamilyQuant.Tests/ClusteringSnapshot.cs) |
| `--dependency-file-check <real.raw> <real.xic2> <new-report.json>` | [Program.Dependencies.cs](../FamilyQuant.Tests/Program.Dependencies.cs); read-only input-opening check |
| `--clustering-export-fcf <input.fcf> <new-output.fasta>` | Same helper, ordered stored-sequence export; never replaces an existing output |
| `--clustering-verify-fcf <input.fcf> <existing-export.fasta>` | Same helper, verifies every identifier/sequence and EOF |
| `--identity-search-study <input.fasta> <empty-output-dir> <fast-or-exhaustive>` | [IdentitySearchStudy.cs](../FamilyQuant.Tests/IdentitySearchStudy.cs); use the bounded study scope, not a new Viperidae exhaustive run |

The frozen golden comparison used by this lane is:

```powershell
& $testExe --golden C:/Users/paulo/Desktop/FamilyQuant/BrProt_Talk/all+DeepNovo/scientific_results.fqproj D:/FamilyQuant-data/golden D:/FamilyQuant-data/out-shell/HK1/golden
```

Choose a fresh lane-owned output for a new validation. This reads a stored project and writes scratch exports. A `4/0/0` result proves those four files match the frozen snapshot; it does not prove fresh extraction or update a reference hash.

## Evidence checkers

Optional positional values following each flag override its native defaults. These are dataset-specific checks; use the
same baseline/provenance packet as the owning scientific step. Some fetch live data or run long native searches. Input
and output must be separate. None is executed merely to maintain this catalog.

| Syntax after `$testExe` | Source and prerequisites |
|---|---|
| `--evidence-storage-check [input.fcf] [reference.tsv] [report.json]` | [EvidenceStorageCheck.cs](../FamilyQuant.Tests/EvidenceStorageCheck.cs) |
| `--evidence-task-check <evidence.fcf> <new-report.json>` | [Program.EvidenceTask.cs](../FamilyQuant.Tests/Program.EvidenceTask.cs); read-only live taxonomy discovery, native JSON report |
| `--evidence-task-header-audit <input.fcf-or-fqproj> <new-report.json>` | Production header-parser audit, joined-taxon counts, input byte hash and refusal boundary; no fetch or save |
| `--evidence-delivery prepare/split/create ...` | [Program.EvidenceDelivery.cs](../FamilyQuant.Tests/Program.EvidenceDelivery.cs); [native delivery syntax](EVIDENCE-DELIVERY.md) |
| `--evidence-coverage-check <prepared.fcf> <applied.fcf> <new-output-directory> [existing-capture-directory]` | [Program.EvidenceCoverage.cs](../FamilyQuant.Tests/Program.EvidenceCoverage.cs); native capture or deterministic offline replay, without applying a counterfactual |
| `--family-world-fetch <input.fcf> <new-output.fcf> <confirmed-scope>` | [Program.FamilyWorldNative.cs](../FamilyQuant.Tests/Program.FamilyWorldNative.cs); live fetch, size report and offline byte proof |
| `--evidence-fetch-check [input.fcf] [reference.tsv] [output.fcf]` | [EvidenceFetchCheck.cs](../FamilyQuant.Tests/EvidenceFetchCheck.cs); live fetch |
| `--evidence-fetch-verify [input.fcf] [reference.tsv] [existing-output.fcf]` | Same helper, verifies the existing native evidence copy |
| `--reference-panel-check [evidence.fcf] [output-dir]` | [ReferencePanelCheck.cs](../FamilyQuant.Tests/ReferencePanelCheck.cs); live panel and validation |
| `--refresh-panel [searched.fcf] [new-output.fcf]` | Same helper, refetches panel and checks stored-search currency |
| `--nearest-reviewed-check [evidence.fcf] [reference.tsv] [output-dir]` | [NearestReviewedCheck.cs](../FamilyQuant.Tests/NearestReviewedCheck.cs) |
| `--nearest-reviewed-full [panel.fcf] [new-output.fcf]` | Same helper, full scoped search and stored copy |
| `--evidence-classifier-check [searched.fcf] [report.json] [scope-terms]` | [EvidenceClassifierCheck.cs](../FamilyQuant.Tests/EvidenceClassifierCheck.cs); current stored search required |
| `--evidence-architecture-check [searched.fcf] [report.json] [scope-terms]` | Same helper, architecture suggestions |
| `--evidence-apply-check [searched.fcf] [report.json] [scope-terms]` | Same helper, explicit apply/undo/reopen checks in separate outputs |
| `--evidence-session-apply-check [searched.fcf] [report.json] [scope-terms]` | Same helper, session preparation and recipe replay |
| `--family-world-lengths [project.fqproj] [output-dir] [world.fcf] [reference.fqproj]` | [FamilyWorldLengthsCheck.cs](../FamilyQuant.Tests/FamilyWorldLengthsCheck.cs), B10: every minimum tag length from one search against an analysis at each length alone, timed, with CSVs of the per-length estimate, what each step down in length adds, and the known-answer halves at 7, 8 and 9. A world file (B4's `.fcf`) replaces the world the project stores, for a project that stores none. A reference project's stored table must come back by choosing its length in this project's stored result (B10b: choosing 9 gives B6's table) |
| `--family-world-report [project.fqproj] [output-dir]` | [FamilyWorldReport.cs](../FamilyQuant.Tests/FamilyWorldReport.cs), the family-world table a project stores, printed and exported |
| `--family-world-check [project.fqproj] [world.fcf] [output-dir]` | [FamilyWorldCheck.cs](../FamilyQuant.Tests/FamilyWorldCheck.cs); B1 gate, gate study, B5 product pass under D11 and B6 per-family errors; live taxonomy lists in output |
| `--coherence-check [panel.fcf] [classes.tsv] [output-dir]` | [CoherenceCheckRun.cs](../FamilyQuant.Tests/CoherenceCheckRun.cs) |
| `--motif-check [evidence.fcf] [classes.tsv] [expected.tsv] [output-dir]` | [MotifCheckRun.cs](../FamilyQuant.Tests/MotifCheckRun.cs) |
| `--left-out-check [searched.fcf] [baseline.csv] [classes.tsv] [output-dir] [scope-terms]` | [LeftOutCheckRun.cs](../FamilyQuant.Tests/LeftOutCheckRun.cs) |
| `--validation-check [searched.fcf] [baseline.tsv] [frozen-evidence.tsv] [output-dir]` | [ValidationCheckRun.cs](../FamilyQuant.Tests/ValidationCheckRun.cs) |
| `--science-gate-list [searched.fcf] [classes_all.tsv] [output-dir] [scope-terms]` | [ScienceGateListRun.cs](../FamilyQuant.Tests/ScienceGateListRun.cs); sibling `classes.tsv` also required |
| `--evidence-operations-check [panel.fcf]` | [EvidenceOperationsCheck.cs](../FamilyQuant.Tests/EvidenceOperationsCheck.cs); live fetch/search and cancellation, not an ordinary fixture pass |

The B1 family-world gate checks known answers without the sample's species (and, ungated, its genus)
and coverage of the unattributed de novo envelope.
B5 writes `family_world_pass.csv`. B6 writes per-family errors to `family_world_errors.csv`,
stores them in a project copy and reads them back.

Quote semicolon-separated scope text, for example `'Toxin;venom gland'`.

## Scientific and presentation lanes retained for their owners

These entries remain discoverable because they are real dispatch paths, not because housekeeping authorizes running
them. They can write data, caches, figures and study packets at their existing configured destinations. Presentation
inputs and canonical slide PNGs remain protected. Read the linked producer and select the exact source/cutoff packet
before an authorized run.

| Flag and invocation convention | Source/scope |
|---|---|
| `. . <deNovoRoot> <acquisitionRoot> --talk-figures` | [TalkFigures.cs](../FamilyQuant.Tests/TalkFigures.cs); complete RAW-backed pack |
| `. . <deNovoRoot> <acquisitionRoot> --talk-only-plus-deepnovo` | Same producer, one exact source variant |
| `. . <deNovoRoot> <acquisitionRoot> --talk-variants` | Same producer, configured source variants and comparison |
| `. . <deNovoRoot> <acquisitionRoot> --talk-source-venn` | Same producer, source-union Venn lane |
| `. . <deNovoRoot> <output-dir> --sweep-figure` | Same producer, CSV-based score sweep |
| `. . <deNovoRoot> <output-dir> --agreement-figure` | Same producer, precursor agreement |
| `. . <deNovoRoot> <output-dir> --score-scatter-figure` | Same producer, caller-score scatter |
| `. . <project.fqproj> <output-dir> --accepted-score-scatter` | Same producer, persisted accepted-call scatter |
| `. . <deNovoRoot> <output-dir> --score-distribution-figure` | Same producer, source-specific score histogram |
| `. . <deNovoRoot> <output-dir> --set-novelty-figure` | Same producer, set novelty |
| `. . <deNovoRoot> <acquisitionRoot> --overlap-study` | [Program.cs](../FamilyQuant.Tests/Program.cs), real reconciliation study |
| `. . <deNovoRoot> <acquisitionRoot> --short-psm-peaks-table` | [ShortPsmPeaksTable.cs](../FamilyQuant.Tests/ShortPsmPeaksTable.cs), writes its table beside the acquisition data |
| `. . <deNovoRoot> <acquisitionRoot> --charge-audit` | [ChargeAudit.cs](../FamilyQuant.Tests/ChargeAudit.cs), RAW-recorded metadata audit |
| `. . <deNovoRoot> <acquisitionRoot> --three-way-audit` | [ThreeWayAudit.cs](../FamilyQuant.Tests/ThreeWayAudit.cs), identification audit |
| `. . <project.fqproj> <curation.fcf-or-dot> <output-dir> --attribution-rule-study` | [AttributionRuleStudy.cs](../FamilyQuant.Tests/AttributionRuleStudy.cs), homology sensitivity study without RAW |

`--operation-native-wait <marker-path>` is an internal child-process fixture, not an operator check. The operation suite launches and cancels its exact owned child. It deliberately waits when invoked alone.

The guarded `build_scientific_deck.mjs` is a historical, deprecated producer with obsolete external/source paths. It is not a current build command and is not repaired or run by housekeeping.

## Generated-ledger status

The committed scan is a frozen-source inventory, not a scan of current view names.
It contains 5,756 observations from 104 source files: 2,009 KEEP, 3,451 IMPROVE
and 296 Paulo-approved DROP. N5's first native proof pass proposes 72 VERIFIED retained
rows and leaves 5,388 OPEN; the retirements remain APPROVED_DROP. The proposal still
requires the independent review sample and merge gate. The evidence audit reports the
current counts and rejects stale receipts. A missing old handler name is not proof of loss.

Regenerate into scratch with the published command and compare both ledger files against the checkout and the Git blobs
separately. On the audited Windows checkout both regenerated files are byte-identical to the working files; the scan
JSON differs from its LF Git blob only by CRLF newlines, and normalized content is identical. The Markdown is also
byte-identical to its Git blob. Never overwrite acceptance evidence or scan moving source to make the counts pass. The
release check intentionally fails while retained rows remain OPEN.

Housekeeping source-path audit: of the retained observations, 3,446 still have their original source path and 2,014 come
from sources relocated to Core. Of the 104 frozen source files, 37 remain at their original path, 66 map to Core, and
the removed `GroupingType.cs` has no observation rows. These are source-location categories, not VERIFIED feature
mappings. Twenty-one old handler-name observations are absent literally after the view rewrites; eight are within
approved DROP, while the others need command/view-model mapping. The source audit distinguishes those cases; no retained
row is dropped on a name search.
