using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> EvidenceCommandTable() =>
    [
        new("--test-evidence-fetch-atomic", 1, TestCommandPhase.Raw, EvidenceAtomicRequested, RunEvidenceAtomic),
        new("--evidence-delivery", 2, TestCommandPhase.Raw, EvidenceDeliveryRequested, RunEvidenceDelivery),
        new("--test-evidence-delivery", 3, TestCommandPhase.Raw, EvidenceDeliveryTestsRequested, RunEvidenceDeliveryTests),
        new("--evidence-task-check", 60, TestCommandPhase.Raw, EvidenceTaskCheckRequested, RunEvidenceTaskCheck),
        new("--evidence-task-header-audit", 65, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--evidence-task-header-audit", (args, _) => EvidenceTaskHeaderAuditCommand(args)),
        new("--test-evidence-task", 70, TestCommandPhase.Raw, EvidenceTaskTestsRequested, RunEvidenceTaskTests),
        new("--test-evidence-task-scope", 430, TestCommandPhase.Raw, EvidenceTaskScopeRequested, RunEvidenceTaskScope),
    ];

    private static bool EvidenceAtomicRequested(string[] args) => args.Contains("--test-evidence-fetch-atomic", StringComparer.Ordinal);
    private static bool EvidenceDeliveryRequested(string[] args) => args.Length > 0 && args[0] == "--evidence-delivery";
    private static bool EvidenceDeliveryTestsRequested(string[] args) => args.Contains("--test-evidence-delivery", StringComparer.Ordinal);

    private static int RunEvidenceAtomic(string[] args, string workDir)
    {
        EnsureApplication();
        EvidenceFetchAtomicTests();
        EvidenceFetchCoordinatorTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunEvidenceDelivery(string[] args, string workDir) => EvidenceDeliveryCommand(args);

    private static int RunEvidenceDeliveryTests(string[] args, string workDir)
    {
        EnsureApplication();
        EvidenceDeliveryFixtureTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool EvidenceTaskCheckRequested(string[] args) => args.Length > 0 && args[0] == "--evidence-task-check";
    private static bool EvidenceTaskTestsRequested(string[] args) => args.Contains("--test-evidence-task", StringComparer.Ordinal);
    private static bool EvidenceTaskScopeRequested(string[] args) => args.Contains("--test-evidence-task-scope", StringComparer.Ordinal);

    private static int RunEvidenceTaskScope(string[] args, string workDir)
    {
        EnsureApplication();
        EvidenceTaskScopeTests();
        EvidenceTaskScopeRecipeTests();
        EvidenceTaskScopeWindowTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.TaskScope"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunEvidenceTaskCheck(string[] args, string workDir)
    {
        return EvidenceTaskCheck(args);
    }

    private static int RunEvidenceTaskTests(string[] args, string workDir)
    {
        EvidenceTaskTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
