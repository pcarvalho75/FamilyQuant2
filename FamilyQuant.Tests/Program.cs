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
    ///   FamilyQuant.Tests.exe [pathToAPlpFile] [pathToAFastaOrTR]
    ///
    /// Everything except the last two sections builds its own fixtures, so the suite runs anywhere.
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

        [STAThread]   // required: the UI smoke section constructs WPF controls
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == WordGroupingSnapshot.Argument) return WordGroupingSnapshot.Run(args);
            if (args.Contains("--test-core-boundary", StringComparer.Ordinal))
            {
                CoreBoundaryTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Contains("--test-inputs", StringComparer.Ordinal))
            {
                RealDataInputTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            // Screen capture is an isolated lane: it never runs scientific export commands.
            if (ScreenHarness.IsRequested(args)) return ScreenHarness.Run(args);

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
                ShellProjectOperationTests(shellWork);
                AppVersionTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }
            if (args.Length == 2 && args[0] == "--shell-menu-screens") return ShellMenuScreens(args[1]);
            if (args.Length == 2 && args[0] == "--start-page-screens") return HomeStartScreens(args[1]);

            // PowerShell 5.1 silently DROPS empty "" arguments to native executables, which shifted
            // the data directories into the wrong positions and turned every real-data section into a
            // silent skip. A dot is the positional placeholder that survives every shell; it maps to
            // "this argument was not given".
            for (int i = 0; i < args.Length; i++) if (args[i] == ".") args[i] = "";

            string workDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
            Directory.CreateDirectory(workDir);

            if (args.Contains(EvidenceFetchCheck.Argument) || args.Contains(EvidenceFetchCheck.VerifyArgument))
            {
                EvidenceFetchCheck.Run(args, Check);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            if (args.Contains(EvidenceStorageCheck.Argument))
            {
                EvidenceStorageCheck.Run(args, Check);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            if (GoldenNumbers.IsRequested(args))
            {
                EnsureApplication();
                GoldenNumbers.Run(args, Check);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            // The conference-talk deliverable, GATED on its own flag and on nothing else, so a normal run
            // (with or without the four data paths) never reaches it. It is an OWN-LANE runner rather than
            // one more section: it re-extracts three 3 GB acquisitions at the 6-isotope ceiling, which is
            // tens of minutes, and it has no business riding along on a suite run. The flag is scanned
            // anywhere on the command line so it cannot disturb the four positional data paths.
            if (args.Any(a => string.Equals(a, TalkFigures.Argument, StringComparison.OrdinalIgnoreCase)))
            {
                Section("BrProt talk - the complete native plot pack and numbers, from the real data (" +
                        TalkFigures.Argument + ")");
                EnsureApplication();
                TalkFigures.Run(Check, PngCornerColour, PngPixelSize,
                    args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);

                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            // Focused real-data lane for the requested spectrum-evidence table. It runs only the exact
            // PEAKS de-novo-only + DeepNovo union and therefore reuses that variant's warm XIC/MS1 caches
            // without computing the other four presentation variants. The CSV is deliberately written
            // beside the user's input data as well as into the evidence pack/project.
            if (args.Any(a => string.Equals(a, TalkFigures.OnlyPlusDeepNovoArgument,
                StringComparison.OrdinalIgnoreCase)))
            {
                Section("BrProt talk - PEAKS de-novo-only + DeepNovo spectrum evidence (" +
                        TalkFigures.OnlyPlusDeepNovoArgument + ")");
                EnsureApplication();
                string? desktopDir = args.Length > 3 ? args[3] : null;
                TalkFigures.RunSourceVenn(Check, PngPixelSize,
                    args.Length > 2 ? args[2] : null, desktopDir);
                string? csvPath = string.IsNullOrWhiteSpace(desktopDir) ? null : Path.Combine(
                    desktopDir, SpectrumComparisonTable.DefaultFileName);
                TalkFigures.Run(Check, PngCornerColour, PngPixelSize,
                    args.Length > 2 ? args[2] : null, desktopDir, workDir,
                    deNovoSource: DeNovoSource.DenovoOnly,
                    additionalDeNovoSources: new[] { DeNovoSource.DeepNovo },
                    variantFolderName: DeNovoVariantComparison.OnlyPlusDeepNovoVariantName,
                    comparisonGenerationId: Guid.NewGuid().ToString("N"),
                    spectrumComparisonCsvPath: csvPath);
                Check("focused spectrum-evidence CSV was written beside the data",
                    !string.IsNullOrWhiteSpace(csvPath) && File.Exists(csvPath), csvPath);

                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            if (args.Any(a => string.Equals(a, TalkFigures.SourceVennArgument,
                StringComparison.OrdinalIgnoreCase)))
            {
                Section("Exact PEAKS de-novo-only / DeepNovo source-set evidence (" +
                    TalkFigures.SourceVennArgument + ")");
                EnsureApplication();
                TalkFigures.RunSourceVenn(Check, PngPixelSize,
                    args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);
                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            // The FIVE de novo variants of the talk in one pass, GATED on its own flag. It loops
            // TalkFigures.Variants and calls the SAME Run once per variant, each into its own subfolder of
            // BrProt_Talk (the "de novo only" variant reproduces the baseline content). It does NOT touch
            // the --talk-figures default: that flag still writes the baseline straight into BrProt_Talk.
            // Like --talk-figures it re-extracts three acquisitions per variant, so it is heavy.
            if (args.Any(a => string.Equals(a, TalkFigures.VariantsArgument, StringComparison.OrdinalIgnoreCase)))
            {
                Section("BrProt talk - all five de novo variants, each into its own subfolder (" +
                        TalkFigures.VariantsArgument + ")");
                EnsureApplication();
                string comparisonGenerationId = Guid.NewGuid().ToString("N");
                foreach (TalkFigures.Variant variant in TalkFigures.Variants)
                {
                    Console.WriteLine();
                    Console.WriteLine("===== variant: " + variant.Primary.DisplayName +
                        (variant.Additional != null && variant.Additional.Count > 0
                            ? " + " + string.Join(" + ", variant.Additional.Select(s => s.DisplayName))
                            : string.Empty) +
                        "  ->  " + TalkFigures.TalkFolderName + "\\" + variant.FolderName + " =====");
                    TalkFigures.Run(Check, PngCornerColour, PngPixelSize,
                        args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir,
                        deNovoSource: variant.Primary,
                        additionalDeNovoSources: variant.Additional,
                        variantFolderName: variant.FolderName,
                        comparisonGenerationId: comparisonGenerationId);
                }
                TalkFigures.WriteVariantComparison(Check, PngPixelSize,
                    args.Length > 3 ? args[3] : null, comparisonGenerationId);

                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            // The score-sweep-with-overlap figure, GATED on its own flag. Unlike the talk figures this
            // is FAST: pure sequence bookkeeping over the CSVs, no XIC, no RAW. Positional argument 3 is
            // the de novo base directory the three sources live under.
            if (args.Any(a => string.Equals(a, TalkFigures.SweepArgument, StringComparison.OrdinalIgnoreCase)))
            {
                Section("De novo score sweep with de-novo-only overlap, from the CSVs alone (" +
                        TalkFigures.SweepArgument + ")");
                EnsureApplication();
                TalkFigures.RunScoreSweep(Check, PngPixelSize,
                    args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);

                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            // The PEAKS-vs-DeepNovo per-spectrum agreement bubble figure, GATED on its own flag. Like the
            // score sweep it is FAST: pure sequence bookkeeping over the CSVs, no XIC, no RAW. Positional
            // argument 3 is the de novo base directory the three sources live under.
            if (args.Any(a => string.Equals(a, TalkFigures.AgreementArgument, StringComparison.OrdinalIgnoreCase)))
            {
                Section("PEAKS de novo vs DeepNovo per-spectrum agreement, from the CSVs alone (" +
                        TalkFigures.AgreementArgument + ")");
                EnsureApplication();
                TalkFigures.RunAgreement(Check, PngPixelSize,
                    args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);

                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            // Fast, project-native accepted-call score/match scatter. Positional argument 3 is the
            // scientific .fqproj; positional argument 4 is the output folder.
            if (args.Any(a => string.Equals(a, TalkFigures.AcceptedScoreScatterArgument,
                StringComparison.OrdinalIgnoreCase)))
            {
                Section("Accepted PEAKS-vs-DeepNovo score and strict sequence agreement (" +
                        TalkFigures.AcceptedScoreScatterArgument + ")");
                EnsureApplication();
                TalkFigures.RunAcceptedScoreScatter(Check, PngPixelSize,
                    args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);

                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            // The score-vs-score dot plot, GATED on its own flag. Like the sweep and the agreement figure
            // it is FAST: pure sequence bookkeeping over the CSVs, no XIC, no RAW. Positional argument 3 is
            // the de novo base directory the three sources live under.
            if (args.Any(a => string.Equals(a, TalkFigures.ScatterArgument, StringComparison.OrdinalIgnoreCase)))
            {
                Section("De novo score-vs-score dot plot with marginal histograms, from the CSVs alone (" +
                        TalkFigures.ScatterArgument + ")");
                EnsureApplication();
                TalkFigures.RunScoreScatter(Check, PngPixelSize,
                    args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);

                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            // Identified-peptide family attribution, every-past-gate vs best-score, GATED on its own flag.
            // Positional argument 3 is the .fqproj, 4 an optional separate .fcf ("." for none), 5 the
            // output folder. A homology search per curation, no RAW.
            if (args.Any(a => string.Equals(a, AttributionRuleStudy.Argument, StringComparison.OrdinalIgnoreCase)))
            {
                Section("Identified-peptide family attribution rule study (" + AttributionRuleStudy.Argument + ")");
                AttributionRuleStudy.Run(Check, args.Length > 2 ? args[2] : null,
                    args.Length > 3 ? args[3] : null, args.Length > 4 ? args[4] : null);

                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            // The PEAKS de novo ALC score histogram, GATED on its own flag. FAST: pure per-spectrum census
            // over the CSVs, no XIC, no RAW. Positional argument 3 is the de novo base directory; positional
            // argument 4 is the output folder.
            if (args.Any(a => string.Equals(a, TalkFigures.ScoreDistributionArgument,
                StringComparison.OrdinalIgnoreCase)))
            {
                Section("PEAKS de novo ALC score histogram, from the CSVs alone (" +
                        TalkFigures.ScoreDistributionArgument + ")");
                EnsureApplication();
                TalkFigures.RunScoreDistribution(Check, PngPixelSize,
                    args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);

                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            // The UpSet-style set-novelty view, GATED on its own flag. FAST: pure sequence bookkeeping over
            // the CSVs, no XIC, no RAW. Positional argument 3 is the de novo base directory.
            if (args.Any(a => string.Equals(a, TalkFigures.SetNoveltyArgument, StringComparison.OrdinalIgnoreCase)))
            {
                Section("De novo set-novelty (UpSet) view, from the CSVs alone (" +
                        TalkFigures.SetNoveltyArgument + ")");
                EnsureApplication();
                TalkFigures.RunSetNovelty(Check, PngPixelSize,
                    args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);

                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            if (args.Any(a => string.Equals(a, "--overlap-study", StringComparison.OrdinalIgnoreCase)))
            {
                Section("De novo/search-engine overlap study from the real data");
                ReconciliationRealTests(args.Length > 2 ? args[2] : null,
                                        args.Length > 3 ? args[3] : null, workDir);
                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            if (args.Any(a => string.Equals(a, ShortPsmPeaksTable.Argument, StringComparison.OrdinalIgnoreCase)))
            {
                Section("Ten highest-scoring short PatternLab PSMs with PEAKS calls on the exact same file and scan");
                try
                {
                    string path = ShortPsmPeaksTable.Run(args.Length > 2 ? args[2] : string.Empty,
                                                        args.Length > 3 ? args[3] : string.Empty);
                    Check("the short-PSM/PEAKS table was written", File.Exists(path), path);
                }
                catch (Exception ex)
                {
                    Check("the short-PSM/PEAKS table was written", false, ex.Message);
                }
                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            if (args.Any(a => string.Equals(a, ChargeAudit.Argument, StringComparison.OrdinalIgnoreCase)))
            {
                Section("RAW-recorded charge audit for PatternLab PSMs and filtered PEAKS de novo calls");
                try
                {
                    ChargeAudit.Run(args.Length > 2 ? args[2] : string.Empty,
                                    args.Length > 3 ? args[3] : string.Empty);
                    Check("the full-dataset RAW charge audit completed", true);
                }
                catch (Exception ex) { Check("the full-dataset RAW charge audit completed", false, ex.ToString()); }
                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            if (args.Any(a => string.Equals(a, ThreeWayAudit.Argument, StringComparison.OrdinalIgnoreCase)))
            {
                Section("PatternLab / PEAKS database / PEAKS de novo three-way identification audit");
                try
                {
                    ThreeWayAudit.Run(args.Length > 2 ? args[2] : string.Empty,
                                      args.Length > 3 ? args[3] : string.Empty);
                    Check("the full-dataset three-way identification audit completed", true);
                }
                catch (Exception ex) { Check("the full-dataset three-way identification audit completed", false, ex.ToString()); }
                Console.WriteLine();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }

            Section("CSV export - locale and escaping");
            CsvTests();

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

            Section("Database parsing and the decoy tag");
            DatabaseTests(workDir);

            Section("Exhaustive clustering - the second pass closes the gaps the fast pass leaves");
            ExhaustiveGateTests();
            ExhaustiveMemoTests(workDir);
            ExhaustiveRealDataTests(args.Length > 3 ? args[3] : null);

            Section("Redundancy removal - order kept, subsets dropped");
            RedundancyTests(workDir);

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
            if (passed + failed == deNovoXicBefore) skipped++;

            Section("Homology - PepExplorer's aligner, ported with its C-terminus and character defects fixed");
            HomologyTests.Run(Check);

            Section("De novo reconciliation against the real .sepr2 - the request's items 3 to 5");
            ReconciliationRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);

            Section("De novo batch XIC - run _01 extracted whole, cached, and compared to PEAKS' areas");
            BatchExtractionRealTests(args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null, workDir);

            Section("Isotope envelope engine (Q1) - configurable isotope count, unit invariants");
            IsotopeEnvelopeUnitTests();

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

            Section("Nearest reviewed protein - never itself, gated, deterministic and memoised");
            NearestReviewedTests(workDir);

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
            ThemePreferenceTests(workDir);

            Section("Feature ledger - frozen source inventory and explicit preservation checks");
            FeatureLedgerTests();

            Section("Standard menu - shortcuts, save paths, curation routing and recent projects");
            StandardMenuTests(workDir);
            ShellStatusTests(workDir);
            HomeStartTests(workDir);
            ShellNavigationTests(workDir);
            ShellProjectOperationTests(workDir);

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

        private static bool Throws<T>(Action action, Action<string>? capture = null) where T : Exception
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
