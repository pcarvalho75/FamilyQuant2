using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> EvidenceDetailSelectionCommandTable() =>
    [
        new("--test-evidence-detail-selection", 74, TestCommandPhase.Raw, args => args.Contains("--test-evidence-detail-selection", StringComparer.Ordinal), (args, _) =>
        {
            EvidenceDetailSelectionTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.EvidenceDetailSelection"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];
    private static void EvidenceDetailSelectionTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var oldTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var file = ManualChoiceFile(); file.MyExtendedFastaItems[1].Evidence!.Family = "Other invented family";
                for (int index = 0; index < file.MyExtendedFastaItems.Count; index++) file.MyExtendedFastaItems[index].Description = "Supplied detail " + index;
                string before = file.ToJsonText();
                var session = new EvidenceGroupingSession(file, "detail selection fixture"); var input = session.Propose("scope");
                var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
                var window = new SuperClusterPreviewWindow(preview, input, session.Propose, initialScope: "scope", evidenceMode: true)
                    { Width = 1366, Height = 820, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    window.ShowAllOptions = true;
                    window.Show(); PumpDispatcher();
                    var grid = (DataGrid)window.FindName("evidenceGrid");
                    var model = ((GroupingReviewViewModel)window.DataContext).Evidence!;
                    Check("Z12g3a " + theme + " initial details show the family first row without highlighting it",
                        grid.SelectedItems.Count == 0 && model.SelectedProtein == null
                        && model.ProteinDetails.Contains(model.VisibleProteins.First().Accession, StringComparison.Ordinal));
                    model.SelectedFamily = model.Families.Single(family => family.Family == "Other invented family"); PumpDispatcher();
                    Check("Z12g3a " + theme + " family change refreshes detail fallback without selecting an alignment anchor",
                        grid.SelectedItems.Count == 0 && model.SelectedProtein == null
                        && model.ProteinDetails.Contains("P10002", StringComparison.Ordinal));
                    grid.SelectedItems.Clear(); PumpDispatcher(); model.SelectedFamily = null; PumpDispatcher();
                    var chosen = model.Proteins.Single(protein => protein.ProteinIndex == 1);
                    grid.SelectedItems.Add(chosen); PumpDispatcher();
                    Check("Z12g3a " + theme + " explicit highlight is the sole selected row after the family filter changes",
                        grid.SelectedItems.Count == 1 && ReferenceEquals(grid.SelectedItems[0], chosen) && ReferenceEquals(model.SelectedProtein, chosen));
                    model.SearchText = "P10001"; PumpDispatcher();
                    Check("Z12g3a " + theme + " filtering out a highlight clears selection but retains first-visible details",
                        grid.SelectedItems.Count == 0 && model.SelectedProtein == null && model.ProteinDetails.Contains("P10001", StringComparison.Ordinal));
                    model.SearchText = "no matching supplied identifier"; PumpDispatcher();
                    Check("Z12g3a " + theme + " empty filter has no stale detail or highlight", grid.SelectedItems.Count == 0
                        && model.ProteinDetails.StartsWith("Select a protein", StringComparison.Ordinal));
                    model.SearchText = ""; PumpDispatcher();
                    ScreenHarness.Save(window, Path.Combine(output, "detail-fallback-" + theme + ".png"), 1366, 820);
                    Check("Z12g3a " + theme + " detail browsing changes no source or tick", file.ToJsonText() == before);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(oldTheme); }
    }
}
