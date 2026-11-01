using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;

namespace FamilyQuant.Tests;

// Linked into the frozen host. The figures are synthetic fixtures, never delivered results.
internal static class ChartExportSnapshot
{
    internal const string Argument = "--chart-export-snapshot";

    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply an output folder.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Directory.CreateDirectory(args[1]);
        foreach (bool empty in new[] { false, true })
        foreach (var variant in new[] { ChartPaletteVariant.Light, ChartPaletteVariant.Dark })
        foreach (var size in new[] { (Width: 960.0, Height: 540.0), (Width: 420.0, Height: 300.0) })
        {
            var peptides = new List<AnalyticsPeptide>();
            var fractions = new List<NativeRunFraction>();
            var families = new List<FamilyContributionBar>();
            var sweep = new List<IdentityScanRow>();
            var partition = new List<ThreeWayPartitionRow>();
            if (!empty)
            {
                for (int i = 1; i <= 4; i++)
                {
                    peptides.Add(new AnalyticsPeptide { Mass = 500 * i, Envelope = 150 * i,
                        Areas = new[] { 100.0 * i, 40.0 * i, 10.0 * i },
                        Side = i % 2 == 0 ? "identified" : "denovo" });
                    sweep.Add(new IdentityScanRow { Identity = 100 - 15 * i, Groups = 200 / i,
                        TargetGroups = 120 / i, MixedGroups = i - 1, Score = (i - 1) / 100.0 });
                    families.Add(new FamilyContributionBar { Label = "Fixture family " + i,
                        Identified = 200 * i, DeNovo = 50 * i });
                }
                fractions.Add(new NativeRunFraction { Run = "fixture_run_01", Tic = 10000,
                    IdentifiedEnvelope = 2500, DeNovoEnvelope = 1500,
                    IdentifiedAreaM0 = 1000, DeNovoAreaM0 = 500 });
                partition.Add(new ThreeWayPartitionRow { RunName = "fixture_run_01",
                    Tic = PartitionValue.Measured(10000, "fixture"),
                    PatternLab = PartitionValue.Recorded(2500, "fixture"),
                    DeNovo = PartitionValue.Measured(1500, "fixture"),
                    Remainder = PartitionValue.Derived(6000, "fixture") });
            }
            var charts = new (string Name, FrameworkElement Chart)[] {
                ("identity", new IdentityScanChart(sweep, 70)),
                ("recovery", new RecoveryChart(fractions)),
                ("mass", new XicMassChart(peptides)),
                ("isotopes", new IsotopeCountChart(peptides)),
                ("percent", new PercentIonCurrentChart(peptides, 10000)),
                ("families", new FamilyContributionChart(families)),
                ("partition", new ThreeWayChart(partition)) };
            foreach (var (name, chart) in charts)
                ChartImageExporter.Save(chart, Path.Combine(args[1],
                    $"{name}-{empty}-{variant}-{size.Width}.png"), size.Width, size.Height, 2, variant);
        }
        Console.WriteLine("Exported 56 chart fixtures to " + args[1]);
        return 0;
    }
}
