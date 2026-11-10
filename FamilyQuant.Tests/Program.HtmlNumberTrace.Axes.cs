using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void HtmlAxisNumberTraceTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.HtmlAxisTrace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string word = Path.Combine(root, "report.docx"), book = Path.Combine(root, "report_supplement.xlsx"), output = Path.Combine(root, "trace.csv");
        var table = new ReportTable("values", "Values", new[] { "Family", "Protein estimate (greedy)", "Run-to-run SD (pp)" },
            new IReadOnlyList<string>[] { new[] { "Cedar", "12", "0.2" } }, "Stored values");
        var document = new ReportDocument { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { table }) } };
        WordReportWriter.Write(word, document);
        ReportSupplementWorkbook.Write(book, document, Array.Empty<ReportSupplementSheet>());
        var fields = new Dictionary<string, HtmlReportField>
        {
            ["family"] = new("Cedar", null, "", new() { Kind = "word-cell", Table = 1, Row = 2, Column = 1 }),
            ["proteinValue"] = new("12", 12, "", new() { Kind = "word-cell", Table = 1, Row = 2, Column = 2 }),
            ["axisLabel"] = new("Protein estimate (greedy)", null, "", new() { Kind = "axis-label", Property = "proteins" }),
            ["axisTick"] = new("1000", 1000, "", new() { Kind = "axis-tick", Property = "proteins/log10/3", Format = "axis-plain" }),
            ["sdValue"] = new("0.2", .2, "pp", new() { Kind = "word-cell", Table = 1, Row = 2, Column = 3 }),
            ["sdLabel"] = new("Run SD of credited-family share (pp)", null, "", new() { Kind = "axis-label", Property = "sd" }),
            ["sdTick"] = new("0.03", .01 * 3, "pp", new() { Kind = "axis-tick", Property = "sd/linear/-2/3", Format = "axis-plain" }),
            ["sdZero"] = new("0", 0, "pp", new() { Kind = "axis-tick", Property = "sd/linear/-1/0", Format = "axis-plain" }),
            ["sdNoisy"] = new("0.3", .1 * 3, "pp", new() { Kind = "axis-tick", Property = "sd/linear/-1/3", Format = "axis-plain" }),
            ["sdSmall"] = new("0.00001", .00001, "pp", new() { Kind = "axis-tick", Property = "sd/linear/-5/1", Format = "axis-plain" }),
        };
        HtmlReportPayload Payload(string label = "axisLabel", string tick = "axisTick", string value = "proteinValue", bool includeAxisFields = true)
        {
            var selected = new Dictionary<string, HtmlReportField>(fields);
            if (!includeAxisFields)
                foreach (string key in new[] { "axisLabel", "axisTick", "sdLabel", "sdTick", "sdZero", "sdNoisy", "sdSmall" }) selected.Remove(key);
            var payload = new HtmlReportPayload { Fields = selected, Composition = new() { Families = new[]
                { new HtmlReportFamily { Id = "7", Name = "family", Proteins = "proteinValue", Sd = "sdValue" } } } };
            // The wire contract is tested independently of the producer's construction helpers.
            var json = JsonSerializer.SerializeToNode(payload, ReportNumberTrace.HtmlJson)!;
            json["dotMeasures"] = JsonSerializer.SerializeToNode(new[] { new
                { id = "proteins", label, unit = "", scale = "log", ticks = new[] { tick }, values = new Dictionary<string, string> { ["7"] = value } },
                new { id = "sd", label = "sdLabel", unit = "pp", scale = "linear", ticks = new[] { "sdZero", "sdSmall", "sdTick", "sdNoisy" }, values = new Dictionary<string, string> { ["7"] = "sdValue" } } });
            return json.Deserialize<HtmlReportPayload>(ReportNumberTrace.HtmlJson)!;
        }
        int Run(HtmlReportPayload? payload = null) => ReportNumberTrace.RunHtmlBindings(payload ?? Payload(), word, book, null, null, output);
        string beforeWord = FileFingerprint.Sha256(word), beforeBook = FileFingerprint.Sha256(book);
        Check("M1m axes resolve labels and plain log/linear ticks including recorded zero", Run() == 0);
        var saved = fields["axisLabel"]; fields["axisLabel"] = saved with { Text = "Wrong axis" };
        Check("M1m axes reject altered axis-label text", Run() == 1); fields["axisLabel"] = saved;
        fields["axisLabel"] = saved with { Value = 1 };
        Check("M1m axes require a text-only axis label", Run() == 1); fields["axisLabel"] = saved;
        saved = fields["axisTick"]; fields["axisTick"] = saved with { Text = "101" };
        Check("M1m axes reject altered tick display", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Text = "1E+03" };
        Check("M1m ticks reject scientific notation for a plain integer", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Value = 101 };
        Check("M1m axes reject altered tick geometry", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Value = null };
        Check("M1m axes require tick geometry even when its text is valid", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Unit = "%" };
        Check("M1m axes reject a tick unit unrelated to its measure", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Source = saved.Source with { Format = "G6" } };
        Check("M1m axes enforce the shared axis-plain format", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Source = saved.Source with { Format = "G3" } };
        Check("M1m ticks reject the retired G3 axis recipe", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Source = saved.Source with { Row = 1 } };
        Check("M1m axes reject pretend Word-cell coordinates", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Source = saved.Source with { Property = "unknown/log10/2" } };
        Check("M1m axes reject an unknown measure", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Source = saved.Source with { Property = "proteins/linear/0/2" } };
        Check("M1m axes reject a scale recipe incompatible with its measure", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Source = saved.Source with { Scale = 100 } };
        Check("M1m axes reject unrequested tick scaling", Run() == 1); fields["axisTick"] = saved;
        fields["axisTick"] = saved with { Source = saved.Source with { Property = "proteins/log10/9999" } };
        Check("M1m axes reject non-finite tick recipes", Run() == 1); fields["axisTick"] = saved;
        saved = fields["sdTick"]; fields["sdTick"] = saved with { Source = saved.Source with { Property = "sd/linear/-1/11" } };
        Check("M1m axes reject linear multiples outside the contract", Run() == 1); fields["sdTick"] = saved;
        saved = fields["sdNoisy"]; fields["sdNoisy"] = saved with { Text = "0.30000000000000004" };
        Check("M1m ticks reject binary noise while retaining the raw geometry", Run() == 1); fields["sdNoisy"] = saved;
        saved = fields["sdSmall"]; fields["sdSmall"] = saved with { Text = "0" };
        Check("M1m ticks keep a small nonzero label distinct from zero", Run() == 1); fields["sdSmall"] = saved;
        Check("M1m axes bind dot-measure label IDs", Run(Payload(label: "missing-label", includeAxisFields: false)) == 1
            && File.ReadAllText(output).Contains("Missing field ID: missing-label", StringComparison.Ordinal));
        Check("M1m axes bind dot-measure tick IDs", Run(Payload(tick: "missing-tick")) == 1
            && File.ReadAllText(output).Contains("Missing field ID: missing-tick", StringComparison.Ordinal));
        Check("M1m axes bind dot-measure value IDs", Run(Payload(value: "missing-value")) == 1
            && File.ReadAllText(output).Contains("Missing field ID: missing-value", StringComparison.Ordinal));
        Check("M1m axes reject a valid label belonging to a different measure", Run(Payload(label: "sdLabel")) == 1);
        Check("M1m axes reject a valid tick belonging to a different measure", Run(Payload(tick: "sdTick")) == 1);
        Check("M1m axes trace keeps native packages unchanged", beforeWord == FileFingerprint.Sha256(word) && beforeBook == FileFingerprint.Sha256(book));
    }
}
