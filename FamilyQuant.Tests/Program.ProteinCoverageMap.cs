using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [LookExportCultureCase]
    private static IEnumerable<AnalyticsFigure> LookCoverageCultureCases() => new[]
    {
        ProteinCoverageFigures.Build(CoverageFixture(rankingSignal: 1.25))
    };

    [TestCommandTable]
    private static IEnumerable<TestCommand> ProteinCoverageCommands() => new[]
    {
        new TestCommand("--test-protein-coverage-map", 10000, TestCommandPhase.Normalized,
            args => args.Contains("--test-protein-coverage-map", StringComparer.Ordinal), (_, work) =>
            {
                ProteinCoverageMapTests(Path.Combine(work, "protein-coverage-map"));
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            })
    };

    private static ProteinCoverageMapData CoverageFixture(string proteinId = "P1", int family = 1, double rankingSignal = 10,
        bool wrongPositionHash = false, bool wrongPeptideHash = false)
    {
        string sequence = new('A', 200);
        string hash = ProteinCoverageMapData.SequenceHash(sequence);
        var positions = new ProteinPositionEvidence
        {
            Accession = proteinId, SequenceLength = 200, SequenceSha256 = wrongPositionHash ? new string('0', 64) : hash,
            Pfam = new()
            {
                new() { Accession = "D1", Name = "Domain A", Start = 10, End = 70,
                    Fragments = new() { new() { Start = 10, End = 30 }, new() { Start = 50, End = 70 } } },
                new() { Accession = "D2", Name = "Domain B", Start = 120, End = 160 }
            },
            Features = new()
            {
                new() { Type = "Feature", Description = "Exact feature", Start = 1, End = 12, StartModifier = "EXACT", EndModifier = "EXACT" },
                new() { Type = "Feature", Description = "Uncertain feature", Start = 90, End = 110, StartModifier = "UNKNOWN", EndModifier = "EXACT" }
            },
            DomainSource = new() { Provider = "Stored domain source", Release = "fixture" },
            FeatureSource = new() { Provider = "Stored feature source", Release = "fixture" }
        };
        return new ProteinCoverageMapData(family, proteinId, "Protein " + proteinId, sequence, positions,
            wrongPeptideHash ? new string('0', 64) : hash, new[]
            {
                new CoveragePeptidePlacement("i1", "Identified 1", CoveragePeptideSource.Identified, CoveragePlacementKind.Exact,
                    new[] { new CoverageInterval(15, 25) }),
                new CoveragePeptidePlacement("i2", "Identified 2", CoveragePeptideSource.Identified, CoveragePlacementKind.Exact,
                    new[] { new CoverageInterval(80, 100) }),
                new CoveragePeptidePlacement("d1", "De novo 1", CoveragePeptideSource.DeNovo, CoveragePlacementKind.Aligned,
                    new[] { new CoverageInterval(20, 24), new CoverageInterval(28, 32) }),
                new CoveragePeptidePlacement("d2", "Unplaced de novo", CoveragePeptideSource.DeNovo, CoveragePlacementKind.Aligned,
                    Array.Empty<CoverageInterval>())
            }, rankingSignal);
    }

    private static void ProteinCoverageMapTests(string folder)
    {
        Section("Protein coverage map - sequence identity, disjoint blocks and honest uncertainty");
        EnsureApplication(); Directory.CreateDirectory(folder);
        var data = CoverageFixture();
        Check("domain fragments remain separate instead of filling the enclosing gap",
            data.Annotations[0].Segments.SequenceEqual(new[] { new CoverageInterval(10, 30), new CoverageInterval(50, 70) }));
        Check("uncertain feature boundaries remain listed without an exact interval",
            data.Annotations[3].Segments.Count == 0 && data.Annotations[3].Note.Contains("uncertain"));
        Check("same-length source sequence with a different SHA cannot project domain positions",
            CoverageFixture(wrongPositionHash: true).Annotations.Count == 0);
        Check("a localization sequence mismatch cannot project peptides",
            CoverageFixture(wrongPeptideHash: true).Peptides.Count == 0
            && CoverageFixture(wrongPeptideHash: true).UnplacedPeptides.Count == 4);
        Check("non-overlapping peptides are packed without merging their blocks or changing coordinates",
            data.Lanes.Count == 2 && data.Lanes.Single(l => l.Source == CoveragePeptideSource.Identified).Peptides.Count == 2
            && data.Peptides.Single(p => p.Key == "d1").Segments.Count == 2);
        var bitmap = ChartImageExporter.Render(new ProteinCoverageMapChart(data), 960, 540, 1);
        int gapX = (int)Math.Round(260 + 664 * 39.5 / 200);
        byte[] gap = new byte[4]; bitmap.CopyPixels(new Int32Rect(gapX, 166, 1, 1), gap, 4, 0);
        var background = ChartPalette.ColorOf(ChartPalette.Light.Backdrop);
        Check("native domain-gap pixels contain no domain fill",
            gap[0] == background.B && gap[1] == background.G && gap[2] == background.R);
        CheckChartNative("protein_coverage.png", () => new ProteinCoverageMapChart(data), folder,
            ProteinCoverageMapChart.RecommendedHeight(data));
        CheckChartNative("unavailable-positions.png", () => new ProteinCoverageMapChart(CoverageFixture(wrongPositionHash: true)), folder, 540);
        var selected = ProteinCoverageFigures.BuildTopPerFamily(new[]
        { CoverageFixture("Low", 1, 1), CoverageFixture("High", 1, 20), CoverageFixture("Other", 2, 5) }, 1);
        Check("top maps are selected per family from supplied localized-signal ranking",
            selected.Count == 2 && ((ProteinCoverageMapChart)selected[0].Chart).Data.ProteinId == "High"
            && ((ProteinCoverageMapChart)selected[1].Chart).Data.ProteinId == "Other");
    }
}
