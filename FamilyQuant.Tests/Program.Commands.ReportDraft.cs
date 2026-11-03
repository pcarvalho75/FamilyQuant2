using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportDraftCommandTable() =>
    [
        new("--export-word-report", 10, TestCommandPhase.Raw,
            args => args.Length == 3 && args[0] == "--export-word-report", RunWordProjectExport),
        new("--test-report-draft", 10, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-report-draft", RunWordDraftChecks),
    ];

    private static int RunWordDraftChecks(string[] args, string workDir)
    {
        WordReportDraftTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunWordProjectExport(string[] args, string workDir)
    {
        string project = Path.GetFullPath(args[1]); string output = Path.GetFullPath(args[2]);
        if (string.Equals(project, output, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Report output must differ from the saved project.");
        string before = FileFingerprint.Sha256(project);
        var (curation, rows, manifest) = ProjectStore.ReadFqproj(project);
        EnsureApplication();
        string figures = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "_figures");
        var export = ReportProjectExporter.Build(rows, manifest, curation, figures);
        ReportProjectExporter.Write(export, output);
        if (FileFingerprint.Sha256(project) != before) throw new InvalidOperationException("The report export changed its source project.");
        Console.WriteLine("Report: " + output);
        Console.WriteLine("PNG figures: " + figures);
        Console.WriteLine("Native CSV sidecars: " + export.CsvExports.Count);
        Console.WriteLine("Document SHA-256: " + FileFingerprint.Sha256(output));
        return 0;
    }
}
