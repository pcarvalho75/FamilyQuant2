using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyPanelCommandTable() =>
    [
        new("--test-family-panel", 71, TestCommandPhase.Raw,
            args => args.Contains("--test-family-panel", StringComparer.Ordinal), (_, _) =>
            {
                FamilyPanelTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void FamilyPanelTests()
    {
        Check("J1a project exposes the optional user family panel",
            typeof(FamilyClusterFile).GetProperty("FamilyPanel")?.PropertyType.FullName == "FamilyQuant.Panels.FamilyPanel");
        Check("J1a evidence recipe exposes the optional user family panel",
            typeof(Evidence.EvidenceRecipe).GetProperty("FamilyPanel")?.PropertyType.FullName == "FamilyQuant.Panels.FamilyPanel");
    }
}
