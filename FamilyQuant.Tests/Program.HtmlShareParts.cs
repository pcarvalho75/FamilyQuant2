using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlSharePartsCommands() =>
    [new("--test-html-share-parts", 84, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-html-share-parts", (args, _) =>
        {
            HtmlSharePartsTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "html-share-parts"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static (ReportDocument Document, ReportSupplementSheet[] Sheets, HtmlReportContext Context) HtmlSharePartsFixture()
    {
        FamilyRunShare Run(string name, double signal, double identified, double assigned, double measured, double tic) => new()
        {
            Run = name, Signal = signal, IdentifiedSignal = identified, ShareOfAssignedSignal = signal / assigned,
            ShareOfMeasuredSignal = signal / measured, ShareOfTic = signal / tic,
        };
        var study = new FamilyRunSharesResult { Policy = SharedPeptidePolicy.FamilyUnique, Runs = new()
        {
            new() { Run = "run-a", AssignedSignal = 50, MeasuredSignal = 100, IdentifiedSignal = 40, TotalIonCurrent = 1000 },
            new() { Run = "run-b", AssignedSignal = 180, MeasuredSignal = 400, IdentifiedSignal = 100, TotalIonCurrent = 2000 },
        }, Families = new()
        {
            new() { FamilyUserId = 7, Runs = new() { Run("run-a", 30, 10, 50, 100, 1000), Run("run-b", 100, 40, 180, 400, 2000) } },
            new() { FamilyUserId = 8, Runs = new() { Run("run-a", 5, 2, 50, 100, 1000), Run("run-b", 30, 10, 180, 400, 2000) } },
            new() { FamilyUserId = 9, Runs = new() { Run("run-a", 15, 3, 50, 100, 1000), Run("run-b", 50, 20, 180, 400, 2000) } },
        } };
        foreach (var family in study.Families)
        {
            family.MeanShare = family.Runs.Average(r => r.ShareOfAssignedSignal!.Value);
            family.MeanShareOfMeasuredSignal = family.Runs.Average(r => r.ShareOfMeasuredSignal!.Value);
        }
        var grouping = FamilyReportThreshold.Build(study, .4);
        string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        string Percent(double value) => (value * 100).ToString("G6", CultureInfo.InvariantCulture);
        double mean = study.Families[0].MeanShare!.Value, measured = study.Families[0].MeanShareOfMeasuredSignal!.Value;
        double otherMean = grouping.OthersAssignedStatistics.Mean!.Value, otherMeasured = grouping.OthersMeasuredStatistics.Mean!.Value;
        string native = "family_user_id,family,status,mean_pct,mean_measured_pct,mean_fraction,mean_measured_fraction\n"
            + "7,Family A,Listed," + Percent(mean) + "," + Percent(measured) + "," + Number(mean) + "," + Number(measured) + "\n"
            + ",Others,Canonical grouped members," + Percent(otherMean) + "," + Percent(otherMeasured) + "," + Number(otherMean) + "," + Number(otherMeasured) + "\n";
        var headline = new ReportTable("family-run-means", "Stored family shares", new[] { "Family", "Mean share (%)", "Whole measured (%)" },
            new IReadOnlyList<string>[] { new[] { "Family A", Percent(mean), Percent(measured) }, new[] { "Others", Percent(otherMean), Percent(otherMeasured) } }, "Stored fixture shares")
        { SupplementSource = new(native, new[] { "family", "mean_pct", "mean_measured_pct" }) };
        var document = new ReportDocument { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { headline }) } };
        var manifest = new QuantManifest { FamilyRunShares = study, DerivedFractions = new()
        {
            new() { Run = "RUN-B.raw", DetectedPeptideLikeArea = 200, DetectedAreaMultiplyCharged = 150, DetectedAreaSinglyCharged = 50 },
            new() { Run = "run-a.raw", DetectedPeptideLikeArea = 80, DetectedAreaMultiplyCharged = 50, DetectedAreaSinglyCharged = 30 },
        } };
        return (document, Array.Empty<ReportSupplementSheet>(), new() { StoredStudies = manifest, SourceGrouping = grouping });
    }

    private static void HtmlSharePartsTests(string output)
    {
        Directory.CreateDirectory(output);
        HtmlPlainPercentLabelTests(output);
        var fixture = HtmlSharePartsFixture();
        var manifest = fixture.Context.StoredStudies!;
        HtmlReportPayload Build(HtmlReportContext? context = null) => HtmlReportPayloadBuilder.Build(fixture.Document, fixture.Sheets, context: context ?? fixture.Context);
        string before = JsonSerializer.Serialize(manifest);
        var payload = Build();
        var family = payload.Composition.Families.Single(f => !f.IsOthers);
        var others = payload.Composition.Families.Single(f => f.IsOthers);
        var contributions = payload.Composition.ContributionRows;
        Check("T11b contribution rows preserve canonical ranking order plus both residual rows",
            contributions.Count == payload.Composition.Families.Count + 2
            && contributions.Take(payload.Composition.Families.Count).Select(row => row.Id)
                .SequenceEqual(payload.Composition.Families.Select(row => row.Id))
            && contributions.Select(row => row.Kind).SequenceEqual(new[] { "family", "others", "shared", "unattributed" }));
        Check("T11b residual rows have program labels and explicitly unavailable bases", contributions.Skip(2).All(row =>
            row.IdentifiedOnly == null && row.IdentifiedAndDeNovo == null && row.DetectorEnvelopes == null && row.Tic == null)
            && payload.Fields[contributions[2].Name].Text == "shared between families"
            && payload.Fields[contributions[3].Name].Text == "unattributed");
        bool Parts(HtmlReportPayload data, HtmlFamilyMeanParts? parts, double identified, double deNovo) => parts != null
            && Math.Abs(data.Fields[parts.Identified].Value!.Value - identified) < 1e-10
            && Math.Abs(data.Fields[parts.DeNovo].Value!.Value - deNovo) < 1e-10;
        Check("T11b PSM-only means have an identified part and zero de novo", Parts(payload, family.IdentifiedOnlyParts, 32.5, 0));
        Check("T11b combined parts are means of run shares, not pooled ratios", Parts(payload, family.IdentifiedAndDeNovoParts, 10, 17.5));
        Check("T11b detector parts use all-charge stored areas and normalized run names", Parts(payload, family.DetectorEnvelopesParts, 16.25, 27.5));
        Check("T11b TIC parts divide by each run's stored TIC", Parts(payload, family.TicParts, 1.5, 2.5));
        foreach (var row in new[] { family, others })
        {
            var contribution = contributions.Single(item => item.Id == row.Id);
            foreach (var (name, statistics, parts) in new[]
            {
                ("PSM only", row.IdentifiedOnly, row.IdentifiedOnlyParts),
                ("PSM and de novo", row.IdentifiedAndDeNovo, row.IdentifiedAndDeNovoParts),
                ("detector", row.DetectorEnvelopes, row.DetectorEnvelopesParts), ("TIC", row.Tic, row.TicParts),
            })
                Check("T11b " + row.Id + " " + name + " parts sum to the existing mean", parts != null && statistics.Mean != null
                    && Math.Abs(payload.Fields[parts.Identified].Value!.Value + payload.Fields[parts.DeNovo].Value!.Value
                        - payload.Fields[statistics.Mean].Value!.Value) < 1e-10);
            Check("T11b " + row.Id + " contribution totals and parts are supplied as field IDs without browser aggregation",
                contribution.Name == row.Name
                && contribution.IdentifiedOnly == new HtmlContributionShares(row.IdentifiedOnly.Mean, row.IdentifiedOnlyParts!.Identified, row.IdentifiedOnlyParts.DeNovo)
                && contribution.IdentifiedAndDeNovo == new HtmlContributionShares(row.IdentifiedAndDeNovo.Mean, row.IdentifiedAndDeNovoParts!.Identified, row.IdentifiedAndDeNovoParts.DeNovo)
                && contribution.DetectorEnvelopes == new HtmlContributionShares(row.DetectorEnvelopes.Mean, row.DetectorEnvelopesParts!.Identified, row.DetectorEnvelopesParts.DeNovo)
                && contribution.Tic == new HtmlContributionShares(row.Tic.Mean, row.TicParts!.Identified, row.TicParts.DeNovo));
        }
        Check("T11b canonical Others keeps both grouped run components", Parts(payload, others.IdentifiedOnlyParts, 21.25, 0)
            && Parts(payload, others.IdentifiedAndDeNovoParts, 6.25, 13.75)
            && Parts(payload, others.DetectorEnvelopesParts, 10.625, 21.875) && Parts(payload, others.TicParts, 1, 2)
            && payload.Fields[others.TicParts!.Identified].Source.Family == "others:8,9");
        Check("T11b stored studies are not mutated", before == JsonSerializer.Serialize(manifest));
        Check("T11b parts have exact stored-input provenance", payload.Fields[family.IdentifiedAndDeNovoParts!.DeNovo].Source
            is { Kind: "family-basis", Family: "7", Property: "IdentifiedAndDeNovo/denovo-mean", Scale: 100 });
        string word = Path.Combine(output, "report.docx"), workbook = Path.Combine(output, "report_supplement.xlsx");
        WordReportWriter.Write(word, fixture.Document);
        ReportSupplementWorkbook.Write(workbook, fixture.Document, fixture.Sheets);
        Check("T11b family and canonical Others mean parts trace without missing bindings",
            ReportNumberTrace.RunHtmlBindings(payload, word, workbook, new FamilyClusterFile(), manifest, Path.Combine(output, "trace.csv")) == 0);
        var forged = payload.Fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        string partId = family.TicParts!.DeNovo;
        forged[partId] = forged[partId] with { Value = 99 };
        Check("T11b numeric trace rejects a forged part", ReportNumberTrace.RunHtmlBindings(payload with { Fields = forged }, word, workbook,
            new FamilyClusterFile(), manifest, Path.Combine(output, "forged-trace.csv")) == 1);
        var dangling = contributions[0] with { Tic = contributions[0].Tic! with { Total = "missing-total-field" } };
        Check("T11b numeric trace checks contribution-row field references", ReportNumberTrace.RunHtmlBindings(payload with
            { Composition = payload.Composition with { ContributionRows = new[] { dangling }.Concat(contributions.Skip(1)).ToArray() } },
            word, workbook, new FamilyClusterFile(), manifest, Path.Combine(output, "missing-total-trace.csv")) == 1);
        manifest.FamilyWorldResult = new() { SharedIdentifiedEnvelope = 5, SharedDatabaseDeNovoEnvelope = 7, UnattributedIdentifiedEnvelope = 11 };
        var pooledWorld = Build();
        Check("T11b missing per-run buckets stay unavailable even with pooled World totals", new[]
        {
            pooledWorld.Composition.SharedBetweenFamilies, pooledWorld.Composition.Unattributed,
        }.All(bucket => bucket is { IdentifiedOnly: null, IdentifiedAndDeNovo: null, DetectorEnvelopes: null, Tic: null }));
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })))
        {
            var first = json.RootElement.GetProperty("composition").GetProperty("families")[0];
            Check("T11b JSON exposes the four named parts records and both field IDs", new[]
            { "identifiedOnlyParts", "identifiedAndDeNovoParts", "detectorEnvelopesParts", "ticParts" }.All(name =>
                first.GetProperty(name).GetProperty("identified").ValueKind == JsonValueKind.String
                && first.GetProperty(name).GetProperty("deNovo").ValueKind == JsonValueKind.String));
            var contribution = json.RootElement.GetProperty("composition").GetProperty("contributionRows")[0];
            Check("T11b JSON contribution bases expose total, identified and deNovo fields", new[]
            { "identifiedOnly", "identifiedAndDeNovo", "detectorEnvelopes", "tic" }.All(name =>
                new[] { "total", "identified", "deNovo" }.All(part => contribution.GetProperty(name).GetProperty(part).ValueKind == JsonValueKind.String)));
        }
        var headlineOnly = Build(fixture.Context with { FamilySections = new Dictionary<string, string> { ["7"] = "family:unchecked" } });
        Check("T11b contribution parts remain available independently of family-detail selection",
            headlineOnly.Composition.ContributionRows[0].Tic is { Total: not null, Identified: not null, DeNovo: not null });
        var unselected = HtmlReportPayloadBuilder.Build(fixture.Document, fixture.Sheets,
            new() { ExcludedIds = new[] { ReportSectionIds.Results } }, fixture.Context);
        Check("T11b unselected Results does not allocate contribution rows", unselected.Composition.ContributionRows.Count == 0);
        var noStudies = Build(fixture.Context with { StoredStudies = null });
        Check("T11b unavailable stored studies leave every contribution base null", noStudies.Composition.ContributionRows.All(row =>
            row.IdentifiedOnly == null && row.IdentifiedAndDeNovo == null && row.DetectorEnvelopes == null && row.Tic == null));
        var allRows = HtmlContributionAllRowsFixture();
        Check("T11b contribution has no independent Top N fold", allRows.Composition.Families.Count == 17
            && allRows.Composition.ContributionRows.Count == 19 && allRows.Composition.ContributionRows.Count(row => row.Kind == "family") == 17
            && allRows.Composition.ContributionRows.Take(17).Select(row => row.Id).SequenceEqual(allRows.Composition.Families.Select(row => row.Id)));
        manifest.DerivedFractions.RemoveAt(0);
        var missingDetectorPayload = Build();
        var missingDetector = missingDetectorPayload.Composition.Families;
        Check("T11b missing detector run makes detector parts unavailable for families and Others",
            missingDetector.All(f => f.DetectorEnvelopesParts == null) && missingDetector.All(f => f.TicParts != null));
        Check("T11b missing detector bases are null in contribution rows", missingDetectorPayload.Composition.ContributionRows.All(row => row.DetectorEnvelopes == null));
        manifest.FamilyRunShares!.Families[0].Runs[0].IdentifiedSignal = null;
        var missingIdentifiedPayload = Build();
        var missingIdentified = missingIdentifiedPayload.Composition.Families.Single(f => !f.IsOthers);
        Check("T11b missing identified inputs never imply a zero de novo part", missingIdentified.IdentifiedOnlyParts == null
            && missingIdentified.IdentifiedAndDeNovoParts == null && missingIdentified.TicParts == null);
        Check("T11b legacy contribution retains a known total without inventing its parts",
            missingIdentifiedPayload.Composition.ContributionRows[0].Tic is { Total: not null, Identified: null, DeNovo: null }
            && missingIdentifiedPayload.Composition.ContributionRows[0].IdentifiedOnly == null);
    }

    private static void HtmlPlainPercentLabelTests(string output)
    {
        var fixture = HtmlSharePartsFixture();
        var manifest = fixture.Context.StoredStudies!;
        foreach (var run in manifest.FamilyRunShares!.Families.Single(family => family.FamilyUserId == 7).Runs)
            run.IdentifiedSignal = run.Signal - 9.76e-8 * manifest.FamilyRunShares.Runs.Single(total => total.Run == run.Run).MeasuredSignal;
        string before = JsonSerializer.Serialize(new { fixture.Document, manifest });
        var payload = HtmlReportPayloadBuilder.Build(fixture.Document, fixture.Sheets, context: fixture.Context);
        var family = payload.Composition.Families.Single(family => family.Id == "7");
        var part = payload.Fields[family.IdentifiedAndDeNovoParts!.DeNovo];
        Check("CT4 tiny family percent parts print three significant plain decimal digits", payload.Fields[part.Short!].Text == "0.00000976");
        Check("CT4 exact numeric part and G6 text stay unchanged", part.Text == "9.76E-06"
            && part.Value == HtmlReportPayloadBuilder.ReadFamilyShare(part.Source, manifest) * 100
            && payload.Fields[part.Short!].Value == part.Value);
        Check("CT4 true-zero family parts keep their existing display", payload.Fields[payload.Fields[family.IdentifiedOnlyParts!.DeNovo].Short!].Text == "0");
        Check("CT4 formatting preserves source tables and stored quantities", before == JsonSerializer.Serialize(new { fixture.Document, manifest }));
        string word = Path.Combine(output, "plain-percent.docx"), workbook = ReportSupplementWorkbook.PathForReport(word);
        WordReportWriter.Write(word, fixture.Document); ReportSupplementWorkbook.Write(workbook, fixture.Document, fixture.Sheets);
        Check("CT4 plain percent labels trace to their unchanged native quantity", ReportNumberTrace.RunHtmlBindings(payload, word, workbook, new(), manifest,
            Path.Combine(output, "plain-percent-trace.csv")) == 0);
        var fields = payload.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        fields[part.Short!] = fields[part.Short!] with { Text = part.Value!.Value.ToString("G3", CultureInfo.InvariantCulture) };
        Check("CT4 trace still accepts legacy G3 labels", ReportNumberTrace.RunHtmlBindings(payload with { Fields = fields }, word, workbook, new(), manifest,
            Path.Combine(output, "legacy-percent-trace.csv")) == 0);
        fields[part.Short!] = fields[part.Short!] with { Text = "0.00000977" };
        Check("CT4 trace rejects an incorrect plain decimal label", ReportNumberTrace.RunHtmlBindings(payload with { Fields = fields }, word, workbook, new(), manifest,
            Path.Combine(output, "forged-percent-trace.csv")) == 1);
        foreach (var (value, expected) in new[] { (9.764e-6, "0.00000976"), (9.766e-6, "0.00000977"),
            (-9.76e-6, "-0.00000976"), (1.23456e8, "123000000"), (.000153, "0.000153"), (0d, "0") })
            Check("CT4 plain short format " + expected, HtmlReportPayloadBuilder.FormatShortNumber(value) == expected);
        HtmlReportBrowserChecks(Path.Combine(output, "plain-percent-browser"), new[] { ("phone", payload, 390), ("desktop", payload, 1366) }, """
            const checks={},row=d.querySelector('#family-contribution-chart [data-row="7"]');
            const label=row.querySelector('.contribution-values').textContent.replace(/\s+/g,' ');
            checks.plainPercent=label.includes('De novo 0.00000976 %')&&!/[eE][+-]?\d/.test(label);
            d.querySelector('input[name="normalization-basis"][value="identifiedOnly"]').click();
            checks.zero=d.querySelector('#family-contribution-chart [data-row="7"] .contribution-values').textContent.replace(/\s+/g,' ').includes('De novo 0 %');
            checks.noOverflow=d.documentElement.scrollWidth<=w.innerWidth+1;
            return checks;
            """);
    }

    private static HtmlReportPayload HtmlContributionAllRowsFixture()
    {
        const double share = 1d / 17;
        string percent = (share * 100).ToString("G6", CultureInfo.InvariantCulture), fraction = share.ToString("R", CultureInfo.InvariantCulture);
        var study = new FamilyRunSharesResult { Policy = SharedPeptidePolicy.FamilyUnique,
            Runs = new() { new() { Run = "run-a", AssignedSignal = 17, MeasuredSignal = 17, IdentifiedSignal = 17, TotalIonCurrent = 34 } },
            Families = Enumerable.Range(1, 17).Select(id => new FamilyRunShareSummary
            {
                FamilyUserId = id, MeanShare = share, MeanShareOfMeasuredSignal = share,
                Runs = new() { new() { Run = "run-a", Signal = 1, IdentifiedSignal = 1,
                    ShareOfAssignedSignal = share, ShareOfMeasuredSignal = share, ShareOfTic = 1d / 34 } },
            }).ToList(),
        };
        var ids = Enumerable.Range(1, 17).Reverse().ToArray();
        string native = "family_user_id,family,status,mean_pct,mean_measured_pct,mean_fraction,mean_measured_fraction\n"
            + string.Concat(ids.Select(id => id.ToString(CultureInfo.InvariantCulture) + ",Family " + id.ToString(CultureInfo.InvariantCulture)
                + ",Listed," + percent + "," + percent + "," + fraction + "," + fraction + "\n"));
        var table = new ReportTable("family-run-means", "All canonical families", new[] { "Family", "Mean share (%)", "Whole measured (%)" },
            ids.Select(id => (IReadOnlyList<string>)new[] { "Family " + id.ToString(CultureInfo.InvariantCulture), percent, percent }).ToArray(), "All listed families")
        { SupplementSource = new(native, new[] { "family", "mean_pct", "mean_measured_pct" }) };
        var document = new ReportDocument { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { table }) } };
        return HtmlReportPayloadBuilder.Build(document, Array.Empty<ReportSupplementSheet>(), context: new() { StoredStudies = new() { FamilyRunShares = study } });
    }
}
