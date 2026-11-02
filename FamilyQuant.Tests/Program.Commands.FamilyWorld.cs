using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyWorldCommandTable() =>
    [
        new("--test-family-world", 9100, TestCommandPhase.Raw, FamilyWorldFixturesRequested, RunFamilyWorldFixtures),
        new(FamilyWorldCheck.Argument, 9101, TestCommandPhase.Normalized, FamilyWorldCheckRequested, RunFamilyWorldCheck),
        new(FamilyWorldProject.Argument, 9102, TestCommandPhase.Normalized, FamilyWorldProjectRequested, (args, _) => FamilyWorldProject.Run(args)),
    ];

    private static bool FamilyWorldFixturesRequested(string[] args) => args.Contains("--test-family-world", StringComparer.Ordinal);

    private static bool FamilyWorldCheckRequested(string[] args) => args.Contains(FamilyWorldCheck.Argument);

    private static bool FamilyWorldProjectRequested(string[] args) => args.Contains(FamilyWorldProject.Argument);

    private static int RunFamilyWorldFixtures(string[] args, string workDir)
    {
        Section("Family world (B1) - a de novo tag credited to UniProt families, on a fixture");
        FamilyWorldCheck.RunFixtures(Check);
        FamilyWorldComputeTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunFamilyWorldCheck(string[] args, string workDir)
    {
        FamilyWorldCheck.Run(args, Check);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
