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
            if (args.Contains("--test-keyword-command", StringComparer.Ordinal))
            {
                KeywordCommandTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Length > 0 && args[0] == "--evidence-task-check") return EvidenceTaskCheck(args);
            if (args.Contains("--test-evidence-task", StringComparer.Ordinal))
            {
                EvidenceTaskTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Length > 0 && args[0] == "--dependency-file-check")
                return DependencyFileCheck(args);
            if (args.Contains("--test-dependencies", StringComparer.Ordinal))
            {
                DependencyCacheTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests"));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Contains("--test-ui-icons", StringComparer.Ordinal))
            {
                UiIconTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Length == 2 && args[0] == "--busy-screens") return BusyScreens(args[1]);
            if (args.Contains("--test-shell-dialogs", StringComparer.Ordinal))
            {
                string dialogWork = Path.Combine(Path.GetTempPath(), "FamilyQuant.Dialog.Tests");
                Directory.CreateDirectory(dialogWork);
                EnsureApplication();
                if (args.Contains("--after-busy", StringComparer.Ordinal)) BusyOperationTests(dialogWork);
                ShellDialogTests(dialogWork);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Contains("--test-busy", StringComparer.Ordinal))
            {
                string busyWork = Path.Combine(Path.GetTempPath(), "FamilyQuant.Busy.Tests");
                Directory.CreateDirectory(busyWork);
                EnsureApplication();
                BusyOperationTests(busyWork);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Contains("--test-evidence-task-scope", StringComparer.Ordinal))
            {
                EnsureApplication();
                EvidenceTaskScopeTests();
                EvidenceTaskScopeRecipeTests();
                EvidenceTaskScopeWindowTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.TaskScope"));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Contains("--test-chart-themes", StringComparer.Ordinal))
            {
                string chartWork = args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.ChartThemes");
                ChartThemeTests(chartWork);
                UiChartPaletteTests(chartWork);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Contains("--test-ui-icons", StringComparer.Ordinal))
            {
                UiIconTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Length > 0 && args[0] == "--evidence-task-check") return EvidenceTaskCheck(args);
            if (args.Contains("--test-evidence-task", StringComparer.Ordinal))
            {
                EvidenceTaskTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Length > 0 && args[0] == IdentitySearchStudy.Argument) return IdentitySearchStudy.Run(args);
            if (args.Contains("--test-identity-study", StringComparer.Ordinal))
            {
                IdentityStudyTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Length > 0 && (args[0] == ClusteringSnapshot.Argument || args[0] == ClusteringSnapshot.ExportArgument || args[0] == ClusteringSnapshot.VerifyExportArgument))
                return ClusteringSnapshot.Run(args);
            if (args.Contains("--test-clustering-results", StringComparer.Ordinal))
            {
                string clusteringWorkDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
                ClusteringResultTests(clusteringWorkDir);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Length == 2 && args[0] == "--operation-native-wait")
            {
                File.WriteAllText(args[1] + ".tmp", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                File.Move(args[1] + ".tmp", args[1]);
                Thread.Sleep(TimeSpan.FromMinutes(2));
                return 0;
            }
            if (args.Contains("--test-operations", StringComparer.Ordinal))
            {
                string operationWorkDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
                CoreOperationTests(operationWorkDir);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Length > 0 && args[0] == WordGroupingSnapshot.Argument) return WordGroupingSnapshot.Run(args);
            if (args.Length > 0 && args[0] == ChartExportSnapshot.Argument) return ChartExportSnapshot.Run(args);
            if (args.Contains("--test-results", StringComparer.Ordinal))
            {
                ResultsViewModelTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Contains("--test-document", StringComparer.Ordinal))
            {
                string documentWorkDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
                Directory.CreateDirectory(documentWorkDir);
                RoundTripTests(documentWorkDir);
                ScoreTests();
                RecordedDenominatorTests(documentWorkDir);
                CuratedAttributionTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Contains("--test-core-boundary", StringComparer.Ordinal))
            {
                CoreBoundaryTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (LogoAssets.IsRequested(args)) return LogoAssets.Run(args);
            if (args.Contains("--test-inputs", StringComparer.Ordinal))
            {
                RealDataInputTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            // Screen capture is an isolated lane: it never runs scientific export commands.
            if (ScreenHarness.IsRequested(args)) return ScreenHarness.Run(args);
            if (args.Length >= 2 && args[0] == GroupingReviewScreens.Argument)
                return GroupingReviewScreens.Run(args[1], args.Length >= 3 ? args[2] : null);

            if (args.Length > 0 && args[0] == FeatureLedger.GenerateArgument)
            {
                if (args.Length != 3) throw new ArgumentException("Supply frozen source root and repository root.");
                return FeatureLedger.Generate(args[1], args[2]);
            }
            if (args.Length > 0 && args[0] == "--feature-ledger-check") return FeatureLedgerTests();
            if (args.Length > 0 && args[0] == "--feature-ledger-release-check") return FeatureLedgerTests(release: true);
            if (args.Length > 0 && args[0] == "--shell-checks")
            {
                string shellWork = Path.Combine(Path.GetTempPath(), "FamilyQuant.Shell.Tests");
                Directory.CreateDirectory(shellWork);
                EnsureApplication();
                StandardMenuTests(shellWork);
                OpenProjectNavigationTests(shellWork);
                DeNovoScreenNavigationTests();
                ShellStatusTests(shellWork);
                ShellStatusBridgeTests();
                HomeStartTests(shellWork);
                ShellNavigationTests(shellWork);
                AppCommandReuseTests(shellWork);
                ShellProjectOperationTests(shellWork);
                ShellSavingTests(shellWork);
                ShellWindowPreferenceTests(shellWork);
                ShellDialogTests(shellWork);
                BusyOperationTests(shellWork);
                BuildFamiliesScreenTests(shellWork);
                QuantifyShellTests(shellWork);
                SecondaryWindowTests(shellWork);
                LegacySurfaceContrastTests();
                AppVersionTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Length == 2 && args[0] == "--shell-menu-screens") return ShellMenuScreens(args[1]);
            if (args.Length == 2 && args[0] == "--start-page-screens") return HomeStartScreens(args[1]);
            if (args.Length == 3 && args[0] == "--shell-save-smoke") return ShellSaveSmoke(args[1], args[2]);
            if (args.Length == 2 && args[0] == "--evidence-recipe-replay-check") return EvidenceRecipeReplayCheck(args[1]);
            if (args.Length == 2 && args[0] == "--shell-save-screens") return ShellSaveScreens(args[1]);
            if (args.Length == 2 && args[0] == "--quantify-screens") return QuantifyScreens(args[1]);
            if (args.Length is 2 or 3 && args[0] == "--results-screens") return ResultsScreens(args[1], args.Length == 3 ? args[2] : null);
            if (args.Length == 2 && args[0] == "--shell-window-screens") return ShellWindowPreferenceScreens(args[1]);
            if (args.Length == 2 && args[0] == "--denovo-screens") return DeNovoScreens(args[1]);
            if (args.Length == 2 && args[0] == "--homology-results-screens") return HomologyResultScreens(args[1]);
            if (args.Length == 2 && args[0] == "--build-family-screens") return BuildFamiliesScreens(args[1]);
            if (args.Length == 2 && args[0] == "--quantify-shell-screens") return QuantifyShellScreens(args[1]);
            if (args.Length == 2 && args[0] == "--secondary-screens") return SecondaryWindowScreens(args[1]);

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
            Section("CSV export - locale and escaping");
            CommandRegistryTests();
            CoreReuseContractTests();
            SharedTestHelperContracts(workDir);
            CsvTests();
            Section("Core operation outcomes, progress and cancellation");
            CoreOperationTests(workDir);
            Section("Display filters and the family counter");
            FilterTests();
            Section("Target/decoy scoring arithmetic");
            ScoreTests();
            Section("Target/decoy mixing - which families mix, and why Divide and Conquer stopped where it did");
            MixedFamilyTests(workDir);
            Section("Identity scan - argument validation");
            ScanValidationTests();
            Section("Family Cluster File round trip");
            RoundTripTests(workDir);
            Section("Word-based reclustering invariants");
            WordClusterTests();
            QuantifyViewModelTests(workDir);
            BusyOperationTests(workDir);
            ShellDialogTests(workDir);
            ResultsViewModelTests();
            DeNovoViewModelTests(workDir);
            HomologyResultsTests(workDir);
            Section("Database parsing and the decoy tag");
            DatabaseTests(workDir);
            Section("Exhaustive clustering - the second pass closes the gaps the fast pass leaves");
            ExhaustiveGateTests();
            ExhaustiveMemoTests(workDir);
            ExhaustiveRealDataTests(args.Length > 3 ? args[3] : null);
            Section("Redundancy removal - order kept, subsets dropped");
            RedundancyTests(workDir);
            ClusteringResultTests(workDir);
            IdentityStudyTests();
            Section("De novo evidence - source-aware reader, filters, reconciliation");
            DeNovoTests(workDir);
            DeNovoAgreementUnitTests();
            IdentificationConcordanceUnitTests();
            DeNovoRealExportTests(args.Length > 2 ? args[2] : null);
            Section("De novo source provenance - separate score semantics, deterministic union and project round trip");
            SourceAwareDeNovoTests.Run(Check, workDir);
            Section("Isotope-aware precursor equivalence - the JOIN/matching paths pair M, M+-1, M+-2 of one precursor");
            IsotopePrecursorEquivalenceTests.Run(Check);
            Section("De novo XIC - stored quants reproduced bit for bit from the RAW");
            int deNovoXicBefore = passed + failed;
            DeNovoXicTests.Run(Check, workDir, args.Length > 3 ? args[3] : null);
            if (passed + failed == deNovoXicBefore)
                skipped++;
            Section("Homology - PepExplorer's aligner, ported with its C-terminus and character defects fixed");
            HomologyTests.Run(Check);
            Section("De novo reconciliation against the real .sepr2 - the request's items 3 to 5");
            ReconciliationRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);
            Section("De novo batch XIC - run _01 extracted whole, cached, and compared to PEAKS' areas");
            BatchExtractionRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);
            Section("Isotope envelope engine (Q1) - configurable isotope count, unit invariants");
            IsotopeEnvelopeUnitTests();
            DependencyCacheTests(workDir);
            Section("Minimum points per peak and the isotope ceiling - the rule, per isotope, and legacy mode");
            MinimumPointsAndCeilingUnitTests();
            Section("Identified but not quantified - the accounting, the units and the closure");
            EnsureApplication();
            UnderSampledAccountingTests(workDir);
            Section("Identified-peptide envelope (Q1) - stored quants reproduced, full envelope measured");
            IdentifiedQuantRealTests(args.Length > 3 ? args[3] : null, workDir);
            Section("Project store (Q2) - .fqproj container round trip, migration and structure");
            ProjectStoreTests(workDir);
            GoldenNumberGuardTests();
            Section("Talk export routing - one override root for heavy and fast lanes");
            TalkOutputRootTests(workDir);
            Section("Full XIC compute (Q2) - the compute path stores the isotope envelope");
            FullXicComputeRealTests(args.Length > 3 ? args[3] : null, workDir);
            Section("Full XIC run matching - a RAW named for no run is refused, never computed as zero rows");
            FullXicRunMatchTests(args.Length > 3 ? args[3] : null, workDir);
            Section("Compute summary - the share of the TIC explained comes first, the diagnostic is named as such");
            TicShareSummaryTests();
            Section("Native fraction of TIC (Q3) - per-peptide dedup, unit invariants and the method");
            NativeFractionUnitTests(workDir);
            Section("Native fraction of TIC (Q3) - run _01 identified/envelope/DDA+de novo, envelope partition");
            NativeTicFractionRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);
            Section("MS1 peptide-like envelope (Q6) - detector invariants on synthetic spectra");
            Ms1EnvelopeUnitTests();
            Section("MS1 peptide-like envelope (Q6) - the charge split, strong and weak evidence apart");
            EnsureApplication();
            Ms1ChargeSplitTests(workDir);
            Section("MS1 peptide-like envelope (Q6) - run _01 three-level ordering TIC >= detected >= assigned");
            Ms1EnvelopeRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);
            Section("Analytics (Q4) - mass, isotopes credited, dedup parity, CSV round trip");
            AnalyticsUnitTests(workDir);
            Section("Analytics (Q4) - the five charts render headless, populated and empty");
            EnsureApplication();
            AnalyticsRenderTests(workDir);
            Section("Family contribution chart - the fold, the order and the four inputs that must not crash");
            EnsureApplication();
            FamilyContributionChartTests(workDir);
            Section("Recovery chart geometry - per-row normalisation and labels that tell the runs apart");
            EnsureApplication();
            RecoveryChartGeometryTests(workDir);
            Section("Chart palette - the light slide variant, and every chart drawn in both");
            EnsureApplication();
            ChartPaletteTests(workDir);
            Section("Figure export - every chart as a projector-sized PNG, with no window");
            EnsureApplication();
            ChartImageExportTests(workDir);
            Section("Analytics (Q4) - run _01 recovery reproduced from the store, CSV round trip");
            AnalyticsRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);
            Section("De novo homology - the kept peptides mapped to the venom families");
            HomologyRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);
            Section("Three-way partition - the arithmetic, the name matching and the drawing");
            ThreeWayPartitionTests(workDir);
            Section("Three-way partition on the real data - TIC, PatternLab and de novo per run");
            ThreeWayPartitionRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);
            Section("Quantitation");
            QuantitationTests(args.Length > 0 ? args[0] : null);
            Section("Recorded denominator - reading it instead of guessing it");
            RecordedDenominatorTests(workDir);
            Section("Locale - the .plp must read identically under a comma-decimal culture");
            LocaleTests(args.Length > 0 ? args[0] : null);
            Section("What the numbers are called - status line, counter and column headings");
            ReportViewTests();
            Section("Embedded help - both pages answer the questions that were actually asked");
            HelpContentTests();
            DeNovoHelpContentTests();
            Section("Curation locks at the interface - the edit handler, the summary and the manager");
            EnsureApplication();
            CurationLockUiTests(workDir);
            Section("Undo at the interface - the button tracks the stack and steps edits back");
            EnsureApplication();
            UndoUiTests(workDir);
            Section("Super Cluster preview - what will join, before anything changes");
            SuperClusterPreviewTests();
            EnsureApplication();
            SuperClusterPreviewUiTests(workDir);
            EnsureApplication();
            GridCopyTests();
            EnsureApplication();
            GridCopyRowNumberTests();
            EnsureApplication();
            ProteinDomainsTests(workDir);
            EvidenceStorageTests(workDir);
            EvidenceFetchTests();
            EvidenceTaskTests();
            EvidenceClassifierTests();
            EvidenceArchitectureTests();
            Section("Nearest reviewed protein and the outside panel - never itself, gated, deterministic, offline");
            NearestReviewedTests(workDir);
            ReferencePanelTests(workDir);
            EvidenceOperationTests();
            Section("Coherence check - what the reviewed members of a family ever show");
            CoherenceCheckTests();
            E16RuleTests();
            Section("Motif check - the user's patterns counted per family on complete entries");
            MotifCheckTests(workDir);
            Section("Validation report - leave-one-out on reviewed proteins and the routes against their labels");
            ValidationReportTests();
            EnsureApplication();
            EvidenceHelpTests();
            Section("Left-out list - marker carriers kept out of the marker's family, with why");
            LeftOutListTests();
            EvidenceExportTests();
            Section("Batch edit - one User ID and annotation for many selected rows");
            BatchCurationTests();
            EnsureApplication();
            BatchCurationUiTests(workDir);
            Section("Open project across screens - Save stays live on Home and Analytics, Curate returns to it");
            EnsureApplication();
            OpenProjectNavigationTests(workDir);
            Section("The title bar shows the installed (ClickOnce) version, all four parts");
            EnsureApplication();
            AppVersionTests();
            Section("Tooltips wrap at a readable width instead of running off the screen");
            EnsureApplication();
            TooltipWrapTests();
            Section("Family names - the User Annotation, else the most common protein name");
            FamilyNamingTests();
            Section("Family composition - shared peptides counted once; the Full XIC column, its options and export");
            FamilyCompositionTests();
            EnsureApplication();
            FullXicColumnUiTests(workDir);
            Section("De Novo screen - import, filter, reconcile and the review queue");
            EnsureApplication();
            DeNovoScreenTests(workDir);
            DeNovoScreenRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);
            Section("Identity sweep chart - it draws what it measured");
            EnsureApplication();
            IdentityScanRenderTest(workDir);
            Section("User interface - the controls actually build and respond");
            UiSmokeTests(args.Length > 0 ? args[0] : null, args.Length > 1 ? args[1] : null, workDir);
            Section("User interface - every control explains itself, and nothing is clipped");
            UiExplanationTests(args.Length > 1 ? args[1] : null, workDir);
            Section("Screen harness - complete capture matrix and exact viewport pixels");
            ScreenHarness.Verify(Check, workDir);
            Section("Appearance directions - theme changes preserve curation and input surfaces");
            AppearanceDirectionTests();
            LegacySurfaceContrastTests();
            ShellStatusBridgeTests();
            LogoResourceTests(workDir);
            TypographyRulesTests();
            UiIconTests();
            KeywordCommandTests();
            TableAppearanceTests();
            EvidenceReviewModelTests();
            EvidenceTaskScopeTests();
            EvidenceTaskScopeRecipeTests();
            EvidenceTaskScopeWindowTests(Path.Combine(workDir, "task-scope"));
            GroupingReviewInteractionTests();
            EvidenceGroupingFlowTests();
            ArchitectureMemberViewTests();
            EvidenceFetchCoordinatorTests();
            CurationSaveResultTests(workDir);
            ThemePreferenceTests(workDir);
            Section("Feature ledger - frozen source inventory and explicit preservation checks");
            FeatureLedgerTests();
            Section("Standard menu - shortcuts, save paths, curation routing and recent projects");
            StandardMenuTests(workDir);
            ShellStatusTests(workDir);
            HomeStartTests(workDir);
            ShellNavigationTests(workDir);
            AppCommandReuseTests(workDir);
            ShellProjectOperationTests(workDir);
            ShellSavingTests(workDir);
            ShellWindowPreferenceTests(workDir);
            BuildFamiliesScreenTests(workDir);
            QuantifyShellTests(workDir);
            SecondaryWindowTests(workDir);
            Console.WriteLine();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }

        // ============================================================ helpers

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
