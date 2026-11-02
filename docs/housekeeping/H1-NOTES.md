# H1 findings and held decisions

Housekeeping source review began at `44f4300dab536e95396016ca5ef5b61413e59f50`. The read-only census is `D:/FamilyQuant-wt/desk/notes/H1-CENSUS.md`; semantic reuse readings are `H1-DUPLICATES-FQReview.md` and `H1-DUPLICATES-FQOpus.md` beside it. These are team artifacts, not portable repository dependencies. Batch anchors are rechecked after each master merge.

HK1 removed uncalled shell forwarding methods/imports; its frozen golden comparison was 4/0/0 with all four exported files unchanged. HK2 removed identical unreachable test-dispatch branches without removing assertions. HK3 refreshed current documentation and added the [command catalog](../INDEX.md). These results do not claim fresh RAW extraction, installation, screen-reader coverage or universal numerical validation.

## LIST: behavior and release decisions, no fix here

| Source | Finding and disposition |
|---|---|
| `MainWindow.Documents.cs`, `MainWindow.Presentation.cs`, `MainWindow.Commands.cs:ReportShellMessage` | Direct message boxes and the status/headless-guarded helper have different behavior. Consolidating them would add status updates or suppress a headless dialog. Leave for a shell fix with its own tests. |
| `MainWindow.WindowPreferences.cs`, `QuantifyControl.xaml.cs:BrowseRaw/BrowseDeNovo` | Folder-dialog adapters do not all use the shell's folder preference. Adding that behavior is a feature correction, not neutral reuse. |
| `FamilyFilter.IsContaminant`, Evidence predicates, `PresentationFigures.IsContaminant` | Contaminant matching differs by prefix versus anywhere and ordinal versus ignore-case. Do not select a new policy during cleanup. HK5 shares only the identical ordinal decoy predicate and leaves protected presentation callers unchanged. |
| `DeNovoViewModel` catch blocks and dialog guards | Similar shapes carry different refusal messages, next steps, status targets and return behavior. Keep these separate. |
| `DeNovoReconciliationReport`, `AnalyticsModel`, Evidence CSV writers, `CsvReport`, `HomologyCsv` | There are distinct CSV quoting and formula-prefix rules. HK5 keeps the two approved shared rules separate; the third rule remains separate. Numeric-format strings are part of the rule. No formula-guard harmonization is authorized. |
| `EvidenceStorageCheck.ReadReference`, `ValidationCheckRun.ReadFrozen`, checker scope predicates | Frozen answer-key readers and scopes intentionally reproduce different reference rules. They are not interchangeable with product matching. Test argument readers likewise keep empty-value fallback and empty-value passthrough as separate rules. |
| `DeNovoReconciler`, `PeptidoformIdentity`, isotope-spacing constants | Sequence cleaning, modification-aware identity and rounded isotope spacing have different roles. A change would alter peptide equivalence or scientific numbers; leave to X6/science review. |
| `FileFingerprint`, Evidence row fingerprints, protein-set and JSON fingerprints | File-stream, length-prefixed field, sorted-line and JSON hashes are different contracts. HK5 shares only the approved length-prefixed primitive and preserves caller hex case. |
| `DeNovoAgreementStudy`, `IdentificationConcordanceStudy` | Best-ppm blocks have different tie conditions, including feature counts. Do not merge a decision by text similarity. |
| `FullXicComputer`, other original text-scan candidates | Quantitation-option setup belongs with X5. Distribution/catch/chart candidates not selected for HK5 are not silently added to its scope. |
| `LICENSE.txt:3` | Copyright year/holder placeholders remain. Paulo must supply the actual rights holder and licensing decision; this pass does not invent them or vendored-binary redistribution rights. |
| `.editorconfig:4` | CS8603 is suppressed while nullable checking is enabled. This is an existing policy, not proof of a current bug; changing it is outside neutral cleanup. |
| `N1-IDENTITY.md`, configured deployment address | The installed-profile/update check remains an owner release gate. The web tool could not verify the configured hosting endpoint; no successful deployment is claimed. |

## Encapsulation and protected presentation work

| File over about 1,000 lines | Roadmap coverage or disposition |
|---|---|
| `FamilyQuant.Core/ProjectStore.cs` | X7 owns the schema/container split. Preserve stored formats and save semantics. |
| `FamilyQuant/ClusterGenerationControl.xaml.cs` | C1/C2/C5 own the curation rewrite; C4 files remain held until released by the desk. |
| `FamilyQuant/PresentationFigures.cs` | X9 owns Reports extraction; protected presentation input. |
| `FamilyQuant.Tests/TalkFigures.cs` | Protected scientific producer. Do not split it as incidental housekeeping. |
| `FamilyQuant.Tests/Program.Charts.cs` | Test organization may be considered in the re-plan, retaining every check and fixture. |
| `FamilyQuant.Tests/Program.DeNovoScreen.cs` | Same; compatibility checks remain intentional after the view rewrite. |
| `FamilyQuant.Tests/Program.UiLayout.cs` | Same; text, tooltip, clipping and navigation checks must remain. |

FQOpus findings 4 and 5 stay with X9: the talk chart base, axis-rounding ladders, RGB brush factories and presentation source-distribution validation. Equal-looking defaults and different ladders are not automatically the same rule. Frozen export palettes, geometry, labels and PNG bytes remain protected.

`build_scientific_deck.mjs` is not a runnable current producer. Its first action is an unconditional deprecation error, and its historical input table still names old paths such as `FamilyQuant/DeNovo/ReferenceEnvelopeEstimator.cs`, which is now under Core. It also names machine-specific historical roots. This is listed for Paulo; the script, scientific method/talk inputs and canonical slide PNGs were not changed or run.

## Ledger status and source migration

The published generator reproduced both committed ledger files byte for byte in scratch: 104 frozen source files and 5,756 observations. Dispositions are 2,009 KEEP, 3,451 IMPROVE and 296 Paulo-approved DROP. All 5,460 retained rows are OPEN; no row was promoted to VERIFIED by a search or a green build. The release guard intentionally remains unsatisfied until owner evidence and review close them.

For retained observations, 3,446 still have their original source path and 2,014 belong to sources moved to Core. The source-file inventory maps 37 old paths directly and 66 to Core; removed `GroupingType.cs` has no observation rows and belongs to the completed F5 selector removal. Source-location categories are not behavior verdicts.

Twenty-one frozen handler-name observations are no longer literal names in current C#/XAML. Eight are in Paulo's approved retired extraction/partition UI. The other thirteen have these source mappings for C14 review; their ledger states remain OPEN:

| Former handlers | Current implementation to verify |
|---|---|
| Analytics peptide, figure and presentation exports | `AnalyticsControl.ExportClicked`, selecting the corresponding `ResultsExportKind` and its view-model export |
| Analytics Top N and percent change handlers | `AnalyticsControl.ChartChoiceChanged` |
| Compute Browse RAW/SEPro/de novo and spectrum handlers | `QuantifyControl.BrowseRaw`, `BrowseSepro`, `BrowseDeNovo`, `ViewSpectrum` |
| Compute and Close handlers | `ComputeXicViewModel.ComputeCommand` and the compatibility window's native closing path |
| De novo homology CSV handler | Results' `ResultsExportKind.Homology` and `HomologyCsv.Write` |

The complete observation IDs and source anchors are in the scratch `out-shell/HK3/ledger-relocations.json`. This is not a declaration that every displaced tooltip/fact has been verified. C14 still needs the per-row preservation evidence.

## Reuse batches and proof boundary

HK5a is limited to the two distinct approved quoting rules, length-prefixed hash, Pfam accessions and parameterized UniProt retry/release-header code. HK5b preserves every per-view command-availability rule, shares only the ordinal decoy rule, and removes the proven dead `CsvField`; a dialog-filter constant is eligible only where bytes are identical. HK5c consolidates the reviewed test helpers while retaining both argument-reader rules. Each product batch needs build, quick suite and frozen golden 4/0/0; docs/test-only batches prove no product input moved. None authorizes changing a score, default, stored format, reference hash or visible string.

Builds reported for HK1-HK3 had zero warnings/errors. The quick suite is fixture coverage, and the four-file golden comparison is stored-project parity. Neither establishes fresh RAW behavior or covers every scientific case. Historical milestone test counts remain historical.

## Operator delete list

No external deletion is proposed. Tracked backup/log/temp suffix scanning found no stray files. Ignored/untracked outputs, worktrees, stashes and other teams' processes were not deleted or cleaned by pattern. Paulo alone may decide whether to retire the deprecated deck script or external historical artifacts. Protected history and version/roadmap files remain unchanged.
