using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportConditionalPageCommands() =>
    [new("--test-import-conditional-pages", 100, TestCommandPhase.Raw,
        args => args is ["--test-import-conditional-pages"] or ["--test-import-conditional-pages", "--screens", _], (args, _) =>
        {
            ImportConditionalPageTests(args.Length == 3 ? args[2] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ImportConditionalPageTests(string? output = null)
    {
        Section("IM8 file, columns and summary, with choices only when needed");
        EnsureApplication();
        string folder = ImportSimpleWork("IM8");
        var file = ManualChoiceFile();
        file.MyExtendedFastaItems[0].Evidence!.Pfam = new() { new() { Accession = "PF12345", Name = "First domain" } };
        string before = file.ToJsonText();
        string path = Path.Combine(folder, "template.xlsx");
        PanelImportTemplate.Write(path);
        FillTemplate(path, new[] { PanelImportTemplate.Columns.Select(column => column.Role switch
        {
            PanelImportFields.FamilyKey => "A", PanelImportFields.DisplayName => "First family",
            PanelImportFields.Pfam => "PF12345", _ => "",
        }).ToArray() });
        var oldMessages = ShellMessages.Presenter;
        var oldTheme = Themes.ThemeManager.Current;
        var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(file)),
            workbookSource: new FamilyListWorkbookCoreSource(), openWorkbookReview: true);
        var window = new FamilyListImportWindow(model, templateWriter: PanelImportTemplate.Write)
        {
            Left = -30000, Top = -30000, Width = 1366, Height = 820, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
        };
        try
        {
            ShellMessages.Presenter = (_, _) => MessageBoxResult.Yes;
            Themes.ThemeManager.Apply(Themes.AppTheme.Light);
            window.Show(); FinishReviewWork(() => model.LoadAsync(path));
            var visited = new List<int> { model.Page };
            int prompts = 0;
            CaptureFamilyListPage(window, output, Themes.AppTheme.Light, 1366, 820, "01-file");
            FinishReviewWork(async () => { await model.NextAsync(_ => { prompts++; return false; }); });
            visited.Add(model.Page);
            CaptureFamilyListPage(window, output, Themes.AppTheme.Light, 1366, 820, "02-columns");
            FinishReviewWork(async () => { await model.NextAsync(_ => { prompts++; return false; }); });
            visited.Add(model.Page);
            CaptureFamilyListPage(window, output, Themes.AppTheme.Light, 1366, 820, "03-summary");
            Check("IM8 the program template takes exactly three pages with no confirmation prompt",
                visited.SequenceEqual(new[] { 1, 2, 5 }) && model.Steps.Select(step => step.Index).SequenceEqual(visited)
                && model.Steps.Select(step => step.Number).SequenceEqual(new[] { "1", "2", "3" })
                && prompts == 0 && model.CanNext && !model.ReviewingWorkbook && !model.ReviewingSubclasses
                && model.Columns.Single(column => column.Role == PanelImportFields.UniProtFamilies).Header == "UniProt family"
                && model.SkippedSheetsSummary.Split('\n').Count(line => line == "Sheet Instructions was skipped: it has no family columns") == 1
                && file.ToJsonText() == before);
            var details = (FamilyListWorkbookOverview)window.FindName("SummaryWorkbookDetails");
            var expander = WalkTree(window).OfType<System.Windows.Controls.Expander>().Single(control => control.Header as string == "Source details");
            expander.IsExpanded = true; window.UpdateLayout();
            Check("IM8 original workbook details remain reachable within Summary", model.Page == 5
                && ((System.Windows.Controls.TabControl)details.FindName("WorkbookDetailsTabs")).ActualHeight > 100
                && file.ToJsonText() == before);
            expander.IsExpanded = false;
            model.Back();
            Check("IM8 Back revisits columns without opening unused pages", model.Page == 2 && file.ToJsonText() == before);

            string unmarked = Path.Combine(folder, "own-headers.xlsx");
            File.Copy(path, unmarked, true);
            using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(unmarked, true))
            {
                var workbook = document.WorkbookPart!.Workbook!;
                foreach (var sheet in workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Where(sheet => sheet.Name != "Families").ToArray()) sheet.Remove();
                workbook.DefinedNames?.Remove(); workbook.Save();
            }
            var importer = new FamilyPanelSpreadsheetImporter();
            var plain = importer.ProposeWorkbook(unmarked);
            Check("IM8 own headers map directly even without the template marker", !plain.NeedsFamilySheetChoice
                && PanelImportTemplate.Columns.All(column => plain.FamilyProposal.Choices.ColumnHeaders.GetValueOrDefault(column.Role) == column.Header));

            path = Path.Combine(folder, "name-choice.xlsx");
            PanelImportTemplate.Write(path); SetImportHeaders(path, new[] { "Abbreviation", "Name", "Pfam domains" });
            FillTemplate(path, new[] { new[] { "A", "First family", "First domain" } });
            FinishReviewWork(() => model.LoadAsync(path));
            FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); });
            Check("IM8 a Pfam name opens its single resolving row instead of skipping it", model.Page == 4
                && model.PfamQuestions.Count == 1 && !model.CanNext && model.SelectedMatchFamily?.Key == "A");
            model.Revisit(2); model.Columns.Single(column => column.Role == PanelImportFields.Pfam).Header = "";
            FinishReviewWork(async () => { await model.NextAsync(); });
            Check("IM8 remapping Pfam to Not used removes the old question and keeps its text", model.Page == 5 && model.PfamQuestions.Count == 0
                && model.CanNext && model.DraftPanel!.Families.Single().TextFields.Any(field => field.Text == "First domain"));

            path = Path.Combine(folder, "row-choice.xlsx");
            PanelImportTemplate.Write(path); SetImportHeaders(path, new[] { "Abbreviation", "Name", "Pfam domains" });
            FillTemplate(path, new[] { new[] { "A", "First family; Second family", "PF12345" } });
            FinishReviewWork(() => model.LoadAsync(path));
            FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); });
            Check("IM8 a multiple-name row opens Special rows and cannot advance unresolved", model.Page == 3 && !model.CanNext);

            path = Path.Combine(folder, "decision-choice.xlsx");
            PanelImportTemplate.Write(path); SetImportHeaders(path, new[] { "Abbreviation", "Name", "Pfam domains", "Decision" });
            FillTemplate(path, new[] { new[] { "A", "First family", "PF12345", "Keep this" } });
            FinishReviewWork(() => model.LoadAsync(path));
            FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); });
            Check("IM8 custom decision words open their choice page", model.Page == 2 && model.ReviewingDecisions && !model.CanNext);

            path = Path.Combine(folder, "saved-panel.xlsx");
            FamilyPanelTemplate.Write(path, new FamilyPanel { Families = new() { new() { Key = "A", DisplayName = "First family",
                Pfam = new() { new() { Accession = "PF12345" } }, UniProtFamilies = new() { "First source family" } } } });
            var saved = importer.ProposeWorkbook(path);
            Check("IM8 the saved panel's own headers select row 2 and keep its key and source family column",
                saved.FamilyTable?.HeaderRowNumber == 2 && !saved.NeedsFamilySheetChoice
                && saved.FamilyProposal.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.FamilyKey) == "Key"
                && saved.FamilyProposal.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.UniProtFamilies) == "UniProt family");
        }
        finally
        {
            window.Close();
            ShellMessages.Presenter = oldMessages;
            Themes.ThemeManager.Apply(oldTheme);
        }
    }
}
