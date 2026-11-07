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
        Section("Family list import - actual workbook, seven pages, explicit choices and one publication");
        EnsureApplication();
        string work = Path.Combine(Path.GetTempPath(), "fq-family-list-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string workbook = Path.Combine(work, ExampleFamilyList.DefaultFileName); ExampleFamilyList.Write(workbook);
        string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workbook)));
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
                    Check(label + " Welcome exposes the example and template without editing the project", ((Button)window.FindName("ExampleButton")).IsEnabled
                        && ((Button)window.FindName("TemplateButton")).IsEnabled && file.ToJsonText() == before && model.Page == 0 && !model.CanBack);
                    FinishReviewWork(() => model.LoadAsync(workbook)); PumpDispatcher();
                    Check(label + " File and sheet reads the real neutral example and detected header", model.Page == 1 && model.Sheets.Count == 6
                        && model.Preview?.ProposedHeaderRow == ExampleFamilyList.FamilyHeaderRow() && model.CanNext
                        && ((DataGrid)window.FindName("SheetPreviewGrid")).Columns.Count == 16 && file.ToJsonText() == before);
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "02-file-and-sheet");
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
                    var decision = model.Columns.Single(c => c.Role == PanelImportFields.Decision);
                    decision.Header = decision.Headers.First(h => h.Contains("DECIS", StringComparison.OrdinalIgnoreCase));
                    Check(label + " free-text decisions remain proposals until explicitly confirmed", model.Decisions.Count == 8
                        && model.Decisions.Any(d => !d.Confirmed) && !model.CanNext && model.Decisions.All(d => d.Rows.Length > 0));
                    ((Button)window.FindName("ForwardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(label + " disabled Next cannot silently accept decision proposals", model.Page == 2);
                    Check(label + " decision review has its own bounded table and clear page heading", model.ReviewingDecisions && model.Title == "Review spreadsheet decisions");
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "03-columns-decisions");
                    model.ConfirmDecisions();
                    Check(label + " explicit batch confirmation accepts the reviewed decision meanings", model.Decisions.All(d => d.Confirmed) && model.CanNext);
                    FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
                    Check(label + " Special rows shows the duplicate and a several-name row with a real choice", model.Page == 3
                        && model.SpecialRows.Any(r => r.Duplicate) && model.SpecialRows.Any(r => r.SeveralNames && r.Parts.Count == 4) && !model.CanNext);
                    model.SelectedSpecialRow = model.SpecialRows.Single(r => r.Duplicate);
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "04-special-rows-duplicate");
                    string newKey = model.SelectedSpecialRow.Key;
                    model.SelectedSpecialRow.Action = FamilyPanelRowAction.Import; model.UseRowChoice();
                    Check(label + " accepting the proposed duplicate rename keeps the multi-name choice pending", newKey.Length > 0
                        && model.SpecialRows.Any(r => r.Chosen && r.Key == newKey) && !model.CanNext);
                    model.SelectedSpecialRow = model.SpecialRows.Single(r => r.SeveralNames && r.Parts.Count == 4 && r.Blocking);
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "04-special-rows");
                    var partKeys = model.SelectedSpecialRow.Parts.Select(p => p.Key).ToArray();
                    model.SelectedSpecialRow.Action = FamilyPanelRowAction.Split; model.UseRowChoice();
                    while (model.SpecialRows.FirstOrDefault(r => r.SeveralNames && r.Blocking) is { } ambiguous)
                    { model.SelectedSpecialRow = ambiguous; ambiguous.Action = FamilyPanelRowAction.Import; model.UseRowChoice(); }
                    Check(label + " one family per name reaches a valid detached panel with every proposed part", model.CanNext
                        && partKeys.Length == 4 && partKeys.All(key => model.DraftPanel!.Families.Any(f => f.Key == key)) && file.ToJsonText() == before);
                    FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
                    Check(label + " evidence matching requires each active family to be confirmed or skipped", model.Page == 4
                        && model.MatchFamilies.Count > 0 && model.MatchFamilies.All(f => !f.Decided) && !model.CanNext
                        && model.MatchFamilies.All(f => f.Suggestions.All(s => !s.Use)));
                    model.SelectedMatchFamily = model.MatchFamilies.FirstOrDefault(f => f.Suggestions.Count > 0) ?? model.SelectedMatchFamily;
                    Check(label + " the stored example protein supplies visible real Core suggestions", model.SelectedMatchFamily?.Suggestions.Count > 0);
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "05-evidence-matches");
                    model.SkipAllMatches();
                    Check(label + " Continue without matches records an explicit choice and no project change", model.CanNext
                        && model.MatchFamilies.All(f => f.Choice == "Skipped") && file.ToJsonText() == before);
                    FinishReviewWork(async () => { await model.NextAsync(); });
                    model.PanelName = "Imported example list";
                    model.Back(); model.Back();
                    FinishReviewWork(async () => { await model.NextAsync(); });
                    Check(label + " Back and Next retain matching decisions and the edited panel name", model.Page == 4
                        && model.MatchFamilies.All(f => f.Choice == "Skipped") && model.PanelName == "Imported example list");
                    FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
                    Check(label + " Summary names the replacement and creates no Undo before Import", model.Page == 5 && model.PreviousPanel.Contains("Previous list", StringComparison.Ordinal)
                        && model.SummaryFamilies.Count == model.DraftPanel!.Families.Count && curation.UndoStack.Count == 0 && file.ToJsonText() == before);
                    Check(label + " Summary includes the skipped empty row and the confirmed duplicate rename", model.Summary.Contains("1 row skipped", StringComparison.Ordinal)
                        && model.Summary.Contains("1 abbreviation renamed", StringComparison.Ordinal));
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "06-summary");
                    FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
                    Check(label + " Import is one panel publication and retains every protein's state", model.Page == 6 && model.Imported && model.CanUndo
                        && file.FamilyPanel!.Name == "Imported example list" && curation.UndoStack.Count == 1 && JsonSerializer.Serialize(file.MyExtendedFastaItems) == proteinsBefore);
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "07-done");
                    string saved = file.ToJsonText();
                    var reopened = FamilyClusterFile.FromJsonText(saved);
                    Check(label + " save and reopen retain names, source choices and explicit row split", reopened.ToJsonText() == saved
                        && reopened.FamilyPanel!.Import!.RowChoices!.Any(c => c.Action == FamilyPanelRowAction.Split)
                        && reopened.FamilyPanel.Families.Any(f => f.Names.Count > 0));
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
        FamilyListImportMatchTests();
        FamilyListChecklistHandoffTests();
    }

    private static void CaptureFamilyListPage(FamilyListImportWindow window, string? output, Themes.AppTheme theme, int width, int height, string page)
    {
        PumpDispatcher();
        var content = (FrameworkElement)window.Content; content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var footer = (FrameworkElement)window.FindName("WizardFooter");
        AuditImportTooltips(window, theme + " " + width + " " + page);
        Check(theme + " " + width + " " + page + " keeps the navigation footer inside the viewport",
            footer.IsVisible && footer.TranslatePoint(new Point(), content).Y + footer.ActualHeight <= height);
        if (output == null) return;
        string folder = Path.Combine(output, theme.ToString().ToLowerInvariant(), width + "x" + height); Directory.CreateDirectory(folder);
        ScreenHarness.Save(window, Path.Combine(folder, page + ".png"), width, height);
    }
}
