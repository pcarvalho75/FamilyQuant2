using FamilyQuant.Report;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportPairedSummaryCommands() =>
    [
        new("--test-report-paired-summary", 10, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-report-paired-summary", (_, _) =>
            {
                ReportPairedSummaryTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void ReportPairedSummaryTests()
    {
        var labels = new Dictionary<int, string> { [1] = "Group A", [2] = "Group B", [3] = "Group hidden",
            [4] = "Group without count", [5] = "Group unavailable", [6] = "Group at threshold" };
        var runs = new FamilyRunSharesResult { Runs = new() { new() { Run = "run", AssignedSignal = 100, MeasuredSignal = 200 } } };
        void Add(int id, double? mean) => runs.Families.Add(new FamilyRunShareSummary
        {
            FamilyUserId = id, MeanShare = mean, AvailableRunCount = mean.HasValue ? 1 : 0,
            PooledShare = mean.HasValue ? .04 : null,
            Runs = new() { new() { Run = "run", Signal = mean.GetValueOrDefault() * 100,
                ShareOfAssignedSignal = mean, ShareOfMeasuredSignal = mean / 2 } }
        });
        Add(1, .294123456); Add(2, .699376544); Add(3, .0005); Add(4, .005); Add(5, null); Add(6, .001);
        var counts = new FamilyProteinCountsResult { Families = new()
        {
            new() { FamilyUserId = 1, GreedyProteinCount = 41 }, new() { FamilyUserId = 2, GreedyProteinCount = 1 },
            new() { FamilyUserId = 3, GreedyProteinCount = 12 }, new() { FamilyUserId = 6, GreedyProteinCount = 0 },
        } };
        var manifest = new QuantManifest { FamilyRunShares = runs, FamilyProteinCounts = counts };
        var grouping = FamilyReportThreshold.Build(runs, .001);
        ReportResultPacket Build() => ReportStoredResults.Apply(ReportCurrentResults.Build(new AnalyticsData(), manifest, null), manifest, labels, grouping);
        var packet = Build();
        var blocks = packet.Sections.Single(s => s.Id == ReportSectionIds.Results).Blocks;
        var intro = blocks.OfType<ReportParagraph>().SingleOrDefault(p => p.Id == "family-paired-intro");
        var lines = blocks.OfType<ReportParagraph>().Where(p => p.Id.StartsWith("family-paired:", StringComparison.Ordinal)).ToArray();
        var headline = blocks.OfType<ReportTable>().Single(t => t.Id == "family-run-means");
        var source = ReportCsvTable.Build("source", "Source", packet.CsvExports["family_composition_headline.csv"], "",
            "family_user_id", "family", "status", "mean_pct", "greedy_protein_estimate");
        var countSource = ReportCsvTable.Build("counts", "Counts", packet.CsvExports["family_protein_counts.csv"], "",
            "family_user_id", "greedy_protein_estimate");
        var listed = source.Rows.Where(r => r[2] == "Listed").ToArray();
        string Expected(IReadOnlyList<string> row) => row[1] + " " + row[3] + "% ("
            + (row[4].Length == 0 ? "protein count unavailable" : row[4] + (row[4] == "1" ? " protein" : " proteins")) + ")";
        Check("J25 paired lines follow the headline order and exact share/count cells",
            lines.Select(x => x.Text).SequenceEqual(listed.Select(Expected)) && lines.Length == 4
            && listed.All(row => headline.Rows.Any(h => h[0] == row[1] && h[1] == row[3] && h[5] == row[4])));
        Check("J25 available paired counts equal the native count export", listed.Where(r => r[4].Length > 0)
            .All(row => lines.Any(line => line.Text == Expected(row))
                && countSource.Rows.Any(count => count[0] == row[0] && count[1] == row[4])));
        Check("J25 missing protein counts remain unavailable rather than zero", lines.Any(p => p.Text == labels[4] + " 0.5% (protein count unavailable)")
            && lines.Any(p => p.Text == labels[6] + " 0.1% (0 proteins)"));
        Check("J25 under-threshold and unavailable families have no paired line", lines.Length > 0 && lines.All(p => !p.Text.Contains(labels[3], StringComparison.Ordinal)
            && !p.Text.Contains(labels[5], StringComparison.Ordinal)));
        var panel = new FamilyClusterFile { FamilyPanel = new FamilyPanel { Families = new()
        {
            new() { Key = "a", DisplayName = "Choice A" }, new() { Key = "b", DisplayName = "Choice B" },
            new() { Key = "other", DisplayName = "Choice C", Decision = FamilyPanelDecision.ReportUnderOthers },
        } } };
        var alternate = FamilyReportThreshold.Build(runs, panel, new Dictionary<int, IReadOnlyList<string>>
        { [1] = new[] { "a", "b" }, [2] = new[] { "other" } });
        var alternatePacket = ReportStoredResults.Apply(ReportCurrentResults.Build(new AnalyticsData(), manifest, null), manifest, labels, alternate);
        var alternateLines = alternatePacket.Sections.Single(s => s.Id == ReportSectionIds.Results).Blocks.OfType<ReportParagraph>()
            .Where(p => p.Id.StartsWith("family-paired:", StringComparison.Ordinal)).ToArray();
        Check("J25 panel-directed Others and conflicting assignments are not paired as listed families", alternateLines.Length == 2
            && alternateLines.All(p => !p.Text.Contains(labels[1], StringComparison.Ordinal) && !p.Text.Contains(labels[2], StringComparison.Ordinal)));
        Check("J25 introduction names credited signal and the greedy estimate, not gene counts", intro != null
            && intro.Text.Contains("credited", StringComparison.OrdinalIgnoreCase) && intro.Text.Contains("signal", StringComparison.OrdinalIgnoreCase)
            && intro.Text.Contains("greedy", StringComparison.OrdinalIgnoreCase) && intro.Text.Contains("database", StringComparison.OrdinalIgnoreCase)
            && intro.Text.Contains("peptides", StringComparison.OrdinalIgnoreCase)
            && intro.Text.Contains("not counts of genes", StringComparison.OrdinalIgnoreCase));
        Check("J25 paired explanation follows the headline table", intro != null && blocks.ToList().IndexOf(intro) == blocks.ToList().IndexOf(headline) + 1);
        var document = ReportSupplementLayout.Apply(new ReportDocument { Sections = packet.Sections }, packet.CsvExports).Document;
        byte[] word = WordReportWriter.BuildBytes(document);
        using var archive = new ZipArchive(new MemoryStream(word));
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        var xml = XDocument.Parse(reader.ReadToEnd());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var paragraphs = xml.Descendants(w + "p").Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value))).ToArray();
        Check("J25 Word report prints the introduction and each paired line", intro != null && paragraphs.Contains(intro.Text)
            && lines.Length == 4 && lines.All(line => paragraphs.Contains(line.Text)));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); var br = Build();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); var us = Build();
            string[] Paired(ReportResultPacket p) => p.Sections.Single(s => s.Id == ReportSectionIds.Results).Blocks
                .OfType<ReportParagraph>().Where(x => x.Id == "family-paired-intro" || x.Id.StartsWith("family-paired:", StringComparison.Ordinal))
                .Select(x => x.Text).ToArray();
            Check("J25 paired output is culture invariant", Paired(br).Length == 5 && Paired(br).SequenceEqual(Paired(us)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
