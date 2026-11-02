using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DeNovoEngineCommandTable() =>
    [
        new(DeNovoEngineSnapshot.Argument, 370, TestCommandPhase.Raw, DeNovoEngineSnapshotRequested, RunDeNovoEngineSnapshot),
        new("--test-denovo-engine", 380, TestCommandPhase.Raw, DeNovoEngineTestsRequested, RunDeNovoEngineTests),
        new("--test-denovo-engine-real", 385, TestCommandPhase.Raw, DeNovoEngineRealRequested, RunDeNovoEngineReal),
    ];

    private static bool DeNovoEngineSnapshotRequested(string[] args) =>
        args.Length > 0 && args[0] == DeNovoEngineSnapshot.Argument;
    private static int RunDeNovoEngineSnapshot(string[] args, string workDir) => DeNovoEngineSnapshot.Run(args);
    private static bool DeNovoEngineTestsRequested(string[] args) => args.Contains("--test-denovo-engine", StringComparer.Ordinal);
    private static int RunDeNovoEngineTests(string[] args, string workDir)
    {
        workDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
        Directory.CreateDirectory(workDir);
        DeNovoEngineTidyTests();
        DeNovoTests(workDir);
        SourceAwareDeNovoTests.Run(Check, workDir);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool DeNovoEngineRealRequested(string[] args) =>
        args.Length > 0 && args[0] == "--test-denovo-engine-real";

    private static int RunDeNovoEngineReal(string[] args, string workDir)
    {
        if (args.Length != 4) throw new ArgumentException("Supply PEAKS-only folder, acquisition folder and output folder.");
        if (!Directory.Exists(args[1]) || !File.Exists(Path.Combine(args[2], "PLVI.sepr2")))
            throw new FileNotFoundException("The focused real-data check requires explicit PEAKS exports and PLVI.sepr2.");
        workDir = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(workDir);
        DeNovoRealExportTests(args[1]);
        ReconciliationRealTests(args[1], args[2], workDir);
        EnsureApplication();
        DeNovoScreenRealTests(args[1], args[2], workDir, includeLegacyExtraction: false);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
