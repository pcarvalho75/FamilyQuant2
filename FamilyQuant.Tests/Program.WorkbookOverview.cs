using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DocumentFormat.OpenXml.Packaging;
using XL = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> WorkbookOverviewCommands() =>
    [new("--test-workbook-overview", 100, TestCommandPhase.Raw, args => args.Contains("--test-workbook-overview", StringComparer.Ordinal), (args, _) =>
    {
        WorkbookOverviewTests(WorkbookScreenOutput(args));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void WriteOverviewShape(string path, bool decisions, bool notes)
    {
        using var document = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart(); workbook.Workbook = new XL.Workbook(); var sheets = workbook.Workbook.AppendChild(new XL.Sheets());
        void Add(string name, uint id, string[][] rows)
        {
            var part = workbook.AddNewPart<WorksheetPart>(); var data = new XL.SheetData(); part.Worksheet = new XL.Worksheet(data);
            for (int r = 0; r < rows.Length; r++)
            {
                var row = new XL.Row { RowIndex = (uint)(r + 1) };
                for (int c = 0; c < rows[r].Length; c++) row.Append(new XL.Cell { CellReference = ((char)('A' + c)).ToString() + (r + 1),
                    DataType = XL.CellValues.InlineString, InlineString = new XL.InlineString(new XL.Text(rows[r][c])) });
                data.Append(row);
            }
            sheets.Append(new XL.Sheet { Name = name, SheetId = id, Id = workbook.GetIdOfPart(part) }); part.Worksheet.Save();
        }
        Add("Supplied list", 1, decisions ? new[] { new[] { "Abbreviation", "Family name", "Decision" }, new[] { "A", "Alpha family", "Quantify" },
            new[] { "B", "Beta family", "Not a family" }, new[] { "C", "Gamma family", "Quantify" }, new[] { "D", "Delta family", "Not a family" } }
            : new[] { new[] { "Abbreviation", "Family name" }, new[] { "A", "Alpha family" }, new[] { "B", "Beta family" }, new[] { "C", "Gamma family" } });
        if (notes) Add("Source context", 2, new[] { new[] { "Context" }, new[] { "Original explanatory source text with no family rows." } });
        workbook.Workbook.Save();
    }

    private static FamilyClusterFile WorkbookOverviewProject()
    {
        var project = ManualChoiceFile();
        project.MyExtendedFastaItems[0].Evidence = new() { Pfam = new() {
            new() { Accession = "PF00089", Name = "DomainOne" }, new() { Accession = "PF01421", Name = "DomainTwo" } } };
        return project;
    }

    private static void ResolveWorkbookUiDraft(FamilyListImportViewModel model)
    {
        FinishReviewWork(() => model.ReviewWorkbookSectionAsync("decisions"));
        foreach (var decision in model.Decisions.Where(d => d.Decision == null)) decision.Decision = FamilyPanelDecision.Quantify;
        FinishReviewWork(() => model.ReviewWorkbookSectionAsync("rows"));
        for (int attempt = 0; attempt < 100 && model.SpecialRows.FirstOrDefault(r => r.Blocking) is { } row; attempt++)
        { model.SelectedSpecialRow = row; row.Action = row.SeveralNames ? FamilyPanelRowAction.Split : FamilyPanelRowAction.Import; model.UseRowChoice(); }
        model.ReturnToWorkbookReview();
    }

    private static void WorkbookOverviewTests(string? output = null)
    {
        Section("Z16a9 actual Core summary and one explicit import over three source shapes");
        EnsureApplication();
        string work = Path.Combine(Path.GetTempPath(), "fq-workbook-overview-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        string six = Path.Combine(work, "six.xlsx"); ExampleFamilyList.Write(six);
        string one = Path.Combine(work, "one.xlsx"); WriteOverviewShape(one, decisions: true, notes: false);
        string noLegend = Path.Combine(work, "no-legend.xlsx"); WriteOverviewShape(noLegend, decisions: false, notes: true);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var shape in new[] { (Name: "six", Path: six), (Name: "one", Path: one), (Name: "no-legend", Path: noLegend) })
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (W: 1366, H: 768), (W: 1920, H: 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                string hash = TestFileHash.Upper(shape.Path);
                var project = WorkbookOverviewProject(); string before = project.ToJsonText(); var curation = new CurationViewModel(project);
                var source = new ObservedWorkbookSource();
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(curation), workbookSource: source, openWorkbookReview: true);
                var window = new FamilyListImportWindow(model) { Left = -30000, Top = -30000, Width = size.W, Height = size.H,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                string label = shape.Name + " " + theme + " " + size.W;
                string? folder = output == null ? null : Path.Combine(output, shape.Name);
                try
                {
                    window.Show(); FinishReviewWork(() => model.LoadAsync(shape.Path)); PumpDispatcher();
                    Check(label + " opens directly on the real complete proposal with one acceptance action", model.ReviewingWorkbook && !model.ShowingWizardPages
                        && model.Title == "Review your workbook" && model.ForwardLabel == "Accept and import" && model.WorkbookSections.Count == 8);
                    Check(label + " proposals remain unconfirmed and leave the project unchanged", source.Completes == 0 && project.ToJsonText() == before
                        && curation.UndoStack.Count == 0 && model.Decisions.All(d => !d.Confirmed) && model.SubclassChoices.All(s => !s.Confirmed));
                    Check(label + " every real finding has source state and a reason", model.WorkbookFindings.Count > 0
                        && model.WorkbookFindings.All(f => f.State.Length > 0 && f.Reason.Length > 0));
                    CaptureFamilyListPage(window, folder, theme, size.W, size.H, "01-workbook-proposal");
                    if (!model.CanAcceptWorkbook)
                    {
                        Check(label + " unresolved required choices stay visible and block acceptance", model.WorkbookReviewProblems.Count > 0);
                        string original = System.Text.Json.JsonSerializer.Serialize(source.Last);
                        ResolveWorkbookUiDraft(model);
                        Check(label + " explicit draft edits preserve the original proposal and project", System.Text.Json.JsonSerializer.Serialize(source.Last) == original
                            && project.ToJsonText() == before && model.Decisions.All(d => !d.Confirmed));
                    }
                    Check(label + " reviewed family rows are available before acceptance", model.WorkbookFamilies.Count > 0 && model.CanAcceptWorkbook,
                        string.Join("; ", model.WorkbookReviewProblems));
                    if (!model.CanAcceptWorkbook) continue;
                    if (model.SourceColorChoices.FirstOrDefault() is { } category)
                    {
                        category.Label = "Reviewed category";
                        Check(label + " summary shows the edited category without changing source facts", model.WorkbookLegend.Any(c => c.ProposedName == "Reviewed category")
                            && project.ToJsonText() == before);
                    }
                    if (shape.Name == "six" && model.SubclassChoices.FirstOrDefault(c => c.IsUncertain
                        && model.WorkbookFamilyKeys.Contains(c.Family) && c.Rules.Any(r => !r.IsUncertain && r.Accession.Length > 0)) is { } resolvedClass)
                    {
                        FinishReviewWork(() => model.ReviewWorkbookSectionAsync("subclasses"));
                        resolvedClass.Abbreviation = "reviewed-class"; resolvedClass.Name = "Reviewed subclass";
                        foreach (var rule in resolvedClass.Rules) rule.Use = !rule.IsUncertain && rule.Accession.Length > 0;
                        resolvedClass.Use = true; model.ReturnToWorkbookReview();
                        Check(label + " explicit valid subclass resolution accepts only certain canonical rules", model.CanAcceptWorkbook && !resolvedClass.Confirmed,
                            string.Join("; ", model.WorkbookReviewProblems));
                        if (!model.CanAcceptWorkbook) continue;
                    }
                    var shown = model.WorkbookFamilies.ToArray();
                    var categoryLabels = model.SourceColorChoices.Select(c => (c.Color, c.Label)).ToArray();
                    int subclasses = model.SubclassChoices.Count(c => c.Use);
                    CaptureFamilyListPage(window, folder, theme, size.W, size.H, "02-reviewed-proposal");
                    if (shape.Name == "six")
                    {
                        var details = (TabControl)((FamilyListWorkbookOverview)window.FindName("WorkbookOverview")).FindName("WorkbookDetailsTabs");
                        for (int tab = 1; tab < details.Items.Count; tab++)
                        { details.SelectedIndex = tab; CaptureFamilyListPage(window, folder, theme, size.W, size.H, "02-detail-" + tab); }
                        details.SelectedIndex = 0;
                        FinishReviewWork(() => model.ReviewWorkbookSectionAsync("subclasses"));
                        Check(label + " subclass review contains the real unconfirmed Core proposals", model.ReviewingSubclasses && model.SubclassChoices.All(s => !s.Confirmed));
                        CaptureFamilyListPage(window, folder, theme, size.W, size.H, "03-subclass-review");
                        model.Back(); Check(label + " subclass Back returns without applying anything", model.ReviewingWorkbook && project.ToJsonText() == before);
                    }
                    FinishReviewWork(async () => { await model.NextAsync(_ => true); });
                    Check(label + " one press imports exactly the real family preview as one Undo", model.Imported && source.Completes == 0 && curation.UndoStack.Count == 1
                        && shown.Select(f => (f.Abbreviation, f.Name, f.Decision, f.MergeTarget)).SequenceEqual(project.FamilyPanel!.Families.Select(f =>
                            (f.Key, f.DisplayName, FamilyListImportViewModel.DecisionOptions.First(d => d.Value == f.Decision).Label, f.MergeTargetFamilyKey))));
                    Check(label + " the accepted panel retains shown notes subclasses and category names", project.FamilyPanel!.SourceNotes.Count == source.Last.Notes.Count
                        + (model.SubclassReviewFindings.Any(item => item.Label == "Subclass notes") ? 1 : 0)
                        && project.FamilyPanel.Families.Sum(f => f.Subclasses.Count) == subclasses
                        && categoryLabels.SequenceEqual((project.FamilyPanel.Import?.ColorLabels ?? new()).Select(c => (c.Color, c.Label))));
                    string saved = Path.Combine(work, label + ".fqproj"); ProjectStore.WriteFqproj(saved, project, new List<QuantRow>(), new QuantManifest());
                    Check(label + " actual acceptance survives save and reopen", ProjectStore.ReadFqproj(saved).fcf.ToJsonText() == project.ToJsonText());
                    model.UndoImport(); Check(label + " one Undo restores the entire original project", project.ToJsonText() == before && curation.UndoStack.Count == 0);
                    Check(label + " the source workbook bytes remain unchanged", TestFileHash.Upper(shape.Path) == hash);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
        WorkbookOverviewGuardTests(one, work);
        WorkbookSubclassFindingUiTests(work);
    }

    private static void WorkbookOverviewGuardTests(string path, string work)
    {
        string changedPath = Path.Combine(work, "changed-after-review.xlsx"); File.Copy(path, changedPath);
        var project = WorkbookOverviewProject(); string before = project.ToJsonText();
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(project)), workbookSource: new ObservedWorkbookSource(), openWorkbookReview: true);
        FinishReviewWork(() => model.LoadAsync(changedPath));
        File.AppendAllText(changedPath, "changed");
        bool accepted = true; FinishReviewWork(async () => { accepted = await model.NextAsync(_ => true); });
        Check("one-press acceptance rechecks every workbook byte", !accepted && !model.Imported && project.ToJsonText() == before
            && model.Status.Contains("workbook changed", StringComparison.OrdinalIgnoreCase));
        string ambiguousPath = Path.Combine(work, "ambiguous.xlsx"); WriteOverviewShape(ambiguousPath, decisions: false, notes: false);
        using (var document = SpreadsheetDocument.Open(ambiguousPath, true))
        {
            var part = document.WorkbookPart!; var first = part.WorksheetParts.Single(); var extra = part.AddNewPart<WorksheetPart>();
            extra.Worksheet = (XL.Worksheet)first.Worksheet!.CloneNode(true);
            part.Workbook!.Sheets!.Append(new XL.Sheet { Name = "Another source list", SheetId = 2, Id = part.GetIdOfPart(extra) });
            extra.Worksheet.Save(); part.Workbook.Save();
        }
        var source = new ObservedWorkbookSource();
        using var ambiguous = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(WorkbookOverviewProject())), workbookSource: source, openWorkbookReview: true);
        FinishReviewWork(() => ambiguous.LoadAsync(ambiguousPath));
        Check("the real summary never chooses the first ambiguous family list", ambiguous.NeedsFamilySheetChoice && !ambiguous.CanAcceptWorkbook
            && ambiguous.WorkbookReviewProblems.Count > 0);
        ambiguous.SelectedWorkbookSheet = ambiguous.WorkbookSheets[0]; FinishReviewWork(ambiguous.ChooseWorkbookFamilySheetAsync);
        Check("an explicit sheet choice resolves the actual summary ambiguity", !ambiguous.NeedsFamilySheetChoice && ambiguous.CanAcceptWorkbook
            && source.Last.FamilySheet == ambiguous.WorkbookSheets[0].Name);

        var ignoredProject = WorkbookOverviewProject();
        using var ignored = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ignoredProject)), workbookSource: new ObservedWorkbookSource(), openWorkbookReview: true);
        FinishReviewWork(() => ignored.LoadAsync(path));
        ignored.Columns.Single(c => c.Role == PanelImportFields.Decision).Header = "";
        ignored.ReturnToWorkbookReview();
        Check("an explicit Not used decision choice is never inferred again", !ignored.Columns.Single(c => c.Role == PanelImportFields.Decision).HasChoice
            && ignored.CanAcceptWorkbook && ignored.WorkbookFamilies.All(f => f.Decision == "Quantify"));
        WorkbookOverviewBroadGuardTest(work);
        WorkbookOverviewFilledNavigationTest(work);
        WorkbookOverviewMeaningPunctuationTests(work);
        WorkbookFamilySheetDraftTests(work);
        WorkbookPlainWordsTests(work);
    }

    private static void WorkbookFamilySheetDraftTests(string work)
    {
        string path = Path.Combine(work, "reviewed-sheet-choice.xlsx"); WriteOverviewShape(path, decisions: true, notes: false);
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            var workbook = document.WorkbookPart!; var first = workbook.WorksheetParts.Single();
            var data = first.Worksheet!.GetFirstChild<XL.SheetData>()!; data.RemoveAllChildren();
            void Row(int number, params string[] values)
            {
                var row = new XL.Row { RowIndex = (uint)number };
                for (int column = 0; column < values.Length; column++) row.Append(new XL.Cell { CellReference = ((char)('A' + column)).ToString() + number,
                    DataType = XL.CellValues.InlineString, InlineString = new XL.InlineString(new XL.Text(values[column])) });
                data.Append(row);
            }
            Row(1, "Abbreviation", "Family name", "Decision");
            for (int i = 1; i <= 13; i++) Row(i + 1, "F" + i, "Family " + i, "Choice " + i);
            first.Worksheet.Save();
            var second = workbook.AddNewPart<WorksheetPart>(); second.Worksheet = (XL.Worksheet)first.Worksheet.CloneNode(true); second.Worksheet.Save();
            workbook.Workbook!.Sheets!.Append(new XL.Sheet { Name = "Other families", SheetId = 2, Id = workbook.GetIdOfPart(second) }); workbook.Workbook.Save();
        }
        var project = WorkbookOverviewProject(); string before = project.ToJsonText(); string hash = TestFileHash.Upper(path);
        var source = new ObservedWorkbookSource();
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(project)), workbookSource: source, openWorkbookReview: true);
        FinishReviewWork(() => model.LoadAsync(path, "Supplied list"));
        model.Columns.Single(column => column.Role == PanelImportFields.Decision).Header = "Decision";
        foreach (var decision in model.Decisions) decision.Decision = FamilyPanelDecision.Quantify;
        model.ConfirmDecisions(); model.PanelName = "Reviewed import";
        var originalRows = model.Decisions.ToArray();
        Check("family sheet regression begins with thirteen confirmed source actions", originalRows.Length == 13 && originalRows.All(d => d.Confirmed),
            "rows=" + originalRows.Length + "; confirmed=" + originalRows.Count(d => d.Confirmed));
        model.SelectedWorkbookSheet = model.WorkbookSheets.Single(sheet => sheet.IsFamilySheet);
        FinishReviewWork(model.ChooseWorkbookFamilySheetAsync);
        Check("confirming the proposed family sheet preserves all thirteen reviewed decisions and the draft", model.Decisions.Count == 13
            && model.Decisions.All(decision => decision.Confirmed) && model.Decisions.SequenceEqual(originalRows)
            && model.PanelName == "Reviewed import" && source.Reads == 1,
            "rows=" + model.Decisions.Count + "; confirmed=" + model.Decisions.Count(d => d.Confirmed) + "; same rows=" + model.Decisions.SequenceEqual(originalRows)
                + "; name=" + model.PanelName + "; reads=" + source.Reads);
        Check("confirming the same sheet never publishes or changes workbook bytes", project.ToJsonText() == before && TestFileHash.Upper(path) == hash && source.Completes == 0);
        int questions = 0;
        model.ConfirmWorkbookSheetChange = message => { questions++; return false; };
        model.SelectedWorkbookSheet = model.WorkbookSheets.Single(sheet => sheet.Name == "Other families");
        FinishReviewWork(model.ChooseWorkbookFamilySheetAsync);
        Check("declining a different family sheet preserves all reviewed choices before any reload", questions == 1 && source.Reads == 1
            && model.Sheet == "Supplied list" && model.Decisions.SequenceEqual(originalRows) && model.Decisions.All(d => d.Confirmed));
        model.ConfirmWorkbookSheetChange = message => { questions++; return message.Contains("clears your reviewed decisions", StringComparison.Ordinal); };
        FinishReviewWork(() => model.SelectWorkbookFamilySheetAsync("Other families"));
        Check("a different family sheet clears choices only after the explicit warning is accepted", questions == 2 && source.Reads == 2
            && model.Sheet == "Other families" && model.Decisions.All(d => !d.Confirmed) && model.PanelName != "Reviewed import"
            && project.ToJsonText() == before && TestFileHash.Upper(path) == hash);
    }

    private static void WorkbookOverviewMeaningPunctuationTests(string work)
    {
        foreach (string ending in new[] { "", "." })
        {
            string path = Path.Combine(work, ending.Length == 0 ? "meaning-no-stop.xlsx" : "meaning-stop.xlsx"); ExampleFamilyList.Write(path);
            var proposal = new FamilyPanelSpreadsheetImporter().ProposeWorkbook(path);
            var entry = proposal.Decisions.Single(d => d.SourceValue == "Opcional").LegendEntries.Single();
            var table = proposal.Workbook.Sheets.Single(s => s.SheetName == entry.SheetName).Table!;
            string meaning = "Quantify when evidence is sufficient" + ending;
            using (var document = SpreadsheetDocument.Open(path, true))
            {
                var workbook = document.WorkbookPart!;
                var source = workbook.Workbook!.Sheets!.Elements<XL.Sheet>().Single(s => s.Name == entry.SheetName);
                var part = (WorksheetPart)workbook.GetPartById(source.Id!.Value!);
                string address = table.ColumnLetters[1] + entry.RowNumber;
                var cell = part.Worksheet!.Descendants<XL.Cell>().Single(c => c.CellReference == address);
                cell.DataType = XL.CellValues.InlineString; cell.CellValue = null; cell.InlineString = new XL.InlineString(new XL.Text(meaning));
                part.Worksheet.Save();
            }
            var observed = new ObservedWorkbookSource();
            using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(WorkbookOverviewProject())),
                workbookSource: observed, openWorkbookReview: true);
            FinishReviewWork(() => model.LoadAsync(path));
            Check("legend meaning " + (ending.Length == 0 ? "without" : "with") + " a trailing stop ends once in the review status",
                model.WorkbookReviewStatus.Contains("Legend meaning: Quantify when evidence is sufficient. ", StringComparison.Ordinal)
                && !model.WorkbookReviewStatus.Contains("sufficient..", StringComparison.Ordinal)
                && model.Decisions.Single(d => d.Source == "Opcional").Meaning == meaning, model.WorkbookReviewStatus);
            string reason = observed.Last.Decisions.Single(d => d.SourceValue == "Opcional").Reason;
            Check("legend reason " + (ending.Length == 0 ? "without" : "with") + " a source stop ends its meaning sentence once",
                reason.Contains("Legend meaning: Quantify when evidence is sufficient. ", StringComparison.Ordinal)
                && !reason.Contains("sufficient..", StringComparison.Ordinal), reason);
        }
    }

    private static void WorkbookPlainWordsTests(string work)
    {
        string path = Path.Combine(work, "plain-workbook.xlsx"); ExampleFamilyList.Write(path);
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(WorkbookOverviewProject())),
            workbookSource: new ObservedWorkbookSource(), openWorkbookReview: true);
        FinishReviewWork(() => model.LoadAsync(path));
        string reason = model.WorkbookSheets.Single(sheet => sheet.IsFamilySheet).Reason;
        Check("family-list explanation uses plain words rather than ranking and short-code terms", reason.Contains("abbreviation", StringComparison.OrdinalIgnoreCase)
            && !reason.Contains("visible-first", StringComparison.Ordinal) && !reason.Contains("short-code", StringComparison.Ordinal), reason);
        string card = model.WorkbookSections.Single(section => section.Key == "sheets").Title;
        FinishReviewWork(() => model.ReviewWorkbookSectionAsync("sheets"));
        Check("Workbook sheets card opens a page with the same name", card == "Workbook sheets" && model.Title == card
            && model.StepLabel.EndsWith(card, StringComparison.Ordinal), model.Title + "; " + model.StepLabel);
        Check("Workbook sheets page explanation names the sheet roles rather than the old file and sheet step",
            model.Explanation.Contains("role", StringComparison.OrdinalIgnoreCase)
            && !model.Explanation.Contains("Choose a file and sheet", StringComparison.Ordinal), model.Explanation);
    }

    private static void WorkbookOverviewFilledNavigationTest(string work)
    {
        string path = Path.Combine(work, "filled-decisions.xlsx"); WriteOverviewShape(path, decisions: true, notes: false);
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            var workbook = document.WorkbookPart!;
            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new XL.Stylesheet(new XL.Fonts(new XL.Font()) { Count = 1 },
                new XL.Fills(new XL.Fill(new XL.PatternFill { PatternType = XL.PatternValues.None }),
                    new XL.Fill(new XL.PatternFill { PatternType = XL.PatternValues.Gray125 }),
                    new XL.Fill(new XL.PatternFill(new XL.ForegroundColor { Rgb = "FFD6EAF8" }, new XL.BackgroundColor { Indexed = 64 }) { PatternType = XL.PatternValues.Solid }),
                    new XL.Fill(new XL.PatternFill(new XL.ForegroundColor { Rgb = "FFE2F0D9" }, new XL.BackgroundColor { Indexed = 64 }) { PatternType = XL.PatternValues.Solid })) { Count = 4 },
                new XL.Borders(new XL.Border()) { Count = 1 }, new XL.CellStyleFormats(new XL.CellFormat()) { Count = 1 },
                new XL.CellFormats(new XL.CellFormat(), new XL.CellFormat { FillId = 2, ApplyFill = true }, new XL.CellFormat { FillId = 3, ApplyFill = true }) { Count = 3 });
            styles.Stylesheet.Save();
            var sheet = workbook.WorksheetParts.Single().Worksheet!;
            foreach (var row in sheet.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1))
                row.Elements<XL.Cell>().First().StyleIndex = row.RowIndex!.Value == 3 ? 2U : 1U;
            sheet.Save();
        }
        string sourceHash = TestFileHash.Upper(path);
        var project = WorkbookOverviewProject(); string before = project.ToJsonText(); var source = new ObservedWorkbookSource();
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(project)), workbookSource: source, openWorkbookReview: true);
        FinishReviewWork(() => model.LoadAsync(path));
        model.ColorColumn!.Header = model.Columns.Single(c => c.Role == PanelImportFields.FamilyKey).Header;
        Check("filled workbook starts with mapped headers and draft decision actions", model.CanAcceptWorkbook && model.SourceColorChoices.Count == 2
            && model.Columns.Single(c => c.Role == PanelImportFields.Decision).HasChoice && model.Decisions.All(d => !d.Confirmed && d.Decision != null),
            "colours=" + model.SourceColorChoices.Count + "; " + string.Join("; ", model.WorkbookReviewProblems));
        FinishReviewWork(() => model.ReviewWorkbookSectionAsync("rows"));
        Check("filled workbook special-rows navigation keeps proposed decisions without requiring early confirmation", model.Page == 3 && model.CanNext
            && model.SpecialRows.All(row => !row.Blocking), string.Join("; ", model.Problems.Select(problem => problem.Message)));
        bool continued = false; FinishReviewWork(async () => { continued = await model.NextAsync(); });
        Check("filled workbook continues past special rows without confirming or publishing", continued && model.Page == 4 && model.Decisions.All(d => !d.Confirmed)
            && project.ToJsonText() == before && TestFileHash.Upper(path) == sourceHash && source.Completes == 0);
    }

    private static void WorkbookOverviewBroadGuardTest(string work)
    {
        string path = Path.Combine(work, "broad.xlsx"); WriteOverviewShape(path, decisions: true, notes: false);
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            var sheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet!;
            var rows = sheet.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().ToArray();
            rows[0].Append(new XL.Cell { CellReference = "D1", DataType = XL.CellValues.InlineString, InlineString = new XL.InlineString(new XL.Text("Pfam")) });
            rows[1].Append(new XL.Cell { CellReference = "D2", DataType = XL.CellValues.InlineString, InlineString = new XL.InlineString(new XL.Text("PF00089")) });
            sheet.Save();
        }
        var project = WorkbookOverviewProject();
        for (int i = 0; i < 100; i++) project.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "workbook-gate-" + i,
            Description = "Unrelated protein", Sequence = "ACDEFGHIK", Evidence = new() { Pfam = new() { new() { Accession = "PF00089", Name = "DomainOne" } } } });
        var curation = new CurationViewModel(project); string before = project.ToJsonText();
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(curation), workbookSource: new ObservedWorkbookSource(), openWorkbookReview: true);
        FinishReviewWork(() => model.LoadAsync(path));
        int questions = 0; bool accepted = true;
        FinishReviewWork(async () => { accepted = await model.NextAsync(_ => { questions++; return false; }); });
        Check("one-press import asks before accepting broad source-Pfam matching", questions == 1 && !accepted && !model.Imported
            && curation.UndoStack.Count == 0 && project.ToJsonText() == before);
        FinishReviewWork(async () => { accepted = await model.NextAsync(_ => { questions++; return true; }); });
        Check("explicit broad confirmation permits exactly one workbook publication", questions == 2 && accepted && model.Imported && curation.UndoStack.Count == 1);
    }
}
