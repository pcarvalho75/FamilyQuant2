using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsCoverageCommandTable() =>
    [
        new("--test-evidence-coverage-ui", 10060, TestCommandPhase.Raw, args => args.Contains("--test-evidence-coverage-ui", StringComparer.Ordinal), RunEvidenceCoverageUi),
    ];
    private static int RunEvidenceCoverageUi(string[] args, string workDir)
    {
        EvidenceCoverageUiTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.EvidenceCoverageUi.Tests"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
