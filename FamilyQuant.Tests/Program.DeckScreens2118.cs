using FamilyQuant.Evidence;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DeckScreens2118Commands() =>
    [
        new("--deck-screens-2118", 450, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--deck-screens-2118", (a, _) => DeckScreens2118Command(a)),
    ];

    /// <summary>AQ2: the 2.1.18 deck's screens from the real windows, on copies. The walk starts unlocked as a fresh project
    /// does, imports the list, groups by evidence and applies; Results and Export read the computed copy; the report's
    /// family chart is switched to Pie in headless Edge. The list row key names the row for the curation and checklist screens.</summary>
    private static int DeckScreens2118Command(string[] args)
    {
        if (args.Length != 7)
        {
            Console.WriteLine("Use --deck-screens-2118 <project.fqproj> <family-list.xlsx> <computed.fqproj> <report.html> <list row key> <new-output-directory>.");
            return 1;
        }
        string output = Path.GetFullPath(args[6]), rowKey = args[5];
        if (Directory.Exists(output) || File.Exists(output)) { Console.WriteLine("Use a fresh screen output directory."); return 1; }
        Directory.CreateDirectory(output);
        string work = Path.Combine(output, "work"); Directory.CreateDirectory(work);
        var inputs = args.Skip(1).Take(4).ToArray();
        var hashes = inputs.Select(FileFingerprint.Sha256).ToArray();
        var copies = inputs.Select((input, index) => Path.Combine(work, index.ToString(CultureInfo.InvariantCulture) + "-" + Path.GetFileName(input))).ToArray();
        for (int index = 0; index < inputs.Length; index++) File.Copy(inputs[index], copies[index]);
        var screens = new List<DeckScreen>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var oldTheme = Themes.ThemeManager.Current; var oldMessages = ShellMessages.Presenter;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride; var oldSave = MainWindow.SelectSaveProjectPathOverride; var oldExport = MainWindow.ExportPresenterOverride;
        MainWindow? main = null; FamilyListImportWindow? importWindow = null;
        void Print(string line) { Console.WriteLine(line); File.AppendAllText(Path.Combine(output, "screens.log"), line + Environment.NewLine); }
        void Save(Window window, string name, string shows, bool dialog = false)
        {
            if (!seen.Add(name)) return;
            PumpDispatcher(); window.UpdateLayout(); PumpDispatcher();
            int width = dialog ? (int)Math.Ceiling(window.ActualWidth) : 1366, height = dialog ? (int)Math.Ceiling(window.ActualHeight) : 820;
            ScreenHarness.Save(window, Path.Combine(output, name), width, height);
            screens.Add(new(name, shows, window.Title, width, height));
            Print("SCREEN " + name + ": " + shows);
        }
        try
        {
            EnsureApplication(); Themes.ThemeManager.Apply(Themes.AppTheme.Light);
            ShellMessages.Presenter = (_, message) => { Print("MESSAGE " + message.Text); return MessageBoxResult.Yes; };
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            MainWindow.SelectSaveProjectPathOverride = () => Path.Combine(work, "walked.fqproj");
            main = OffscreenProofWindow(new MainWindow { Width = 1366, Height = 820 }); main.Show(); PumpDispatcher();
            Themes.ThemeManager.Apply(Themes.AppTheme.Light);
            var capture = new FamilyListWalkCapture(
                (owner, model) =>
                {
                    importWindow = OffscreenProofWindow(new FamilyListImportWindow(model, templateWriter: PanelImportTemplate.Write)
                    { Owner = owner, Width = 1366, Height = 820 });
                    importWindow.Show(); PumpDispatcher();
                },
                (_, model) =>
                {
                    if (model.Page == 2 && !model.ReviewingDecisions) Save(importWindow!, "01-import-columns.png", "Import family list: Columns page with the sheet's columns");
                    if (model.Page == 5 && !model.ReviewingSubclasses)
                    {
                        Print("CHECK 02 summary rows " + model.SummaryFamilies.Count.ToString(CultureInfo.InvariantCulture) + ", with Un-confirm "
                            + model.SummaryFamilies.Count(row => row.CanUnconfirm).ToString(CultureInfo.InvariantCulture));
                        Save(importWindow!, "02-import-summary.png", "Import family list: summary with every row confirmed and Un-confirm per row");
                    }
                    return Task.CompletedTask;
                },
                (owner, curation, window, model, session) =>
                {
                    importWindow?.Close(); importWindow = null;
                    window.Width = 1366; window.Height = 820; OffscreenProofWindow(window);
                    window.ShowAllOptions = false; window.Show(); PumpDispatcher();
                    var evidence = model.Evidence!; evidence.SelectedFamily = null;
                    ((Expander)window.FindName("ProteinDestinationsExpander")).IsExpanded = true;
                    Print("CHECK 05 " + evidence.ListDestinationText + " | " + evidence.SequenceDestinationText + " | " + evidence.OutsideScopeDestinationText);
                    Save(window, "05-review-destinations.png", "Review grouping: Where proteins go expanded");
                    ((Expander)window.FindName("ProteinDestinationsExpander")).IsExpanded = false;
                    window.ShowAllOptions = true;
                    var tabs = (TabControl)window.FindName("evidenceTabs");
                    tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => (string)tab.Header == "Family checklist"); PumpDispatcher();
                    var row = evidence.PanelChecklist.Single(family => family.Key == rowKey);
                    evidence.SelectedChecklistFamily = row; PumpDispatcher();
                    var grid = (DataGrid)window.FindName("PanelChecklistGrid"); grid.ScrollIntoView(row); PumpDispatcher();
                    var unconfirm = (Button)window.FindName("UnconfirmPanelMatchesButton"); window.UpdateLayout(); unconfirm.BringIntoView(); PumpDispatcher();
                    Print("CHECK 06 " + row.StateText + "; matches " + row.Matches.ToString(CultureInfo.InvariantCulture) + ", placed "
                        + row.Placed.ToString(CultureInfo.InvariantCulture) + ", outside " + row.Outside.ToString(CultureInfo.InvariantCulture)
                        + "; Un-confirm visible " + unconfirm.IsVisible + ", enabled " + unconfirm.IsEnabled);
                    Save(window, "06-review-checklist.png", "Review grouping: Family checklist with the row selected and Un-confirm matches");
                    window.Hide();
                    return Task.CompletedTask;
                },
                (owner, curation) =>
                {
                    foreach (var tab in WalkTree(curation).OfType<TabItem>().Where(tab => tab.Header as string == "Families")) tab.IsSelected = true;
                    PumpDispatcher();
                    Print("CHECK 03 Quantify visible " + ((Button)curation.FindName("ButtonComputeFullXic")).IsVisible
                        + "; words part " + ((TextBlock)curation.FindName("TextBlockPartGroupByWords")).Text);
                    Save(owner, "03-grouping-panel.png", "Curate families after Apply grouping, with the Grouping panel in its new order");
                    curation = ((Grid)owner.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
                    ((TextBox)curation.FindName("TextBoxSearch")).Text = "metallo";
                    typeof(ClusterGenerationControl).GetMethod("UpdateFilteredResults", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(curation, null);
                    PumpDispatcher();
                    var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(curation.fcf);
                    var ids = (curation.fcf.PanelProteinAssignments ?? new()).Where(fact => fact.IsApplied && fact.PanelFamilyKey == rowKey && fact.FamilyUserId.HasValue)
                        .Select(fact => fact.FamilyUserId!.Value).ToHashSet();
                    Print("CHECK 04 facts current " + map.IsCurrent + "; row families " + string.Join(",", ids) + "; visible families " + curation.ViewModel.Families.Count.ToString(CultureInfo.InvariantCulture));
                    var selected = curation.ViewModel.Families.Where(family => ids.Contains(family.FamilyId)).OrderByDescending(family => family.Proteins).First();
                    curation.ViewModel.SelectedFamily = selected; PumpDispatcher();
                    // As a user drags the splitter and clicks the Proteins header: the pane shows the counts, largest family first.
                    var families = (DataGrid)curation.FindName("DataGridFamilies");
                    ((Grid)families.Parent).ColumnDefinitions[0].Width = new GridLength(2.2, GridUnitType.Star);
                    var proteinsColumn = families.Columns.Single(column => column.Header as string == "Proteins");
                    families.Items.SortDescriptions.Clear();
                    families.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("Proteins", System.ComponentModel.ListSortDirection.Descending));
                    proteinsColumn.SortDirection = System.ComponentModel.ListSortDirection.Descending;
                    curation.UpdateLayout(); families.ScrollIntoView(selected); PumpDispatcher();
                    Print("CHECK 04 search metallo; selected family #" + selected.FamilyId.ToString(CultureInfo.InvariantCulture) + " with "
                        + selected.Proteins.ToString(CultureInfo.InvariantCulture) + " proteins; " + curation.ViewModel.FamilyCounter);
                    Save(owner, "04-curate-metallo.png", "Curate families: Search metallo, the list row's family selected with its protein count");
                    return Task.CompletedTask;
                });
            Task<int> walk = AppOperationHost.OnUi(main, () => FamilyListWalkCoreAsync(main, copies[0], copies[1], work, null, Print, unlockAll: true, capture: capture));
            while (!walk.IsCompleted) { PumpDispatcher(); Thread.Sleep(5); }
            if (walk.GetAwaiter().GetResult() != 0) throw new InvalidOperationException("The walk did not complete.");

            Task results = AppOperationHost.OnUi(main, async () =>
            {
                if (!await main.OpenProjectSafelyAsync(copies[2])) throw new InvalidOperationException("The computed project could not be opened.");
                var host = (Grid)main.FindName("GridApp");
                await host.Children.OfType<ClusterGenerationControl>().Single().FullXicReady;
                AnalyticsControl? analytics = null;
                var waited = Stopwatch.StartNew();
                while ((analytics = host.Children.OfType<AnalyticsControl>().FirstOrDefault(control => control.IsVisible)) == null
                    || !WalkTree(analytics).OfType<FamilyContributionChart>().Any())
                {
                    if (waited.Elapsed > TimeSpan.FromMinutes(8)) throw new InvalidOperationException("Results did not show the family chart: " + main.StatusModel.Text);
                    if (!main.Operations.IsBusy && analytics == null) main.NavigationModel.RequestNavigate(ShellDestination.Results);
                    await Task.Delay(500);
                }
                var choice = (ComboBox)analytics.FindName("ComboBoxFamilyTopN");
                var scroll = (ScrollViewer)analytics.FindName("ScrollAnalytics");
                void Fit()
                {
                    analytics.UpdateLayout(); PumpDispatcher();
                    // The chart choice at the top of the view, so the whole chart below it fits.
                    var options = (FrameworkElement)analytics.FindName("FamilyChartOptions");
                    double top = options.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + top); analytics.UpdateLayout(); PumpDispatcher();
                }
                choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == "15"); Fit();
                var plot = WalkTree(analytics).OfType<FamilyContributionChart>().First();
                if (plot.Parent is Border frame) frame.Height = Math.Max(plot.MinHeight, scroll.ViewportHeight - 65);
                Fit();
                Save(main, "07-results-bars.png", "Results: Family chart, Top 15 families");
                choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == "pie"); Fit();
                // The pie's frame fitted to the space left below the chart choice, as the bars' frame was.
                double bottom = host.TransformToAncestor(main).Transform(new Point(0, 0)).Y + host.ActualHeight;
                foreach (var pieFrame in WalkTree(analytics).OfType<FamilyPieChart>().Select(chart => chart.Parent).OfType<Border>())
                    pieFrame.Height = Math.Max(240, bottom - pieFrame.TransformToAncestor(main).Transform(new Point(0, 0)).Y - 16);
                Fit();
                Print("CHECK 08 pie shown " + WalkTree(analytics).OfType<FamilyPieChart>().Any(chart => chart.IsVisible));
                Save(main, "08-results-pie.png", "Results: Family chart, Pie");
                MainWindow.ExportPresenterOverride = window =>
                {
                    window.ShowInTaskbar = false; window.ShowActivated = false; window.Left = -30000; window.Top = -30000; window.Show(); PumpDispatcher();
                    ((ComboBox)window.FindName("FamilyNameChoice")).SelectedIndex = 1;
                    Print("CHECK 09 family names " + window.FamilyNames);
                    Save(window, "09-export-names.png", "File > Export with Family names set to Other-language name", dialog: true);
                    window.Close();
                };
                typeof(MainWindow).GetMethod("MenuItemExport_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { main, new RoutedEventArgs() });
            });
            while (!results.IsCompleted) { PumpDispatcher(); Thread.Sleep(5); }
            results.GetAwaiter().GetResult();

            string png = Path.Combine(output, "10-html-pie.png"), profile = Path.Combine(work, "browser-" + Guid.NewGuid().ToString("N"));
            string? edge = new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
                .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Microsoft", "Edge", "Application", "msedge.exe")).FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException("The report screen needs installed Microsoft Edge.");
            var start = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
                "--disable-component-update", "--force-device-scale-factor=1", "--hide-scrollbars", "--window-size=1366,900",
                "--user-data-dir=" + profile, "--remote-debugging-port=0", new Uri(copies[3]).AbsoluteUri })
                start.ArgumentList.Add(argument);
            using (var browser = Process.Start(start) ?? throw new IOException("Could not start the isolated screenshot browser."))
            {
                var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
                try
                {
                    if (!CaptureExactViewport(profile, png, 1366, "document.documentElement.dataset.theme='light';document.getElementById('tab-families').click();"
                        + "document.getElementById('contribution-pie').click();document.documentElement.style.scrollBehavior='auto';", "family-contribution-section").GetAwaiter().GetResult())
                        throw new InvalidOperationException("The report screen was not captured at 1366 wide.");
                }
                finally
                {
                    if (!browser.HasExited) browser.Kill(entireProcessTree: true);
                    browser.WaitForExit();
                    File.WriteAllText(Path.Combine(work, "browser.txt"), stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
                }
            }
            screens.Add(new("10-html-pie.png", "Report: family chart switched to Pie in headless Edge", "report.html", 1366, 900));
            Print("SCREEN 10-html-pie.png: Report: family chart switched to Pie in headless Edge");

            if (screens.Count != 10) throw new InvalidOperationException("Only " + screens.Count.ToString(CultureInfo.InvariantCulture) + " of the ten screens were made.");
            if (inputs.Select(FileFingerprint.Sha256).Where((hash, index) => hash != hashes[index]).Any()) throw new InvalidDataException("An input changed.");
            File.WriteAllText(Path.Combine(output, "screens.json"), JsonSerializer.Serialize(new
            { Theme = "Light", Version = AppVersionInfo.Current(), InputsUnchanged = true, Screens = screens.OrderBy(screen => screen.File, StringComparer.Ordinal) },
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { Print("RESULT " + error.Message); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); return 1; }
        finally
        {
            importWindow?.Close(); main?.Close(); PumpDispatcher(); Themes.ThemeManager.Apply(oldTheme);
            ShellMessages.Presenter = oldMessages; MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            MainWindow.SelectSaveProjectPathOverride = oldSave; MainWindow.ExportPresenterOverride = oldExport;
        }
    }
}
