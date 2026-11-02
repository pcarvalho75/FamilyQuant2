using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LookCommandTable() =>
    [
        new("--curation-screens", 10, TestCommandPhase.Raw, args => args.Length == 3 && args[0] == "--curation-screens", (args, _) => CurationScreens(args[1], args[2])),
        new("--test-curation-layout", 20, TestCommandPhase.Raw, args => args.Contains("--test-curation-layout", StringComparer.Ordinal), RunCurationLayoutChecks),
        new("--test-curation-view", 30, TestCommandPhase.Raw, args => args.Contains("--test-curation-view", StringComparer.Ordinal), RunCurationViewChecks),
        new("--test-curation-quantitation", 40, TestCommandPhase.Raw, args => args.Contains("--test-curation-quantitation", StringComparer.Ordinal), RunCurationQuantitationChecks),
        new("--test-curation-operations", 45, TestCommandPhase.Raw, args => args.Contains("--test-curation-operations", StringComparer.Ordinal), RunCurationOperationChecks),
        new("--test-family-view", 46, TestCommandPhase.Raw, args => args.Contains("--test-family-view", StringComparer.Ordinal), RunFamilyViewChecks),
        new("--family-view-screens", 47, TestCommandPhase.Raw, args => args.Length == 3 && args[0] == "--family-view-screens", (args, _) => CurationScreens(args[1], args[2], selectFamily: true)),
        new("--test-ui-colors", 48, TestCommandPhase.Raw, args => args.Contains("--test-ui-colors", StringComparer.Ordinal), RunUiColorChecks),
        new("--test-review-wait", 49, TestCommandPhase.Raw, args => args.Contains("--test-review-wait", StringComparer.Ordinal), RunReviewWaitChecks),
    ];

    private static int RunCurationLayoutChecks(string[] args, string workDir)
    {
        string layoutWork = Path.Combine(Path.GetTempPath(), "FamilyQuant.Curation.Layout.Tests");
        Directory.CreateDirectory(layoutWork);
        EnsureApplication(); UiExplanationTests(null, layoutWork);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunCurationViewChecks(string[] args, string workDir)
    {
        CurationScreenTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunCurationQuantitationChecks(string[] args, string workDir)
    {
        string quantWork = Path.Combine(Path.GetTempPath(), "FamilyQuant.Curation.Quantitation.Tests");
        Directory.CreateDirectory(quantWork);
        EnsureApplication(); FullXicColumnUiTests(quantWork);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunCurationOperationChecks(string[] args, string workDir)
    {
        CurationOperationTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunFamilyViewChecks(string[] args, string workDir)
    {
        FamilyViewTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunUiColorChecks(string[] args, string workDir)
    {
        UiColorTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunReviewWaitChecks(string[] args, string workDir)
    {
        ReviewWorkWaitTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
