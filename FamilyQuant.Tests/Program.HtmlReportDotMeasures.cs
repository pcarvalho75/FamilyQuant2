using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void HtmlDotMeasureTests()
    {
        Section("M1q program-made dot measures and axis labels");
        var fixture = HtmlDotFixture((.0021, "3", "225"), (91, "1260", "100"));
        HtmlReportPayload Build(ReportSelection? selection = null, HtmlReportContext? context = null)
            => HtmlReportPayloadBuilder.Build(fixture.Document, fixture.Sheets, selection, context ?? fixture.Context);
        var model = Build();
        JsonElement List(HtmlReportPayload payload)
        {
            var json = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return json.TryGetProperty("dotMeasures", out var list) ? list : default;
        }
        JsonElement Measure(HtmlReportPayload payload, string id) => List(payload).ValueKind == JsonValueKind.Array
            ? List(payload).EnumerateArray().FirstOrDefault(m => m.GetProperty("id").GetString() == id) : default;
        string[] Ticks(HtmlReportPayload payload, string id) => Measure(payload, id).ValueKind == JsonValueKind.Object
            ? Measure(payload, id).GetProperty("ticks").EnumerateArray().Select(t => payload.Fields[t.GetString()!].Text).ToArray() : Array.Empty<string>();
        bool HasFamily(JsonElement measure, string id) => measure.ValueKind == JsonValueKind.Object
            && measure.GetProperty("values").TryGetProperty(id, out _);
        Check("dot selectors offer only existing native measures in stable order", List(model).ValueKind == JsonValueKind.Array
            && List(model).EnumerateArray().Select(m => m.GetProperty("id").GetString()).SequenceEqual(
                new[] { "mean", "peptides", "proteins", "uniqueProteins", "sd", "cv" }));
        Check("share and peptide log ticks bracket their stored ranges with invariant labels",
            Ticks(model, "mean").SequenceEqual(new[] { "0.001", "0.01", "0.1", "1", "10", "100" })
            && Ticks(model, "peptides").SequenceEqual(new[] { "1", "10", "100", "1E+03", "1E+04" })
            && Measure(model, "peptides").GetProperty("ticks").EnumerateArray().Select(t => model.Fields[t.GetString()!].Value)
                .SequenceEqual(new double?[] { 1, 10, 100, 1000, 10000 }));
        Check("SD is linear with zero and program-made ticks, not a logarithmic axis", Measure(model, "sd").ValueKind == JsonValueKind.Object
            && Measure(model, "sd").GetProperty("scale").GetString() == "linear"
            && Ticks(model, "sd").SequenceEqual(new[] { "0", "1", "2" }));
        var mean = Measure(model, "mean");
        Check("dot values reuse exact existing family fields rather than introduce new results", HasFamily(mean, "7")
            && mean.GetProperty("values").GetProperty("7").GetString() == model.Composition.Families[0].Mean
            && !HasFamily(mean, "others"));
        Check("axis labels and ticks identify presentation constants rather than pretend Word cells",
            model.Fields.Values.Any(f => f.Source.Kind == "axis-label")
            && model.Fields.Values.Any(f => f.Source.Kind == "axis-tick" && f.Value is > 0 && f.Source.Format == "G3"));
        var hidden = Build(new() { ExcludedIds = new[] { ReportSectionIds.Results } });
        Check("unselected Results emit neither dot measures nor their axis fields", List(hidden).ValueKind == JsonValueKind.Array
            && List(hidden).GetArrayLength() == 0 && !hidden.Fields.Values.Any(f => f.Source.Kind.StartsWith("axis-", StringComparison.Ordinal)));
        var omitted = Build(new() { ExcludedIds = new[] { "family:cedar" } }, fixture.Context with
        { FamilySections = new Dictionary<string, string> { ["7"] = "family:cedar" } });
        Check("an unchecked family owner cannot leak peptide detail through the measure map", HasFamily(Measure(omitted, "mean"), "7")
            && !HasFamily(Measure(omitted, "peptides"), "7") && HasFamily(Measure(omitted, "peptides"), "8"));
        var sparse = HtmlDotFixture((1, "", "0"), (0, "0", "5"));
        var zeros = HtmlReportPayloadBuilder.Build(sparse.Document, sparse.Sheets, context: sparse.Context);
        Check("missing values stay absent while recorded zero remains an exact available value",
            !HasFamily(Measure(zeros, "peptides"), "7") && HasFamily(Measure(zeros, "peptides"), "8")
            && zeros.Fields[Measure(zeros, "peptides").GetProperty("values").GetProperty("8").GetString()!].Value == 0
            && Ticks(zeros, "peptides").SequenceEqual(new[] { "1", "10" }));
        var tick = new HtmlReportReference { Kind = "axis-tick", Property = "mean/log10/-1", Format = "G3" };
        Check("shared axis resolver binds exact native text, units and geometry", HtmlReportAxisTicks.TryResolve(tick, out var resolved)
            && resolved is { Text: "0.1", Value: .1, Unit: "%" }
            && HtmlReportAxisTicks.TryResolve(new() { Kind = "axis-label", Property = "proteins" }, out var label)
            && label is { Text: "Protein estimate (greedy)", Value: null, Unit: "" });
        Check("shared axis resolver refuses forged scale, locator, recipe or measure", new[]
        {
            tick with { Scale = 100 }, tick with { Row = 1 }, tick with { Format = null },
            tick with { Property = "mean/log10/+1" }, tick with { Property = "mean/log10/309" },
            tick with { Property = "sd/log10/0" }, tick with { Property = "cv/linear/0/11" },
            tick with { Property = "unknown/log10/1" }, tick with { Kind = "word-cell" },
        }.All(source => !HtmlReportAxisTicks.TryResolve(source, out _)));
        var priorCulture = CultureInfo.CurrentCulture; var priorUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            byte[] english = HtmlReportWriter.BuildBytes(Build());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            Check("program-made axis labels keep the complete HTML byte-identical under pt-BR/en-US", english.SequenceEqual(HtmlReportWriter.BuildBytes(Build())));
        }
        finally { CultureInfo.CurrentCulture = priorCulture; CultureInfo.CurrentUICulture = priorUi; }
    }

    private static (ReportDocument Document, IReadOnlyList<ReportSupplementSheet> Sheets, HtmlReportContext Context) HtmlDotFixture(
        params (double Share, string Peptides, string Proteins)[] points)
    {
        var summaries = points.Select((p, i) => new FamilyRunShareSummary { FamilyUserId = i + 7, MeanShare = p.Share / 100,
            StandardDeviation = .011, CoefficientOfVariation = .02, MeanShareOfMeasuredSignal = 0 }).ToList();
        var rows = points.Select((p, i) => (IReadOnlyList<string>)new[] { "Family " + (i + 7).ToString(CultureInfo.InvariantCulture),
            (summaries[i].MeanShare.GetValueOrDefault() * 100).ToString("G6", CultureInfo.InvariantCulture), "1.1", "0", "0", p.Proteins, "4" }).ToArray();
        var table = new ReportTable("family-run-means", "Stored headline", new[] { "Family", "Mean share (%)", "Sample SD (pp)",
            "Whole measured (%)", "Whole-signal SD (pp)", "Proteins needed (greedy)", "Proteins with own peptide" }, rows, "")
        { SupplementSource = new("family_user_id,family,status,mean_pct\n" + string.Join("\n", rows.Select((r, i) =>
            (i + 7).ToString(CultureInfo.InvariantCulture) + "," + r[0] + ",Listed," + r[1])) + "\n", new[] { "family", "mean_pct" }) };
        var counts = new ReportTable("protein-counts", "Counts", new[] { "family_user_id", "observed_peptides" },
            points.Select((p, i) => (IReadOnlyList<string>)new[] { (i + 7).ToString(CultureInfo.InvariantCulture), p.Peptides }).ToArray(), "");
        var means = new ReportTable("export:family_run_means.csv", "Means", new[] { "family_user_id", "cv_pct", "cv_ratio" },
            points.Select((p, i) => (IReadOnlyList<string>)new[] { (i + 7).ToString(CultureInfo.InvariantCulture), "2", "0.02" }).ToArray(), "");
        return (new ReportDocument { Title = "Dot fixture", Sections = new[]
        {
            new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { table }),
            new ReportSection("family:cedar", "Family detail", Array.Empty<ReportBlock>()),
        } }, new[] { new ReportSupplementSheet(counts.Id, ReportSectionIds.Results, "Counts", counts),
            new ReportSupplementSheet(means.Id, ReportSectionIds.Results, "Means", means) },
            new HtmlReportContext { StoredStudies = new QuantManifest { FamilyRunShares = new FamilyRunSharesResult { Families = summaries } } });
    }
}
