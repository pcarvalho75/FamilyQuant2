using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> StoredCompositionCommands() => new[]
    {
        new TestCommand("--test-stored-composition-charts", 10000, TestCommandPhase.Normalized,
            args => args.Contains("--test-stored-composition-charts", StringComparer.Ordinal), (_, work) =>
            {
                StoredCompositionTests(Path.Combine(work, "stored-composition-charts"));
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            })
    };

    private static void StoredCompositionTests(string folder)
    {
        Section("Stored composition figures - single threshold accessor and localized-source integration");
        EnsureApplication(); Directory.CreateDirectory(folder);
        var (manifest, project) = StoredCompositionFixture();
        var partition = FamilyReportThreshold.Build(manifest.FamilyRunShares!, project);
        Check("the central report accessor selects family 3 from its mean share", partition.OtherFamilies.Select(f => f.FamilyUserId).SequenceEqual(new[] { 3 }));
        var figures = StoredCompositionFigures.Build(manifest, project: project);
        var ring = figures.Select(f => f.Chart).OfType<FamilyRingChart>().Single();
        Check("Results ring uses central mean membership even when that family's pooled share is largest",
            ring.Families.Single(f => f.IsOthers).Members.Single().Key == "3"
            && Math.Abs(ring.Families.Single(f => f.IsOthers).Percent - 100.0 * 600 / 1020) < 1e-10);
        var coverage = figures.Select(f => f.Chart).OfType<ProteinCoverageMapChart>().Single();
        Check("stored FL exact/aligned kinds and gap-free intervals reach the map unchanged",
            coverage.Data.Peptides.Single(p => p.Source == CoveragePeptideSource.DeNovo).Kind == CoveragePlacementKind.Aligned
            && coverage.Data.Peptides.Single(p => p.Source == CoveragePeptideSource.DeNovo).Segments.Count == 2
            && coverage.Data.RankingSignal == 10);
        var control = new AnalyticsControl(SyntheticAnalyticsRows(), manifest, project);
        Check("actual Results screen includes the stored ring and protein coverage maps",
            WalkTree(control).OfType<FamilyRingChart>().Count() == 1 && WalkTree(control).OfType<ProteinCoverageMapChart>().Count() == 1);
        string export = Path.Combine(folder, "export");
        control.ViewModel.ExportKind = ResultsExportKind.Figures; control.ViewModel.Export(export);
        Check("Results export writes the same stored ring and coverage PNG bytes",
            control.ViewModel.BuildCompositionFigures(strict: true).Where(f => f.Chart is FamilyRingChart or ProteinCoverageMapChart).All(f =>
                File.Exists(Path.Combine(export, f.FileName))
                && File.ReadAllBytes(Path.Combine(export, f.FileName)).SequenceEqual(FamilyCompositionFigures.RenderPng(f))));
        manifest.FamilyPeptideLocalization!.Families[0].Proteins[0].ProteinSequenceSha256 = new string('0', 64);
        Check("equal protein length cannot override a stale localization sequence hash",
            Throws<InvalidOperationException>(() => ProteinCoverageFigures.FromStored(manifest.FamilyPeptideLocalization, project)));
        var invalid = new AnalyticsViewModel(SyntheticAnalyticsRows(), manifest, project);
        Check("unsupported stored coverage is reported in Results instead of crashing it",
            invalid.BuildCompositionFigures().Count == 0 && invalid.Status.Contains("sequence differs"));
        invalid.ExportKind = ResultsExportKind.Figures;
        string refused = Path.Combine(folder, "refused-export");
        invalid.Export(refused);
        Check("strict figure export refuses before writing any partial figure set",
            !Directory.Exists(refused) && invalid.Status.Contains("did not finish"));
    }

    private static (QuantManifest, FamilyClusterFile) StoredCompositionFixture()
    {
        string[] runs = { "heavy", "light1", "light2" };
        double[] totals = { 1000, 10, 10 };
        double[][] shares = { new[] { .4, .55, .55 }, new[] { 0, .45, .45 }, new[] { .6, 0, 0 } };
        var perRun = new FamilyRunSharesResult();
        for (int r = 0; r < runs.Length; r++)
            perRun.Runs.Add(new FamilyRunTotal { Run = runs[r], AssignedSignal = totals[r], MeasuredSignal = totals[r] * 2 });
        for (int f = 0; f < shares.Length; f++)
        {
            double mean = shares[f].Average();
            double sd = Math.Sqrt(shares[f].Sum(x => (x - mean) * (x - mean)) / 2);
            perRun.Families.Add(new FamilyRunShareSummary
            {
                FamilyUserId = f + 1, MeanShare = mean, StandardDeviation = sd, AvailableRunCount = 3,
                CoefficientOfVariation = sd / mean,
                Runs = Enumerable.Range(0, 3).Select(r => new FamilyRunShare
                { Run = runs[r], Signal = shares[f][r] * totals[r], ShareOfAssignedSignal = shares[f][r], ShareOfMeasuredSignal = shares[f][r] / 2 }).ToList()
            });
        }
        var parts = new FamilySubclassPartsResult();
        foreach (var family in perRun.Families)
        {
            double signal = family.Runs.Sum(r => r.Signal);
            parts.Families.Add(new FamilySignalBreakdown { FamilyUserId = family.FamilyUserId, FamilySignal = signal,
                Parts = new() { new() { PartKey = "s" + family.FamilyUserId, Label = "Subclass " + family.FamilyUserId, Signal = signal, ShareOfFamily = 1 } } });
        }
        string sequence = new('A', 200), hash = ProteinCoverageMapData.SequenceHash(sequence);
        var project = new FamilyClusterFile { ReportThreshold = .25, MyExtendedFastaItems = new()
        {
            new() { UserID = 1, SequenceIdentifier = "P1", Sequence = sequence,
                Evidence = new ProteinEvidence { Positions = new ProteinPositionEvidence { Accession = "P1", SequenceLength = 200,
                    SequenceSha256 = hash, Pfam = new() { new() { Accession = "D", Name = "Domain", Start = 10, End = 70 } } } } },
            new() { UserID = 2, SequenceIdentifier = "P2", Sequence = new string('C', 200) },
            new() { UserID = 3, SequenceIdentifier = "P3", Sequence = new string('D', 200) }
        } };
        var localization = new FamilyPeptideLocalizationResult { Method = "All runs pooled; display allocation across candidate proteins and placements",
            Families = new() { new() { FamilyUserId = 1, FamilySignal = 411, UnlocalizedSignal = 401, Proteins = new()
            { new() { Rank = 1, ProteinIdentifier = "P1", ProteinLength = 200, ProteinSequenceSha256 = hash, PositionEvidenceVerified = true,
                CreditedSignal = 10, ExactSignal = 9, AlignedSignal = 1 } } },
                new() { FamilyUserId = 2, FamilySignal = 9, UnlocalizedSignal = 9 }, new() { FamilyUserId = 3, FamilySignal = 600, UnlocalizedSignal = 600 } },
            Segments = new()
            {
                new() { FamilyUserId = 1, ProteinIdentifier = "P1", Side = NativeQuantFractions.SideIdentified, NormalizedSequence = new string('A', 11),
                    Signal = 9, MatchKind = PeptideLocalizationMatchKind.ExactContainment, Intervals = new() { new() { Start = 15, End = 25 } } },
                new() { FamilyUserId = 1, ProteinIdentifier = "P1", Side = NativeQuantFractions.SideDeNovo, NormalizedSequence = "AAAATAAAAA",
                    Signal = 1, MatchKind = PeptideLocalizationMatchKind.Aligned, Intervals = new() { new() { Start = 20, End = 24 }, new() { Start = 28, End = 32 } } }
            }
        };
        return (new QuantManifest { FamilyRunShares = perRun, FamilySubclassParts = parts, FamilyPeptideLocalization = localization }, project);
    }
}
