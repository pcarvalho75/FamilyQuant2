using FamilyQuant.DatabaseUpdate;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DatabaseUpdateShellCommands() =>
    [
        new("--test-database-update-shell", 10100, TestCommandPhase.Raw, args => args.Contains("--test-database-update-shell", StringComparer.Ordinal), (args, _) =>
        {
            int at = Array.IndexOf(args, "--test-database-update-shell");
            DatabaseUpdateShellTests(at + 1 < args.Length && args[at + 1] != "." ? args[at + 1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private sealed record DatabaseUpdateShellFixture(FamilyClusterFile Project, List<DatabaseUpdateEntry> Entries, List<QuantRow> Quant,
        string KeptPeptide, string AddedPeptide);

    /// <summary>FQOpus's apply fixture (every class, the name rules, a locked family that empties) plus one ambiguous
    /// containment case (two old rows inside one longer new record) and stored quantitation: one peptide of a kept
    /// protein and one of an added protein. Invented sequences only.</summary>
    private static DatabaseUpdateShellFixture DatabaseUpdateShellData()
    {
        var basis = DatabaseUpdateApplyFixture();
        FamilyClusterFile project = basis.Project;
        var entries = basis.Source.Records.ToList();
        string x = UpdateSequence(430, 120), y = UpdateSequence(431, 110);
        project.MyExtendedFastaItems.Add(UpdateRow("tr|Q30030|Q30030_SYN", "Theta part one OS=Synthetica una OX=1", x, 13));
        project.MyExtendedFastaItems.Add(UpdateRow("tr|Q30031|Q30031_SYN", "Theta part two OS=Synthetica una OX=1", y, 13));
        project.SourceSequenceCount = project.MyExtendedFastaItems.Count;
        entries.Add(new DatabaseUpdateEntry("tr|Q40030|Q40030_SYN", "Theta OS=Synthetica una OX=1", x + y));
        string kept = project.MyExtendedFastaItems[0].Sequence[20..32];
        string added = entries.Single(e => e.Identifier == "tr|Q40005|Q40005_SYN").Sequence[30..42];
        var quant = new List<QuantRow> { Qr("identified", "r1", 10, 2, kept, 100, 200), Qr("identified", "r1", 20, 2, added, 50, 100) };
        return new DatabaseUpdateShellFixture(project, entries, quant, kept, added);
    }

    private static void WriteUpdateFasta(string path, IEnumerable<DatabaseUpdateEntry> entries)
    {
        var text = new StringBuilder();
        foreach (DatabaseUpdateEntry entry in entries) text.Append('>').Append(entry.Identifier).Append(' ').Append(entry.Description).Append('\n').Append(entry.Sequence).Append('\n');
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Z14c3d (design note test 10): the menu item, the unsaved-changes refusal, the low-overlap stop, Cancel,
    /// the preview's counts and rows, a pick compared again, Keep the old name carried over, Apply as one unsaved
    /// change with a fresh Families screen whose share column follows the new proteins, the status sentence, and
    /// the preview window itself (Copy, Save as CSV, Apply waiting for Compare again, Cancel, tooltips, screens).</summary>
    private static void DatabaseUpdateShellTests(string? output = null)
    {
        Section("Database update: File > Update database, the preview and Apply (Z14c3d)");
        EnsureApplication();
        string root = Path.Combine(Path.GetTempPath(), "database-update-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var data = DatabaseUpdateShellData();
        string fasta = Path.Combine(root, "new-database.fasta"), projectPath = Path.Combine(root, "project.fqproj");
        WriteUpdateFasta(fasta, data.Entries);
        ProjectStore.WriteFqproj(projectPath, data.Project, data.Quant, new QuantManifest());
        string far = Path.Combine(root, "other-database.fasta");
        WriteUpdateFasta(far, data.Project.MyExtendedFastaItems.Take(2).Select(p => new DatabaseUpdateEntry(p.SequenceIdentifier, p.Description, p.Sequence))
            .Concat(Enumerable.Range(0, 12).Select(i => new DatabaseUpdateEntry("tr|Q5" + i.ToString("0000") + "|OTHER_" + i, "Other protein " + i, UpdateSequence(900 + i, 150)))));

        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        var oldPreview = MainWindow.DatabaseUpdatePreviewOverride;
        var oldConfirm = MainWindow.ConfirmDatabaseUpdateOverride;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(root, "settings.json");
        var main = new MainWindow { Left = -30000, Top = -30000, Width = 1280, Height = 800, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual };
        bool Run(string path) { var task = main.UpdateDatabaseAsync(path); FinishBusyTask(task); return task.GetAwaiter().GetResult(); }
        string StatusText() => main.StatusModel.Text;
        try
        {
            main.Show(); PumpDispatcher();
            var fileMenu = (MenuItem)main.FindName("MenuFile");
            var items = fileMenu.Items.OfType<object>().ToList();
            var menu = (MenuItem)main.FindName("MenuItemUpdateDatabase");
            Check("Update database sits right after Build families in the File menu",
                items.IndexOf(menu) == items.IndexOf(main.FindName("MenuItemRunUSearch")) + 1 && (string)menu.Header == "Update database");
            Check("it is disabled and refuses without a project", !menu.IsEnabled && !Run(fasta)
                && StatusText().Contains("Open a project before updating", StringComparison.Ordinal), StatusText());

            Check("a saved project with stored quantitation opens", main.OpenProjectPath(projectPath));
            var before = UiField<ClusterGenerationControl>(main, "cgc")!;
            FinishBusyTask(before.FullXicReady);
            FamilyClusterFile project = before.fcf;
            PumpDispatcher();
            Check("then it is enabled", menu.IsEnabled);
            Check("before the update the added protein's peptide reaches no family; the kept one does",
                project.MyExtendedFastaItems[0].FullXicShare is > 0 && project.MyExtendedFastaItems.All(p => p.SequenceIdentifier != "tr|Q40005|Q40005_SYN"));
            string saved = project.ToJsonText();

            int previews = 0;
            MainWindow.DatabaseUpdatePreviewOverride = model => { previews++; return DatabaseUpdateOutcome.Cancel; };
            string annotation = project.MyExtendedFastaItems[2].UserAnnotation;
            project.MyExtendedFastaItems[2].UserAnnotation = "an unsaved edit";
            Check("unsaved changes are refused before anything is read", !Run(fasta) && previews == 0
                && StatusText().Contains("Save the project first", StringComparison.Ordinal), StatusText());
            project.MyExtendedFastaItems[2].UserAnnotation = annotation;
            main.DocumentState.CheckForChanges(project);

            string? prompted = null;
            MainWindow.ConfirmDatabaseUpdateOverride = message => { prompted = message; return false; };
            Check("a file sharing too few proteins stops with a warning before placement, nothing changed",
                !Run(far) && prompted != null && prompted.Contains("Build families may be the better choice", StringComparison.Ordinal)
                && previews == 0 && project.ToJsonText() == saved && StatusText().Contains("Update stopped", StringComparison.Ordinal), prompted);
            MainWindow.ConfirmDatabaseUpdateOverride = message => throw new InvalidOperationException("The fixture file must not warn: " + message);

            DatabaseUpdateViewModel? seen = null;
            MainWindow.DatabaseUpdatePreviewOverride = model => { seen = model; return DatabaseUpdateOutcome.Cancel; };
            Check("Cancel in the preview leaves the project exactly as it was", !Run(fasta) && seen != null && project.ToJsonText() == saved
                && ReferenceEquals(UiField<ClusterGenerationControl>(main, "cgc"), before) && !main.DocumentState.IsModified
                && StatusText().Contains("Database update cancelled. Nothing changed.", StringComparison.Ordinal), StatusText());
            if (seen == null) return;
            DatabaseUpdatePreview preview = seen.Preview;
            string Count(string what) => seen.Counts.Single(c => c.What.StartsWith(what, StringComparison.Ordinal)).Proteins;
            Check("the counts table reads the preview's counts",
                Count("Removed") == preview.Counts.Removed.ToString("N0") && Count("Added, joining") == preview.Counts.AddedJoinedFamilies.ToString("N0")
                && Count("Added, in new") == preview.Counts.AddedInNewFamilies.ToString("N0") && Count("Header changed") == preview.Counts.HeaderChanged.ToString("N0")
                && Count("Sequence changed") == preview.Counts.SequenceChanged.ToString("N0") && Count("Ambiguous") == "1 case"
                && Count("Family names") == preview.NameChanges.Count.ToString("N0") + (preview.NameChanges.Count == 1 ? " family" : " families"),
                string.Join(" | ", seen.Counts.Select(c => c.What + "=" + c.Proteins)));
            Check("the table lists every protein that is not unchanged, with kind, identifiers, family and placement",
                seen.Rows.Count == preview.Rows.Count && seen.VisibleRows.Count == preview.Rows.Count(r => !r.Decoy)
                && seen.Rows.Any(r => r.Change.StartsWith("Added", StringComparison.Ordinal) && r.PlacedBy.Length > 0 && r.Identity.Length > 0 && r.Aligned.Length > 0)
                && seen.Rows.Any(r => r.Change == "Removed" && r.OldIdentifier.Length > 0 && r.NewIdentifier.Length == 0));
            seen.RowFilter = "Removed";
            Check("the Show filter narrows the table and says so", seen.VisibleRows.All(r => r.Change.StartsWith("Removed", StringComparison.Ordinal))
                && seen.VisibleRows.Count == preview.Counts.Removed && seen.RowsShown.StartsWith("Showing ", StringComparison.Ordinal));
            seen.RowFilter = DatabaseUpdateViewModel.RowFilterAll;
            Check("the ambiguous case offers no pair first and each pair", seen.Cases.Count == 1 && seen.Cases[0].Selected.Pick == null
                && seen.Cases[0].Options.Count == 3 && seen.CanApply);
            Check("Keep the old name starts from the core's default for each family",
                seen.Names.All(n => n.Keep == n.Change.KeepByDefault) && seen.Names.Any(n => n.Keep) && seen.Names.Any(n => !n.Keep));

            int calls = 0;
            DatabaseUpdateViewModel? first = null, second = null;
            int oldX = project.MyExtendedFastaItems.FindIndex(p => p.SequenceIdentifier == "tr|Q30030|Q30030_SYN");
            int newZ = data.Entries.FindIndex(e => e.Identifier == "tr|Q40030|Q40030_SYN");
            MainWindow.DatabaseUpdatePreviewOverride = model =>
            {
                calls++;
                if (calls == 1)
                {
                    first = model;
                    var ambiguous = model.Cases.Single();
                    ambiguous.Selected = ambiguous.Options.Single(o => o.Pick is { } p && p.OldIndex == oldX && p.NewIndex == newZ);
                    model.Names.Single(n => n.Family == 9).Keep = true;
                    bool waits = model.PicksPending && !model.CanApply && !model.Choose(DatabaseUpdateOutcome.Apply);
                    Check("a changed pick makes Apply wait for Compare again", waits);
                    model.Choose(DatabaseUpdateOutcome.CompareAgain);
                    return model.Outcome;
                }
                second = model;
                model.Choose(DatabaseUpdateOutcome.Apply);
                return model.Outcome;
            };
            bool applied = Run(fasta);
            var after = UiField<ClusterGenerationControl>(main, "cgc")!;
            Check("Compare again runs the comparison and placement with the pick, then Apply updates the project", applied && calls == 2
                && second != null && second.Preview.Comparison.Picks.Count == 1 && !second.PicksPending, StatusText());
            Check("the Keep the old name choice is carried into the preview computed again", second?.Names.Single(n => n.Family == 9).Keep == true);
            var rows = project.MyExtendedFastaItems;
            Check("the picked longer record takes the chosen old protein's family; the other old row leaves",
                rows.Single(r => r.SequenceIdentifier == "tr|Q40030|Q40030_SYN").UserID == 13 && rows.All(r => r.SequenceIdentifier != "tr|Q30030|Q30030_SYN")
                && rows.All(r => r.SequenceIdentifier != "tr|Q30031|Q30031_SYN"));
            Check("the kept name is written on every member of its family", rows.Where(r => r.UserID == 9).All(r => r.UserAnnotation == "Old nine"));
            Check("the update is recorded once with the counts the preview showed", project.DatabaseUpdates?.Count == 1
                && project.DatabaseUpdates[0].Counts.Added == second!.Preview.Counts.Added && project.DatabaseUpdates[0].New.FileName == "new-database.fasta");
            Check("Apply is one unsaved change: the file on disk is untouched", main.DocumentState.IsModified
                && ProjectStore.ReadFqproj(projectPath).fcf.ToJsonText() == saved);
            Check("a fresh Families screen shows the updated project with an empty Undo", !ReferenceEquals(after, before) && ReferenceEquals(after.fcf, project)
                && after.ViewModel.UndoStack.Count == 0 && ReferenceEquals(main.NavigationModel.Document, project));
            FinishBusyTask(after.FullXicReady);
            var newProtein = rows.Single(r => r.SequenceIdentifier == "tr|Q40005|Q40005_SYN");
            Check("the share column is recomputed over the new protein set: the added protein's peptide now counts",
                newProtein.FullXicShare is > 0 && rows[0].FullXicShare is > 0, newProtein.FullXicShare?.ToString());
            string status = StatusText();
            Check("the status line says what changed and what to do next, in plain words",
                status.Contains("Database updated: ", StringComparison.Ordinal) && status.Contains(" added, ", StringComparison.Ordinal)
                && status.Contains("no evidence yet: Fetch evidence.", StringComparison.Ordinal)
                && status.Contains("Results are from the previous database: Quantify again.", StringComparison.Ordinal), status);
            Check("the menu item stays available for the updated project", menu.IsEnabled);
        }
        finally
        {
            MainWindow.DatabaseUpdatePreviewOverride = oldPreview;
            MainWindow.ConfirmDatabaseUpdateOverride = oldConfirm;
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            main.Close(); PumpDispatcher();
            MainWindow.UnsavedProjectChoiceOverride = oldChoice;
            MainWindow.ShellSettingsPathOverride = oldSettings;
        }

        Section("Database update: the preview window");
        var windowData = DatabaseUpdateShellData();
        var source = DatabaseUpdateSource.FromEntries("new-database.fasta", "NEWSHA", windowData.Entries);
        var comparison = DatabaseUpdateComparer.Compare(windowData.Project, source);
        var windowPreview = DatabaseUpdatePlanner.Preview(windowData.Project, comparison, DatabaseUpdatePlacer.Place(windowData.Project, comparison));
        string[] identifiers = windowData.Project.MyExtendedFastaItems.Select(p => p.SequenceIdentifier).ToArray();
        var previousTheme = Themes.ThemeManager.Current;
        var oldPresenter = ShellDialogs.FilePresenter;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var model = new DatabaseUpdateViewModel(windowPreview, identifiers, "new-database.fasta", "project.fqproj");
                var window = new DatabaseUpdateWindow(model) { Width = 1366, Height = 820, Left = -30000, Top = -30000,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    if (theme == Themes.AppTheme.Dark) CheckTooltipCoverage("Update database window", (FrameworkElement)window.Content);
                    var tabs = (TabControl)window.FindName("updateTabs");
                    string Shot(string tab, int width, int height)
                    {
                        window.Width = width; window.Height = height;
                        tabs.SelectedItem = window.FindName(tab) ?? tabs.Items[0]; PumpDispatcher();
                        var content = (FrameworkElement)window.Content;
                        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout(); PumpDispatcher();
                        if (output == null) return "";
                        Directory.CreateDirectory(output);
                        string name = Path.Combine(output, "z14c3d-" + theme.ToString().ToLowerInvariant() + "-" + tab.Replace("Tab", "") + "-" + width + "x" + height + ".png");
                        System.Threading.Thread.Sleep(200); PumpDispatcher();
                        ScreenHarness.Save(window, name, width, height);
                        return name;
                    }
                    Shot("summaryTab", 1366, 820);
                    var grid = (DataGrid)window.FindName("countsGrid");
                    Check(theme + " the Summary tab shows every count row", grid.Items.Count == model.Counts.Count && model.Counts.Count == 11);
                    Shot("summaryTab", 1024, 768);
                    Shot("rowsTab", 1366, 820);
                    var rowsGrid = (DataGrid)window.FindName("rowsGrid");
                    Check(theme + " the Proteins tab lists the target rows", rowsGrid.Items.Count == model.VisibleRows.Count && rowsGrid.Items.Count > 10);
                    rowsGrid.SelectedItem = model.VisibleRows.First(r => r.Row.OtherLinkedFamilies != null || r.PlacedBy.Length > 0);
                    Shot("rowsTab", 1366, 820);
                    Check(theme + " the selected row's whole note shows under the table",
                        ((TextBlock)window.FindName("selectedNote")).Text == ((DatabaseUpdateRowView)rowsGrid.SelectedItem).Note && ((DatabaseUpdateRowView)rowsGrid.SelectedItem).Note.Length > 0);
                    Shot("rowsTab", 1024, 768);
                    string? copied = null;
                    DatabaseUpdateWindow.ClipboardOverride = text => copied = text;
                    ((Button)window.FindName("copyRowsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    DatabaseUpdateWindow.ClipboardOverride = null;
                    Check(theme + " Copy puts the rows shown on the clipboard as CSV with the change export's columns",
                        copied == model.VisibleCsv() && copied!.StartsWith("Kind,Step,Picked,", StringComparison.Ordinal)
                        && copied.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length == model.VisibleRows.Count + 1);
                    string csv = Path.Combine(root, "preview-" + theme + ".csv");
                    ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = csv; return true; };
                    ((Button)window.FindName("saveCsvButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(theme + " Save as CSV writes every row, decoys included", File.Exists(csv)
                        && File.ReadAllText(csv) == DatabaseUpdateCsv.Build(windowPreview.Rows) && model.Status.StartsWith("Saved ", StringComparison.Ordinal));
                    Shot("namesTab", 1366, 820);
                    Shot("casesTab", 1366, 820);
                    var apply = (Button)window.FindName("applyButton");
                    var compare = (Button)window.FindName("compareAgainButton");
                    Check(theme + " Apply is ready and Compare again idle before any pick", apply.IsEnabled && !compare.IsEnabled);
                    model.Cases[0].Selected = model.Cases[0].Options[1]; PumpDispatcher();
                    Check(theme + " after a pick Apply waits and Compare again is offered", !apply.IsEnabled && compare.IsEnabled
                        && model.PicksStatus.StartsWith("A pick changed", StringComparison.Ordinal));
                    Shot("casesTab", 1024, 768);
                    model.Cases[0].Selected = model.Cases[0].Options[0]; PumpDispatcher();
                    ((Button)window.FindName("cancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    Check(theme + " Cancel closes the preview with Cancel", model.Outcome == DatabaseUpdateOutcome.Cancel && !window.IsVisible);
                }
                finally { if (window.IsVisible) window.Close(); PumpDispatcher(); }
            }
        }
        finally
        {
            ShellDialogs.FilePresenter = oldPresenter;
            Themes.ThemeManager.Apply(previousTheme);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
