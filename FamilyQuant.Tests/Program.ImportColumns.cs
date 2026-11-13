using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportColumnsCommands() =>
    [new("--test-import-columns", 100, TestCommandPhase.Raw, args => args is ["--test-import-columns"], (_, _) =>
    {
        ImportColumnsTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void ImportColumnsTests()
    {
        Section("IM11 source columns and visible Next reasons");
        EnsureApplication();
        using var dialogs = new ImportFixtureDialogs();
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "fq2", "FQDeNovo", "v2118", "IM11", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "template.xlsx");
        PanelImportTemplate.Write(path);
        FillTemplate(path, new[] { new[] { "A", "First family", "First other name", "PF12345", "First source family" } });
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(new FamilyClusterFile())),
            workbookSource: new FamilyListWorkbookCoreSource());
        FinishReviewWork(async () => { await model.LoadAsync(path); await model.NextAsync(); });
        var window = new FamilyListImportWindow(model) { Width = 1366, Height = 768, Left = -30000, Top = -30000,
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); PumpDispatcher();
            var reason = (TextBlock)window.FindName("NextDisabledReason");
            var name = model.Columns.Single(column => column.Role == PanelImportFields.DisplayName);
            var abbreviation = model.Columns.Single(column => column.Role == PanelImportFields.FamilyKey);
            string nameHeader = name.Header;
            Check("IM11 template Columns hides absent Notes, Decision and Subclass names",
                model.MainColumns.Count == 5 && model.MainColumns.All(column => column.Role is not
                    (PanelImportFields.Notes or PanelImportFields.Decision or WorkbookImportSummaryFields.Subclasses)) && !reason.IsVisible);
            ScreenHarness.Save(window, Path.Combine(folder, "columns.png"), 1366, 768);
            name.Header = ""; PumpDispatcher();
            Check("IM11 a missing required mapping names its field beneath the buttons",
                !model.CanNext && reason.IsVisible && reason.Text == "Choose a column for Family name."
                && reason.TranslatePoint(new Point(), window).Y >= ((Button)window.FindName("ForwardButton")).TranslatePoint(new Point(), window).Y
                    + ((Button)window.FindName("ForwardButton")).ActualHeight);
            name.Header = abbreviation.Header; PumpDispatcher();
            Check("IM11 a duplicate mapping names the source column and both fields",
                !model.CanNext && reason.IsVisible && reason.Text.Contains("\"Abbreviation\"", StringComparison.Ordinal)
                && reason.Text.Contains("Abbreviation and Family name", StringComparison.Ordinal));
            ScreenHarness.Save(window, Path.Combine(folder, "duplicate.png"), 1366, 768);
            name.Header = nameHeader; PumpDispatcher();
            Check("IM11 fixing the mapping enables Next and hides its reason", model.CanNext && !reason.IsVisible);
            model.Columns.Single(column => column.Role == PanelImportFields.OtherName).Header = "";
            name.Header = PanelImportTemplate.OtherName;
            Check("IM11 mapping the other-language column as Family name remains supported", model.CanNext);
        }
        finally { window.Close(); }
        string optional = Path.Combine(folder, "optional.xlsx");
        PanelImportTemplate.Write(optional);
        SetImportHeaders(optional, new[] { "Abbreviation", "Name", "Other-language name", "Pfam domains", "UniProt family", "Notes", "Decision", "Subclass names" });
        FillTemplate(optional, new[] { new[] { "A", "First family", "Other name", "PF12345", "First source family", "Source notes", "Quantify", "First class; Second class" } });
        using var full = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(new FamilyClusterFile())),
            workbookSource: new FamilyListWorkbookCoreSource(), openWorkbookReview: true);
        FinishReviewWork(() => full.LoadAsync(optional));
        Check("IM11 source-present optional columns retain their mapping controls",
            new[] { PanelImportFields.Notes, PanelImportFields.Decision, WorkbookImportSummaryFields.Subclasses }
                .All(role => full.MainColumns.Any(column => column.Role == role)),
            string.Join(", ", full.Columns.Select(column => column.Role + "=" + column.Header)));
        full.Columns.Single(column => column.Role == PanelImportFields.Notes).Header = "";
        Check("IM11 choosing Not used keeps a source-present optional field available",
            full.MainColumns.Any(column => column.Role == PanelImportFields.Notes));
        Console.WriteLine("IM11 renders: " + folder);
    }
}
