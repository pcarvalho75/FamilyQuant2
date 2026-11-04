using FamilyQuant.Report;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlReportExportCommands() =>
    [new("--test-html-report-export", 443, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-report-export", (args, _) =>
        {
            HtmlReportExportTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-report-export"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlReportExportTests(string output)
    {
        Directory.CreateDirectory(output);
        Check("HTML writer is available in WPF-free Core", typeof(HtmlReportWriter).Assembly == typeof(HtmlReportPayload).Assembly);
        byte[] Write(HtmlReportPayload value) => HtmlReportWriter.BuildBytes(value);
        const string hostile = "Name </script><script>alert(\"quoted\")</script><!-- < & ' \u2028 \u2029";
        var fields = new Dictionary<string, HtmlReportField>(StringComparer.Ordinal)
        {
            ["title"] = new(hostile, null, "", new() { Kind = "word-property", Property = "Title" }),
            ["authors"] = new(hostile + " authors", null, "", new() { Kind = "word-property", Property = "Authors" }),
        };
        var payload = new HtmlReportPayload { Title = "title", Authors = "authors", Fields = fields,
            Links = new() { Word = hostile + ".docx", Workbook = hostile + ".xlsx" } };
        byte[] first = Write(payload);
        string html = Encoding.UTF8.GetString(first);
        File.WriteAllBytes(Path.Combine(output, "report.html"), first);
        const string marker = "<script id=\"fq-data\" type=\"application/json\">";
        int start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        int end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        string json = html[start..end];
        using var parsed = JsonDocument.Parse(json);
        Check("HTML embeds one payload in the native template", start >= marker.Length && html.IndexOf(marker, start, StringComparison.Ordinal) < 0
            && html.Contains("<!doctype html>", StringComparison.OrdinalIgnoreCase));
        Check("HTML payload cannot terminate its script or introduce markup", !json.Contains('<') && !json.Contains('>') && !json.Contains('&')
            && !json.Contains('\u2028') && !json.Contains('\u2029'));
        Check("HTML preserves hostile title authors and file names after JSON parsing",
            parsed.RootElement.GetProperty("fields").GetProperty("title").GetProperty("text").GetString() == hostile
            && parsed.RootElement.GetProperty("fields").GetProperty("authors").GetProperty("text").GetString() == hostile + " authors"
            && parsed.RootElement.GetProperty("links").GetProperty("word").GetString() == hostile + ".docx"
            && parsed.RootElement.GetProperty("links").GetProperty("workbook").GetString() == hostile + ".xlsx");
        Check("HTML output has fixed LF newlines and UTF-8 without BOM", !html.Contains('\r') && !first.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                var reversed = payload with { Fields = fields.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) };
                Check("HTML bytes repeat across culture and field insertion order " + name, first.SequenceEqual(Write(reversed)));
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        HtmlReportPublicationTests(Path.Combine(output, "publication"));
        var source = HtmlFixture();
        // This writer fixture has display cells only; its producer-only partial native recipe is not a workbook recipe.
        var exportDocument = source.Document with { Sections = source.Document.Sections.Select(section => section with
            { Blocks = section.Blocks.Select(block => block is ReportTable table ? table with { SupplementSource = null } : block).ToArray() }).ToArray() };
        var prepared = new ReportProjectExport(exportDocument, new Dictionary<string, string>(), Array.Empty<ReportPlacedFigure>())
            { HtmlContext = source.Context };
        string word = Path.Combine(output, "prepared.docx");
        var exportOperation = new OperationContext();
        ReportProjectExporter.Write(prepared, word, operation: exportOperation);
        Check("prepared report export publishes Word workbook and HTML with one final commit", exportOperation.HasCommitted
            && File.Exists(word) && File.Exists(ReportSupplementWorkbook.PathForReport(word)) && File.Exists(HtmlReportWriter.PathForReport(word))
            && !Directory.EnumerateDirectories(output, ".fq-report-*").Any());
    }

    private static void HtmlReportPublicationTests(string output)
    {
        (string[] Staged, string[] Targets) Fixture(string name, bool existing)
        {
            string folder = Path.Combine(output, name), stage = Path.Combine(folder, ".stage");
            Directory.CreateDirectory(stage);
            string[] names = { "report.docx", "report_supplement.xlsx", "report.html" };
            var staged = names.Select(name => Path.Combine(stage, name)).ToArray();
            var targets = names.Select(name => Path.Combine(folder, name)).ToArray();
            for (int i = 0; i < 3; i++)
            {
                File.WriteAllText(staged[i], "new " + names[i]);
                if (existing) File.WriteAllText(targets[i], "old " + names[i]);
            }
            return (staged, targets);
        }
        var fixture = Fixture("success", true);
        var operation = new OperationContext();
        var steps = new List<int>();
        ReportArtifactPublication.Publish(fixture.Staged[0], fixture.Staged[1], fixture.Targets[0], operation, steps.Add);
        Check("HTML publication commits all three files together", operation.HasCommitted && steps.SequenceEqual(new[] { 1, 2, 3 })
            && fixture.Targets.All(path => File.ReadAllText(path) == "new " + Path.GetFileName(path)));
        foreach (bool existing in new[] { false, true })
            foreach (int boundary in new[] { 1, 2, 3 })
            {
                fixture = Fixture("cancel-" + existing + "-" + boundary, existing);
                using var cancel = new CancellationTokenSource();
                operation = new(cancel.Token);
                bool cancelled = false;
                try
                {
                    ReportArtifactPublication.Publish(fixture.Staged[0], fixture.Staged[1], fixture.Targets[0], operation,
                        step => { if (step == boundary) cancel.Cancel(); });
                }
                catch (OperationCanceledException) { cancelled = true; }
                Check("HTML cancel restores all three files at boundary " + boundary + " existing=" + existing,
                    cancelled && !operation.HasCommitted && fixture.Targets.All(path => existing
                        ? File.ReadAllText(path) == "old " + Path.GetFileName(path) : !File.Exists(path)));
                Check("HTML cancel removes private backups at boundary " + boundary + " existing=" + existing,
                    !Directory.EnumerateFiles(Path.GetDirectoryName(fixture.Staged[0])!, ".previous-*").Any());
            }
        fixture = Fixture("missing-html", true);
        File.Delete(fixture.Staged[2]);
        bool refused = false;
        try { ReportArtifactPublication.Publish(fixture.Staged[0], fixture.Staged[1], fixture.Targets[0], new()); }
        catch (FileNotFoundException) { refused = true; }
        Check("HTML publication refuses an incomplete triple before replacing anything", refused
            && fixture.Targets.All(path => File.ReadAllText(path) == "old " + Path.GetFileName(path)));
        foreach (int boundary in new[] { 1, 2, 3 })
        {
            fixture = Fixture("failure-" + boundary, true);
            bool failedPublication = false;
            operation = new();
            try
            {
                ReportArtifactPublication.Publish(fixture.Staged[0], fixture.Staged[1], fixture.Targets[0], operation,
                    step => { if (step == boundary) throw new IOException("Synthetic publication failure"); });
            }
            catch (IOException) { failedPublication = true; }
            Check("HTML failure restores all three previous files at boundary " + boundary, failedPublication && !operation.HasCommitted
                && fixture.Targets.All(path => File.ReadAllText(path) == "old " + Path.GetFileName(path)));
        }
    }
}
