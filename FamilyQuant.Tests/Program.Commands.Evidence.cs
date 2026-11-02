using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> EvidenceCommandTable() =>
    [
        new("--evidence-task-check", 310, TestCommandPhase.Raw, EvidenceTaskCheckRequested, RunEvidenceTaskCheck),
        new("--test-evidence-task", 320, TestCommandPhase.Raw, EvidenceTaskTestsRequested, RunEvidenceTaskTests),
    ];

    private static bool EvidenceTaskCheckRequested(string[] args) => args.Length > 0 && args[0] == "--evidence-task-check";
    private static bool EvidenceTaskTestsRequested(string[] args) => args.Contains("--test-evidence-task", StringComparer.Ordinal);

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
