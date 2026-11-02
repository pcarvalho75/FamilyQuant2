# Repository and test-command catalog

This is the navigation index for the current checkout. The roadmap owns unfinished work. Recorded check counts in milestone notes describe those runs, not a live suite total.

## Documents

| Document | Purpose |
|---|---|
| [CLAUDE.md](../CLAUDE.md) | Current projects, build/test setup, scientific invariants and implementation guidance |
| [Architecture](ARCHITECTURE.md) | Current boundaries, planned Reports/operation host, budgets and historical destination inventory |
| [Core operations](CORE-OPERATIONS.md) | Progress, cancellation, outcomes and atomic publication |
| [Clustering refactor](CLUSTERING-REFACTOR.md) | Result contracts and bounded frozen/current comparison |
| [Identity study](IDENTITY-SEARCH-STUDY.md) | Why Scan and Divide remain separate, exact measured scope |
| [Identity/deployment](N1-IDENTITY.md) | Separate application identity and the installed-profile check still owned by Paulo |
| [Science gate](SCIENCE-GATE.md) | Recorded evidence-rule comparison, verdicts and limitations |
| [H1 findings](housekeeping/H1-NOTES.md) | Behavior decisions, protected work, ledger migration and remaining reuse scope |
| [Vision](vision/VISION.md) | Fable's study: the family world proposal behind workstream W8, with the desk's corrections |
| [Code map](vision/CODE-MAP.md) | How a FASTA and a RAW file become a family number, and what that read did not cover |
| [Feature ledger](FEATURE-LEDGER.md) | Frozen preservation contract, dispositions and acceptance state |
| [Ledger scan](feature-ledger-scan.json) | Exact frozen observations and source hashes |
| [Roadmap](ROADMAP.md) | Current work and acceptance gates; orchestrator owned |
| [Roadmap archive](ROADMAP-archive.md) | Historical step records; orchestrator owned |
| [Release notes](../RELEASE_NOTES.md) | Historical release descriptions, not current publication status |
| [Security](../SECURITY.md) | Trusted SEPro-input boundary and existing native-process policy |
| [Scientific method](../SCIENTIFIC_METHOD.md) | Protected scientific reference; not edited by housekeeping |
| [Scientific revision report](../SCIENTIFIC_REVISION_REPORT.md) | Historical validation account and limitations |
| [Talk script](../SCIENTIFIC_TALK_SCRIPT.md) | Protected presentation input |
| [Screen harness](../FamilyQuant.Tests/SCREENS.md) | Capture matrix, isolation and what screenshots do not prove |
| [Appearance](../FamilyQuant/Themes/README.md) | Theme/token behavior and historical migration context |
| [Screen rules](../FamilyQuant/Themes/STYLE-GUIDE.md) | Shared typography, tables and action roles |
| [Brand assets](../FamilyQuant/Assets/Brand/README.md) | Geometry, generated assets and regeneration command |
| [License](../LICENSE.txt) | Existing license text; unresolved identity placeholders require the owner |

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

On the team machine, the equivalent launcher is `powershell -NoProfile -ExecutionPolicy Bypass -File D:/FamilyQuant-wt/desk/quick-suite.ps1`. It sets the same processor limit and isolated TEMP, runs at normal priority and reports the exit status. Its `-Full` mode belongs to the gate and supplies the real-data arguments.

The ordinary data slots are `[plp] [database] [deNovoRoot] [acquisitionRoot]`. Use `.` for an absent slot; do not pass empty strings from PowerShell. No-data sections use fixtures; data-dependent sections report skips. A quick green result is not a fresh RAW or installed-product validation.

The syntax tables below use `<name>` for a required supplied path/value and `[name]` for an optional argument. Replace those markers before execution. Paths containing spaces must be quoted. Use explicit output paths outside the repository and outside read-only inputs; many native checkers have shared-machine defaults. The tables are source-audited syntax, not a claim that every expensive or data-specific command was run during housekeeping.

## Fixture and contract commands

Invoke each flag with `& $testExe <flag>` after building. Dispatch and arity are defined by [Program.cs](../FamilyQuant.Tests/Program.cs).

| Flag | Coverage |
|---|---|
| `--test-core-boundary` | Core/App ownership, references and resource contract |
| `--test-document` | Document round trip, scoring and denominator attribution fixtures |
| `--test-inputs` | Real-data argument-selection contract, without choosing Desktop fallbacks |
| `--test-operations` | Core outcomes, progress, cancellation and the owned native-child fixture |
| `--test-clustering-results` | Typed clustering/redundancy results and native synthetic fixtures |
| `--test-identity-study` | Probe parser and grid-versus-probe interpretation |
| `--test-results` | Results view-model readiness and export choices |
| `--shell-checks` | Shell navigation, saving, preferences, Build/Quantify integration and secondary views |
| `--feature-ledger-check` | Inventory, row coverage, dispositions and guard regressions |
| `--feature-ledger-release-check` | Same inventory with all retained rows required VERIFIED; OPEN rows intentionally fail |

## Screens and UI metadata

These commands create native WPF client captures or labelled synthetic fixtures, not scientific deliverables. The broad harness accepts read-only real inputs; focused producers state their fixture scope in their logs.

| Syntax after `$testExe` | Source |
|---|---|
| `--screens <output-dir>` | [ScreenHarness.cs](../FamilyQuant.Tests/ScreenHarness.cs) |
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
| `--word-grouping-snapshot <input.fcf-or-fqproj> <output.txt>` | [WordGroupingSnapshot.cs](../FamilyQuant.Tests/WordGroupingSnapshot.cs), also linked into frozen host |
| `--chart-export-snapshot <output-dir>` | [ChartExportSnapshot.cs](../FamilyQuant.Tests/ChartExportSnapshot.cs), synthetic chart parity fixtures |
| `--clustering-snapshot <input.fasta> <output.tsv> <integer-identity> <fast-or-exhaustive>` | [ClusteringSnapshot.cs](../FamilyQuant.Tests/ClusteringSnapshot.cs) |
| `--clustering-export-fcf <input.fcf> <new-output.fasta>` | Same helper, ordered stored-sequence export; never replaces an existing output |
| `--clustering-verify-fcf <input.fcf> <existing-export.fasta>` | Same helper, verifies every identifier/sequence and EOF |
| `--identity-search-study <input.fasta> <empty-output-dir> <fast-or-exhaustive>` | [IdentitySearchStudy.cs](../FamilyQuant.Tests/IdentitySearchStudy.cs); use the bounded study scope, not a new Viperidae exhaustive run |

The frozen golden comparison used by this lane is:

```powershell
& $testExe --golden C:/Users/paulo/Desktop/FamilyQuant/BrProt_Talk/all+DeepNovo/scientific_results.fqproj D:/FamilyQuant-data/golden D:/FamilyQuant-data/out-shell/HK1/golden
```

Choose a fresh lane-owned output for a new validation. This reads a stored project and writes scratch exports. A `4/0/0` result proves those four files match the frozen snapshot; it does not prove fresh extraction or update a reference hash.

## Evidence checkers

Optional positional values following each flag override its native defaults. These are dataset-specific checks; use the same baseline/provenance packet as the owning scientific step. Some fetch live data or run long native searches. Input and output must be separate. None is executed merely to maintain this catalog.

| Syntax after `$testExe` | Source and prerequisites |
|---|---|
| `--evidence-storage-check [input.fcf] [reference.tsv] [report.json]` | [EvidenceStorageCheck.cs](../FamilyQuant.Tests/EvidenceStorageCheck.cs) |
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
| `--coherence-check [panel.fcf] [classes.tsv] [output-dir]` | [CoherenceCheckRun.cs](../FamilyQuant.Tests/CoherenceCheckRun.cs) |
| `--motif-check [evidence.fcf] [classes.tsv] [expected.tsv] [output-dir]` | [MotifCheckRun.cs](../FamilyQuant.Tests/MotifCheckRun.cs) |
| `--left-out-check [searched.fcf] [baseline.csv] [classes.tsv] [output-dir] [scope-terms]` | [LeftOutCheckRun.cs](../FamilyQuant.Tests/LeftOutCheckRun.cs) |
| `--validation-check [searched.fcf] [baseline.tsv] [frozen-evidence.tsv] [output-dir]` | [ValidationCheckRun.cs](../FamilyQuant.Tests/ValidationCheckRun.cs) |
| `--science-gate-list [searched.fcf] [classes_all.tsv] [output-dir] [scope-terms]` | [ScienceGateListRun.cs](../FamilyQuant.Tests/ScienceGateListRun.cs); sibling `classes.tsv` also required |
| `--evidence-operations-check [panel.fcf]` | [EvidenceOperationsCheck.cs](../FamilyQuant.Tests/EvidenceOperationsCheck.cs); live fetch/search and cancellation, not an ordinary fixture pass |

Quote semicolon-separated scope text, for example `'Toxin;venom gland'`.

## Scientific and presentation lanes retained for their owners

These entries remain discoverable because they are real dispatch paths, not because housekeeping authorizes running them. They can write data, caches, figures and study packets at their existing configured destinations. Presentation inputs and canonical slide PNGs remain protected. Read the linked producer and select the exact source/cutoff packet before an authorized run.

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

The committed scan is a frozen-source inventory, not a scan of current view names. It contains 5,756 observations from 104 source files: 2,009 KEEP, 3,451 IMPROVE and 296 Paulo-approved DROP. All 5,460 retained rows remain OPEN; the approved retirements use APPROVED_DROP. Source moves and command-backed replacements require a mapping and reviewed execution evidence before VERIFIED. A missing old handler name is not proof that the function is lost.

Regenerate into scratch with the published command and compare both ledger files against the checkout and the Git blobs separately. On the audited Windows checkout both regenerated files are byte-identical to the working files; the scan JSON differs from its LF Git blob only by CRLF newlines, and normalized content is identical. The Markdown is also byte-identical to its Git blob. Never overwrite acceptance evidence or scan moving source to make the counts pass. The release check intentionally fails while retained rows remain OPEN.

Housekeeping source-path audit: of the retained observations, 3,446 still have their original source path and 2,014 come from sources relocated to Core. Of the 104 frozen source files, 37 remain at their original path, 66 map to Core, and the removed `GroupingType.cs` has no observation rows. These are source-location categories, not VERIFIED feature mappings. Twenty-one old handler-name observations are absent literally after the view rewrites; eight are within approved DROP, while the others need command/view-model mapping. The source audit distinguishes those cases; no retained row is dropped on a name search.
