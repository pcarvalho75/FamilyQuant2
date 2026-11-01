using FamilyQuant.Homology;
using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void HomologyResultsTests(string workDir)
    {
        Section("C11 standalone homology Results export without quantitation");
        var families = new FamilyClusterFile { MyExtendedFastaItems = new() { new() {
            SequenceIdentifier = "sp|fixture|X", Description = "Synthetic homology family", Sequence = "AAAPEPTIDEKAAA",
            UserID = 7, USearchID = 7 } } };
        var mapped = DeNovoControl.SearchHomology(new[] { "PEPTIDEK" }, families, new HomologyOptions(), "standalone-fixture.fcf");
        Check("the standalone mapping has an inspectable assignment", mapped.MappedToTargets == 1
            && mapped.Assignments.Count == 1 && mapped.Assignments[0].FamilyIds.SequenceEqual(new[] { 7 }));
        var model = new AnalyticsViewModel(Array.Empty<QuantRow>(), null, homology: mapped);
        Check("Results enables homology CSV when quantitation and project are absent", !model.HasData
            && model.HasHomology && model.CanExport && model.ExportKind == ResultsExportKind.Homology);
        string output = Path.Combine(workDir, "standalone-homology-results.csv");
        model.Export(output);
        string expected = "peptide,family_ids,protein_ids,ambiguous" + Environment.NewLine +
            "\"" + mapped.Assignments.Single().Peptide + "\",\"7\",\"sp|fixture|X\",false" + Environment.NewLine;
        Check("the homology CSV preserves the original RFC-4180 four-column contract", File.ReadAllText(output) == expected);
        var legacy = new DeNovoViewModel();
        legacy.ApplyHomology(mapped);
        string legacyPath = Path.Combine(workDir, "legacy-homology-results.csv");
        legacy.ExportHomologyCsv(legacyPath);
        Check("legacy callers and Results write identical homology bytes",
            File.ReadAllBytes(output).SequenceEqual(File.ReadAllBytes(legacyPath)));
        string help = HelpContent.BuildDeNovo();
        Check("Help names the Results homology export and removes retired De novo navigation",
            help.Contains("Homology assignments (CSV)") && help.Contains("open Results")
            && !help.Contains("Extract areas") && !help.Contains("Partition ion current"));
        Check("the Help refresh retains the scientific legacy comparison facts",
            help.Contains("DISTINCT PEAKS") && help.Contains("NEGATIVE remainder") && help.Contains("PREFILTER")
            && help.Contains("both count one integrated peak once"));
        EnsureApplication();
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(workDir, "standalone-homology-shell.json");
        var main = new MainWindow();
        try
        {
            MainWindow.NavigateShellCommand.Execute(ShellDestination.DeNovo, main);
            var host = UiField<Grid>(main, "GridApp")!;
            var deNovo = host.Children.OfType<DeNovoControl>().Single();
            Check("the retired De novo groups and its homology export button are gone",
                deNovo.FindName("GroupBoxExtractXic") == null && deNovo.FindName("GroupBoxPartition") == null
                && deNovo.FindName("ButtonExportHomologyCsv") == null);
            deNovo.ApplyHomology(mapped);
            Check("a completed standalone search enables Results navigation without an open project",
                !main.NavigationModel.HasProject && main.NavigationModel.CanNavigate(ShellDestination.Results)
                && UiField<MenuItem>(main, "MenuItemAnalytics")!.IsEnabled);
            MainWindow.NavigateShellCommand.Execute(ShellDestination.Results, main);
            var results = host.Children.OfType<AnalyticsControl>().Single();
            Check("the shell passes the exact standalone snapshot into Results", !results.ViewModel.HasData
                && ReferenceEquals(results.ViewModel.Homology, mapped)
                && main.NavigationModel.Current == ShellDestination.Results);
            string shellCsv = Path.Combine(workDir, "shell-homology-results.csv");
            results.ViewModel.Export(shellCsv);
            Check("the complete no-project shell route still exports the same CSV",
                File.ReadAllBytes(output).SequenceEqual(File.ReadAllBytes(shellCsv)));
        }
        finally { main.Close(); MainWindow.ShellSettingsPathOverride = oldSettings; }
    }
}
