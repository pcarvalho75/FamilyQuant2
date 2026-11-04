using FamilyQuant.Operations;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportPublicationCommandTable() =>
    [new("--test-report-publication", 434, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-report-publication", (_, _) => ReportPublicationTests())];

    private static int ReportPublicationTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.ReportPublication", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        (string Word, string Book, string Target) Fixture(string name)
        {
            string folder = Path.Combine(root, name), stage = Path.Combine(folder, ".stage");
            Directory.CreateDirectory(stage);
            string word = Path.Combine(stage, "report.docx"), book = ReportSupplementWorkbook.PathForReport(word);
            File.WriteAllText(word, "new word bytes"); File.WriteAllText(book, "new workbook bytes");
            File.WriteAllText(HtmlReportWriter.PathForReport(word), "new HTML bytes");
            return (word, book, Path.Combine(folder, "report.docx"));
        }
        var pair = Fixture("success");
        var operation = new OperationContext();
        ReportArtifactPublication.Publish(pair.Word, pair.Book, pair.Target, operation);
        Check("one final commit publishes both report artifacts", operation.HasCommitted && File.ReadAllText(pair.Target) == "new word bytes"
            && File.ReadAllText(ReportSupplementWorkbook.PathForReport(pair.Target)) == "new workbook bytes");
        foreach (int after in new[] { 1, 2 })
        {
            pair = Fixture("cancel-new-" + after);
            using var cancel = new CancellationTokenSource();
            operation = new(cancel.Token);
            bool cancelled = false;
            try { ReportArtifactPublication.Publish(pair.Word, pair.Book, pair.Target, operation, step => { if (step == after) cancel.Cancel(); }); }
            catch (OperationCanceledException) { cancelled = true; }
            Check("Cancel after artifact " + after + " leaves neither new output", cancelled && !operation.HasCommitted && !File.Exists(pair.Target)
                && !File.Exists(ReportSupplementWorkbook.PathForReport(pair.Target)));
            pair = Fixture("cancel-existing-" + after);
            File.WriteAllText(pair.Target, "previous word bytes");
            File.WriteAllText(ReportSupplementWorkbook.PathForReport(pair.Target), "previous workbook bytes");
            using var replaceCancel = new CancellationTokenSource();
            operation = new(replaceCancel.Token);
            cancelled = false;
            try { ReportArtifactPublication.Publish(pair.Word, pair.Book, pair.Target, operation, step => { if (step == after) replaceCancel.Cancel(); }); }
            catch (OperationCanceledException) { cancelled = true; }
            Check("Cancel after artifact " + after + " restores both previous outputs", cancelled && !operation.HasCommitted
                && File.ReadAllText(pair.Target) == "previous word bytes"
                && File.ReadAllText(ReportSupplementWorkbook.PathForReport(pair.Target)) == "previous workbook bytes");
        }
        pair = Fixture("io-failure");
        bool ioFailed = false;
        try
        {
            ReportArtifactPublication.Publish(pair.Word, pair.Book, pair.Target, new(), step =>
            { if (step == 1) Directory.CreateDirectory(ReportSupplementWorkbook.PathForReport(pair.Target)); });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ioFailed = true; }
        Check("a second-artifact failure rolls back the first publication", ioFailed && !File.Exists(pair.Target));
        pair = Fixture("recovery");
        File.WriteAllText(pair.Target, "old word recovery bytes");
        FileStream? locked = null;
        string? recovery = null;
        try
        {
            ReportArtifactPublication.Publish(pair.Word, pair.Book, pair.Target, new(), step =>
            {
                if (step != 1) return;
                locked = new FileStream(pair.Target, FileMode.Open, FileAccess.Read, FileShare.None);
                throw new IOException("Synthetic publication failure after the first rename");
            });
        }
        catch (ReportPublicationRecoveryException exception) { recovery = exception.RecoveryDirectory; }
        finally { locked?.Dispose(); }
        Check("rollback failure keeps identifiable recovery copies", recovery == Path.GetDirectoryName(pair.Word)
            && Directory.EnumerateFiles(recovery!, ".previous-word-*").Any(path => File.ReadAllText(path) == "old word recovery bytes"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
