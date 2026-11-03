using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportDetailsCommandTable() =>
    [
        new("--test-report-details", 425, TestCommandPhase.Raw,
            args => args.Contains("--test-report-details", StringComparer.Ordinal), (_, _) => RunReportDetailsTests()),
        new("--report-details-screens", 426, TestCommandPhase.Raw,
            args => args.Length == 2 && args[0] == "--report-details-screens", (args, _) => ReportDetailsScreens(args[1])),
    ];

    private static int ReportDetailsScreens(string output)
    {
        EnsureApplication();
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Use a fresh report-details capture directory.");
        Directory.CreateDirectory(output);
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        foreach (var size in new[] { (780, 760), (520, 440) })
        {
            Themes.ThemeManager.Apply(theme);
            var model = new ReportDetailsViewModel(new ReportDetails { Title = "Study report", Authors = "Study authors",
                SampleDescription = "Sample description", Workflow = "Acquisition workflow", DatabaseKind = "Search database",
                WrittenInterpretation = "First interpretation paragraph.\n\nSecond interpretation paragraph." }, _ => { });
            var window = new ReportDetailsWindow(model) { Width = size.Item1, Height = size.Item2, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual };
            try
            {
                window.Show(); PumpDispatcher(); window.UpdateLayout();
                ScreenHarness.Save(window, Path.Combine(output, theme + "-" + size.Item1 + "x" + size.Item2 + ".png"), size.Item1, size.Item2);
            }
            finally { window.Close(); }
        }
        Console.WriteLine("Report details: 4 native captures, both themes and minimum/default sizes: " + output);
        return 0;
    }

    private static int RunReportDetailsTests()
    {
        string folder = Path.Combine(Path.GetTempPath(), "FamilyQuant.ReportDetails.Tests");
        Directory.CreateDirectory(folder);
        EnsureApplication();
        int applied = 0;
        ReportDetails? saved = null;
        var draft = new ReportDetailsViewModel(null, value => { applied++; saved = value; });
        Check("saving an unchanged empty draft adds no Undo step", !draft.Save() && applied == 0);
        draft.Title = "Private draft";
        Check("editing a draft does not touch the project", applied == 0);
        var editor = new ReportDetailsWindow(draft);
        editor.SaveButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check("native Save details applies the draft once", applied == 1 && saved?.Title == "Private draft");
        var slot = typeof(FamilyClusterFile).GetProperty("ReportDetails");
        Check("report details have a project slot", slot?.PropertyType == typeof(ReportDetails));
        if (slot == null) return 1;
        var file = new FamilyClusterFile();
        string original = file.ToJsonText();
        Check("old curation omits absent report details", !original.Contains("ReportDetails", StringComparison.Ordinal));
        var details = new ReportDetails
        {
            Title = "Study title", Authors = "Author one; Author two", SampleDescription = "Sample description",
            Workflow = "Acquisition workflow", DatabaseKind = "Database description",
            WrittenInterpretation = "First paragraph, with a comma.\r\n\r\nSecond paragraph.\n\nThird paragraph."
        };
        var curation = new CurationViewModel(file);
        int changed = 0;
        curation.DocumentChanged += (_, _) => changed++;
        Check("applying all details creates one Undo step and one document change", curation.ApplyReportDetails(details)
            && curation.UndoStack.Count == 1 && changed == 1);
        Check("saving the same fields again creates no Undo step", !curation.ApplyReportDetails(details) && curation.UndoStack.Count == 1);
        curation.Undo();
        Check("curation Undo restores the absent slot", file.ReportDetails == null && !curation.CanUndo && changed == 2);
        slot.SetValue(file, details);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "pt-BR", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                string path = Path.Combine(folder, culture + ".fqproj");
                ProjectStore.WriteFqproj(path, file, Array.Empty<QuantRow>(), new QuantManifest());
                var reopened = ProjectStore.ReadFqproj(path);
                Check("all report fields and paragraph breaks survive " + culture + " reopen", Equals(slot.GetValue(reopened.fcf), details));
            }
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        var capture = typeof(GroupingSnapshot).GetMethod("CaptureReportDetails");
        Check("report details have a dedicated undo snapshot", capture != null);
        if (capture != null)
        {
            var snapshot = (GroupingSnapshot)capture.Invoke(null, new object[] { file })!;
            slot.SetValue(file, details with { WrittenInterpretation = "Changed" });
            file.LockedUserIds.Add(9);
            snapshot.Restore(file);
            Check("Undo restores every report field", Equals(slot.GetValue(file), details));
            Check("report-text Undo leaves unrelated locks untouched", file.LockedUserIds.SetEquals(new[] { 9 }));
            file.LockedUserIds.Clear();
            slot.SetValue(file, null);
            snapshot = (GroupingSnapshot)capture.Invoke(null, new object[] { file })!;
            slot.SetValue(file, details);
            snapshot.Restore(file);
            Check("Undo to an old project restores its exact curation text", file.ToJsonText() == original);
        }
        string shellPath = Path.Combine(folder, "native.fqproj");
        ProjectStore.WriteFqproj(shellPath, new FamilyClusterFile(), Array.Empty<QuantRow>(), new QuantManifest());
        var shell = new MainWindow();
        try
        {
            Check("native shell opens the fixture without unsaved changes", shell.OpenProjectPath(shellPath) && !shell.DocumentState.IsModified);
            MainWindow.ReportDetailsPresenterOverride = window =>
            {
                window.ViewModel.Title = details.Title; window.ViewModel.Authors = details.Authors;
                window.ViewModel.SampleDescription = details.SampleDescription; window.ViewModel.Workflow = details.Workflow;
                window.ViewModel.DatabaseKind = details.DatabaseKind; window.ViewModel.WrittenInterpretation = details.WrittenInterpretation;
                window.SaveButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            };
            var menu = (System.Windows.Controls.MenuItem)shell.FindName("MenuItemReportDetails");
            Check("File Report details is available for an open project", menu.IsEnabled);
            menu.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            var reopenedEditor = shell.CreateReportDetailsEditor()!;
            Check("native menu applies all fields and marks the project changed", shell.DocumentState.IsModified
                && reopenedEditor.ViewModel.Title == details.Title && reopenedEditor.ViewModel.Authors == details.Authors
                && reopenedEditor.ViewModel.SampleDescription == details.SampleDescription && reopenedEditor.ViewModel.Workflow == details.Workflow
                && reopenedEditor.ViewModel.DatabaseKind == details.DatabaseKind && reopenedEditor.ViewModel.WrittenInterpretation == details.WrittenInterpretation);
            reopenedEditor.Close();
            System.Windows.Input.ApplicationCommands.Undo.Execute(null, shell);
            reopenedEditor = shell.CreateReportDetailsEditor()!;
            Check("shell Undo restores the old report fields", reopenedEditor.ViewModel.Title == "" && reopenedEditor.ViewModel.WrittenInterpretation == "");
            reopenedEditor.Close();
        }
        finally
        {
            MainWindow.ReportDetailsPresenterOverride = null;
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            try { shell.Close(); }
            finally { MainWindow.UnsavedProjectChoiceOverride = null; }
        }
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
