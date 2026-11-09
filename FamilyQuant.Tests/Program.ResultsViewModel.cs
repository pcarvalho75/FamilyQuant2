using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ResultsViewModelTests()
    {
        Section("Results view model - complete facts and export readiness");
        var empty = new AnalyticsViewModel(Array.Empty<QuantRow>(), null);
        Check("empty Results withholds cards and exports and explains the next step", !empty.HasData && !empty.CanExport
            && empty.Cards.Count == 0 && empty.EmptyMessage.Contains("Quantify"));
        var model = new AnalyticsViewModel(SyntheticAnalyticsRows(), new QuantManifest
            { DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method, PerRunTic = new() { ["ui_01"] = 5e8, ["ui_02"] = 4e8 } });
        Check("Results exposes three short cards and retains all method facts on demand", model.Cards.Count == 3
            && model.Details.Contains("PTM-aware peptidoform and charge") && model.Details.Contains("NOT quantified")
            && model.Details.Contains("No Family Cluster File is open") && model.Details.Contains("before cross-side suppression"));
        Check("the headline explicitly labels measured-envelope recovery and its RAW TIC denominator",
            model.Cards[0].Title == "Measured envelope recovery" && model.Cards[0].Detail.Contains("measured envelope")
            && model.Cards[0].Detail.Contains("RAW TIC"));
        Check("Results keeps its original exports and adds the two evidence-dependent Database gap choices", model.CanExport
            && Enum.GetValues<ResultsExportKind>().Length == 7
            && new[] { ResultsExportKind.Summary, ResultsExportKind.Peptides, ResultsExportKind.Figures, ResultsExportKind.EvidencePack,
                ResultsExportKind.Homology }.All(kind => Enum.IsDefined(kind)));
        model.ExportKind = ResultsExportKind.EvidencePack;
        Check("the evidence pack stays unavailable until its project evidence exists", !model.CanExport);
        model.ExportKind = ResultsExportKind.Peptides;
        Check("peptide export remains available without family attribution", model.CanExport);
    }

    private static int ResultsScreens(string output, string? projectPath = null)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        var project = projectPath == null
            ? (fcf: (FamilyClusterFile?)null, quant: SyntheticAnalyticsRows(), manifest: new QuantManifest
                { DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method, PerRunTic = new() { ["ui_01"] = 5e8, ["ui_02"] = 4e8 } })
            : ProjectStore.ReadFqproj(projectPath);
        var model = new AnalyticsViewModel(project.quant, project.manifest, project.fcf);
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        foreach (var size in new[] { (1024, 768), (1366, 820) })
        {
            Themes.ThemeManager.Apply(theme);
            var control = new AnalyticsControl(model);
            var window = new Window { Content = control, Width = size.Item1, Height = size.Item2,
                Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
            window.Show();
            try
            {
                PumpDispatcher();
                ScreenHarness.Save(window, Path.Combine(output, $"results-{theme}-{size.Item1}-cards.png"), size.Item1, size.Item2);
                WalkTree(control).OfType<Expander>().Single().IsExpanded = true;
                PumpDispatcher();
                System.Threading.Thread.Sleep(350);
                PumpDispatcher();
                ScreenHarness.Save(window, Path.Combine(output, $"results-{theme}-{size.Item1}-details.png"), size.Item1, size.Item2);
            }
            finally { window.Close(); }
        }
        Console.WriteLine("Results: 8 cards/detail captures, both themes and sizes; " + (projectPath ?? "synthetic fixture") + "; " + output);
        return 0;
    }
}
