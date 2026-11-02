using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ClusteringDiagnosticShellCommandTable() =>
    [
        new("--test-clustering-diagnostic-shell", 10130, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-clustering-diagnostic-shell", RunClusteringDiagnosticShellChecks),
    ];

    private static int RunClusteringDiagnosticShellChecks(string[] args, string workDir)
    {
        string diagnosticWork = Path.Combine(Path.GetTempPath(), "FamilyQuant.ClusteringDiagnostic.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(diagnosticWork);
        EnsureApplication();
        ClusteringDiagnosticShellTests(diagnosticWork);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
