using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsArchitectureCommandTable() =>
    [
        new("--test-architecture-group-split", 5, TestCommandPhase.Raw, ArchitectureGroupSplitRequested, RunArchitectureGroupSplitCommand),
        new("--test-architecture-member-view", 6, TestCommandPhase.Raw, args => args.Contains("--test-architecture-member-view", StringComparer.Ordinal), (_, _) =>
        {
            EnsureApplication(); ArchitectureMemberViewTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
        }),
    ];

    private static bool ArchitectureGroupSplitRequested(string[] args) =>
        args.Contains("--test-architecture-group-split", StringComparer.Ordinal);

    private static int RunArchitectureGroupSplitCommand(string[] args, string workDir)
    {
        ArchitectureGroupSplitTests(args.Length == 2 ? args[1] : null);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
