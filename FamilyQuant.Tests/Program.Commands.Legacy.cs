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

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LegacyCommandTable() =>
    [
        new("--test-keyword-command", 50, TestCommandPhase.Raw, MatchesCommand003, RunCommand003),
        new("--test-ui-icons", 400, TestCommandPhase.Raw, MatchesCommand004, RunCommand004),
        new(IdentitySearchStudy.Argument, 500, TestCommandPhase.Raw, MatchesCommand005, RunCommand005),
        new("--test-identity-study", 600, TestCommandPhase.Raw, MatchesCommand006, RunCommand006),
        new(ClusteringSnapshot.Argument, 700, TestCommandPhase.Raw, MatchesCommand007, RunCommand007),
        new(ClusteringSnapshot.ExportArgument, 701, TestCommandPhase.Raw, MatchesCommand007, RunCommand007),
        new(ClusteringSnapshot.VerifyExportArgument, 702, TestCommandPhase.Raw, MatchesCommand007, RunCommand007),
        new("--test-clustering-results", 800, TestCommandPhase.Raw, MatchesCommand008, RunCommand008),
        new("--operation-native-wait", 900, TestCommandPhase.Raw, MatchesCommand009, RunCommand009),
        new("--test-operations", 1000, TestCommandPhase.Raw, MatchesCommand010, RunCommand010),
        new(WordGroupingSnapshot.Argument, 1100, TestCommandPhase.Raw, MatchesCommand011, RunCommand011),
        new(ChartExportSnapshot.Argument, 1200, TestCommandPhase.Raw, MatchesCommand012, RunCommand012),
        new("--test-results", 1300, TestCommandPhase.Raw, MatchesCommand013, RunCommand013),
        new("--test-document", 1400, TestCommandPhase.Raw, MatchesCommand014, RunCommand014),
        new("--test-core-boundary", 1500, TestCommandPhase.Raw, MatchesCommand015, RunCommand015),
        new(LogoAssets.Argument, 1600, TestCommandPhase.Raw, MatchesCommand016, RunCommand016),
        new("--test-inputs", 1700, TestCommandPhase.Raw, MatchesCommand017, RunCommand017),
        new(ScreenHarness.Argument, 1800, TestCommandPhase.Raw, MatchesCommand018, RunCommand018),
        new(GroupingReviewScreens.Argument, 1900, TestCommandPhase.Raw, MatchesCommand019, RunCommand019),
        new(FeatureLedger.GenerateArgument, 2000, TestCommandPhase.Raw, MatchesCommand020, RunCommand020),
        new("--feature-ledger-check", 2100, TestCommandPhase.Raw, MatchesCommand021, RunCommand021),
        new("--feature-ledger-release-check", 2200, TestCommandPhase.Raw, MatchesCommand022, RunCommand022),
        new("--shell-checks", 2300, TestCommandPhase.Raw, MatchesCommand023, RunCommand023),
        new("--shell-menu-screens", 2400, TestCommandPhase.Raw, MatchesCommand024, RunCommand024),
        new("--start-page-screens", 2500, TestCommandPhase.Raw, MatchesCommand025, RunCommand025),
        new("--shell-save-smoke", 2600, TestCommandPhase.Raw, MatchesCommand026, RunCommand026),
        new("--evidence-recipe-replay-check", 2700, TestCommandPhase.Raw, MatchesCommand027, RunCommand027),
        new("--shell-save-screens", 2800, TestCommandPhase.Raw, MatchesCommand028, RunCommand028),
        new("--quantify-screens", 2900, TestCommandPhase.Raw, MatchesCommand029, RunCommand029),
        new("--results-screens", 3000, TestCommandPhase.Raw, MatchesCommand030, RunCommand030),
        new("--shell-window-screens", 3100, TestCommandPhase.Raw, MatchesCommand031, RunCommand031),
        new("--denovo-screens", 3200, TestCommandPhase.Raw, MatchesCommand032, RunCommand032),
        new("--homology-results-screens", 3300, TestCommandPhase.Raw, MatchesCommand033, RunCommand033),
        new("--build-family-screens", 3400, TestCommandPhase.Raw, MatchesCommand034, RunCommand034),
        new("--quantify-shell-screens", 3500, TestCommandPhase.Raw, MatchesCommand035, RunCommand035),
        new("--secondary-screens", 3600, TestCommandPhase.Raw, MatchesCommand036, RunCommand036),
        new(EvidenceClassifierCheck.Argument, 3700, TestCommandPhase.Normalized, MatchesCommand037, RunCommand037),
        new(EvidenceClassifierCheck.ArchitectureArgument, 3701, TestCommandPhase.Normalized, MatchesCommand037, RunCommand037),
        new(EvidenceClassifierCheck.ApplyArgument, 3702, TestCommandPhase.Normalized, MatchesCommand037, RunCommand037),
        new(EvidenceClassifierCheck.SessionApplyArgument, 3703, TestCommandPhase.Normalized, MatchesCommand037, RunCommand037),
        new(EvidenceFetchCheck.Argument, 3800, TestCommandPhase.Normalized, MatchesCommand038, RunCommand038),
        new(EvidenceFetchCheck.VerifyArgument, 3801, TestCommandPhase.Normalized, MatchesCommand038, RunCommand038),
        new(EvidenceStorageCheck.Argument, 3900, TestCommandPhase.Normalized, MatchesCommand039, RunCommand039),
        new(ReferencePanelCheck.RefreshArgument, 4000, TestCommandPhase.Normalized, MatchesCommand040, RunCommand040),
        new(ReferencePanelCheck.Argument, 4100, TestCommandPhase.Normalized, MatchesCommand041, RunCommand041),
        new(EvidenceOperationsCheck.Argument, 4200, TestCommandPhase.Normalized, MatchesCommand042, RunCommand042),
        new(ScienceGateListRun.Argument, 4300, TestCommandPhase.Normalized, MatchesCommand043, RunCommand043),
        new(LeftOutCheckRun.Argument, 4400, TestCommandPhase.Normalized, MatchesCommand044, RunCommand044),
        new(ValidationCheckRun.Argument, 4500, TestCommandPhase.Normalized, MatchesCommand045, RunCommand045),
        new(MotifCheckRun.Argument, 4600, TestCommandPhase.Normalized, MatchesCommand046, RunCommand046),
        new(CoherenceCheckRun.Argument, 4700, TestCommandPhase.Normalized, MatchesCommand047, RunCommand047),
        new(NearestReviewedCheck.FullArgument, 4800, TestCommandPhase.Normalized, MatchesCommand048, RunCommand048),
        new(NearestReviewedCheck.Argument, 4900, TestCommandPhase.Normalized, MatchesCommand049, RunCommand049),
        new("--golden", 5000, TestCommandPhase.Normalized, MatchesCommand050, RunCommand050),
        new("--golden-record", 5001, TestCommandPhase.Normalized, MatchesCommand050, RunCommand050),
        new("--golden-verify", 5002, TestCommandPhase.Normalized, MatchesCommand050, RunCommand050),
        new(TalkFigures.Argument, 5100, TestCommandPhase.Normalized, MatchesCommand051, RunCommand051),
        new(TalkFigures.OnlyPlusDeepNovoArgument, 5200, TestCommandPhase.Normalized, MatchesCommand052, RunCommand052),
        new(TalkFigures.SourceVennArgument, 5300, TestCommandPhase.Normalized, MatchesCommand053, RunCommand053),
        new(TalkFigures.VariantsArgument, 5400, TestCommandPhase.Normalized, MatchesCommand054, RunCommand054),
        new(TalkFigures.SweepArgument, 5500, TestCommandPhase.Normalized, MatchesCommand055, RunCommand055),
        new(TalkFigures.AgreementArgument, 5600, TestCommandPhase.Normalized, MatchesCommand056, RunCommand056),
        new(TalkFigures.AcceptedScoreScatterArgument, 5700, TestCommandPhase.Normalized, MatchesCommand057, RunCommand057),
        new(TalkFigures.ScatterArgument, 5800, TestCommandPhase.Normalized, MatchesCommand058, RunCommand058),
        new(AttributionRuleStudy.Argument, 5900, TestCommandPhase.Normalized, MatchesCommand059, RunCommand059),
        new(TalkFigures.ScoreDistributionArgument, 6000, TestCommandPhase.Normalized, MatchesCommand060, RunCommand060),
        new(TalkFigures.SetNoveltyArgument, 6100, TestCommandPhase.Normalized, MatchesCommand061, RunCommand061),
        new("--overlap-study", 6200, TestCommandPhase.Normalized, MatchesCommand062, RunCommand062),
        new(ShortPsmPeaksTable.Argument, 6300, TestCommandPhase.Normalized, MatchesCommand063, RunCommand063),
        new(ChargeAudit.Argument, 6400, TestCommandPhase.Normalized, MatchesCommand064, RunCommand064),
        new(ThreeWayAudit.Argument, 6500, TestCommandPhase.Normalized, MatchesCommand065, RunCommand065),
    ];

    private static bool MatchesCommand003(string[] args) => args.Contains("--test-keyword-command", StringComparer.Ordinal);
    private static int RunCommand003(string[] args, string workDir)
    {
        KeywordCommandTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand004(string[] args) => args.Contains("--test-ui-icons", StringComparer.Ordinal);
    private static int RunCommand004(string[] args, string workDir)
    {
        UiIconTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand005(string[] args) => args.Length > 0 && args[0] == IdentitySearchStudy.Argument;
    private static int RunCommand005(string[] args, string workDir)
    {
        return IdentitySearchStudy.Run(args);

    }
    private static bool MatchesCommand006(string[] args) => args.Contains("--test-identity-study", StringComparer.Ordinal);
    private static int RunCommand006(string[] args, string workDir)
    {
        IdentityStudyTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand007(string[] args) => args.Length > 0 && (args[0] == ClusteringSnapshot.Argument || args[0] == ClusteringSnapshot.ExportArgument || args[0] == ClusteringSnapshot.VerifyExportArgument);
    private static int RunCommand007(string[] args, string workDir)
    {
        return ClusteringSnapshot.Run(args);

    }
    private static bool MatchesCommand008(string[] args) => args.Contains("--test-clustering-results", StringComparer.Ordinal);
    private static int RunCommand008(string[] args, string workDir)
    {
        string clusteringWorkDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
        ClusteringResultTests(clusteringWorkDir);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand009(string[] args) => args.Length == 2 && args[0] == "--operation-native-wait";
    private static int RunCommand009(string[] args, string workDir)
    {
        File.WriteAllText(args[1] + ".tmp", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        File.Move(args[1] + ".tmp", args[1]);
        Thread.Sleep(TimeSpan.FromMinutes(2));
        return 0;
    }

    private static bool MatchesCommand010(string[] args) => args.Contains("--test-operations", StringComparer.Ordinal);
    private static int RunCommand010(string[] args, string workDir)
    {
        string operationWorkDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
        CoreOperationTests(operationWorkDir);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand011(string[] args) => args.Length > 0 && args[0] == WordGroupingSnapshot.Argument;
    private static int RunCommand011(string[] args, string workDir)
    {
        return WordGroupingSnapshot.Run(args);

    }
    private static bool MatchesCommand012(string[] args) => args.Length > 0 && args[0] == ChartExportSnapshot.Argument;
    private static int RunCommand012(string[] args, string workDir)
    {
        return ChartExportSnapshot.Run(args);

    }
    private static bool MatchesCommand013(string[] args) => args.Contains("--test-results", StringComparer.Ordinal);
    private static int RunCommand013(string[] args, string workDir)
    {
        ResultsViewModelTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand014(string[] args) => args.Contains("--test-document", StringComparer.Ordinal);
    private static int RunCommand014(string[] args, string workDir)
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

    private static bool MatchesCommand015(string[] args) => args.Contains("--test-core-boundary", StringComparer.Ordinal);
    private static int RunCommand015(string[] args, string workDir)
    {
        CoreBoundaryTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand016(string[] args) => LogoAssets.IsRequested(args);
    private static int RunCommand016(string[] args, string workDir)
    {
        return LogoAssets.Run(args);

    }
    private static bool MatchesCommand017(string[] args) => args.Contains("--test-inputs", StringComparer.Ordinal);
    private static int RunCommand017(string[] args, string workDir)
    {
        RealDataInputTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand018(string[] args) => ScreenHarness.IsRequested(args);
    // Screen capture is an isolated lane: it never runs scientific export commands.
    private static int RunCommand018(string[] args, string workDir)
    {
        return ScreenHarness.Run(args);

    }
    private static bool MatchesCommand019(string[] args) => args.Length >= 2 && args[0] == GroupingReviewScreens.Argument;
    private static int RunCommand019(string[] args, string workDir)
    {
        return GroupingReviewScreens.Run(args[1], args.Length >= 3 ? args[2] : null);

    }
    private static bool MatchesCommand020(string[] args) => args.Length > 0 && args[0] == FeatureLedger.GenerateArgument;

    private static int RunCommand020(string[] args, string workDir)
    {
        if (args.Length != 3) throw new ArgumentException("Supply frozen source root and repository root.");
        return FeatureLedger.Generate(args[1], args[2]);
    }

    private static bool MatchesCommand021(string[] args) => args.Length > 0 && args[0] == "--feature-ledger-check";
    private static int RunCommand021(string[] args, string workDir)
    {
        return FeatureLedgerTests();

    }
    private static bool MatchesCommand022(string[] args) => args.Length > 0 && args[0] == "--feature-ledger-release-check";
    private static int RunCommand022(string[] args, string workDir)
    {
        if (args.Length == 2 && args[1] == "--guard-only") return FeatureLedgerReleaseGuardCheck();
        return FeatureLedgerTests(release: true);

    }
    private static bool MatchesCommand023(string[] args) => args.Length > 0 && args[0] == "--shell-checks";
    private static int RunCommand023(string[] args, string workDir)
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
        WorkflowNameTests();
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

    private static bool MatchesCommand024(string[] args) => args.Length == 2 && args[0] == "--shell-menu-screens";
    private static int RunCommand024(string[] args, string workDir)
    {
        return ShellMenuScreens(args[1]);

    }
    private static bool MatchesCommand025(string[] args) => args.Length == 2 && args[0] == "--start-page-screens";
    private static int RunCommand025(string[] args, string workDir)
    {
        return HomeStartScreens(args[1]);

    }
    private static bool MatchesCommand026(string[] args) => args.Length == 3 && args[0] == "--shell-save-smoke";
    private static int RunCommand026(string[] args, string workDir)
    {
        return ShellSaveSmoke(args[1], args[2]);

    }
    private static bool MatchesCommand027(string[] args) => args.Length == 2 && args[0] == "--evidence-recipe-replay-check";
    private static int RunCommand027(string[] args, string workDir)
    {
        return EvidenceRecipeReplayCheck(args[1]);

    }
    private static bool MatchesCommand028(string[] args) => args.Length == 2 && args[0] == "--shell-save-screens";
    private static int RunCommand028(string[] args, string workDir)
    {
        return ShellSaveScreens(args[1]);

    }
    private static bool MatchesCommand029(string[] args) => args.Length == 2 && args[0] == "--quantify-screens";
    private static int RunCommand029(string[] args, string workDir)
    {
        return QuantifyScreens(args[1]);

    }
    private static bool MatchesCommand030(string[] args) => args.Length is 2 or 3 && args[0] == "--results-screens";
    private static int RunCommand030(string[] args, string workDir)
    {
        return ResultsScreens(args[1], args.Length == 3 ? args[2] : null);

    }
    private static bool MatchesCommand031(string[] args) => args.Length == 2 && args[0] == "--shell-window-screens";
    private static int RunCommand031(string[] args, string workDir)
    {
        return ShellWindowPreferenceScreens(args[1]);

    }
    private static bool MatchesCommand032(string[] args) => args.Length == 2 && args[0] == "--denovo-screens";
    private static int RunCommand032(string[] args, string workDir)
    {
        return DeNovoScreens(args[1]);

    }
    private static bool MatchesCommand033(string[] args) => args.Length == 2 && args[0] == "--homology-results-screens";
    private static int RunCommand033(string[] args, string workDir)
    {
        return HomologyResultScreens(args[1]);

    }
    private static bool MatchesCommand034(string[] args) => args.Length == 2 && args[0] == "--build-family-screens";
    private static int RunCommand034(string[] args, string workDir)
    {
        return BuildFamiliesScreens(args[1]);

    }
    private static bool MatchesCommand035(string[] args) => args.Length == 2 && args[0] == "--quantify-shell-screens";
    private static int RunCommand035(string[] args, string workDir)
    {
        return QuantifyShellScreens(args[1]);

    }
    private static bool MatchesCommand036(string[] args) => args.Length == 2 && args[0] == "--secondary-screens";
    private static int RunCommand036(string[] args, string workDir)
    {
        return SecondaryWindowScreens(args[1]);

    }
    private static bool MatchesCommand037(string[] args) => args.Contains(EvidenceClassifierCheck.Argument) || args.Contains(EvidenceClassifierCheck.ArchitectureArgument)
                || args.Contains(EvidenceClassifierCheck.ApplyArgument) || args.Contains(EvidenceClassifierCheck.SessionApplyArgument);

    private static int RunCommand037(string[] args, string workDir)
    {
        EvidenceClassifierCheck.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand038(string[] args) => args.Contains(EvidenceFetchCheck.Argument) || args.Contains(EvidenceFetchCheck.VerifyArgument);

    private static int RunCommand038(string[] args, string workDir)
    {
        EvidenceFetchCheck.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand039(string[] args) => args.Contains(EvidenceStorageCheck.Argument);

    private static int RunCommand039(string[] args, string workDir)
    {
        EvidenceStorageCheck.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand040(string[] args) => args.Contains(ReferencePanelCheck.RefreshArgument);

    private static int RunCommand040(string[] args, string workDir)
    {
        ReferencePanelCheck.Refresh(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand041(string[] args) => args.Contains(ReferencePanelCheck.Argument);

    private static int RunCommand041(string[] args, string workDir)
    {
        ReferencePanelCheck.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand042(string[] args) => args.Contains(EvidenceOperationsCheck.Argument);

    private static int RunCommand042(string[] args, string workDir)
    {
        EvidenceOperationsCheck.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand043(string[] args) => args.Contains(ScienceGateListRun.Argument);

    private static int RunCommand043(string[] args, string workDir)
    {
        ScienceGateListRun.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand044(string[] args) => args.Contains(LeftOutCheckRun.Argument);

    private static int RunCommand044(string[] args, string workDir)
    {
        LeftOutCheckRun.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand045(string[] args) => args.Contains(ValidationCheckRun.Argument);

    private static int RunCommand045(string[] args, string workDir)
    {
        ValidationCheckRun.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand046(string[] args) => args.Contains(MotifCheckRun.Argument);

    private static int RunCommand046(string[] args, string workDir)
    {
        MotifCheckRun.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand047(string[] args) => args.Contains(CoherenceCheckRun.Argument);

    private static int RunCommand047(string[] args, string workDir)
    {
        CoherenceCheckRun.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand048(string[] args) => args.Contains(NearestReviewedCheck.FullArgument);

    private static int RunCommand048(string[] args, string workDir)
    {
        NearestReviewedCheck.RunFull(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand049(string[] args) => args.Contains(NearestReviewedCheck.Argument);

    private static int RunCommand049(string[] args, string workDir)
    {
        NearestReviewedCheck.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand050(string[] args) => GoldenNumbers.IsRequested(args);

    private static int RunCommand050(string[] args, string workDir)
    {
        EnsureApplication();
        GoldenNumbers.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand051(string[] args) => args.Any(a => string.Equals(a, TalkFigures.Argument, StringComparison.OrdinalIgnoreCase));

    // The conference-talk deliverable, GATED on its own flag and on nothing else, so a normal run
    // (with or without the four data paths) never reaches it. It is an OWN-LANE runner rather than
    // one more section: it re-extracts three 3 GB acquisitions at the 6-isotope ceiling, which is
    // tens of minutes, and it has no business riding along on a suite run. The flag is scanned
    // anywhere on the command line so it cannot disturb the four positional data paths.
    private static int RunCommand051(string[] args, string workDir)
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

    private static bool MatchesCommand052(string[] args) => args.Any(a => string.Equals(a, TalkFigures.OnlyPlusDeepNovoArgument,
                StringComparison.OrdinalIgnoreCase));

    // Focused real-data lane for the requested spectrum-evidence table. It runs only the exact
    // PEAKS de-novo-only + DeepNovo union and therefore reuses that variant's warm XIC/MS1 caches
    // without computing the other four presentation variants. The CSV is deliberately written
    // beside the user's input data as well as into the evidence pack/project.
    private static int RunCommand052(string[] args, string workDir)
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

    private static bool MatchesCommand053(string[] args) => args.Any(a => string.Equals(a, TalkFigures.SourceVennArgument,
                StringComparison.OrdinalIgnoreCase));

    private static int RunCommand053(string[] args, string workDir)
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

    private static bool MatchesCommand054(string[] args) => args.Any(a => string.Equals(a, TalkFigures.VariantsArgument, StringComparison.OrdinalIgnoreCase));

    // The FIVE de novo variants of the talk in one pass, GATED on its own flag. It loops
    // TalkFigures.Variants and calls the SAME Run once per variant, each into its own subfolder of
    // BrProt_Talk (the "de novo only" variant reproduces the baseline content). It does NOT touch
    // the --talk-figures default: that flag still writes the baseline straight into BrProt_Talk.
    // Like --talk-figures it re-extracts three acquisitions per variant, so it is heavy.
    private static int RunCommand054(string[] args, string workDir)
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

    private static bool MatchesCommand055(string[] args) => args.Any(a => string.Equals(a, TalkFigures.SweepArgument, StringComparison.OrdinalIgnoreCase));

    // The score-sweep-with-overlap figure, GATED on its own flag. Unlike the talk figures this
    // is FAST: pure sequence bookkeeping over the CSVs, no XIC, no RAW. Positional argument 3 is
    // the de novo base directory the three sources live under.
    private static int RunCommand055(string[] args, string workDir)
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

    private static bool MatchesCommand056(string[] args) => args.Any(a => string.Equals(a, TalkFigures.AgreementArgument, StringComparison.OrdinalIgnoreCase));

    // The PEAKS-vs-DeepNovo per-spectrum agreement bubble figure, GATED on its own flag. Like the
    // score sweep it is FAST: pure sequence bookkeeping over the CSVs, no XIC, no RAW. Positional
    // argument 3 is the de novo base directory the three sources live under.
    private static int RunCommand056(string[] args, string workDir)
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

    private static bool MatchesCommand057(string[] args) => args.Any(a => string.Equals(a, TalkFigures.AcceptedScoreScatterArgument,
                StringComparison.OrdinalIgnoreCase));

    // Fast, project-native accepted-call score/match scatter. Positional argument 3 is the
    // scientific .fqproj; positional argument 4 is the output folder.
    private static int RunCommand057(string[] args, string workDir)
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

    private static bool MatchesCommand058(string[] args) => args.Any(a => string.Equals(a, TalkFigures.ScatterArgument, StringComparison.OrdinalIgnoreCase));

    // The score-vs-score dot plot, GATED on its own flag. Like the sweep and the agreement figure
    // it is FAST: pure sequence bookkeeping over the CSVs, no XIC, no RAW. Positional argument 3 is
    // the de novo base directory the three sources live under.
    private static int RunCommand058(string[] args, string workDir)
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

    private static bool MatchesCommand059(string[] args) => args.Any(a => string.Equals(a, AttributionRuleStudy.Argument, StringComparison.OrdinalIgnoreCase));

    // Identified-peptide family attribution, every-past-gate vs best-score, GATED on its own flag.
    // Positional argument 3 is the .fqproj, 4 an optional separate .fcf ("." for none), 5 the
    // output folder. A homology search per curation, no RAW.
    private static int RunCommand059(string[] args, string workDir)
    {
        Section("Identified-peptide family attribution rule study (" + AttributionRuleStudy.Argument + ")");
        AttributionRuleStudy.Run(Check, args.Length > 2 ? args[2] : null,
            args.Length > 3 ? args[3] : null, args.Length > 4 ? args[4] : null);

        Console.WriteLine();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand060(string[] args) => args.Any(a => string.Equals(a, TalkFigures.ScoreDistributionArgument,
                StringComparison.OrdinalIgnoreCase));

    // The PEAKS de novo ALC score histogram, GATED on its own flag. FAST: pure per-spectrum census
    // over the CSVs, no XIC, no RAW. Positional argument 3 is the de novo base directory; positional
    // argument 4 is the output folder.
    private static int RunCommand060(string[] args, string workDir)
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

    private static bool MatchesCommand061(string[] args) => args.Any(a => string.Equals(a, TalkFigures.SetNoveltyArgument, StringComparison.OrdinalIgnoreCase));

    // The UpSet-style set-novelty view, GATED on its own flag. FAST: pure sequence bookkeeping over
    // the CSVs, no XIC, no RAW. Positional argument 3 is the de novo base directory.
    private static int RunCommand061(string[] args, string workDir)
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

    private static bool MatchesCommand062(string[] args) => args.Any(a => string.Equals(a, "--overlap-study", StringComparison.OrdinalIgnoreCase));

    private static int RunCommand062(string[] args, string workDir)
    {
        Section("De novo/search-engine overlap study from the real data");
        ReconciliationRealTests(args.Length > 2 ? args[2] : null,
                                args.Length > 3 ? args[3] : null, workDir);
        Console.WriteLine();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool MatchesCommand063(string[] args) => args.Any(a => string.Equals(a, ShortPsmPeaksTable.Argument, StringComparison.OrdinalIgnoreCase));

    private static int RunCommand063(string[] args, string workDir)
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

    private static bool MatchesCommand064(string[] args) => args.Any(a => string.Equals(a, ChargeAudit.Argument, StringComparison.OrdinalIgnoreCase));

    private static int RunCommand064(string[] args, string workDir)
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

    private static bool MatchesCommand065(string[] args) => args.Any(a => string.Equals(a, ThreeWayAudit.Argument, StringComparison.OrdinalIgnoreCase));

    private static int RunCommand065(string[] args, string workDir)
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


}
