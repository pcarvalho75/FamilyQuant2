using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// Checks the parts of FamilyQuant that decide numbers, without a window to click.
    ///
    ///   FamilyQuant.Tests.exe [plp] [database] [deNovoRoot] [acquisitionRoot]
    ///
    /// Sections without real-data inputs build fixtures and run without a dataset.
    /// Pass a .plp to exercise the quantitation path against real data, and a database as well to run
    /// the UI smoke section - which builds the real WPF controls, because the XAML in
    /// ClusterGenerationControl is only parsed when a Family Cluster File is opened and nothing else
    /// here would catch a broken binding or a handler that throws. Set FAMILYQUANT_USEARCH if the
    /// bundled USEARCH cannot be executed from the local application data folder.
    /// </summary>
    internal static partial class Program
    {
        private static int passed;
        private static int failed;
        private static int skipped;

        [STAThread] // required: the UI smoke section constructs WPF controls
        private static int Main(string[] args)
        {
            IsolateShellSessionState();
            if (TestCommandRegistry.TryRun(args, TestCommandPhase.Raw, string.Empty, out int rawExit))
                return rawExit;
            // PowerShell 5.1 silently DROPS empty "" arguments to native executables, which shifted
            // the data directories into the wrong positions and turned every real-data section into a
            // silent skip. A dot is the positional placeholder that survives every shell; it maps to
            // "this argument was not given".
            for (int i = 0; i < args.Length; i++)
                if (args[i] == ".")
                    args[i] = "";
            string workDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
            Directory.CreateDirectory(workDir);
            if (TestCommandRegistry.TryRun(args, TestCommandPhase.Normalized, workDir, out int commandExit))
                return commandExit;
            SuiteShard shard = TakeSuiteShard(ref args);
            RunSuite(DefaultSuite(args, workDir), shard);
            Console.WriteLine();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }

        private static void IsolateShellSessionState()
        {
            string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "shell-state", Guid.NewGuid().ToString("N"));
            ApplicationPaths.LocalDataDirectoryOverride = Path.Combine(root, "app-data");
            void Isolate(Assembly assembly)
            {
                var shell = assembly.GetType("FamilyQuant.MainWindow");
                if (shell == null) return;
                // Frozen comparison shells have their own statics. Isolate them before any window is constructed too.
                var settings = shell.GetProperty("ShellSettingsPathOverride", BindingFlags.Public | BindingFlags.Static);
                if (settings?.CanWrite != true)
                    throw new InvalidOperationException("The shell test assembly must expose an isolated settings path.");
                settings.SetValue(null, Path.Combine(root, assembly.ManifestModule.ModuleVersionId.ToString("N"), "shell.json"));
            }
            Isolate(typeof(MainWindow).Assembly);
            AppDomain.CurrentDomain.AssemblyLoad += (_, loaded) => Isolate(loaded.LoadedAssembly);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) Isolate(assembly);
        }

        // Later real-data checks read what earlier ones left in static fields, so each group stays in one shard.
        private const string DeNovoRun01Extraction = "run 01 de novo extraction";
        private const string DeNovoScreenCensus = "de novo screen censuses";

        /// <summary>The default run, in order: each entry is one original call under its section heading. A
        /// serial run executes every entry exactly as the former straight-line Main did; a shard runs its share.</summary>
        private static SuiteTable DefaultSuite(string[] args, string workDir)
        {
            var suite = new SuiteTable();
            suite.Section("CSV export - locale and escaping");
            suite.Add(() => CommandRegistryTests());
            suite.Add(() => SuiteCommandCoverageTests(null));
            suite.Add(() => CoreReuseContractTests());
            suite.Add(() => SharedTestHelperContracts(workDir));
            suite.Add(() => CsvTests());
            suite.Add(() => ExportProteinNameTests());
            suite.Section("Core operation outcomes, progress and cancellation");
            suite.Add(() => CoreOperationTests(workDir));
            suite.Add(() => StreamingSaveTests(Path.Combine(workDir, "streaming-save")));
            suite.Add(() => CurationStreamingCallerTests());
            suite.Add(() => CurationProofSamplerTests());
            suite.Add(() => CurationFormat2Tests(Path.Combine(workDir, "curation-format2")));
            suite.AddWithApplication(() => RecipeCopiesTests(Path.Combine(workDir, "recipe-copies")));
            suite.Add(() => FeatureProvenanceFormatTests(Path.Combine(workDir, "feature-provenance")));
            suite.Add(() => PanelEntryIdentityFormatTests(Path.Combine(workDir, "panel-entry-identity")));
            suite.Add(() => CurationFormat2CompositionTests(Path.Combine(workDir, "curation-format2-composition")));
            suite.AddWithApplication(() => CurationStreamingContractTests(Path.Combine(workDir, "curation-streaming-contract")));
            suite.Add(() => JsonStreamCopyTests());
            suite.Add(() => CompactCurationTests(Path.Combine(workDir, "compact-curation")));
            suite.Section("Display filters and the family counter");
            suite.Add(() => FilterTests());
            suite.Section("Target/decoy scoring arithmetic");
            suite.Add(() => ScoreTests());
            suite.Section("Target/decoy mixing - which families mix, and why Divide and Conquer stopped where it did");
            suite.Add(() => MixedFamilyTests(workDir));
            suite.Section("Identity scan - argument validation");
            suite.Add(() => ScanValidationTests());
            suite.Section("Family Cluster File round trip");
            suite.Add(() => RoundTripTests(workDir));
            suite.Section("Word-based reclustering invariants");
            suite.Add(() => WordClusterTests());
            suite.AddWithApplication(() => DeNovoSourceProposalTests());
            suite.AddWithApplication(() => FamilyStudyMembershipTests());
            suite.Add(() => QuantifyViewModelTests(workDir));
            suite.Add(() => BusyOperationTests(workDir));
            suite.Add(() => ShellDialogTests(workDir));
            suite.Add(() => UpdateCheckTests());
            suite.Add(() => ResultsViewModelTests());
            suite.Add(() => DatabaseGapTests(Path.Combine(workDir, "database-gap")));
            suite.Add(() => DatabaseGapLengthTests(Path.Combine(workDir, "database-gap-lengths")));
            suite.Add(() => ResultsPreservationTests(Path.Combine(workDir, "results-preservation")));
            suite.Add(() => ResultsTooltipTests());
            suite.Add(() => Check("native Results export fixture propagates its worker exit status",
                RunResultsExportFixture(Path.Combine(workDir, "results-export-facts")) == 0));
            suite.AddWithApplication(() => ResultsDocumentLifetimeTests(Path.Combine(workDir, "results-document-lifetime")));
            suite.Add(() => Check("native dispatcher reclaim fixture propagates its worker exit status",
                CurationDispatcherReclaimTests(Path.Combine(workDir, "dispatcher-reclaim-" + Guid.NewGuid().ToString("N"))) == 0));
            suite.Add(() => DeNovoViewModelTests(workDir));
            suite.Add(() => HomologyResultsTests(workDir));
            suite.Section("Database parsing and the decoy tag");
            suite.Add(() => DatabaseTests(workDir));
            suite.Section("Exhaustive clustering - the second pass closes the gaps the fast pass leaves");
            suite.Add(() => ExhaustiveGateTests());
            suite.Add(() => ExhaustiveMemoTests(workDir));
            suite.Add(() => ExhaustiveRealDataTests(args.Length > 3 ? args[3] : null));
            suite.Section("Redundancy removal - order kept, subsets dropped");
            suite.Add(() => RedundancyTests(workDir));
            suite.Add(() => ClusteringResultTests(workDir));
            suite.Add(() => IdentityStudyTests());
            suite.Section("De novo evidence - source-aware reader, filters, reconciliation");
            suite.Add(() => DeNovoTests(workDir));
            suite.Add(() => DeNovoAgreementUnitTests());
            suite.Add(() => IdentificationConcordanceUnitTests());
            suite.Add(() => DeNovoRealExportTests(args.Length > 2 ? args[2] : null));
            suite.Section("De novo source provenance - separate score semantics, deterministic union and project round trip");
            suite.Add(() => SourceAwareDeNovoTests.Run(Check, workDir));
            suite.Add(() => DeNovoEngineTidyTests());
            suite.Section("Isotope-aware precursor equivalence - the JOIN/matching paths pair M, M+-1, M+-2 of one precursor");
            suite.Add(() => IsotopePrecursorEquivalenceTests.Run(Check));
            suite.Section("De novo XIC - stored quants reproduced bit for bit from the RAW");
            suite.Add(() =>
            {
                int deNovoXicBefore = passed + failed;
                DeNovoXicTests.Run(Check, workDir, args.Length > 3 ? args[3] : null);
                if (passed + failed == deNovoXicBefore)
                    skipped++;
            });
            suite.Section("Homology - PepExplorer's aligner, ported with its C-terminus and character defects fixed");
            suite.Add(() => HomologyTests.Run(Check));
            suite.Add(() => HomologySearchEquivalenceTests());
            suite.Add(() => HomologyScoreTierTests());
            suite.Add(() => HomologyDecoyGateTests());
            suite.Add(() => HomologyTagGateTests());
            suite.Add(() => HomologyDefaultMigrationTests());
            suite.Section("Family world (B1) - a de novo tag credited to UniProt families, on a fixture");
            suite.Add(() => FamilyWorldCheck.RunFixtures(Check));
            suite.Add(() => FamilyWorldRateTests());
            suite.Add(() => FamilyWorldComputeTests());
            suite.Section("De novo reconciliation against the real .sepr2 - the request's items 3 to 5");
            suite.Add(() => ReconciliationRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir), DeNovoScreenCensus);
            suite.Section("De novo batch XIC - run _01 extracted whole, cached, and compared to PEAKS' areas");
            suite.Add(() => BatchExtractionRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir), DeNovoRun01Extraction);
            suite.Section("Isotope envelope engine (Q1) - configurable isotope count, unit invariants");
            suite.Add(() => IsotopeEnvelopeUnitTests());
            suite.Add(() => DependencyCacheTests(workDir));
            suite.Section("Minimum points per peak and the isotope ceiling - the rule, per isotope, and legacy mode");
            suite.Add(() => MinimumPointsAndCeilingUnitTests());
            suite.Section("Identified but not quantified - the accounting, the units and the closure");
            suite.AddWithApplication(() => UnderSampledAccountingTests(workDir));
            suite.Section("Identified-peptide envelope (Q1) - stored quants reproduced, full envelope measured");
            suite.Add(() => IdentifiedQuantRealTests(args.Length > 3 ? args[3] : null, workDir), DeNovoRun01Extraction);
            suite.Section("Project store (Q2) - .fqproj container round trip, migration and structure");
            suite.Add(() => ProjectFormatStageTests());
            suite.Add(() => ProjectStoreTests(workDir));
            suite.Add(() => ReferencePanelLegacyTests(Path.Combine(workDir, "reference-panel-legacy")));
            suite.Add(() => ReferencePanelSecondDatabaseTests(Path.Combine(workDir, "reference-panel-second")));
            suite.Add(() => GoldenNumberGuardTests());
            suite.Section("Talk export routing - one override root for heavy and fast lanes");
            suite.Add(() => TalkOutputRootTests(workDir));
            suite.Section("Full XIC compute (Q2) - the compute path stores the isotope envelope");
            suite.Add(() => FullXicComputeRealTests(args.Length > 3 ? args[3] : null, workDir));
            suite.Section("Full XIC run matching - a RAW named for no run is refused, never computed as zero rows");
            suite.Add(() => FullXicRunMatchTests(args.Length > 3 ? args[3] : null, workDir));
            suite.Section("Compute summary - the share of the TIC explained comes first, the diagnostic is named as such");
            suite.Add(() => TicShareSummaryTests());
            suite.Section("Native fraction of TIC (Q3) - per-peptide dedup, unit invariants and the method");
            suite.Add(() => NativeFractionUnitTests(workDir));
            suite.Add(() => ReferenceEnvelopeOptimizationTests());
            suite.Add(() => DeNovoEnvelopeCheckTests());
            suite.Add(() => DeNovoMeasuredEnvelopeTests(Path.Combine(workDir, "denovo-measured")));
            suite.Add(() => ResultsMethodGateTests());
            suite.Add(() => DeNovoSwitchFallbackTests(Path.Combine(workDir, "denovo-switch-fallback")));
            suite.Add(() => DeNovoRunWarningTests());
            suite.AddWithApplication(() => QuantifyIsotopeDefaultTests(Path.Combine(workDir, "quantify-isotope-default")));
            suite.Add(() => StreamingProvenanceHashTests());
            suite.Section("Native fraction of TIC (Q3) - run _01 identified/envelope/DDA+de novo, envelope partition");
            suite.Add(() => NativeTicFractionRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir));
            suite.Section("MS1 peptide-like envelope (Q6) - detector invariants on synthetic spectra");
            suite.Add(() => Ms1EnvelopeUnitTests());
            suite.Section("MS1 peptide-like envelope (Q6) - the charge split, strong and weak evidence apart");
            suite.AddWithApplication(() => Ms1ChargeSplitTests(workDir));
            suite.Section("MS1 peptide-like envelope (Q6) - run _01 three-level ordering TIC >= detected >= assigned");
            suite.Add(() => Ms1EnvelopeRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir));
            suite.Section("Analytics (Q4) - mass, isotopes credited, dedup parity, CSV round trip");
            suite.Add(() => AnalyticsUnitTests(workDir));
            suite.Section("Analytics (Q4) - the five charts render headless, populated and empty");
            suite.AddWithApplication(() => AnalyticsRenderTests(workDir));
            suite.Section("Family contribution chart - the fold, the order and the four inputs that must not crash");
            suite.AddWithApplication(() => FamilyContributionChartTests(workDir));
            suite.AddWithApplication(() => FamilyCountOnceTests(workDir));
            suite.AddWithApplication(() => ResultsPercentOnlyTests(workDir));
            suite.AddWithApplication(() => ResultsTopNPlacementTests(workDir));
            suite.Add(() => HtmlShareBasesTests());
            suite.Add(() => HtmlSharePartsTests(workDir));
            suite.AddWithApplication(() => HtmlFamilyNativeTests(Path.Combine(workDir, "html-family-native")));
            suite.AddWithApplication(() => SharedPolicyChoiceTests(workDir));
            suite.Section("Recovery chart geometry - per-row normalisation and labels that tell the runs apart");
            suite.AddWithApplication(() => RecoveryChartGeometryTests(workDir));
            suite.AddWithApplication(() => MeasuredRecoveryChartTests(Path.Combine(workDir, "measured-recovery-chart")));
            suite.Section("Chart palette - the light slide variant, and every chart drawn in both");
            suite.AddWithApplication(() => ChartPaletteTests(workDir));
            suite.Section("Figure export - every chart as a projector-sized PNG, with no window");
            suite.AddWithApplication(() => ChartImageExportTests(workDir));
            suite.Section("Composition, subclass parts and diversity from stored family studies");
            suite.AddWithApplication(() => CompositionChartTests(Path.Combine(workDir, "composition-charts")));
            suite.AddWithApplication(() => AbundanceDiversityTests(Path.Combine(workDir, "abundance-diversity")));
            suite.AddWithApplication(() => FamilyRingTests(Path.Combine(workDir, "family-ring")));
            suite.AddWithApplication(() => ProteinCoverageMapTests(Path.Combine(workDir, "protein-coverage-map")));
            suite.AddWithApplication(() => StoredCompositionTests(Path.Combine(workDir, "stored-composition-charts")));
            suite.AddWithApplication(() => CompositionExportCultureTests(Path.Combine(workDir, "composition-export-culture")));
            suite.Section("Automatic report captions and stored-study figure parity");
            suite.AddWithApplication(() => ReportCaptionTests(Path.Combine(workDir, "report-captions")));
            suite.AddWithApplication(() => CaptionSurfaceTests(Path.Combine(workDir, "caption-surfaces")));
            suite.AddWithApplication(() => ReportStudyFigureTests(Path.Combine(workDir, "report-study-figures")));
            suite.AddWithApplication(() => FigureLimitTests(Path.Combine(workDir, "figure-limits")));
            suite.Section("Analytics (Q4) - run _01 recovery reproduced from the store, CSV round trip");
            suite.Add(() => AnalyticsRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir));
            suite.Section("De novo homology - the kept peptides mapped to the venom families");
            suite.Add(() => HomologyRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir), DeNovoScreenCensus);
            suite.Section("Three-way partition - the arithmetic, the name matching and the drawing");
            suite.Add(() => ThreeWayPartitionTests(workDir));
            suite.Section("Three-way partition on the real data - TIC, PatternLab and de novo per run");
            suite.Add(() => ThreeWayPartitionRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir), DeNovoScreenCensus);
            suite.Section("Quantitation");
            suite.Add(() => QuantitationTests(args.Length > 0 ? args[0] : null));
            suite.Section("Recorded denominator - reading it instead of guessing it");
            suite.Add(() => RecordedDenominatorTests(workDir));
            suite.Section("Locale - the .plp must read identically under a comma-decimal culture");
            suite.Add(() => LocaleTests(args.Length > 0 ? args[0] : null));
            suite.Section("What the numbers are called - status line, counter and column headings");
            suite.Add(() => ReportViewTests());
            suite.Section("Embedded help - both pages answer the questions that were actually asked");
            suite.Add(() => HelpContentTests());
            suite.Add(() => DeNovoHelpContentTests());
            suite.Section("Curation locks at the interface - the edit handler, the summary and the manager");
            suite.AddWithApplication(() => CurationLockUiTests(workDir));
            suite.Section("Undo at the interface - the button tracks the stack and steps edits back");
            suite.AddWithApplication(() => UndoUiTests(workDir));
            suite.Section("Super Cluster preview - what will join, before anything changes");
            suite.Add(() => SuperClusterPreviewTests());
            suite.AddWithApplication(() => EvidenceReviewClarityTests());
            suite.AddWithApplication(() => EvidenceNewcomerTests());
            suite.AddWithApplication(() => EvidenceProteinDetailsTests());
            suite.AddWithApplication(() => CoherenceMembersUiTests());
            suite.Add(() => ManualFamilyChoiceTests());
            suite.Add(() => EvidenceFamilyReuseTests());
            suite.AddWithApplication(() => EvidenceUnlockIdentityTests(Path.Combine(workDir, "evidence-unlock-identity")));
            suite.AddWithApplication(() => PanelChecklistTests(Path.Combine(workDir, "panel-checklist")));
            suite.AddWithApplication(() => PanelChecklistActionsTests(Path.Combine(workDir, "panel-checklist-actions")));
            suite.Add(() => PanelEvidenceMatchingTests());
            suite.Add(() => PanelEvidenceRankingTests());
            suite.AddWithApplication(() => SheetPfamMatchTests(Path.Combine(workDir, "sheet-pfam-matches")));
            suite.Add(() => PanelEvidenceFingerprintTests());
            suite.AddWithApplication(() => PanelEvidenceBroadTests(Path.Combine(workDir, "panel-evidence-broad")));
            suite.AddWithApplication(() => EmptyPanelChecklistTests(Path.Combine(workDir, "empty-panel-checklist")));
            suite.AddWithApplication(() => PanelMatchLifecycleTests(Path.Combine(workDir, "panel-match-lifecycle")));
            suite.AddWithApplication(() => PanelEvidenceMatchUiTests(Path.Combine(workDir, "panel-evidence-match-ui")));
            suite.AddWithApplication(() => EvidenceDetailSelectionTests(Path.Combine(workDir, "evidence-detail-selection")));
            suite.Add(() => HandChoiceRecordTests());
            suite.AddWithApplication(() => ManualFamilyUiTests());
            suite.AddWithApplication(() => ImportWizardMockTests());
            suite.AddWithApplication(() => FamilyListImportTests());
            suite.AddWithApplication(() => ImportWalkLayoutTests(Path.Combine(workDir, "import-walk-layout")));
            suite.AddWithApplication(() => ImportPreparationTests());
            suite.AddWithApplication(() => DisabledTableTests());
            suite.AddWithApplication(() => ImportTooltipCopyTests());
            suite.AddWithApplication(() => WorkbookWizardTests());
            suite.AddWithApplication(() => WorkbookOverviewTests());
            suite.AddWithApplication(() => WorkbookSummaryPolishTests());
            suite.AddWithApplication(() => WorkbookSubclassReviewTests());
            suite.AddWithApplication(() => ImportWizardToolTipTests());
            suite.AddWithApplication(() => SuperClusterPreviewUiTests(workDir));
            suite.AddWithApplication(() => GridCopyTests());
            suite.AddWithApplication(() => GridCopyRowNumberTests());
            suite.AddWithApplication(() => ProteinDomainsTests(workDir));
            suite.Add(() => EvidenceStorageTests(workDir));
            suite.Add(() => DomainPositionTests(Path.Combine(workDir, "domain-positions")));
            suite.Add(() => PanelSpreadsheetTests(Path.Combine(workDir, "panel-spreadsheet")));
            suite.Add(() => PanelImportGracefulTests(Path.Combine(workDir, "panel-import-graceful")));
            suite.Add(() => PanelImportSeveralTests(Path.Combine(workDir, "panel-import-several")));
            suite.Add(() => PanelImportTemplateTests(Path.Combine(workDir, "panel-import-template")));
            suite.Add(() => PanelImportExampleTests(Path.Combine(workDir, "panel-import-example")));
            suite.Add(() => PanelWorkbookMapTests(Path.Combine(workDir, "panel-workbook-map")));
            suite.AddWithApplication(() => PanelWorkbookNotesTests());
            suite.Add(() => PanelWorkbookLegendTests(Path.Combine(workDir, "panel-workbook-legend")));
            suite.Add(() => PanelSubclassImportTests(Path.Combine(workDir, "panel-subclass-import")));
            suite.Add(() => WorkbookImportSummaryTests());



            suite.AddWithApplication(() => ToolTipStyleTests());
            suite.AddWithApplication(() => ToolTipCheckTests());
            suite.Add(() => FamilySensitivityTests(Path.Combine(workDir, "family-sensitivity")));
            suite.Section("Panel, threshold, template and report Help");
            suite.Add(() => PanelAuditTests());
            suite.Add(() => PanelAuditCurrentTests(Path.Combine(workDir, "panel-current-audit-" + Guid.NewGuid().ToString("N"))));
            suite.Add(() => ReportThresholdTests());
            suite.Add(() => ReportThresholdPolicyTests());
            suite.Add(() => PanelTemplateTests());
            suite.AddWithApplication(() => PanelEditorTests());
            suite.AddWithApplication(() => PanelEditorTemplateRoundtripTests());
            suite.Add(() => AnalysisHelpTests());
            suite.AddWithApplication(() => WorkflowReportHelpTests());
            suite.Add(() => AiKeyStoreTests());
            suite.Add(() => AiTransportTests());
            suite.AddWithApplication(() => AiKeySettingsTests());
            suite.Add(() => InterpretationNumberTraceTests());
            suite.Add(() => AiInterpretationDraftTests());
            suite.Add(() => AiInterpretationWorkflowTests());
            suite.AddWithApplication(() => AiInterpretationWindowsTests());
            suite.Add(() => AiInterpretationInputTests());
            suite.Add(() => AiKeyOfferTests());
            suite.AddWithApplication(() => AiShellTests());
            suite.Add(() => AiJ45ParityTests());
            suite.AddWithApplication(() => AiAssistantHelpTests());
            suite.Add(() => HelpAssistantTests());
            suite.AddWithApplication(() => AiHelpWindowTests());
            suite.Add(() => SuiteMergeGuardTests());
            suite.Add(() => EvidenceFetchTests());
            suite.Add(() => EvidenceFetchAtomicTests());
            suite.Add(() => UniProtRetryTests());
            suite.Add(() => EvidenceResumeTests(workDir));
            suite.Add(() => PanelRecoveryTests(workDir));
            suite.AddWithApplication(() => PanelRetryUiTests());
            suite.Add(() => InheritedRetryProgressTests());
            suite.Add(() => SameScanConflictTests());
            suite.AddWithApplication(() => PatternLabPriorityTests(workDir));
            suite.Add(() => ExampleFamilyListTests());
            suite.AddWithApplication(() => FamilyListImportGuideTests());
            suite.AddWithApplication(() => FamilyListImportHelpRouteTests());
            suite.AddWithApplication(() => AlignSelectedTests());
            suite.AddWithApplication(() => DeNovoWorkflowHelpTests());
            suite.Add(() => EvidenceCoverageTests());
            suite.Add(() => QueryResidueCoverageTests());
            suite.Add(() => FamilyWorldFetchTests());
            suite.Add(() => DesktopWorldChecks());
            suite.Add(() => EvidenceTaskTests());
            suite.Add(() => TaxonomyBatchTests(workDir));
            suite.Add(() => TaxaEntryCountTests());
            suite.AddWithApplication(() => TaskTaxaClipboardTests());
            suite.Add(() => KeywordCategoryTests());
            suite.AddWithApplication(() => KeywordCategoryFilterTests());
            suite.AddWithApplication(() => AddKeywordScopeTests());
            suite.Add(() => ScopeExpansionHelpTests());
            suite.AddWithApplication(() => ScopeReachabilityTests());
            suite.AddWithApplication(() => FamilyListStepTests());
            suite.AddWithApplication(() => SimpleGroupingTests());
            suite.AddWithApplication(() => ExportWindowTests(Path.Combine(workDir, "export-window")));
            suite.AddWithApplication(() => ScopeCountWordingTests());
            suite.AddWithApplication(() => ReviewedHeaderCountTests());
            suite.Add(() => OrganismEvidenceTests());
            suite.AddWithApplication(() => ScopeChangePreviewTests(Path.Combine(workDir, "scope-preview")));
            suite.Add(() => SmartFetchRetryTests());
            suite.AddWithApplication(() => DomainsFirstTests(Path.Combine(workDir, "domains-first")));
            suite.AddWithApplication(() => GroupingDiscoverySafetyTests());
            suite.AddWithApplication(() => ScopeVisibilityTests());
            suite.Add(() => PanelMultiScopeAuditTests());
            suite.AddWithApplication(() => PanelLinkNotesTests());
            suite.Add(() => EvidenceClassifierTests());
            suite.AddWithApplication(() => FamilyPanelSubclassTests());
            suite.Add(() => EvidenceDeliveryFixtureTests(workDir));
            suite.Add(() => EvidenceArchitectureTests());
            suite.Section("Nearest reviewed protein and the outside panel - never itself, gated, deterministic, offline");
            suite.Add(() => NearestReviewedTests(workDir));
            suite.Add(() => ReferencePanelTests(workDir));
            suite.Add(() => ReferencePanelStreamTests());
            suite.Add(() => ReferencePanelScopeTests(Path.Combine(workDir, "reference-panel-scope")));
            suite.AddWithApplication(() => ReferencePanelScopeRouteTests());
            suite.AddWithApplication(() => ReferencePanelScopeUiTests(Path.Combine(workDir, "reference-panel-scope-ui")));
            suite.Add(() => ReferencePanelExpansionTests(Path.Combine(workDir, "reference-panel-expansion")));
            suite.AddWithApplication(() => ReferencePanelExpansionUiTests(Path.Combine(workDir, "reference-panel-expansion-ui")));
            suite.AddWithApplication(() => ScopedSubclassFlowTests(Path.Combine(workDir, "scoped-subclass-flow")));
            suite.Add(() => EvidenceOperationTests());
            suite.Section("Database update - one-to-one matching of a project with a new database file");
            suite.Add(() => DatabaseUpdateMatchingTests());
            suite.Add(() => DatabaseUpdatePlacementTests());
            suite.Add(() => DatabaseUpdatePlacementSideTests());
            suite.Add(() => DatabaseUpdateApplyTests());
            suite.Add(() => DatabaseUpdateBindingTests());
            suite.AddWithApplication(() => DatabaseUpdateShellTests());
            suite.Add(() => DatabaseUpdateExportTests());
            suite.Section("Coherence check - what the reviewed members of a family ever show");
            suite.Add(() => CoherenceCheckTests());
            suite.Add(() => CoherenceSourceDetailsTests());
            suite.Add(() => E16RuleTests());
            suite.Section("Motif check - the user's patterns counted per family on complete entries");
            suite.Add(() => MotifCheckTests(workDir));
            suite.Section("Validation report - leave-one-out on reviewed proteins and the routes against their labels");
            suite.Add(() => ValidationReportTests());
            suite.AddWithApplication(() => EvidenceHelpTests());
            suite.Add(() => EvidenceScopeHelpTests());
            suite.Add(() => EvidenceOutsideScopeHelpTests());
            suite.Section("Left-out list - marker carriers kept out of the marker's family, with why");
            suite.Add(() => LeftOutListTests());
            suite.Add(() => EvidenceExportTests());
            suite.Section("Batch edit - one User ID and annotation for many selected rows");
            suite.Add(() => BatchCurationTests());
            suite.AddWithApplication(() => BatchCurationUiTests(workDir));
            suite.Section("Open project across screens - Save stays live on Home and Analytics, Curate returns to it");
            suite.AddWithApplication(() => OpenProjectNavigationTests(workDir));
            suite.Section("The title bar shows the installed (ClickOnce) version, all four parts");
            suite.AddWithApplication(() => AppVersionTests());
            suite.AddWithApplication(CrashHandlerTests);
            suite.Section("Tooltips wrap at a readable width instead of running off the screen");
            suite.AddWithApplication(() => TooltipWrapTests());
            suite.Section("Family names - the User Annotation, else the most common protein name");
            suite.Add(() => FamilyNamingTests());
            suite.Section("Family composition - shared peptides counted once; the Full XIC column, its options and export");
            suite.Add(() => FamilyCompositionTests());
            suite.Add(() => CurationHomologyCacheTests());
            suite.Add(() => FullXicStorageTests());
            suite.Add(() => QuantitationStageTests());
            suite.AddWithApplication(() => FullXicColumnUiTests(workDir));
            suite.Section("De Novo screen - import, filter, reconcile and the review queue");
            suite.AddWithApplication(() => DeNovoScreenTests(workDir));
            suite.Add(() => DeNovoScreenRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir), DeNovoScreenCensus);
            suite.Section("Identity sweep chart - it draws what it measured");
            suite.AddWithApplication(() => IdentityScanRenderTest(workDir));
            suite.Section("User interface - the controls actually build and respond");
            suite.Add(() => UiSmokeTests(args.Length > 0 ? args[0] : null, args.Length > 1 ? args[1] : null, workDir));
            suite.Section("User interface - every control explains itself, and nothing is clipped");
            suite.Add(() => UiExplanationTests(args.Length > 1 ? args[1] : null, workDir));
            suite.Section("Screen harness - complete capture matrix and exact viewport pixels");
            suite.Add(() => ScreenHarness.Verify(Check, workDir));
            suite.Section("Appearance directions - theme changes preserve curation and input surfaces");
            suite.Add(() => AppearanceDirectionTests());
            suite.Add(() => LegacySurfaceContrastTests());
            suite.Add(() => ShellStatusBridgeTests());
            suite.Add(() => LogoResourceTests(workDir));
            suite.Add(() => TypographyRulesTests());
            suite.Add(() => UiIconTests());
            suite.Add(() => UiColorTests());
            suite.Add(() => KeywordCommandTests());
            suite.Add(() => CurationScreenTests());
            suite.Add(() => CurationOperationTests());
            suite.AddWithApplication(() => CurationExportFeedbackTests(Path.Combine(workDir, "curation-export-feedback")));
            suite.AddWithApplication(() => ReportProgressTests(Path.Combine(workDir, "report-progress")));
            suite.AddWithApplication(() => ProjectCommandReadyTests(Path.Combine(workDir, "project-command-ready")));
            suite.AddWithApplication(() => ComputeProgressTests(Path.Combine(workDir, "compute-progress")));
            suite.Add(() => FamilyViewTests());
            suite.AddWithApplication(() => FamilyGridLayoutTests());
            suite.Add(() => TableAppearanceTests());
            suite.Add(() => EvidenceReviewModelTests());
            suite.Add(() => EvidenceCoverageUiTests(Path.Combine(workDir, "evidence-coverage-ui")));
            suite.Add(() => EvidenceTaskScopeTests());
            suite.Add(() => EvidenceTaskScopeRecipeTests());
            suite.Add(() => EvidenceTaskScopeWindowTests(Path.Combine(workDir, "task-scope")));
            suite.Add(() => GroupingReviewInteractionTests());
            suite.Add(() => EvidenceGroupingFlowTests());
            suite.Add(() => ReportStudyBoundaryTests());
            suite.Section("Word report and companion - source values, selection and exact native images");
            suite.Add(() => WordReportTests(null));
            suite.Add(() => ReportFindingsTests());
            suite.AddWithApplication(() => InterpretationInputTests());
            suite.Add(() => ReportNumberTraceTests());
            suite.Add(() => HtmlReportDataTests());
            suite.Add(() => HtmlReportTabsTests(Path.Combine(workDir, "html-report-tabs")));
            suite.AddWithApplication(() => HtmlReportFiguresTests(Path.Combine(workDir, "html-report-figures")));
            suite.Add(() => HtmlReportLegendTests(Path.Combine(workDir, "html-report-legends")));
            suite.Add(() => HtmlReportDenominatorTests(Path.Combine(workDir, "html-report-denominators")));
            suite.Add(() => HtmlReportNormalizationTests(Path.Combine(workDir, "html-report-normalization")));
            suite.AddWithApplication(() => HtmlCoverageCatalogTests(Path.Combine(workDir, "html-coverage-catalog")));
            suite.Add(() => HtmlRingBasesTests(Path.Combine(workDir, "html-ring-bases")));
            suite.AddWithApplication(() => HtmlSignalRecoveryTests(Path.Combine(workDir, "html-signal-recovery")));
            suite.AddWithApplication(() => HtmlMassQualityTests(Path.Combine(workDir, "html-mass-quality")));
            suite.AddWithApplication(() => HtmlRecoveryExplanationTests(Path.Combine(workDir, "html-recovery-explanations")));
            suite.Add(() => DatabaseEntryCountTests(null));
            suite.Add(() => AssignmentPresenceCopyTests(null));
            suite.Add(() => HtmlDatabaseEntryTests());
            suite.Add(() => DatabaseEntryTraceTests(null));
            suite.Add(() => HtmlDotDefaultsTests(Path.Combine(workDir, "html-dot-defaults")));
            suite.Add(() => SetupTests(Path.Combine(workDir, "setup")));
            suite.AddWithApplication(UpdateCompletionTests);
            suite.AddWithApplication(CloseProjectTests);
            suite.AddWithApplication(() => SetupWindowTests());
            suite.Add(() => HtmlCoverageTests());
            suite.Add(() => HtmlNumberTraceTests());
            suite.Add(() => HtmlStoredProofGuardTests());
            suite.AddWithApplication(() => ReportStoredChecks());
            suite.AddWithApplication(() => ReportAttributionCacheTests());
            suite.AddWithApplication(() => ReportSourceCensusCacheTests());
            suite.Add(() => ReportPairedSummaryTests());
            suite.AddWithApplication(() => ReportSupplementChecks(null));
            suite.Add(() => ReportTableSheetTests(null));
            suite.AddWithApplication(() => ReportNumericLayoutTests(Path.Combine(workDir, "report-numeric-layout")));
            suite.Add(() => ReportTablePaginationTests(Path.Combine(workDir, "report-table-pagination")));
            suite.AddWithApplication(() => HtmlReportExportTests(Path.Combine(workDir, "html-report-export")));
            suite.Add(() => ReportListSheetTests(null));
            suite.AddWithApplication(() => ReportCommandTests(new[] { "--test-report-command" }));
            suite.AddWithApplication(() => ReportRecoveryTests(new[] { "--test-report-recovery" }));
            suite.Add(FamilyUnobservedTests);
            suite.Add(() => EffectivePanelFamiliesTests(null));
            suite.Add(() => PanelHeadlineTests(null));
            suite.AddWithApplication(() => ReportFigureTests(Path.Combine(workDir, "report-figures")));
            suite.AddWithApplication(() => WordReportDraftTests());
            suite.AddWithApplication(() => ReportAssemblyChecks(Path.Combine(workDir, "report-assembly")));
            suite.AddWithApplication(() => ReportWordOthersChecks(Path.Combine(workDir, "report-word-others")));
            suite.AddWithApplication(() => ReportStaChecks(Path.Combine(workDir, "report-sta")));
            suite.AddWithApplication(() => DeliveryRehearsalChecks(Path.Combine(workDir, "delivery-rehearsal")));
            suite.Add(() => QuantitationPanelTests(Path.Combine(workDir, "quantitation-panel")));
            suite.Add(() => ArchitectureGroupSplitTests());
            suite.Add(() => ArchitectureMemberViewTests());
            suite.Add(() => EvidenceFetchCoordinatorTests());
            suite.Add(() => CurationSaveResultTests(workDir));
            suite.Add(() => ThemePreferenceTests(workDir));
            suite.Section("Feature ledger - frozen source inventory and explicit preservation checks");
            suite.Add(() => LedgerDiagnosticTests());
            suite.Add(() => { if (FeatureLedgerReady()) LedgerDrops2114Tests(); });
            suite.Add(() => CurationCsvBasisTests());
            suite.Add(() => FeatureLedgerBaseTests());
            for (int mutation = 0; mutation < FeatureLedgerMutations; mutation++)
            {
                int planted = mutation;
                suite.Add(() => { if (FeatureLedgerReady()) FeatureLedgerMutationTest(planted); });
            }
            suite.Add(() => { if (FeatureLedgerReady()) FeatureLedgerReceiptGuardTests(); });
            suite.Add(() => { if (FeatureLedgerReady()) FeatureLedgerReceiptFixtureTests(); });
            suite.Add(() => { if (FeatureLedgerReady()) LedgerImportBatchTests(); });
            suite.Add(() => LedgerAddedContractsTests(null));
            suite.Add(() => { if (FeatureLedgerReady()) ShellMetadataComparisonTests(); });
            suite.Add(() => { if (FeatureLedgerReady()) ShellMessageFrameTests(); });
            suite.Add(() => { if (FeatureLedgerReady()) FeatureLedgerLiveTests(); });
            suite.Section("Standard menu - shortcuts, save paths, curation routing and recent projects");
            suite.Add(() => StandardMenuTests(workDir));
            suite.Add(() => ShellStatusTests(workDir));
            suite.Add(() => HomeStartTests(workDir));
            suite.Add(() => ShellNavigationTests(workDir));
            suite.Add(() => WorkflowNameTests());
            suite.Add(() => AppCommandReuseTests(workDir));
            suite.Add(() => ShellProjectOperationTests(workDir));
            suite.Add(() => ShellSavingTests(workDir));
            suite.Add(() => ShellWindowPreferenceTests(workDir));
            suite.Add(() => BuildFamiliesScreenTests(workDir));
            suite.Add(() => QuantifyShellTests(workDir));
            suite.Add(() => SecondaryWindowTests(workDir));
            suite.Section("Additional core and preservation fixture guards");
            suite.Add(() => AssayBuildWorkflowGuardTests());
            suite.Add(() => AssayFigureGapGuardTests());
            suite.Add(() => AssaySpectrumPropertyTests());
            suite.Add(() => AssaySpectrumActionTests());
            suite.Add(() => AssayBuildPropertyTests());
            suite.Add(() => AssaySpectrumExportGuardTests());
            suite.AddWithApplication(() => AssaySpectrumRowIsolationTests(Array.Empty<string>(), workDir));
            suite.Add(() => LedgerDeclarationComparisonTests());
            suite.Add(() => LedgerMemberFactComparisonTests());
            suite.Add(() => ShellLedgerComparisonTests());
            suite.Add(() => LookRefreshCohortTests());
            suite.Add(() => WordingTsvRoundTripCommand(new[] { "--test-wording-tsv",
                Path.Combine(workDir, "wording-roundtrip-" + Guid.NewGuid().ToString("N")) }));
            suite.Add(() => ShellSeproFixtureCheck(new[] { "--test-shell-sepro-fixture",
                Path.Combine(workDir, "shell-sepro-" + Guid.NewGuid().ToString("N"), "fixture.sepr2") }, workDir));
            suite.Section("Panel contracts and native panel review fixtures");
            suite.AddWithApplication(() => FamilyPanelReportTests());
            suite.AddWithApplication(() => PanelEvidenceUiTests());
            suite.Add(() => PanelEvidenceValidationTests());
            suite.Add(() => PanelFamilyMapContractTests());
            suite.Add(() => PanelFamilyDependencyContractTests());
            suite.Section("Complete report command and document fixture packs");
            suite.AddWithApplication(() => RunReportDetailsTests());
            suite.AddWithApplication(() => RunReportInputTests());
            suite.Add(() => ReportPublicationTests());
            suite.AddWithApplication(() => ReportReaderTests(Path.Combine(workDir, "report-reader-" + Guid.NewGuid().ToString("N"))));
            suite.AddWithApplication(() => RunReportSelectionTests());
            suite.Add(() => ReportSupplementTests(new[] { "--test-report-supplement",
                Path.Combine(workDir, "report-workbook-" + Guid.NewGuid().ToString("N")) }));
            suite.AddWithApplication(() => J34dTests());
            suite.AddWithApplication(() => K16LayoutTests());
            suite.Section("Native fixture processes and UI lifetime");
            suite.Add(() => Check("native busy-logo fixture propagates its worker exit status",
                BusyLogoFixtureTests(Path.Combine(workDir, "busy-logo-" + Guid.NewGuid().ToString("N"))) == 0));
            suite.AddWithApplication(() => RunClusteringDiagnosticShellChecks(new[] { "--test-clustering-diagnostic-shell",
                Path.Combine(workDir, "dc-" + Path.GetRandomFileName()) }, workDir));
            suite.Add(() => Check("native feedback fixture propagates its worker exit status",
                FeedbackFactTests(new[] { "--test-feedback-facts",
                    Path.Combine(workDir, "feedback-facts-" + Guid.NewGuid().ToString("N")) }) == 0));
            suite.AddWithApplication(() => ShellEvidenceFetchCancellationTests());
            suite.AddWithApplication(() => ShellProofWindowChecks(Array.Empty<string>(), workDir));
            suite.AddWithApplication(() => TaskOrderRegression(new[] { "--test-task-order",
                Path.Combine(workDir, "task-order-" + Guid.NewGuid().ToString("N")) }));
            suite.Add(() => Check("native update fixture propagates its worker exit status",
                UpdateCheckCommand(new[] { "--test-update-check",
                    Path.Combine(workDir, "update-check-" + Guid.NewGuid().ToString("N")) }, workDir) == 0));
            return suite;
        }

        // ==== helpers

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("### " + title);
        }

        private static void Check(string name, bool condition, string? detail = null)
        {
            if (condition)
            {
                passed++;
                Console.WriteLine("  PASS  " + name);
            }
            else
            {
                failed++;
                Console.WriteLine("  FAIL  " + name + (detail != null ? "  (" + detail + ")" : ""));
            }
        }

        internal static bool Throws<T>(Action action, Action<string>? capture = null) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T ex)
            {
                capture?.Invoke(ex.Message);
                return true;
            }
            catch (Exception ex)
            {
                capture?.Invoke("wrong exception type: " + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }
    }
}
