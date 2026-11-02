using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsQuantitationCommandTable() =>
    [
        new("--test-quantitation-panel", 4, TestCommandPhase.Raw, args => args.Contains("--test-quantitation-panel", StringComparer.Ordinal), RunQuantitationPanelChecks),
    ];

    private static int RunQuantitationPanelChecks(string[] args, string workDir)
    {
        QuantitationPanelTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.QuantitationPanel.Tests"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
