using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportTooltipCopyCommands() =>
    [new("--test-import-tooltip-copy", 100, TestCommandPhase.Raw, args => args.Contains("--test-import-tooltip-copy", StringComparer.Ordinal), (args, _) =>
    {
        ImportTooltipCopyTests(args.Length == 2 ? args[1] : null);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void ImportTooltipCopyTests(string? output = null)
    {
        Section("Z12q7b wizard tips describe read-only columns and actual enablement");
        EnsureApplication();
        string folder = Path.Combine(Path.GetTempPath(), "fq-tooltip-copy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string workbook = Path.Combine(folder, "example.xlsx"); ExampleFamilyList.Write(workbook);
        var project = ManualChoiceFile(); string before = project.ToJsonText();
        var curation = new CurationViewModel(project);
        var model = new FamilyListImportViewModel(new FamilyListImportTransaction(curation));
        var window = new FamilyListImportWindow(model)
        { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        var originalTheme = Themes.ThemeManager.Current;
        try
        {
            window.Show(); ReadyImportPreparation(model, workbook); PumpDispatcher();
            string Header(string gridName, string name) => Themes.TableAppearance.GetHeaderToolTip(
                ((DataGrid)window.FindName(gridName)).Columns.Single(c => c.Header as string == name)) ?? "";
            string special = Header("SpecialRowsGrid", "Abbreviation");
            Check("Special rows abbreviation header points to its real editor and says read only", special.Contains("read only", StringComparison.OrdinalIgnoreCase)
                && special.Contains("Abbreviation box", StringComparison.Ordinal) && !Regex.IsMatch(special, @"\bkeys?\b", RegexOptions.IgnoreCase));
            foreach (string gridName in new[] { "MatchFamiliesGrid", "SummaryGrid" })
            foreach (string header in new[] { "Abbreviation", "Family name" })
                Check(gridName + " " + header + " accurately says read only", ((DataGrid)window.FindName(gridName)).IsReadOnly
                    && Header(gridName, header).Contains("read only", StringComparison.OrdinalIgnoreCase)
                    && !Header(gridName, header).Contains("editable", StringComparison.OrdinalIgnoreCase)
                    && !Regex.IsMatch(Header(gridName, header), @"\bkeys?\b", RegexOptions.IgnoreCase));
            var abbreviation = WalkTree(window).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Abbreviation for this source row");
            Check("the abbreviation input and merge target use the Help's one name", !Regex.IsMatch(abbreviation.ToolTip?.ToString() ?? "", @"\bkeys?\b", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(Header("DecisionGrid", "Merge into abbreviation"), @"\bkeys?\b", RegexOptions.IgnoreCase)
                && model.Columns.All(c => !Regex.IsMatch(c.ToolTip, @"\bkeys?\b", RegexOptions.IgnoreCase)));
            var useRow = (Button)window.FindName("UseRowButton"); model.SelectedSpecialRow = null; PumpDispatcher();
            Check("Use this choice explains its missing-row and action condition", !model.CanUseRowChoice
                && useRow.ToolTip!.ToString()!.Contains("Choose a row and an action", StringComparison.Ordinal));
            model.SelectedSpecialRow = model.SpecialRows[0]; model.SelectedSpecialRow.Action = FamilyPanelRowAction.Import;
            model.SelectedSpecialRow.Key = ""; model.SelectedSpecialRow.Name = ""; PumpDispatcher();
            Check("row values are checked on press rather than claimed as an enablement gate", model.CanUseRowChoice
                && useRow.ToolTip!.ToString()!.Contains("problem appears in Status", StringComparison.Ordinal));
            var confirm = WalkTree(window).OfType<Button>().Single(b => b.Content as string == "Use selected matches");
            model.SelectedMatchFamily = null; PumpDispatcher();
            Check("Use selected matches explains the missing-family selection", !model.CanConfirmMatches
                && confirm.ToolTip!.ToString()!.Contains("Choose a family", StringComparison.Ordinal));
            var family = new ImportMatchFamily { Key = "supplied", Name = "Supplied family", Suggestions = new[] { new ImportEvidenceMatch { Text = "", Use = true } } };
            model.MatchFamilies.Add(family); model.SelectedMatchFamily = family; PumpDispatcher();
            Check("the matching tip distinguishes a tick from value validation", model.CanConfirmMatches
                && confirm.ToolTip!.ToString()!.Contains("tick at least one suggestion", StringComparison.Ordinal)
                && confirm.ToolTip!.ToString()!.Contains("when you confirm", StringComparison.Ordinal));
            model.ConfirmMatches();
            Check("an enabled ticked empty value is still rejected on confirmation", family.Choice == "Needs a choice"
                && model.Status.Contains("one-line", StringComparison.Ordinal) && project.ToJsonText() == before && curation.UndoStack.Count == 0);
            if (output != null)
            {
                Directory.CreateDirectory(output);
                foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
                {
                    Themes.ThemeManager.Apply(theme);
                    var tooltip = new ToolTip { Content = special, PlacementTarget = useRow, IsOpen = true };
                    try
                    {
                        PumpDispatcher(); tooltip.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(tooltip.ActualWidth), (int)Math.Ceiling(tooltip.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(tooltip); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(output, theme + "-abbreviation-header.png")); encoder.Save(stream);
                    }
                    finally { tooltip.IsOpen = false; }
                }
            }
        }
        finally { window.Close(); PumpDispatcher(); Themes.ThemeManager.Apply(originalTheme); }
    }
}
