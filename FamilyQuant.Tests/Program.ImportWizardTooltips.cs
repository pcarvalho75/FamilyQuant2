using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

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
        Console.WriteLine("Import wizard tooltip census: " + importTooltipCensus.Count + " distinct controls and headers.");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, "tooltip-census.tsv"), new[] { "Control\tTooltip" }
                .Concat(importTooltipCensus.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "\t" + p.Value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '))));
        }
    }

    private static void AuditImportTooltips(FamilyListImportWindow window, string label)
    {
        if (!auditImportTooltips) return;
        var controls = WalkTree(window).OfType<FrameworkElement>().Where(e => e.IsVisible && !e.Name.StartsWith("PART_", StringComparison.Ordinal)
            && e is Button or CheckBox or ComboBox or TextBox or DataGrid or Expander).ToArray();
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
    }
}
