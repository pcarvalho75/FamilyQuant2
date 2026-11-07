using FamilyQuant.Examples;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> WorkbookSummaryPolishCommands() =>
    [new("--test-workbook-summary-polish", 100, TestCommandPhase.Raw, args => args.Contains("--test-workbook-summary-polish", StringComparer.Ordinal), (args, _) =>
    {
        WorkbookSummaryPolishTests(WorkbookScreenOutput(args));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void WorkbookSummaryPolishTests(string? output = null)
    {
        Section("Z12p2f2 summary guidance, optional findings and actual cell purposes");
        EnsureApplication();
        string work = Path.Combine(Path.GetTempPath(), "fq-summary-polish-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        string path = Path.Combine(work, "workbook.xlsx"); ExampleFamilyList.Write(path);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var project = WorkbookOverviewProject(); string before = project.ToJsonText();
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(project)), workbookSource: new ObservedWorkbookSource(), openWorkbookReview: true);
                var window = new FamilyListImportWindow(model) { Width = 1366, Height = 768, Left = -30000, Top = -30000, WindowStyle = WindowStyle.None,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show(); FinishReviewWork(() => model.LoadAsync(path)); PumpDispatcher();
                    var overview = (FamilyListWorkbookOverview)window.FindName("WorkbookOverview");
                    Border Card(string key) => WalkTree(overview).OfType<Border>().First(b => b.Child is DockPanel && b.DataContext is ImportWorkbookSection s && s.Key == key);
                    Check(theme + " blocking summary uses plain guidance and the exact Decision labels section name", model.WorkbookReviewStatus.Contains("Decision labels", StringComparison.Ordinal)
                        && !model.WorkbookReviewStatus.Contains("Matched program words", StringComparison.Ordinal)
                        && !model.WorkbookReviewStatus.Contains("review its action. Review", StringComparison.OrdinalIgnoreCase), model.WorkbookReviewStatus);
                    Check(theme + " disabled acceptance explains the same counted choices", model.ForwardToolTip.Contains(model.WorkbookReviewProblems.Count + " choices", StringComparison.Ordinal)
                        && !model.ForwardToolTip.Contains("Matched program words", StringComparison.Ordinal), model.ForwardToolTip);
                    Check(theme + " required choice card has a distinct token border", Equals(Card("decisions").BorderBrush, Application.Current.FindResource("AccentBrush")));
                    var families = WalkTree(overview).OfType<DataGrid>().Single(grid => AutomationProperties.GetName(grid) == "Families proposed for import");
                    families.UpdateLayout();
                    foreach (string header in new[] { "Family name", "Merge into" })
                    {
                        var column = families.Columns.Single(c => c.Header as string == header);
                        var text = (TextBlock)column.GetCellContent(families.Items[0]);
                        string tip = text.ToolTip as string ?? "";
                        string purpose = Themes.TableAppearance.GetHeaderToolTip(column) ?? "";
                        Check(theme + " actual " + header + " cell retains header purpose and full value even when blank", purpose.Length > 0 && tip.Contains(purpose, StringComparison.Ordinal)
                            && (text.Text.Length == 0 || tip.Contains(text.Text, StringComparison.Ordinal)), tip);
                    }
                    CaptureFamilyListPage(window, output, theme, 1366, 768, "01-required-choices");
                    ResolveWorkbookUiDraft(model); PumpDispatcher();
                    var optional = model.WorkbookSections.Single(section => section.Key == "matches");
                    Check(theme + " nonblocking Pfam findings say optional while acceptance stays available", !optional.NeedsChoice && optional.Summary.Contains("optional", StringComparison.OrdinalIgnoreCase) && model.CanAcceptWorkbook,
                        optional.Summary + "; " + string.Join("; ", model.WorkbookReviewProblems));
                    Check(theme + " optional card does not use the required-choice token border", !Equals(Card("matches").BorderBrush, Application.Current.FindResource("AccentBrush")));
                    CaptureFamilyListPage(window, output, theme, 1366, 768, "02-optional-findings");
                    var subclass = model.SubclassChoices.First(); subclass.Family = "missing-family"; subclass.Use = true;
                    FinishReviewWork(() => model.ReviewWorkbookSectionAsync("subclasses")); PumpDispatcher();
                    Check(theme + " opening a blocked subclass review immediately exposes its item problems", subclass.Problems.Count > 0);
                    CaptureFamilyListPage(window, output, theme, 1366, 768, "03-subclass-problems");
                    Check(theme + " presentation and validation never confirm or publish the draft", project.ToJsonText() == before && model.Decisions.All(d => !d.Confirmed)
                        && model.SubclassChoices.All(c => !c.Confirmed));
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
