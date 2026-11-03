using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ReportStoredChecks()
    {
        Section("Stored report studies - source cells, independent bases and missing values");
        var labels = new Dictionary<int, string> { [1] = "Synthetic family α", [2] = "Synthetic family β" };
        var manifest = new QuantManifest
        {
            FamilyRunShares = new()
            {
                Method = "Synthetic stored run method", Policy = SharedPeptidePolicy.FamilyUnique,
                Runs = new() { new() { Run = "r1", AssignedSignal = 100, MeasuredSignal = 200, TotalIonCurrent = 1000 },
                    new() { Run = "r2", AssignedSignal = 400, MeasuredSignal = 800, TotalIonCurrent = 4000 } },
                Families = new()
                {
                    new() { FamilyUserId = 1, AvailableRunCount = 2, MeanShare = .25, StandardDeviation = Math.Sqrt(.125),
                        CoefficientOfVariation = Math.Sqrt(.125) / .25, PooledSignal = 200, PooledShare = .4, PooledMinusMeanShare = .15,
                        MeanShareOfMeasuredSignal = .125, StandardDeviationOfMeasuredSignal = .1,
                        Runs = new() { new() { Run = "r1", Signal = 0, ShareOfAssignedSignal = 0, ShareOfMeasuredSignal = 0, ShareOfTic = 0 },
                            new() { Run = "r2", Signal = 200, ShareOfAssignedSignal = .5, ShareOfMeasuredSignal = .25, ShareOfTic = .05 } } },
                    new() { FamilyUserId = 2, AvailableRunCount = 2, MeanShare = .75, StandardDeviation = Math.Sqrt(.125),
                        CoefficientOfVariation = Math.Sqrt(.125) / .75, PooledSignal = 300, PooledShare = .6, PooledMinusMeanShare = -.15,
                        Runs = new() { new() { Run = "r1", Signal = 100, ShareOfAssignedSignal = 1, ShareOfMeasuredSignal = .5, ShareOfTic = .1 },
                            new() { Run = "r2", Signal = 200, ShareOfAssignedSignal = .5, ShareOfMeasuredSignal = .25, ShareOfTic = .05 } } },
                },
            },
            FamilyProteinCounts = new() { Method = "Synthetic stored greedy method", Families = new()
            {
                new() { FamilyUserId = 1, GreedyProteinCount = 2, UniquePeptideProteinCount = 1, ObservedPeptideCount = 3,
                    SelectedProteinIdentifiers = new() { "P1", "P2" }, UnexplainedPeptides = new() { "UNEXPLAINED" } },
            } },
            FamilySubclassParts = new() { Method = "Synthetic stored subclass method", Families = new()
            {
                new() { FamilyUserId = 1, FamilySignal = 200, Parts = new()
                {
                    new() { PartKey = "part", Label = "Supplied subclass", Signal = 80, ShareOfFamily = .4 },
                    new() { IsUnresolved = true, Label = "Subclass not resolved", Signal = 120, ShareOfFamily = .6 },
                } },
                new() { FamilyUserId = 2, FamilySignal = 300, Parts = new()
                {
                    new() { IsUnresolved = true, Label = "Subclass not resolved", Signal = 300, ShareOfFamily = 1 },
                } },
            } },
        };
        var source = ReportCurrentResults.Build(new AnalyticsData(), manifest, null);
        var grouping = FamilyReportThreshold.Build(manifest.FamilyRunShares, FamilyReportThreshold.DefaultThreshold);
        var packet = ReportStoredResults.Apply(source, manifest, labels, grouping);
        ReportTable Table(string id) => packet.Sections.SelectMany(s => s.Blocks).OfType<ReportTable>().Single(t => t.Id == id);
        Check("headline uses stored per-run mean, not pooled share", Table("family-run-means").Rows.Single(r => r[0] == labels[1])[1] == "25");
        Check("whole-signal mean and SD come from separate stored fields", Table("family-run-means").Rows.Single(r => r[0] == labels[1])[3] == "12.5"
            && Table("family-run-means").Rows.Single(r => r[0] == labels[1])[4] == "10");
        Check("pooled-minus-mean stays a separate signed diagnostic", Table("pooled-minus-mean").Rows.Single(r => r[0] == labels[1])[1] == "15"
            && Table("pooled-minus-mean").Rows.Single(r => r[0] == labels[2])[1] == "-15");
        Check("pooled subclass percentage is not multiplied by the run mean", Table("subclass-parts").Title.Contains("all runs pooled", StringComparison.Ordinal)
            && Table("subclass-parts").Rows.Single(r => r[1] == "Supplied subclass")[4] == "40");
        Check("partial protein explanation prints unexplained count and status", Table("protein-counts").Rows.Single().SequenceEqual(new[] { labels[1], "2", "1", "1", "Partial" }));
        Check("protein support retains selected identifiers and unexplained sequences", Table("protein-count-support").Rows.Single()[1] == "P1; P2"
            && Table("protein-count-support").Rows.Single()[2] == "UNEXPLAINED");
        Check("stored studies replace only their missing-study paragraphs", !packet.Sections.SelectMany(s => s.Blocks).Any(b => b.Id is "family-runs-unavailable" or "protein-counts-unavailable" or "subclasses-unavailable")
            && packet.Sections.SelectMany(s => s.Blocks).Any(b => b.Id == "domains-unavailable"));
        var meanSource = ReportCsvTable.Build("source", "Source", packet.CsvExports["family_composition_headline.csv"], "", "family", "mean_pct", "sample_sd_pct", "mean_measured_pct", "sample_sd_measured_pct", "greedy_protein_estimate", "unique_peptide_proteins");
        Check("every displayed mean statistic equals its program CSV cell", Table("family-run-means").Rows.Zip(meanSource.Rows).All(pair => pair.First.SequenceEqual(pair.Second)));
        var empty = ReportCsvTable.Build("unavailable", "Unavailable", ReportStudyExports.MeanShares(new() { Families = new() { new() { FamilyUserId = 9 } } }, labels), "");
        Check("unavailable statistics stay empty rather than becoming zero", empty.Rows.Single().Skip(3).All(v => v == ""));
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); string pt = ReportStudyExports.MeanShares(manifest.FamilyRunShares, labels);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("stored-study exports are culture invariant", pt == ReportStudyExports.MeanShares(manifest.FamilyRunShares, labels));
        }
        finally { CultureInfo.CurrentCulture = old; }
        bool invalid = false;
        try { ReportStudyExports.MeanShares(new() { Families = new() { new() { FamilyUserId = 1, MeanShare = double.NaN } } }, labels); }
        catch (InvalidDataException) { invalid = true; }
        Check("invalid stored numbers fail export", invalid);
        var conflictingProject = new FamilyClusterFile { FamilyPanel = new() { Families = new()
        {
            new() { Key = "a", DisplayName = "Source A" }, new() { Key = "b", DisplayName = "Source B" },
        } } };
        var conflictGrouping = FamilyReportThreshold.Build(manifest.FamilyRunShares, conflictingProject,
            new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "a", "b" }, [2] = new[] { "b" } });
        var conflictPacket = ReportStoredResults.Apply(source, manifest, labels, conflictGrouping);
        var headline = conflictPacket.Sections.SelectMany(s => s.Blocks).OfType<ReportTable>().Single(t => t.Id == "family-run-means");
        var conflictTable = conflictPacket.Sections.Single(s => s.Id == ReportSectionIds.AssignmentConflicts).Blocks.OfType<ReportTable>().Single();
        Check("conflicting UserID stays outside every family and Others", !headline.Rows.Any(r => r[0] == labels[1])
            && !conflictGrouping.OtherFamilies.Any(f => f.FamilyUserId == 1));
        Check("conflict section retains exact amount, keys and reason", conflictTable.Rows.Single()[0] == "1"
            && conflictTable.Rows.Single()[2] == "200" && conflictTable.Rows.Single()[3] == "a; b" && conflictTable.Rows.Single()[5].Length > 0);
        EnsureApplication();
        var rows = SyntheticAnalyticsRows();
        var project = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "P1", Sequence = "PEPTIDEK", UserID = 1 },
            new() { SequenceIdentifier = "P2", Sequence = "AAAAAAAK", UserID = 1 },
            new() { SequenceIdentifier = "P3", Sequence = "CCCCCCCK", UserID = 2 },
        } };
        string membership = FamilyStudyMembership.Fingerprint(project);
        manifest.FamilyRunShares.MembershipSha256 = manifest.FamilyProteinCounts.MembershipSha256 = manifest.FamilySubclassParts.MembershipSha256 = membership;
        var families = FamilyFixture(2);
        for (int i = 0; i < families.Families.Count; i++) families.Families[i].FamilyUserId = i + 1;
        var export = ReportProjectExporter.Build(rows, manifest, project,
            Path.Combine(Path.GetTempPath(), "fq-report-stored-" + Guid.NewGuid().ToString("N")), familyResults: families);
        Check("saved-project export includes its stored headline table", export.Document.Sections.SelectMany(s => s.Blocks).Any(b => b.Id == "family-run-means"));
    }
}
