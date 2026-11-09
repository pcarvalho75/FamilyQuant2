using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyListStepCommands() =>
    [
        new("--test-family-list-step", 70, TestCommandPhase.Raw,
            args => args.Contains("--test-family-list-step", StringComparer.Ordinal), (args, _) =>
            {
                FamilyListStepTests(args.Length == 2 ? args[1] : null);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            })
    ];

    private static void FamilyListStepTests(string? output = null)
    {
        EnsureApplication();
        string work = Path.Combine(Path.GetTempPath(), "family-list-step");
        Directory.CreateDirectory(work);
        string path = Path.Combine(work, "fixture.fcf");
        new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "fixture", Description = "Review fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 }
        } }.Save(path);
        var previousOpen = MainWindow.SelectOpenPathOverride;
        MainWindow.SelectOpenPathOverride = () => path;
        var main = new MainWindow { Width = 1366, Height = 820, Left = -30000, Top = -30000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            main.Show();
            ApplicationCommands.Open.Execute(null, main);
            FinishBusyOperation(main.Operations); PumpDispatcher();
            var control = WalkTree(main).OfType<ClusterGenerationControl>().Single();
            var button = (Button)control.FindName("ButtonImportFamilyList");
            var listStatus = (TextBlock)control.FindName("TextBlockFamilyListStatus");
            var groupingStatus = (TextBlock)control.FindName("TextBlockEvidenceGroupingStatus");
            var fetch = (Button)control.FindName("ButtonFetchDomains");
            var primary = control.FindResource("PrimaryButton");
            Check("missing list before grouping highlights optional step 0 and keeps Fetch evidence available",
                control.ViewModel.NextGroupingStep == 0 && ReferenceEquals(button.Style, primary)
                && !ReferenceEquals(fetch.Style, primary) && fetch.IsEnabled);
            Check("optional step 0 precedes Fetch evidence and explains the import change", button.IsVisible
                && button.TranslatePoint(new Point(), main).Y < ((Button)control.FindName("ButtonFetchDomains")).TranslatePoint(new Point(), main).Y
                && button.ToolTip is string tip && tip.Contains("File > Import family list", StringComparison.Ordinal)
                && tip.Contains("Undo", StringComparison.Ordinal));
            Check("grouping names the missing family list once in its status", listStatus.Text == "No family list"
                && groupingStatus.Text == "Not yet. No family list; step 0 is optional.");
            bool opened = false;
            main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var wizard = Application.Current.Windows.OfType<FamilyListImportWindow>().SingleOrDefault();
                opened = wizard != null && wizard.Owner == main;
                wizard?.Close();
            }));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            Check("step 0 opens the existing File import wizard", opened);
            string before = control.fcf.ToJsonText();
            if (output != null)
            {
                Directory.CreateDirectory(output);
                ScreenHarness.Save(main, Path.Combine(output, "no-family-list.png"), 1366, 820);
            }
            control.ViewModel.ApplyFamilyPanel(new FamilyPanel { Name = "Fixture list", Families = new()
            {
                new() { Key = "one", DisplayName = "First family" }, new() { Key = "two", DisplayName = "Second family" }
            } });
            PumpDispatcher();
            Check("import refreshes the list name and count and clears the missing-list notice",
                listStatus.Text == "Fixture list (2 families)" && groupingStatus.Text == "Not yet");
            Check("an imported list moves the highlight to Fetch evidence", control.ViewModel.NextGroupingStep == 1
                && ReferenceEquals(fetch.Style, primary) && !ReferenceEquals(button.Style, primary));
            if (output != null) ScreenHarness.Save(main, Path.Combine(output, "family-list.png"), 1366, 820);
            control.ViewModel.Undo(); PumpDispatcher();
            Check("Undo restores the missing-list status without changing protein grouping or values",
                listStatus.Text == "No family list" && groupingStatus.Text.Contains("No family list", StringComparison.Ordinal)
                && control.fcf.ToJsonText() == before);
            Check("Help starts the guided grouping order with optional import", HelpContent.GroupingOrder.StartsWith("0. Import family list (optional):", StringComparison.Ordinal));
            control.fcf.EvidenceSource = new Evidence.EvidenceProvenance { Provider = "UniProt" };
            control.ViewModel.RefreshRows(); PumpDispatcher();
            Check("downloaded evidence alone still suggests the optional import", control.ViewModel.NextGroupingStep == 0
                && ReferenceEquals(button.Style, primary) && fetch.IsEnabled);
            control.fcf.EvidenceRecipe = new Evidence.EvidenceRecipe();
            control.ViewModel.RefreshRows(); PumpDispatcher();
            Check("applied grouping with no list follows required steps and retains No family list",
                control.ViewModel.NextGroupingStep == 3 && !ReferenceEquals(button.Style, primary)
                && ReferenceEquals(((Button)control.FindName("ButtonFetchFamilyWorld")).Style, primary)
                && listStatus.Text == "No family list");
        }
        finally { main.Close(); MainWindow.SelectOpenPathOverride = previousOpen; PumpDispatcher(); }
    }
}
