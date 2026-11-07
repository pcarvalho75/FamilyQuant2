using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static bool auditImportTooltips;
    private static readonly Dictionary<string, string> importTooltipCensus = new(StringComparer.Ordinal);
    private static void ImportWizardToolTipTests(string? output = null)
    {
        Section("Z12q2 import wizard tooltips across every real page");
        importTooltipCensus.Clear(); auditImportTooltips = true;
        try { FamilyListImportTests(output); }
        finally { auditImportTooltips = false; }
        ImportWizardToolTipScreens(output);
        Console.WriteLine("Import wizard tooltip census: " + importTooltipCensus.Count + " distinct controls and headers.");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, "tooltip-census.tsv"), new[] { "Control\tTooltip" }
                .Concat(importTooltipCensus.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "\t" + p.Value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '))));
        }
    }

    private static void AuditImportTooltips(FamilyListImportWindow window, string label, bool force = false)
    {
        if (!auditImportTooltips && !force) return;
        var controls = WalkTree(window).OfType<FrameworkElement>().Where(e => e.IsVisible && !e.Name.StartsWith("PART_", StringComparison.Ordinal)
            && e is Button or CheckBox or ComboBox or TextBox or DataGrid or Expander or TabItem)
            .Where(e => e is not Button { TemplatedParent: not null, Content: not string }).ToArray();
        var missing = new List<string>(); var repeats = new List<string>();
        foreach (var control in controls)
        {
            string name = control.Name.Length > 0 ? control.Name : AutomationProperties.GetName(control);
            if (name.Length == 0 && control is ContentControl { Content: string content }) name = content;
            if (name.Length == 0 && control.DataContext is ImportColumnChoice role) name = role.Role;
            if (name.Length == 0) name = control.GetType().Name;
            string text = control.ToolTip is ToolTip tip ? tip.Content?.ToString() ?? "" : control.ToolTip?.ToString() ?? "";
            string id = control.GetType().Name + ": " + name;
            if (string.IsNullOrWhiteSpace(text)) missing.Add(id);
            else if (text.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) || text.Trim().Length < 20) repeats.Add(id);
            importTooltipCensus[id] = text;
        }
        foreach (var grid in controls.OfType<DataGrid>())
            foreach (var column in grid.Columns)
            {
                string id = "Column: " + (grid.Name.Length > 0 ? grid.Name : AutomationProperties.GetName(grid)) + "/" + column.Header;
                string text = Themes.TableAppearance.GetHeaderToolTip(column) ?? "";
                if (string.IsNullOrWhiteSpace(text)) missing.Add(id);
                importTooltipCensus[id] = text;
            }
        Check(label + " every visible wizard control and column explains its purpose", missing.Count == 0, string.Join("; ", missing.Distinct()));
        Check(label + " tooltips contain useful copy instead of repeating a label", repeats.Count == 0, string.Join("; ", repeats.Distinct()));
        Check(label + " disabled wizard controls inherit the shared hover behavior", controls.All(ToolTipService.GetShowOnDisabled));
        Check(label + " authored tips follow the shared wording rules", importTooltipCensus.Values.All(t => !CensusColourWord.IsMatch(t)
            && !CensusD21Word.IsMatch(t) && !t.Contains('\u2013') && !t.Contains('\u2014')));
    }

    private static void ImportWizardToolTipScreens(string? output)
    {
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())));
                var window = new FamilyListImportWindow(model)
                { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
                window.Show(); PumpDispatcher();
                try
                {
                    var back = (Button)window.FindName("BackButton");
                    var tip = new ToolTip { Content = back.ToolTip, PlacementTarget = back, IsOpen = true };
                    try
                    {
                        PumpDispatcher(); tip.UpdateLayout();
                        Check(theme + " the disabled Back explanation renders with the shared wrapped tooltip style", !back.IsEnabled
                            && tip.ActualWidth > 100 && tip.ActualWidth <= 380 && tip.ActualHeight > 20 && tip.ActualHeight < 180
                            && ToolTipService.GetInitialShowDelay(back) == 500 && ToolTipService.GetShowDuration(back) == 20000);
                        if (output != null)
                        {
                            Directory.CreateDirectory(output);
                            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(tip.ActualWidth), (int)Math.Ceiling(tip.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(tip); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = File.Create(Path.Combine(output, theme + "-disabled-back-tooltip.png")); encoder.Save(stream);
                        }
                    }
                    finally { tip.IsOpen = false; }
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }
}
