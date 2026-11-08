using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using DocumentFormat.OpenXml.Packaging;
using XL = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static FamilyListImportViewModel ReadyFamilyListImport(string workbook, CurationViewModel curation, bool split = true,
        IFamilyListImportMatches? matcher = null)
    {
        var model = new FamilyListImportViewModel(new FamilyListImportTransaction(curation), matcher: matcher);
        FinishReviewWork(() => model.LoadAsync(workbook));
        FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(_ => true); });
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var row = model.SpecialRows.FirstOrDefault(r => r.Blocking);
            if (row == null) break;
            model.SelectedSpecialRow = row;
            if (row.SeveralNames) row.Action = split ? FamilyPanelRowAction.Split : FamilyPanelRowAction.ReportUnderOthers;
            else if (row.Duplicate) row.Action = FamilyPanelRowAction.Import;
            else throw new InvalidOperationException("Unexpected fixture problem: " + row.Message);
            model.UseRowChoice();
        }
        FinishReviewWork(async () => { await model.NextAsync(); }); model.SkipAllMatches();
        FinishReviewWork(async () => { await model.NextAsync(); });
        return model;
    }

    private static void FamilyListImportGuardTests(string workbook, string work)
    {
        var cancelled = ManualChoiceFile(); string cancelledBefore = cancelled.ToJsonText();
        var cancelModel = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(cancelled)));
        var cancelWindow = new FamilyListImportWindow(cancelModel) { Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
        cancelWindow.Show(); FinishReviewWork(() => cancelModel.LoadAsync(workbook)); cancelWindow.Close(); PumpDispatcher();
        Check("closing a loaded draft leaves the whole project unchanged", cancelled.ToJsonText() == cancelledBefore);

        var changed = ManualChoiceFile(); var changedCuration = new CurationViewModel(changed);
        using var changedModel = ReadyFamilyListImport(workbook, changedCuration);
        changed.MyExtendedFastaItems[0].UserAnnotation = "A later edit";
        string changedBefore = changed.ToJsonText();
        FinishReviewWork(async () => { await changedModel.NextAsync(); });
        Check("Import refuses an intervening project edit before publishing or adding Undo", !changedModel.Imported && changed.ToJsonText() == changedBefore
            && changedCuration.UndoStack.Count == 0 && changedModel.Status.Contains("project changed", StringComparison.Ordinal));

        string changedBook = Path.Combine(work, "changed-workbook.xlsx"); File.Copy(workbook, changedBook);
        var sourceFile = ManualChoiceFile(); var sourceCuration = new CurationViewModel(sourceFile);
        using var sourceModel = ReadyFamilyListImport(changedBook, sourceCuration);
        string sourceBefore = sourceFile.ToJsonText(); File.AppendAllText(changedBook, "changed");
        FinishReviewWork(async () => { await sourceModel.NextAsync(); });
        Check("Import rechecks the workbook hash and refuses changed bytes atomically", !sourceModel.Imported && sourceFile.ToJsonText() == sourceBefore
            && sourceCuration.UndoStack.Count == 0 && sourceModel.Status.Contains("workbook changed", StringComparison.Ordinal));

        var laterFile = ManualChoiceFile(); var laterCuration = new CurationViewModel(laterFile);
        using var laterModel = ReadyFamilyListImport(workbook, laterCuration, split: false);
        Check("Report under others is a valid explicit alternative for a several-name row", laterModel.DraftPanel!.Import!.RowChoices!.Any(r => r.Action == FamilyPanelRowAction.ReportUnderOthers)
            && laterModel.DraftPanel.Families.Any(f => f.Decision == FamilyPanelDecision.ReportUnderOthers));
        laterModel.KeepOtherSheets = true;
        FinishReviewWork(async () => { await laterModel.NextAsync(); });
        Check("keeping other sheets stores only their original text with sheet and cell provenance", laterModel.Imported
            && laterFile.FamilyPanel!.TextFields.Count(t => t.Name.StartsWith("Source sheet: ", StringComparison.Ordinal)) == 5
            && laterFile.FamilyPanel.TextFields.Where(t => t.Name.StartsWith("Source sheet: ", StringComparison.Ordinal)).All(t => t.Text.Contains(": ", StringComparison.Ordinal)));
        laterCuration.PushUndo(); laterFile.MyExtendedFastaItems[0].UserAnnotation = "Another later edit";
        string laterBefore = laterFile.ToJsonText(); laterModel.UndoImport();
        Check("Done Undo cannot consume another edit's snapshot", laterModel.Imported && !laterModel.CanUndo && laterFile.ToJsonText() == laterBefore
            && laterCuration.UndoStack.Count == 2 && laterModel.Status.Contains("later changes", StringComparison.Ordinal));

        var gate = new GatedImportReader(); var readFile = ManualChoiceFile(); string readBefore = readFile.ToJsonText();
        var readModel = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(readFile)), gate);
        var readWindow = new FamilyListImportWindow(readModel) { Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
        readWindow.Show();
        var reading = readModel.LoadAsync(workbook);
        FinishReviewWork(async () =>
        {
            await gate.Started.Task; readWindow.Close(); gate.Release.Set(); await readModel.CancelReadAsync(); await reading;
        }); PumpDispatcher();
        Check("Cancel while reading awaits the read and cannot publish a late result", !readWindow.IsVisible && !readModel.IsBusy && readModel.Preview == null
            && readFile.ToJsonText() == readBefore);
        gate.Release.Dispose();

        using var invalidModel = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())));
        FinishReviewWork(() => invalidModel.LoadAsync(Path.Combine(work, "absent.xlsx")));
        Check("an unreadable workbook is a plain problem and cannot advance", !invalidModel.CanNext && !invalidModel.IsBusy
            && invalidModel.Status.Length > 0 && invalidModel.Preview!.Problems.Count > 0);

        string template = Path.Combine(work, "filled-template.xlsx"); PanelImportTemplate.Write(template);
        using (var document = SpreadsheetDocument.Open(template, true))
        {
            var book = document.WorkbookPart!; var tab = book.Workbook!.Sheets!.Elements<XL.Sheet>().First(s => s.Name == PanelImportTemplate.FamilySheet);
            var part = (WorksheetPart)book.GetPartById(tab.Id!); var data = part.Worksheet!.GetFirstChild<XL.SheetData>()!;
            var row = new XL.Row { RowIndex = 2 };
            string[] values = { "FA", "Supplied family", "Família fornecida", "Supplied note", "PF12345", "Quantify" };
            for (int i = 0; i < values.Length; i++) row.Append(new XL.Cell { CellReference = ((char)('A' + i)).ToString() + "2", DataType = XL.CellValues.InlineString,
                InlineString = new XL.InlineString(new XL.Text(values[i])) });
            data.Append(row); part.Worksheet.Save();
        }
        using var templateModel = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())));
        FinishReviewWork(() => templateModel.LoadAsync(template)); FinishReviewWork(async () => { await templateModel.NextAsync(); });
        Check("the program's filled template maps its columns and exact decision without a mapping question", templateModel.Page == 2 && templateModel.CanNext
            && templateModel.Columns.Where(c => c.Required).All(c => c.Header.Length > 0) && templateModel.Decisions.All(d => d.Confirmed));

        FamilyListReimportTests(workbook);

        var fill = new ImportSourceFillConverter();
        foreach (string source in new[] { "#FFD700", "#FFF2CC" })
        {
            var cell = new PanelPreviewCell { Text = "Supplied source cell", Fill = source, FillName = "yellow or gold" };
            Check("prohibited source fill " + source + " is neutral with its original colour named", ReferenceEquals(fill.Convert(cell, typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture), Application.Current.FindResource("SurfaceBrush"))
                && ImportSourceDetailConverter.Describe(cell).Contains(source, StringComparison.Ordinal)
                && ImportSourceDetailConverter.Describe(cell).Contains("shown neutrally", StringComparison.Ordinal));
        }
        var blue = new PanelPreviewCell { Fill = "#336699", FillName = "blue" };
        Check("an allowed workbook fill remains source data instead of a theme decision", fill.Convert(blue, typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture) is SolidColorBrush brush
            && brush.Color.ToString() == "#FF336699");
    }

    private static void FamilyListReimportTests(string workbook)
    {
        using var seed = ReadyFamilyListImport(workbook, new CurationViewModel(ManualChoiceFile()));
        var definition = seed.DraftPanel!.Families.First(f => FamilyPanelValidation.ResolveFamilyKey(seed.DraftPanel, f.Key) == f.Key);
        var previous = new FamilyPanel { Name = "Previously confirmed list", Families = new() {
            System.Text.Json.JsonSerializer.Deserialize<FamilyPanelFamily>(System.Text.Json.JsonSerializer.Serialize(definition))! } };
        previous = FamilyQuant.Evidence.PanelEvidenceMatching.WithConfirmed(previous, definition.Key,
            FamilyQuant.Evidence.PanelEvidenceMatching.Confirm(new[] { definition.DisplayName },
                new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Keyword, "An earlier explicit match") }));
        var file = ManualChoiceFile(); file.FamilyPanel = previous;
        string before = file.ToJsonText(); string records = System.Text.Json.JsonSerializer.Serialize(previous.ConfirmedEvidenceMatches);
        var curation = new CurationViewModel(file);
        using var model = ReadyFamilyListImport(workbook, curation);
        var family = model.MatchFamilies.Single(f => f.Key == definition.Key);
        Check("re-import identifies prior confirmed choices and Skip keeps their original values", family.PreviouslyConfirmed && family.Choice == "Kept from previous panel"
            && family.Suggestions.Single(s => s.PreviousValue != null).Text == "An earlier explicit match");
        FinishReviewWork(async () => { await model.NextAsync(); });
        Check("the production wizard carries stable-key confirmations through replacement without changing the source panel", model.Imported
            && System.Text.Json.JsonSerializer.Serialize(file.FamilyPanel!.ConfirmedEvidenceMatches) == records
            && System.Text.Json.JsonSerializer.Serialize(previous.ConfirmedEvidenceMatches) == records);
        model.UndoImport();
        Check("Undo after re-import restores the previous panel and its confirmations byte for byte", file.ToJsonText() == before);
        model.Back(); model.SelectedMatchFamily = model.MatchFamilies.Single(f => f.Key == definition.Key);
        var selected = model.SelectedMatchFamily.Suggestions.Single(s => s.PreviousValue != null);
        selected.Text = "An explicitly confirmed replacement";
        Check("editing a kept value requires a new explicit confirmation", model.SelectedMatchFamily.Choice == "Needs a choice" && !model.CanNext);
        model.ConfirmMatches(); FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); });
        Check("an explicit re-import replacement wins over the carried record", model.Imported
            && file.FamilyPanel!.ConfirmedEvidenceMatches![definition.Key].Choices.Single().Value == "An explicitly confirmed replacement");
    }

    private sealed class GatedImportReader : IPanelSheetPreviewReader
    {
        private readonly PanelSpreadsheetReader actual = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public PanelSheetPreview ReadPreview(string path, string sheetName = "")
        { Started.TrySetResult(); if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new IOException("Fixture read was not released."); return actual.ReadPreview(path, sheetName); }
        public IReadOnlyList<string> ReadSheetNames(string path) => actual.ReadSheetNames(path);
        public PanelSpreadsheetTable ReadTable(string path, string sheetName, int headerRowNumber = 1) => actual.ReadTable(path, sheetName, headerRowNumber);
    }

    private static void FamilyListChecklistHandoffTests()
    {
        var file = ChecklistActionFile(); string before = file.ToJsonText();
        var session = new EvidenceGroupingSession(file, "import handoff fixture"); int updates = 0;
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
        var window = new SuperClusterPreviewWindow(preview, null, scope => { updates++; return session.Propose(scope); }, initialScope: "selected", evidenceMode: true)
        { Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.RequestFamilyChecklist(); window.Show(); PumpDispatcher();
            Check("the import checklist handoff opens review without an implicit proposal update", updates == 0 && file.ToJsonText() == before);
            var grouping = (GroupingReviewViewModel)window.DataContext;
            FinishReviewWork(grouping.UpdateProposalAsync); PumpDispatcher();
            var tabs = (TabControl)window.FindName("evidenceTabs");
            Check("after explicit Update the pending request selects the actual checklist tab", updates == 1
                && tabs.SelectedItem is TabItem { Header: "Family checklist" } && file.ToJsonText() == before);
        }
        finally { window.Close(); PumpDispatcher(); }
    }
}
