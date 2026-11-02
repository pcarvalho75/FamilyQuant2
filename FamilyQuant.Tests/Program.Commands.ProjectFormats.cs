using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ProjectFormatCommandTable() =>
    [
        new("--test-project-formats", 390, TestCommandPhase.Raw, ProjectFormatsRequested, RunProjectFormats),
        new(FcfCompatibilitySnapshot.Argument, 392, TestCommandPhase.Raw, FcfSnapshotRequested, RunFcfSnapshot),
        new(FcfCompatibilitySnapshot.ExportArgument, 394, TestCommandPhase.Raw, FcfExportRequested, RunFcfSnapshot),
    ];

    private static bool ProjectFormatsRequested(string[] args) => args.Contains("--test-project-formats", StringComparer.Ordinal);
    private static int RunProjectFormats(string[] args, string workDir)
    {
        workDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
        Directory.CreateDirectory(workDir);
        ProjectFormatStageTests();
        ProjectStoreTests(workDir);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
    private static bool FcfSnapshotRequested(string[] args) => args.Length > 0 && args[0] == FcfCompatibilitySnapshot.Argument;
    private static bool FcfExportRequested(string[] args) => args.Length > 0 && args[0] == FcfCompatibilitySnapshot.ExportArgument;
    private static int RunFcfSnapshot(string[] args, string workDir) => FcfCompatibilitySnapshot.Run(args);
}
