using FamilyQuant.DeNovo;
using FamilyQuant.Evidence.Alignment;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record FamilyListWalkCapture(
        Action<MainWindow, FamilyListImportViewModel>? OpenImport,
        Func<MainWindow, FamilyListImportViewModel, Task>? ImportPage,
        Func<MainWindow, ClusterGenerationControl, SuperClusterPreviewWindow, GroupingReviewViewModel, EvidenceGroupingSession, Task>? Review,
        Func<MainWindow, ClusterGenerationControl, Task>? Applied);
    private sealed record DeckScreen(string File, string Shows, string WindowTitle, int Width, int Height);

    [TestCommandTable]
    private static IEnumerable<TestCommand> DeckScreenCommands() =>
    [
        new("--deck-screens", 450, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--deck-screens", (a, _) => DeckScreensCommand(a)),
        new("--test-deck-screens", 450, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--test-deck-screens", (_, _) =>
        {
            DeckScreenTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static int DeckScreensCommand(string[] args)
    {
        if (args.Length != 4) { Console.WriteLine("Use --deck-screens <project.fqproj> <family-list.xlsx> <new-output-directory>."); return 1; }
        string output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) || File.Exists(output)) { Console.WriteLine("Use a fresh screen output directory."); return 1; }
        Directory.CreateDirectory(output);
        var screens = new List<DeckScreen>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        string projectHash = FileFingerprint.Sha256(args[1]), workbookHash = FileFingerprint.Sha256(args[2]);
        string work = Path.Combine(output, "work"); Directory.CreateDirectory(work);
        string project = Path.Combine(work, Path.GetFileName(args[1])), workbook = Path.Combine(work, Path.GetFileName(args[2]));
        File.Copy(args[1], project); File.Copy(args[2], workbook);
        var oldTheme = Themes.ThemeManager.Current; var oldMessages = ShellMessages.Presenter;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride; var oldSave = MainWindow.SelectSaveProjectPathOverride;
        MainWindow? main = null; FamilyListImportWindow? importWindow = null;
        void Print(string line) { Console.WriteLine(line); File.AppendAllText(Path.Combine(output, "screens.log"), line + Environment.NewLine); }
        void Save(Window window, string name, string shows, bool dialog = false)
        {
            if (!seen.Add(name)) return;
            PumpDispatcher(); window.UpdateLayout();
            int width = dialog ? (int)Math.Ceiling(window.ActualWidth) : 1366;
            int height = dialog ? (int)Math.Ceiling(window.ActualHeight) : 820;
            ScreenHarness.Save(window, Path.Combine(output, name), width, height);
            screens.Add(new(name, shows, window.Title, width, height));
            Print("SCREEN " + name + ": " + shows);
        }
        try
        {
            EnsureApplication(); Themes.ThemeManager.Apply(Themes.AppTheme.Light);
            ShellMessages.Presenter = (_, message) => { Print("MESSAGE " + message.Text); return MessageBoxResult.Yes; };
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            MainWindow.SelectSaveProjectPathOverride = () => Path.Combine(output, "walked.fqproj");
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
                    if (model.Page == 1)
                    {
                        if (((DataGrid)importWindow!.FindName("SheetPreviewGrid")).Items.Count == 0)
                            throw new InvalidOperationException("The workbook preview has no visible rows.");
                        Save(importWindow, "01-import-file.png", "Import family list: file and populated workbook preview");
                    }
                    if (model.Page == 2 && !model.ReviewingDecisions) Save(importWindow!, "02-import-columns.png", "Import family list: proposed column meanings");
                    if (model.Page == 5 && !model.ReviewingSubclasses) Save(importWindow!, "03-import-summary.png", "Import family list: summary before Accept and import");
                    return Task.CompletedTask;
                },
                (owner, curation, window, model, session) =>
                {
                    importWindow?.Close(); importWindow = null;
                    window.Width = 1366; window.Height = 820; OffscreenProofWindow(window);
                    window.AlignmentSource = highlighted => AlignmentCandidates.Find(curation.fcf, session.Proposal, highlighted);
                    window.ShowAllOptions = false; window.Show(); PumpDispatcher();
                    var evidence = model.Evidence!; evidence.SelectedFamily = null;
                    var destinations = (Expander)window.FindName("ProteinDestinationsExpander"); destinations.IsExpanded = true;
                    Save(window, "04-review-evidence.png", "Review grouping: Simple view, Evidence tab, destination counts expanded");
                    destinations.IsExpanded = false;
                    var tabs = (TabControl)window.FindName("evidenceTabs");
                    void Tab(string title) { tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => (string)tab.Header == title); PumpDispatcher(); }
                    window.ShowAllOptions = true;
                    Tab("Panel links"); Save(window, "05-panel-links.png", "Review grouping: Panel links and remaining choices");
                    Tab("Family checklist"); Save(window, "06-family-checklist.png", "Review grouping: Family checklist states and matching reasons");
                    window.ShowAllOptions = true; Tab("Architecture");
                    if (!WalkTree((DependencyObject)((TabItem)tabs.SelectedItem).Content).OfType<TextBlock>()
                        .Any(text => text.IsVisible && text.Text == GroupingReviewHelp.AdvancedTabGuides["Architecture"]))
                        throw new InvalidOperationException("The Architecture purpose line is not visible.");
                    Save(window, "07-architecture.png", "Show all options: Architecture tab with its purpose line");
                    window.ShowAllOptions = false; Tab("Proteins");
                    var grid = (DataGrid)window.FindName("evidenceGrid");
                    var selected = evidence.Proteins.Where(row => row.Selected).Take(2).ToArray();
                    if (selected.Length < 2) throw new InvalidOperationException("Two ticked proteins are required for the requested details and alignment screens.");
                    grid.SelectedItem = selected[0]; grid.ScrollIntoView(selected[0]); PumpDispatcher();
                    Save(window, "08-protein-details.png", "Selected protein details for the first ticked protein");
                    window.ShowAllOptions = true; Tab("Proteins");
                    grid.SelectedItems.Clear(); foreach (var row in selected) grid.SelectedItems.Add(row);
                    ((Button)window.FindName("alignSelectedButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    var alignment = window.OpenAlignmentWindow ?? throw new InvalidOperationException("Align selected did not open.");
                    try { alignment.Left = -30000; alignment.Top = -30000; PumpDispatcher(); Save(alignment, "09-align-selected.png", "Align selected: two highlighted proteins and the offered reviewed references", dialog: true); }
                    finally { alignment.Close(); }
                    window.Hide();
                    return Task.CompletedTask;
                },
                async (owner, curation) =>
                {
                    foreach (var tab in WalkTree(curation).OfType<TabItem>().Where(tab => tab.Header as string == "Families")) tab.IsSelected = true;
                    Save(owner, "10-curate-families.png", "Curate families: Families counter and Grouping order with Fetch UniProt families");
                    owner.NavigationModel.RequestNavigate(ShellDestination.Results);
                    var host = (Grid)owner.FindName("GridApp");
                    while (!host.Children.OfType<AnalyticsControl>().Any())
                    {
                        if (!owner.Operations.IsBusy) throw new InvalidOperationException("Results did not open: " + owner.StatusModel.Text);
                        await Task.Delay(10);
                    }
                    var results = host.Children.OfType<AnalyticsControl>().Single();
                    var plot = WalkTree(results).OfType<FamilyContributionChart>().FirstOrDefault();
                    if (plot == null) throw new InvalidOperationException("Results has no family contribution chart.");
                    var scroll = (ScrollViewer)results.FindName("ScrollAnalytics"); results.UpdateLayout();
                    if (plot.Parent is Border frame) frame.Height = Math.Max(plot.MinHeight, scroll.ViewportHeight - 65);
                    plot.BringIntoView(); PumpDispatcher();
                    Save(owner, "11-results-family-chart.png", "Results native family contribution chart after Apply grouping, fitted to the viewport");
                });
            Task<int> walk = AppOperationHost.OnUi(main, () => FamilyListWalkCoreAsync(main, project, workbook, output, null, Print, capture));
            while (!walk.IsCompleted) { PumpDispatcher(); Thread.Sleep(5); }
            if (walk.GetAwaiter().GetResult() != 0 || screens.Count != 11) throw new InvalidOperationException("The walk did not reach all eleven requested screens.");
            if (projectHash != FileFingerprint.Sha256(args[1]) || workbookHash != FileFingerprint.Sha256(args[2])) throw new InvalidDataException("An input changed.");
            File.WriteAllText(Path.Combine(output, "screens.json"), JsonSerializer.Serialize(new
            { Theme = "Light", Version = AppVersionInfo.Current(), InputProjectUnchanged = true, InputWorkbookUnchanged = true, Screens = screens }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { Print("RESULT " + error.Message); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); return 1; }
        finally
        {
            importWindow?.Close(); main?.Close(); PumpDispatcher(); Themes.ThemeManager.Apply(oldTheme);
            ShellMessages.Presenter = oldMessages; MainWindow.UnsavedProjectChoiceOverride = oldChoice; MainWindow.SelectSaveProjectPathOverride = oldSave;
        }
    }

    private static void DeckScreenTests()
    {
        EnsureApplication();
        string root = Path.Combine(Path.GetTempPath(), "deck-screens", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var file = new FamilyClusterFile
        {
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "sp|P65001|FIXTURE", Description = "First synthetic protein", Sequence = "ACDEFGHIK", UserID = 1, USearchID = 1,
                    Evidence = new() { Accession = "P65001", Reviewed = true, Family = "First supplied family", Keywords = new() { "selected" }, Pfam = new() { new() { Accession = "PF12345" } } } },
                new() { SequenceIdentifier = "sp|P65002|FIXTURE", Description = "Second synthetic protein", Sequence = "LMNPQRSTK", UserID = 2, USearchID = 2,
                    Evidence = new() { Accession = "P65002", Reviewed = true, Family = "Second supplied family", Keywords = new() { "selected" }, Pfam = new() { new() { Accession = "PF54321" } } } },
            },
            EvidenceRecipe = new() { Options = new() { ScopeTerms = new[] { "selected" } } },
        };
        string project = Path.Combine(root, "fixture.fqproj");
        ProjectStore.WriteFqproj(project, file, new[] { Qr("identified", "fixture", 1, 2, "ACDEFGHIK", 100, 100), Qr("identified", "fixture", 2, 2, "LMNPQRSTK", 200, 200) },
            new QuantManifest { DerivedMethod = NativeQuantFractions.Method, PerRunTic = new() { ["fixture"] = 1000 } });
        string workbook = Path.Combine(root, "families.xlsx"); PanelImportTemplate.Write(workbook);
        FillTemplate(workbook, new[] { (Key: "A", Name: "First user family", Domain: "PF12345"), (Key: "B", Name: "Second user family", Domain: "PF54321") }
            .Select(value => PanelImportTemplate.Columns.Select(column => column.Role switch
            { PanelImportFields.FamilyKey => value.Key, PanelImportFields.DisplayName => value.Name, PanelImportFields.Pfam => value.Domain, _ => "" }).ToArray()).ToArray());
        string hash = TestFileHash.Upper(project), bookHash = TestFileHash.Upper(workbook), output = Path.Combine(root, "screens");
        int exit = DeckScreensCommand(new[] { "--deck-screens", project, workbook, output });
        Check("AP7 native screen walk completes", exit == 0);
        if (exit != 0) return;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "screens.json")));
        var entries = manifest.RootElement.GetProperty("Screens").EnumerateArray().ToArray();
        Check("AP7 every requested image is named and described in screens.json", entries.Length == 11 && entries.All(entry => entry.GetProperty("Shows").GetString()!.Length > 0)
            && entries.Select(entry => entry.GetProperty("File").GetString()).Distinct().Count() == 11 && manifest.RootElement.GetProperty("Theme").GetString() == "Light");
        Check("AP7 real PNG dimensions match screen/dialog dimensions", entries.All(entry =>
        {
            using var stream = File.OpenRead(Path.Combine(output, entry.GetProperty("File").GetString()!));
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return image.PixelWidth == entry.GetProperty("Width").GetInt32() && image.PixelHeight == entry.GetProperty("Height").GetInt32()
                && (entry.GetProperty("File").GetString() == "09-align-selected.png" || image.PixelWidth == 1366 && image.PixelHeight == 820);
        }));
        Check("AP7 captures on copies and leaves both inputs unchanged", TestFileHash.Upper(project) == hash && TestFileHash.Upper(workbook) == bookHash);
        Console.WriteLine("AP7 synthetic images: " + output);
    }
}
