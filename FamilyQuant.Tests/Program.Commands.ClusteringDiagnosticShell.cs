using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        if (args.Length is not (1 or 2)) throw new ArgumentException("Supply an optional fresh diagnostic check output folder.");
        string diagnosticWork = args.Length == 2 ? Path.GetFullPath(args[1])
            : Path.Combine(Path.GetTempPath(), "FamilyQuant.ClusteringDiagnostic.Tests-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(diagnosticWork) && Directory.EnumerateFileSystemEntries(diagnosticWork).Any())
            throw new IOException("Diagnostic check output must be fresh.");
        Directory.CreateDirectory(diagnosticWork);
        EnsureApplication();
        ClusteringDiagnosticShellTests(diagnosticWork);
        ClusteringDiagnosticNativeShellTests(Path.Combine(diagnosticWork, "native"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
