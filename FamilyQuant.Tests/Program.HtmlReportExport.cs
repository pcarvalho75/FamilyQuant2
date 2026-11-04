using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

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
    }
}
