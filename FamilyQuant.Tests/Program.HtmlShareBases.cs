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
    private static IEnumerable<TestCommand> HtmlShareBasesCommands() =>
    [new("--test-html-share-bases", 84, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-html-share-bases", (_, _) =>
        {
            HtmlShareBasesTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlShareBasesTests()
    {
        var study = new FamilyRunSharesResult
        {
            Policy = SharedPeptidePolicy.FamilyUnique,
            Runs = new()
            {
                new() { Run = "run-a", AssignedSignal = 60, MeasuredSignal = 100, IdentifiedSignal = 40, TotalIonCurrent = 1000 },
                new() { Run = "run-b", AssignedSignal = 120, MeasuredSignal = 200, IdentifiedSignal = 60, TotalIonCurrent = 2000 },
            },
            Families = new() { new() { FamilyUserId = 7, MeanShare = .5, StandardDeviation = 0, MeanShareOfMeasuredSignal = .3,
                Runs = new()
                {
                    new() { Run = "run-a", Signal = 30, IdentifiedSignal = 10, ShareOfAssignedSignal = .5, ShareOfMeasuredSignal = .3, ShareOfTic = .03 },
                    new() { Run = "run-b", Signal = 60, IdentifiedSignal = 30, ShareOfAssignedSignal = .5, ShareOfMeasuredSignal = .3, ShareOfTic = .03 },
                } } },
        };
        var manifest = new QuantManifest { FamilyRunShares = study, DerivedFractions = new()
        {
            new() { Run = "RUN-B.raw", DetectedPeptideLikeArea = 80, DetectedAreaMultiplyCharged = 70, DetectedAreaSinglyCharged = 10 },
            new() { Run = "run-a.raw", DetectedPeptideLikeArea = 50, DetectedAreaMultiplyCharged = 30, DetectedAreaSinglyCharged = 20 },
            new() { Run = "all", Grouped = true, DetectedPeptideLikeArea = 130 },
        } };
        var headline = new ReportTable("family-run-means", "Family shares", new[] { "Family", "Mean share (%)", "Sample SD (pp)", "Whole measured (%)" },
            new IReadOnlyList<string>[] { new[] { "Cedar", "50", "0", "30" } }, "Stored shares")
        { SupplementSource = new("family_user_id,family,status,mean_pct,sample_sd_pct,mean_measured_pct,mean_fraction,sample_sd_fraction,mean_measured_fraction\n7,Cedar,Listed,50,0,30,0.5,0,0.3\n",
            new[] { "family", "mean_pct", "sample_sd_pct", "mean_measured_pct" }) };
        var document = new ReportDocument { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { headline }) } };
        var table = new ReportTable("family-run-shares", "Run shares", new[] { "family_user_id", "run", "assigned_signal_pct", "measured_peptide_signal_pct", "assigned_signal_fraction", "measured_peptide_signal_fraction" },
            new IReadOnlyList<string>[] { new[] { "7", "run-a", "50", "30", "0.5", "0.3" }, new[] { "7", "run-b", "50", "30", "0.5", "0.3" } }, "Stored shares");
        var sheets = new[] { new ReportSupplementSheet(table.Id, ReportSectionIds.Results, "Run shares", table) };
        HtmlReportPayload Build() => HtmlReportPayloadBuilder.Build(document, sheets, context: new() { StoredStudies = manifest });
        string before = JsonSerializer.Serialize(manifest);
        var payload = Build();
        var family = payload.Composition.Families.Single();
        bool Equal(string? id, double expected) => id != null && payload.Fields[id].Value is double actual && Math.Abs(actual - expected) < 1e-10;
        Check("HTML four bases retain distinct stored-only per-run shares", family.Runs.Count == 2
            && Equal(family.Runs[0].IdentifiedOnly, 25) && Equal(family.Runs[0].IdentifiedAndDeNovo, 30)
            && Equal(family.Runs[0].DetectorEnvelopes, 60) && Equal(family.Runs[0].Tic, 3)
            && Equal(family.Runs[1].IdentifiedOnly, 50) && Equal(family.Runs[1].DetectorEnvelopes, 75));
        Check("HTML detector denominator uses all charges and normalized run names", Equal(family.DetectorEnvelopes.Mean, 67.5)
            && Equal(family.DetectorEnvelopes.Sd, Math.Sqrt(.01125) * 100));
        Check("HTML provides program-computed arithmetic means and sample SD for each basis",
            Equal(family.IdentifiedOnly.Mean, 37.5) && Equal(family.IdentifiedOnly.Sd, Math.Sqrt(.03125) * 100)
            && Equal(family.IdentifiedAndDeNovo.Mean, 30) && Equal(family.IdentifiedAndDeNovo.Sd, 0)
            && Equal(family.Tic.Mean, 3) && Equal(family.Tic.Sd, 0));
        Check("HTML never reuses assigned-family CV for another denominator", family.IdentifiedOnly.Cv == null
            && family.IdentifiedAndDeNovo.Cv == null && family.DetectorEnvelopes.Cv == null && family.Tic.Cv == null);
        Check("HTML share fields carry a reproducible stored-input recipe", payload.Fields[family.Runs[0].DetectorEnvelopes!].Source
            is { Kind: "family-basis", Family: "7", Run: "run-a", Property: "DetectorEnvelopes", Scale: 100 });
        Check("HTML share projection does not mutate stored studies", before == JsonSerializer.Serialize(manifest));
        string traceRoot = Path.Combine(Path.GetTempPath(), "html-share-bases");
        Directory.CreateDirectory(traceRoot);
        string word = Path.Combine(traceRoot, "report.docx"), workbook = Path.Combine(traceRoot, "report_supplement.xlsx");
        WordReportWriter.Write(word, document);
        ReportSupplementWorkbook.Write(workbook, document, sheets);
        Check("HTML all four bases and statistics trace to stored inputs without missing values",
            ReportNumberTrace.RunHtmlBindings(payload, word, workbook, new FamilyClusterFile(), manifest,
                Path.Combine(traceRoot, "trace.csv")) == 0);
        var corrupt = payload.Fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        string detectorId = family.Runs[0].DetectorEnvelopes!;
        corrupt[detectorId] = corrupt[detectorId] with { Value = 99 };
        Check("HTML trace rejects forged geometry on a derived family share",
            ReportNumberTrace.RunHtmlBindings(payload with { Fields = corrupt }, word, workbook, new FamilyClusterFile(), manifest,
                Path.Combine(traceRoot, "forged-trace.csv")) == 1);
        study.Families.AddRange(new[]
        {
            new FamilyRunShareSummary { FamilyUserId = 8, MeanShare = 7d / 24, Runs = new()
            {
                new() { Run = "run-a", Signal = 10, IdentifiedSignal = 0, ShareOfAssignedSignal = 1d / 6, ShareOfMeasuredSignal = .1, ShareOfTic = .01 },
                new() { Run = "run-b", Signal = 50, IdentifiedSignal = 20, ShareOfAssignedSignal = 5d / 12, ShareOfMeasuredSignal = .25, ShareOfTic = .025 },
            } },
            new FamilyRunShareSummary { FamilyUserId = 9, MeanShare = 5d / 24, Runs = new()
            {
                new() { Run = "run-a", Signal = 20, IdentifiedSignal = 20, ShareOfAssignedSignal = 1d / 3, ShareOfMeasuredSignal = .2, ShareOfTic = .02 },
                new() { Run = "run-b", Signal = 10, IdentifiedSignal = 0, ShareOfAssignedSignal = 1d / 12, ShareOfMeasuredSignal = .05, ShareOfTic = .005 },
            } },
        });
        var grouping = FamilyReportThreshold.Build(study, .4);
        var otherTable = headline with { Rows = headline.Rows.Append(new[] { "Others", "50", "0", "30" }).ToArray(),
            SupplementSource = new(headline.SupplementSource!.Csv + ",Others,Canonical grouped members,50,0,30,0.5,0,0.3\n", headline.SupplementSource.Columns) };
        var otherDocument = document with { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results", new ReportBlock[] { otherTable }) } };
        var otherPayload = HtmlReportPayloadBuilder.Build(otherDocument, sheets, context: new() { StoredStudies = manifest, SourceGrouping = grouping });
        var others = otherPayload.Composition.Families.Single(f => f.IsOthers);
        double? OtherValue(string? id) => id == null ? null : otherPayload.Fields[id].Value;
        Check("HTML canonical Others retains the exact native membership for all four bases", others.Runs.Count == 2
            && otherPayload.Fields[others.Runs[0].IdentifiedOnly!].Source.Family == "others:8,9"
            && grouping.OtherFamilies.Select(f => f.FamilyUserId).SequenceEqual(new[] { 8, 9 })
            && Math.Abs(OtherValue(others.IdentifiedOnly.Mean)!.Value - (0.5 + 1d / 3) / 2 * 100) < 1e-10
            && Math.Abs(OtherValue(others.IdentifiedAndDeNovo.Mean)!.Value - 30) < 1e-10
            && Math.Abs(OtherValue(others.DetectorEnvelopes.Mean)!.Value - 67.5) < 1e-10
            && Math.Abs(OtherValue(others.Tic.Mean)!.Value - 3) < 1e-10);
        Check("HTML canonical Others SD uses the grouped run sums, preserving covariance",
            Math.Abs(OtherValue(others.IdentifiedOnly.Sd)!.Value - (0.5 - 1d / 3) / Math.Sqrt(2) * 100) < 1e-10
            && Math.Abs(OtherValue(others.IdentifiedAndDeNovo.Sd)!.Value) < 1e-10);
        WordReportWriter.Write(word, otherDocument);
        ReportSupplementWorkbook.Write(workbook, otherDocument, sheets);
        Check("HTML canonical Others ratios and statistics trace to stored member inputs",
            ReportNumberTrace.RunHtmlBindings(otherPayload, word, workbook, new FamilyClusterFile(), manifest,
                Path.Combine(traceRoot, "others-trace.csv")) == 0);
        manifest.EffectivePanelFamilies = new() { RunShares = study };
        var panelDocument = otherDocument with { Sections = new[] { new ReportSection(ReportSectionIds.Results, "Results",
            new ReportBlock[] { otherTable with { Id = "panel-family-run-means" } }) } };
        var panelPayload = HtmlReportPayloadBuilder.Build(panelDocument, sheets, context: new()
        {
            StoredStudies = manifest, UsesPanel = true, IsIdentity = false,
            SourceGrouping = new() { OtherFamilies = new() { new() { FamilyUserId = 108 }, new() { FamilyUserId = 109 } } },
            SourceUserIds = new Dictionary<int, IReadOnlyList<int>> { [7] = new[] { 107 }, [8] = new[] { 108 }, [9] = new[] { 109 } },
        });
        var panelOthers = panelPayload.Composition.Families.Single(f => f.IsOthers);
        Check("HTML collapsed-panel Others maps canonical source IDs to the stored effective IDs",
            panelOthers.Runs.Count == 2 && panelPayload.Fields[panelOthers.Runs[0].IdentifiedOnly!].Source
                is { Study: "effective", Family: "others:8,9" }
            && Math.Abs(panelPayload.Fields[panelOthers.DetectorEnvelopes.Mean!].Value!.Value - 67.5) < 1e-10);
        study.Runs[1].Run = "different-run";
        var mismatched = Build().Composition.Families.Single();
        Check("HTML statistics require matching run identities, not only equal row counts",
            mismatched.IdentifiedAndDeNovo is { Mean: null, Sd: null } && mismatched.Tic is { Mean: null, Sd: null });
        study.Runs[1].Run = "run-b";
        manifest.DerivedFractions.RemoveAt(0);
        var missingDetector = Build().Composition.Families.Single();
        Check("HTML missing detector stays unavailable for its run and complete-run statistics",
            missingDetector.Runs[0].DetectorEnvelopes != null && missingDetector.Runs[1].DetectorEnvelopes == null
            && missingDetector.DetectorEnvelopes is { Mean: null, Sd: null });
        foreach (var run in study.Runs) run.IdentifiedSignal = null;
        foreach (var run in study.Families[0].Runs) run.IdentifiedSignal = null;
        var legacy = Build().Composition.Families.Single();
        Check("HTML old studies keep PSM only unavailable without losing other bases",
            legacy.Runs.All(r => r.IdentifiedOnly == null && r.IdentifiedAndDeNovo != null && r.Tic != null)
            && legacy.IdentifiedOnly is { Mean: null, Sd: null });
        Check("HTML old stored JSON leaves identified signal unavailable",
            JsonSerializer.Deserialize<FamilyRunShare>("{\"Signal\":12}")!.IdentifiedSignal == null
            && JsonSerializer.Deserialize<FamilyRunTotal>("{\"MeasuredSignal\":20}")!.IdentifiedSignal == null);
        study.Policy = SharedPeptidePolicy.CountInEveryFamily;
        var overlap = Build().Composition.Families.Single();
        Check("HTML refuses overlapping full counts for every new basis", overlap.Runs.All(r =>
            r.IdentifiedOnly == null && r.IdentifiedAndDeNovo == null && r.DetectorEnvelopes == null && r.Tic == null));

        var map = new PeptideFamilyAttribution();
        map.IdentifiedBySequence["AAAAAAAK"] = new[] { 1 };
        map.IdentifiedBySequence["CCCCCCCK"] = new[] { 1, 2 };
        var attribution = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(new[]
        {
            Qr("identified", "run-a", 1, 2, "AAAAAAAK", 10, 10),
            Qr("identified", "run-a", 2, 2, "CCCCCCCK", 20, 20),
            Qr("identified", "run-a", 3, 2, "DDDDDDDK", 5, 5),
        }), map);
        foreach (var policy in new[] { SharedPeptidePolicy.FamilyUnique, SharedPeptidePolicy.SplitEqually })
        {
            var computed = FamilyCompositionStudy.BuildRunShares(attribution, policy: policy);
            double expected = policy == SharedPeptidePolicy.SplitEqually ? 20 : 10;
            Check("Quantify stores identified numerator and each-peptide-once denominator under " + policy,
                computed.Runs.Single().IdentifiedSignal == 35
                && computed.Families.Single(f => f.FamilyUserId == 1).Runs.Single().IdentifiedSignal == expected);
        }
    }
}
