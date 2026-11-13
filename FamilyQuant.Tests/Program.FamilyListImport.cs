using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FamilyListImportTests(string? output = null)
    {
        Section("Family list import - actual workbook, conditional pages, explicit choices and one publication");
        EnsureApplication();
        using var dialogs = new ImportFixtureDialogs();
        string work = Path.Combine(Path.GetTempPath(), "fq-family-list-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string workbook = Path.Combine(work, ExampleFamilyList.DefaultFileName); ExampleFamilyList.Write(workbook);
        string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workbook)));
        using (var workbookModel = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())),
            workbookSource: new FamilyListWorkbookCoreSource(), openWorkbookReview: true))
        {
            FinishReviewWork(async () => { await workbookModel.LoadAsync(workbook); await workbookModel.ReviewWorkbookSectionAsync("columns"); });
            workbookModel.ReviewingDecisions = false;
            Check("Columns with pending workbook decisions names its available review and return actions",
                workbookModel.CanNext && workbookModel.CanReturnToWorkbook
                && workbookModel.Status.Contains("Next opens", StringComparison.Ordinal)
                && workbookModel.Status.Contains("Back to workbook review", StringComparison.Ordinal)
                && workbookModel.ForwardToolTip.Contains("custom decision labels", StringComparison.Ordinal));
        }
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (Width: 1366, Height: 768), (Width: 1920, Height: 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var file = ManualChoiceFile();
                file.MyExtendedFastaItems[0].Description = "Trypsin-like serine protease";
                file.MyExtendedFastaItems[0].Evidence!.Family = "Peptidase S1 family";
                file.MyExtendedFastaItems[0].Evidence!.Reviewed = true;
                file.MyExtendedFastaItems[0].Evidence!.Keywords.Add("Serine protease");
                file.FamilyPanel = new FamilyPanel { Name = "Previous list", Families = new() { new() { Key = "previous", DisplayName = "Previous family" } } };
                string before = file.ToJsonText(), proteinsBefore = JsonSerializer.Serialize(file.MyExtendedFastaItems);
                var curation = new CurationViewModel(file);
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(curation), matcher: new FamilyListStoredEvidenceMatches(file));
                var window = new FamilyListImportWindow(model, path => ExampleFamilyList.Write(path), PanelImportTemplate.Write)
                { Width = size.Width, Height = size.Height, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false };
                string label = theme + " " + size.Width;
                try
                {
                    window.Show(); PumpDispatcher();
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "01-welcome");
                    Check(label + " import uses one plain list name in its title and Help", window.Title == "Import family list"
                        && model.Title == "Choose your spreadsheet" && FamilyListImportHelp.Text.StartsWith("IMPORT FAMILY LIST", StringComparison.Ordinal)
                        && model.Status == "Nothing has changed yet.");
                    Check(label + " File exposes Use template without a practice action or project edit", window.FindName("ExampleButton") == null
                        && ((Button)window.FindName("TemplateButton")).IsEnabled && file.ToJsonText() == before && model.Page == 1 && !model.CanBack);
                    FinishReviewWork(() => model.LoadAsync(workbook)); PumpDispatcher();
                    Check(label + " File and sheet reads the real neutral example and detected header", model.Page == 1 && model.Sheets.Count == 6
                        && model.Preview?.ProposedHeaderRow == ExampleFamilyList.FamilyHeaderRow() && model.CanNext
                        && ((DataGrid)window.FindName("SheetPreviewGrid")).Columns.Count == 16 && file.ToJsonText() == before);
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "02-file-and-sheet");
                    var previewGrid = (DataGrid)window.FindName("SheetPreviewGrid");
                    Check(label + " preview keeps at least eight complete rows visible despite long source notes", WalkTree(previewGrid).OfType<DataGridRow>()
                        .Count(r => r.IsVisible && r.TranslatePoint(new Point(), previewGrid).Y >= 0
                            && r.TranslatePoint(new Point(), previewGrid).Y + r.ActualHeight <= previewGrid.ActualHeight - 18) >= 8);
                    model.HeaderRowText = "not a row";
                    Check(label + " an unconfirmed header edit cannot advance with an old table", !model.CanNext);
                    model.HeaderRowText = ExampleFamilyList.FamilyHeaderRow().ToString();
                    ((Button)window.FindName("ForwardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    Check(label + " Columns proposes names with examples and defaults to Quantify without Decision", model.Page == 2
                        && model.Columns.Single(c => c.Role == PanelImportFields.FamilyKey).Header.Length > 0
                        && model.Columns.Single(c => c.Role == PanelImportFields.OtherName).Header.Length > 0
                        && model.Columns.Single(c => c.Role == PanelImportFields.DisplayName).Example.Length > 0
                        && model.Columns.Single(c => c.Role == PanelImportFields.Decision).Header.Length == 0 && model.CanNext);
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "03-columns");
                    Check(label + " Columns replaces the sheet-specific status", !model.Status.Contains("sheet and header", StringComparison.Ordinal));
                    Check(label + " source fills remain preview data without category controls", window.FindName("ColourCategories") == null
                        && model.Preview!.Rows.Any(row => row.Cells.Any(cell => cell.Fill.Length > 0)) && model.CanNext);
                    var decision = model.Columns.Single(c => c.Role == PanelImportFields.Decision);
                    decision.Header = decision.Headers.First(h => h.Contains("DECIS", StringComparison.OrdinalIgnoreCase));
                    Check(label + " free-text decisions remain proposals until explicitly confirmed", model.Decisions.Count == 8
                        && model.Decisions.Any(d => !d.Confirmed) && model.Decisions.All(d => d.Rows.Length > 0));
                    WalkTree(window).OfType<Button>().Single(button => Equals(button.Content, "Back to columns"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    Check(label + " Columns offers custom decision review before continuing", !model.ReviewingDecisions && model.CanNext
                        && model.Status.Contains("Next opens", StringComparison.Ordinal)
                        && ((Button)window.FindName("ForwardButton")).ToolTip is string tip && tip.Contains("custom decision labels", StringComparison.Ordinal));
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "03-columns-needs-decisions");
                    WalkTree(window).OfType<Button>().Single(button => Equals(button.Content, "Review decision values"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    ((Button)window.FindName("ForwardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(label + " disabled Next cannot silently accept decision proposals", model.Page == 2);
                    Check(label + " decision review has its own bounded table and clear page heading", model.ReviewingDecisions && model.Title == "Review spreadsheet decisions");
                    const string decisionExplanation = "Quantify, merge, report under others or leave out; no conditional action. Your labels stay with each family.";
                    Check(label + " decision page and Help explain all four actions and source labels in one line", model.Explanation == decisionExplanation
                        && FamilyListImportHelp.Text.Contains(decisionExplanation, StringComparison.Ordinal) && !model.Explanation.Contains('\n'));
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "03-columns-decisions");
                    model.ConfirmDecisions();
                    Check(label + " explicit batch confirmation accepts the reviewed decision meanings", model.Decisions.All(d => d.Confirmed) && model.CanNext);
                    Check(label + " confirming decisions removes the Columns blocking instruction",
                        !model.Status.Contains("Next opens", StringComparison.Ordinal));
                    model.Decisions[0].Confirmed = false;
                    Check(label + " an action needing confirmation again refreshes the Columns instruction",
                        model.CanNext && model.Status.Contains("Next opens", StringComparison.Ordinal));
                    model.Decisions[0].Confirmed = true;
                    Check(label + " resolving the last pending confirmation clears the stale instruction",
                        model.CanNext && !model.Status.Contains("Next opens", StringComparison.Ordinal));
                    FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
                    Check(label + " Special rows shows the duplicate and a several-name row with a real choice", model.Page == 3
                        && model.SpecialRows.Any(r => r.Duplicate) && model.SpecialRows.Any(r => r.SeveralNames && r.Parts.Count == 4) && !model.CanNext);
                    model.SelectedSpecialRow = model.SpecialRows.Single(r => r.Duplicate);
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "04-special-rows-duplicate");
                    var specialGrid = (DataGrid)window.FindName("SpecialRowsGrid");
                    Check(label + " selected review message uses the row's readable text ink",
                        WizardPolishMessageInk(specialGrid, model.SelectedSpecialRow, out string selectedInk), selectedInk);
                    Check(label + " the complete duplicate explanation is laid out without clipped lines",
                        WizardPolishMessageFits(specialGrid, model.SelectedSpecialRow, out string messageHeight), messageHeight);
                    Check(label + " duplicate rename starts as an enabled proposal without accepting the row",
                        model.SelectedSpecialRow.Action == FamilyPanelRowAction.Import && model.CanUseRowChoice
                        && model.SelectedSpecialRow.Blocking && !model.SelectedSpecialRow.Chosen && !model.CanNext
                        && Equals(((ComboBox)window.FindName("RowActionChoice")).SelectedValue, FamilyPanelRowAction.Import)
                        && file.ToJsonText() == before, WizardPolishProposalState(model, window));
                    string newKey = model.SelectedSpecialRow.Key;
                    var rowAction = (ComboBox)window.FindName("RowActionChoice");
                    rowAction.SelectedValue = FamilyPanelRowAction.Skip;
                    Check(label + " changing the native row action updates only its unaccepted draft proposal",
                        model.SelectedSpecialRow.Action == FamilyPanelRowAction.Skip && model.SelectedSpecialRow.Blocking
                        && !model.SelectedSpecialRow.Chosen && !model.CanNext && file.ToJsonText() == before);
                    rowAction.SelectedValue = FamilyPanelRowAction.Import;
                    model.SelectedSpecialRow.Action = FamilyPanelRowAction.Import; model.UseRowChoice();
                    Check(label + " accepting the proposed duplicate rename keeps the multi-name choice pending", newKey.Length > 0
                        && model.SpecialRows.Any(r => r.Chosen && r.Key == newKey) && !model.CanNext);
                    model.SelectedSpecialRow = model.SpecialRows.Single(r => r.SeveralNames && r.Parts.Count == 4 && r.Blocking);
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "04-special-rows");
                    Check(label + " the complete several-name explanation is laid out without clipped lines",
                        WizardPolishMessageFits(specialGrid, model.SelectedSpecialRow, out string severalHeight), severalHeight);
                    Check(label + " several-name row starts with its proposed split but still needs explicit acceptance",
                        model.SelectedSpecialRow.Action == FamilyPanelRowAction.Split && model.CanUseRowChoice
                        && model.SelectedSpecialRow.Blocking && !model.SelectedSpecialRow.Chosen && !model.CanNext
                        && Equals(((ComboBox)window.FindName("RowActionChoice")).SelectedValue, FamilyPanelRowAction.Split)
                        && file.ToJsonText() == before, WizardPolishProposalState(model, window));
                    var nameEditor = WizardPolishRowEditor(window, "Family name for this source row");
                    var keyEditor = WizardPolishRowEditor(window, "Abbreviation for this source row");
                    Check(label + " split action hides the unused single-family fields", !nameEditor.IsVisible && !keyEditor.IsVisible);
                    rowAction.SelectedValue = FamilyPanelRowAction.Skip; window.UpdateLayout(); PumpDispatcher();
                    Check(label + " skip action hides the unused single-family fields", !nameEditor.IsVisible && !keyEditor.IsVisible);
                    rowAction.SelectedValue = FamilyPanelRowAction.Import; window.UpdateLayout(); PumpDispatcher();
                    Check(label + " a kept long family name is readable without horizontal clipping", nameEditor.IsVisible
                        && nameEditor.ViewportWidth > 0 && nameEditor.ExtentWidth <= nameEditor.ViewportWidth + 2,
                        $"Extent={nameEditor.ExtentWidth}; viewport={nameEditor.ViewportWidth}.");
                    rowAction.SelectedValue = FamilyPanelRowAction.Split; window.UpdateLayout(); PumpDispatcher();
                    var partKeys = model.SelectedSpecialRow.Parts.Select(p => p.Key).ToArray();
                    model.SelectedSpecialRow.Action = FamilyPanelRowAction.Split; model.UseRowChoice();
                    while (model.SpecialRows.FirstOrDefault(r => r.SeveralNames && r.Blocking) is { } ambiguous)
                    { model.SelectedSpecialRow = ambiguous; ambiguous.Action = FamilyPanelRowAction.Import; model.UseRowChoice(); }
                    Check(label + " one family per name reaches a valid detached panel with every proposed part", model.CanNext
                        && partKeys.Length == 4 && partKeys.All(key => model.DraftPanel!.Families.Any(f => f.Key == key)) && file.ToJsonText() == before);
                    FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
                    Check(label + " evidence matching requires each active family to be confirmed or skipped", model.Page == 4
                        && model.PfamQuestions.Count > 0 && model.PfamQuestions.All(f => !f.Decided) && !model.CanNext
                        && model.MatchFamilies.All(f => f.Suggestions.All(s => s.Use == s.IsFromSheet)));
                    model.SelectedMatchFamily = model.PfamQuestions.FirstOrDefault(f => f.Suggestions.Count > 0) ?? model.SelectedMatchFamily;
                    Check(label + " supplied-code rows retain real Core suggestions without another question",
                        model.MatchFamilies.Any(f => f.Automatic && f.Suggestions.Count > 0));
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "05-evidence-matches");
                    model.SkipAllMatches();
                    Check(label + " Continue without matches records an explicit choice and no project change", model.CanNext
                        && model.PfamQuestions.All(f => f.Choice == "Skipped") && file.ToJsonText() == before);
                    model.SelectedMatchFamily = model.PfamQuestions[0];
                    model.SelectedMatchFamily.Choice = "Needs a choice"; model.SkipMatch();
                    Check(label + " the last matching choice reports completion", model.Status == "Every family has a matching choice.");
                    FinishReviewWork(async () => { await model.NextAsync(); });
                    model.PanelName = "Imported example list";
                    model.Back(); model.Back();
                    FinishReviewWork(async () => { await model.NextAsync(); });
                    Check(label + " Back and Next retain matching decisions and the edited panel name", model.Page == 5
                        && model.PfamQuestions.All(f => f.Choice == "Skipped") && model.PanelName == "Imported example list");
                    PumpDispatcher();
                    Check(label + " Summary names the replacement and creates no Undo before Import", model.Page == 5 && model.PreviousPanel.Contains("Previous list", StringComparison.Ordinal)
                        && model.SummaryFamilies.Count == model.DraftPanel!.Families.Count && curation.UndoStack.Count == 0 && file.ToJsonText() == before);
                    Check(label + " Summary includes the skipped empty row and the confirmed duplicate rename", model.Summary.Contains("1 row skipped", StringComparison.Ordinal)
                        && model.Summary.Contains("1 abbreviation renamed", StringComparison.Ordinal));
                    const string breakdown = "30 families to quantify (plus 1 merged, 11 not a family)";
                    var mergedFamily = model.SummaryFamilies.Single(f => f.Decision.StartsWith("Merge into ", StringComparison.Ordinal));
                    string mergeTarget = model.DraftPanel!.Families.Single(f => f.Key == mergedFamily.Key).MergeTargetFamilyKey;
                    Check(label + " Summary counts families to quantify, plus merged and not a family, and omits reported under others when that count is zero",
                        model.Summary.StartsWith(breakdown + "; ", StringComparison.Ordinal)
                        && !model.Summary.Contains("reported under others", StringComparison.Ordinal)
                        && !model.Summary.Contains("42 families", StringComparison.Ordinal));
                    Check(label + " a merge Decision names its target and merged or not-a-family rows need no match",
                        mergeTarget.Length > 0 && mergedFamily.Decision == "Merge into " + mergeTarget && mergedFamily.Decision != "Merge into family"
                        && mergedFamily.Matches == "Not needed"
                        && model.SummaryFamilies.Count(f => f.Decision == "Not a family") == 11
                        && model.SummaryFamilies.Where(f => f.Decision == "Not a family").All(f => f.Matches == "Not needed")
                        && model.SummaryFamilies.Where(f => f.Decision == "Quantify").All(f => f.Matches is "Confirmed" or "Skipped" || f.Matches.StartsWith("No Pfam code", StringComparison.Ordinal)));
                    Check(label + " a single confirmed match uses family grammar", !model.Summary.Contains("1 families confirmed", StringComparison.Ordinal));
                    Check(label + " Summary omits categories and keeps complete original source fields", !((DataGrid)window.FindName("SummaryGrid")).Columns.Any(c => c.Header as string == "Colour")
                        && model.SummaryFamilies.All(f => f.Notes.Length > 0));
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "06-summary");
                    FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
                    Check(label + " Import is one panel publication and retains every protein's state", model.Page == 6 && model.Imported && model.CanUndo
                        && file.FamilyPanel!.Name == "Imported example list" && curation.UndoStack.Count == 1 && JsonSerializer.Serialize(file.MyExtendedFastaItems) == proteinsBefore);
                    Check(label + " Done title is the same family breakdown plus imported", model.ImportedFamilyCount == breakdown + " imported"
                        && WalkTree(window).OfType<TextBlock>().Any(block => block.Text == breakdown + " imported"));
                    Check(label + " Done explains the automatic read before Update proposal", model.Explanation.Contains("reads the database and scope by itself when it opens", StringComparison.Ordinal)
                        && model.Explanation.Contains("then press Update proposal", StringComparison.Ordinal)
                        && model.Explanation.Contains("takes minutes", StringComparison.Ordinal) && !model.Explanation.Contains('\n'));
                    Check(label + " Done distinguishes label matching from moving proteins", window.FindName("MatchingEffectNote") is TextBlock note
                        && note.Text.Contains("Matching labels families", StringComparison.Ordinal) && note.Text.Contains("place them by hand", StringComparison.Ordinal)
                        && note.Text.Contains("Apply grouping", StringComparison.Ordinal) && !note.Text.Contains('\n'));
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "07-done");
                    string saved = file.ToJsonText();
                    var reopened = FamilyClusterFile.FromJsonText(saved);
                    Check(label + " save and reopen retain names, source choices and explicit row split", reopened.ToJsonText() == saved
                        && reopened.FamilyPanel!.Import!.RowChoices!.Any(c => c.Action == FamilyPanelRowAction.Split)
                        && reopened.FamilyPanel.Families.Any(f => f.Names.Count > 0)
                        && reopened.FamilyPanel.Families.Any(f => f.SourceDecision.Length > 0));
                    string bundle = Path.Combine(work, theme + "-" + size.Width + ".fqproj");
                    ProjectStore.WriteFqproj(bundle, file, new List<QuantRow>(), new QuantManifest());
                    Check(label + " the actual project bundle reopens with the imported panel and source choices intact", ProjectStore.ReadFqproj(bundle).fcf.ToJsonText() == saved);
                    model.Back(); FinishReviewWork(async () => { await model.NextAsync(); });
                    Check(label + " revisiting committed Summary cannot import twice", model.Page == 6 && curation.UndoStack.Count == 1 && file.ToJsonText() == saved);
                    ((Button)window.FindName("UndoImportButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    Check(label + " Done Undo restores the entire prior project in one step", !model.Imported && model.Page == 5 && file.ToJsonText() == before && curation.UndoStack.Count == 0);
                    Check(label + " native source controls have screen-reader names and one primary action", AutomationProperties.GetName((DependencyObject)window.FindName("SheetChoice")) == "Sheet"
                        && WalkTree(window).OfType<Button>().Count(b => ReferenceEquals(b.Style, window.FindResource("PrimaryButton"))) == 1);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
        Check("the real example input is byte-identical after every wizard walk", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workbook))) == sourceHash);
        FamilyListImportGuardTests(workbook, work);
        FamilyListImportMatchTests(output);
        FamilyListImportCountGrammarTests();
        FamilyListImportBreakdownTests(work);
        FamilyListDecisionHintTests(workbook);
        FamilyListChecklistHandoffTests();
    }

    private static void FamilyListImportCountGrammarTests()
    {
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())));
        model.SpecialRows.Add(new ImportSpecialRow { RowNumber = 1, Blocking = true, Message = "Choose this row's action." });
        Check("exactly one unresolved special row uses singular grammar", model.SpecialRows.Count(r => r.Blocking) == 1
            && model.SpecialSummary == "1 row needs a choice");
        Check("no imported panel yet says 0 families imported", model.ImportedFamilyCount == "0 families imported");
        model.Decisions.Add(new ImportDecisionChoice { Source = "Supplied choice", Decision = FamilyPanelDecision.Quantify });
        Check("exactly one unconfirmed decision uses singular grammar", model.Decisions.Count == 1 && model.DecisionSummary == "1 value to review");
        model.Decisions[0].Confirmed = true;
        Check("exactly one confirmed decision uses singular grammar", model.Decisions.Count == 1 && model.DecisionSummary == "1 value confirmed");
        model.Decisions.Add(new ImportDecisionChoice { Source = "Another choice", Decision = FamilyPanelDecision.NotAFamily, Confirmed = true });
        Check("two confirmed decisions retain plural grammar", model.Decisions.Count == 2 && model.DecisionSummary == "2 values confirmed");
    }

    private static void FamilyListImportBreakdownTests(string work)
    {
        CheckBreakdown(work, "only quantify", new[]
        {
            new[] { "AA", "Supplied family", "", "", "", "Quantify" },
            new[] { "BB", "Supplied second", "", "", "", "Quantify" },
        }, "2 families to quantify", "AA", "Quantify", "Skipped");
        CheckBreakdown(work, "reported under others and no merge or exclusion", new[]
        {
            new[] { "AA", "Supplied family", "", "", "", "Quantify" },
            new[] { "BB", "Supplied second", "", "", "", "Report under others" },
        }, "1 family to quantify (plus 1 reported under others)", "BB", "Report under others", "Skipped");
        CheckBreakdown(work, "not a family and no merge or others", new[]
        {
            new[] { "AA", "Supplied family", "", "", "", "Quantify" },
            new[] { "BB", "Supplied second", "", "", "", "Not a family" },
        }, "1 family to quantify (plus 1 not a family)", "BB", "Not a family", "Not needed");
        CheckBreakdown(work, "one merge and no others or exclusion", new[]
        {
            new[] { "AA", "Supplied family", "", "", "", "Quantify" },
            new[] { "BB", "Supplied second", "", "", "", "Merge into AA" },
        }, "1 family to quantify (plus 1 merged)", "BB", "Merge into AA", "Not needed");
        CheckBreakdown(work, "nothing to quantify", new[]
        {
            new[] { "AA", "Supplied family", "", "", "", "Not a family" },
        }, "0 families to quantify (plus 1 not a family)", "AA", "Not a family", "Not needed");
    }

    private static void CheckBreakdown(string work, string name, string[][] rows, string breakdown, string key, string decision, string matches)
    {
        string path = Path.Combine(work, "breakdown-" + name.Replace(' ', '-') + ".xlsx");
        PanelImportTemplate.Write(path);
        FillTemplate(path, rows);
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())));
        FinishReviewWork(() => model.LoadAsync(path));
        FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); });
        if (model.Page == 4) { model.SkipAllMatches(); FinishReviewWork(async () => { await model.NextAsync(); }); }
        var row = model.SummaryFamilies.Single(f => f.Key == key);
        bool zeroMerged = !breakdown.Contains(" merged", StringComparison.Ordinal);
        bool zeroOthers = !breakdown.Contains("reported under others", StringComparison.Ordinal);
        bool zeroExcluded = !breakdown.Contains("not a family", StringComparison.Ordinal);
        Check("summary " + name + " uses the family breakdown and keeps a zero count out of the plus list",
            model.Page == 5 && model.Summary.StartsWith(breakdown + "; ", StringComparison.Ordinal)
            && (zeroMerged || model.Summary.Contains(" merged", StringComparison.Ordinal))
            && (zeroOthers || model.Summary.Contains("reported under others", StringComparison.Ordinal))
            && (zeroExcluded || model.Summary.Contains("not a family", StringComparison.Ordinal))
            && (!zeroMerged || !model.Summary.Contains(" merged", StringComparison.Ordinal))
            && (!zeroOthers || !model.Summary.Contains("reported under others", StringComparison.Ordinal))
            && (!zeroExcluded || !model.Summary.Contains("not a family", StringComparison.Ordinal))
            && row.Decision == decision && row.Matches == (matches == "Skipped" ? "No Pfam code; no proteins until it has one" : matches));
        model.PanelName = "Supplied list";
        FinishReviewWork(async () => { await model.NextAsync(); });
        Check("done title " + name + " repeats that breakdown", model.Page == 6 && model.ImportedFamilyCount == breakdown + " imported");
    }

    private static void CaptureFamilyListPage(FamilyListImportWindow window, string? output, Themes.AppTheme theme, int width, int height, string page)
    {
        PumpDispatcher();
        var content = (FrameworkElement)window.Content; content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var footer = (FrameworkElement)window.FindName("WizardFooter");
        AuditImportTooltips(window, theme + " " + width + " " + page, force: window.Model.HasWorkbookMap);
        Check(theme + " " + width + " " + page + " keeps the navigation footer inside the viewport",
            footer.IsVisible && footer.TranslatePoint(new Point(), content).Y + footer.ActualHeight <= height);
        if (output == null) return;
        string folder = Path.Combine(output, theme.ToString().ToLowerInvariant(), width + "x" + height); Directory.CreateDirectory(folder);
        ScreenHarness.Save(window, Path.Combine(folder, page + ".png"), width, height);
    }
}
