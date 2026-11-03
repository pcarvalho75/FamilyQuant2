using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using FamilyQuant.Panels;
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
    private static IEnumerable<TestCommand> EffectivePanelFamilyCommands() =>
    [
        new("--test-effective-panel-families", 442, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-effective-panel-families", (args, _) =>
            {
                EffectivePanelFamiliesTests(args.Length > 1 ? args[1] : null);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static (FamilyClusterFile File, List<QuantRow> Rows, CurationHomologyHits Hits,
        FamilyPeptideAttribution Assigned, QuantManifest Manifest) EffectivePanelFixture()
    {
        var file = new FamilyClusterFile { FamilyPanel = new() { Families = new()
        {
            new() { Key = "main", DisplayName = "Main group", Pfam = new()
                { new() { Accession = "domain-a" }, new() { Accession = "domain-b" }, new() { Accession = "domain-unseen" } }, Subclasses = new()
                { new() { Key = "a", DisplayName = "First part", RequiredPfam = new() { "domain-a" } },
                  new() { Key = "b", DisplayName = "Second part", RequiredPfam = new() { "domain-b" } },
                  new() { Key = "unseen", DisplayName = "Unseen part", RequiredPfam = new() { "domain-unseen" } } } },
            new() { Key = "alias", DisplayName = "Reference alias", Decision = FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey = "main" },
            new() { Key = "other", DisplayName = "Other group" },
        } }, MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "P1", UserID = 11, Sequence = "AAAAAAAKCCCCCCCKGGGGGGGK" },
            new() { SequenceIdentifier = "P2", UserID = 22, Sequence = "AAAAAAAKDDDDDDDKHHHHHHHK" },
            new() { SequenceIdentifier = "P3", UserID = 33, Sequence = "EEEEEEEK" },
        } };
        for (int i = 0; i < 2; i++)
        {
            var protein = file.MyExtendedFastaItems[i]; string domain = i == 0 ? "domain-a" : "domain-b";
            protein.Evidence = new() { Reviewed = true, Pfam = new() { new() { Accession = domain } }, Positions = new()
            {
                SequenceLength = protein.Sequence.Length, SequenceSha256 = ProteinPositionFetcher.SequenceHash(protein.Sequence),
                DomainSource = new() { Provider = "fixture" }, FeatureSource = new() { Provider = "fixture" },
                Pfam = new() { new() { Accession = domain, Name = domain, Start = 1, End = 24 } },
                Features = new()
                {
                    new() { Type = "Peptide", Description = "Shared region", Start = 1, End = 8, StartModifier = "EXACT", EndModifier = "EXACT" },
                    new() { Type = "Peptide", Description = i == 0 ? "First segment" : "Second segment", Start = 9, End = 16, StartModifier = "EXACT", EndModifier = "EXACT" },
                    new() { Type = "Peptide", Description = "Unseen segment", Start = 17, End = 24, StartModifier = "EXACT", EndModifier = "EXACT" },
                },
            } };
        }
        file.PanelProteinAssignments = file.MyExtendedFastaItems.Select((p, i) => new PanelProteinAssignment
        { ProteinIndex = i, Identifier = p.SequenceIdentifier, FamilyUserId = p.UserID, IsApplied = true,
            PanelFamilyKey = i == 0 ? "main" : i == 1 ? "alias" : "other" }).ToList();
        var rows = new List<QuantRow>
        {
            Qr("identified", "run_a", 1, 2, "AAAAAAAK", 10, 10), Qr("identified", "run_a", 2, 2, "CCCCCCCK", 10, 10),
            Qr("identified", "run_a", 3, 2, "EEEEEEEK", 20, 20), Qr("identified", "run_b", 4, 2, "AAAAAAAK", 20, 20),
            Qr("identified", "run_b", 5, 2, "DDDDDDDK", 20, 20), Qr("identified", "run_b", 6, 2, "EEEEEEEK", 40, 40),
        };
        return PrepareEffectivePanelFixture(file, rows);
    }

    private static (FamilyClusterFile File, List<QuantRow> Rows, CurationHomologyHits Hits,
        FamilyPeptideAttribution Assigned, QuantManifest Manifest) PrepareEffectivePanelFixture(FamilyClusterFile file, List<QuantRow> rows)
    {
        MakePanelAssignmentsCurrent(file, new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } });
        SubclassAssignment.CommitCandidate(file, SubclassAssignment.ComputeCurrent(file));
        var hits = FamilyEnvelopeQuant.SearchCuration(rows, file);
        var assigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), FamilyEnvelopeQuant.AttributeHits(hits, file));
        var manifest = new QuantManifest { PerRunTic = new() { ["run_a.raw"] = 400, ["run_b.raw"] = 800 } };
        FamilyCompositionStudy.StoreRunShares(manifest, assigned);
        FamilyCompositionStudy.StoreProteinCounts(manifest, assigned, file.MyExtendedFastaItems);
        FamilyCompositionStudy.StoreSubclassesFromCuration(manifest, assigned, hits, file);
        FamilyCompositionStudy.StorePositionsFromCuration(manifest, assigned, hits, file);
        return (file, rows, hits, assigned, manifest);
    }

    private static void EffectivePanelFamiliesTests(string? output)
    {
        Section("Native effective panel-family study before shared-peptide credit");
        var (file, rows, hits, assigned, manifest) = EffectivePanelFixture();
        string before = JsonSerializer.Serialize(manifest);
        var mainBefore = ReportPanelSections.BuildFromResults(file, manifest).Single(s => s.Id == "family:main");
        Check("a missing effective-key study does not print two source totals as one panel family", mainBefore.Blocks.OfType<ReportTable>()
            .Count(t => t.Title == "Observed family share") <= 1);
        var producer = typeof(FamilyCompositionStudy).Assembly.GetType("FamilyQuant.EffectivePanelFamilyStudy");
        var build = producer?.GetMethod("Build");
        Check("the native effective-key producer exists", build != null);
        if (build == null) return;
        var result = (EffectivePanelFamilyResult)build.Invoke(null, new object?[] { assigned, hits, file, new[] { "run_b.raw", "run_a.raw" }, manifest.PerRunTic, SharedPeptidePolicy.FamilyUnique })!;
        var main = result.Families.Single(f => f.PanelFamilyKey == "main");
        var share = result.RunShares.Families.Single(f => f.FamilyUserId == main.StudyFamilyId);
        var count = result.ProteinCounts.Families.Single(f => f.FamilyUserId == main.StudyFamilyId);
        Check("candidate UserIDs collapse and deduplicate before family-unique credit", main.SourceUserIds.SequenceEqual(new[] { 11, 22 })
            && share.Runs.Select(r => r.Signal).SequenceEqual(new[] { 20d, 40d }) && share.MeanShare == .5 && share.StandardDeviation == 0);
        Check("the collapsed denominator credits the within-key shared peptide once", result.RunShares.Runs.Select(r => r.AssignedSignal).SequenceEqual(new[] { 40d, 80d })
            && share.PooledSignal == 60 && share.PooledShare == .5);
        Check("native protein explanation uses the same collapsed peptide membership", count.GreedyProteinCount == 2
            && count.UniquePeptideProteinCount == 2 && count.ObservedPeptideCount == 3);
        Check("building the bundle preserves every source UserID study", before == JsonSerializer.Serialize(manifest));
        manifest.EffectivePanelFamilies = result;
        var report = ReportPanelSections.BuildFromResults(file, manifest);
        var mainTables = report.Single(s => s.Id == "family:main").Blocks.OfType<ReportTable>().ToArray();
        Check("the panel brief prints one stored effective-key total", mainTables.Count(t => t.Title == "Effective panel-family share") == 1
            && mainTables.All(t => t.Title != "Observed family share") && mainTables.Any(t => t.Rows.Any(r => r.Contains("0.5"))));
        Check("merge aliases stay reference text with no extra measured total", report.Single(s => s.Id == "family:alias").Blocks.OfType<ReportTable>()
            .All(t => !t.Title.Contains("share", StringComparison.OrdinalIgnoreCase)));
        EffectivePanelFamilyBoundaries(file, rows, hits, assigned, manifest, result, output);
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "native-bundle.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
