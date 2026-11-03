using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FamilyQuant.Panels;
using FamilyQuant.Themes;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelEditorScreenCommandTable() =>
    [
        new("--compare-panel-templates", 77, TestCommandPhase.Raw, args => args.Length == 3 && args[0] == "--compare-panel-templates",
            (args, _) => ComparePanelTemplates(args[1], args[2])),
        new("--panel-editor-screens", 75, TestCommandPhase.Raw,
            args => args.Length == 2 && args[0] == "--panel-editor-screens", (args, _) => PanelEditorScreens(args[1])),
    ];

    private static int PanelEditorScreens(string output)
    {
        EnsureApplication();
        var old = ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                ThemeManager.Apply(theme);
                foreach (var (width, height) in new[] { (900, 600), (1024, 768), (1366, 820) })
                {
                    var panel = new FamilyPanel { Name = "Example panel", Families = new()
                    {
                        new FamilyPanelFamily { Key = "family-a", DisplayName = "Family A", Pfam = new() { new FamilyPanelDomain { Accession = "PF00001" } } },
                        new FamilyPanelFamily { Key = "family-b", DisplayName = "Family B", Subclasses = new() { new FamilyPanelSubclass { Key = "part-1", DisplayName = "Part 1" } } }
                    } };
                    var window = new PanelEditorWindow(panel, new PanelReaderFixture(), pfamReview: new PanelPfamFixture()) { Width = width, Height = height, Left = -30000, Top = -30000,
                        WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
                    window.Show();
                    try
                    {
                        window.LoadColumns("fixture.xlsx", "Sheet", 2);
                        var sheetChoice = (ComboBox)window.FindName("SheetChoice"); sheetChoice.ItemsSource = new[] { "Sheet" }; sheetChoice.SelectedIndex = 0;
                        ((TextBox)window.FindName("HeaderRow")).Text = "2";
                        ((ComboBox)window.FindName("KeyColumn")).SelectedItem = "Code";
                        ((ComboBox)window.FindName("NameColumn")).SelectedItem = "Name";
                        ((ComboBox)window.FindName("SourceDecisionColumn")).SelectedItem = "Choice";
                        ((ComboBox)window.FindName("PfamColumn")).SelectedItem = "Domains";
                        window.DecisionMappings.Single(x => x.Source == "keep").Decision = FamilyPanelDecision.Quantify;
                        window.DecisionMappings.Single(x => x.Source == "join").Decision = FamilyPanelDecision.MergeIntoAnotherFamily;
                        window.DecisionMappings.Single(x => x.Source == "join").MergeTarget = "one";
                        ((DataGrid)window.FindName("MappingGrid")).Items.Refresh();
                        ((Button)window.FindName("ButtonPreviewPanel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        ((Button)window.FindName("ButtonCheckPfam")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        PumpDispatcher();
                        string path = Path.Combine(output, theme.ToString().ToLowerInvariant(), $"{width}x{height}.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        ScreenHarness.Save(window, path, width, height);
                    }
                    finally { window.Close(); }
                }
            }
            FamilyPanelTemplate.Write(Path.Combine(output, "template.xlsx"), new FamilyPanel { Name = "Sample panel", Families = new()
            { new FamilyPanelFamily { Key = "sample", DisplayName = "Sample family", TextFields = new() { new FamilyPanelText { Name = "Notes", Text = "A note\nwith a line break" } },
                Subclasses = new() { new FamilyPanelSubclass { Key = "part", DisplayName = "Part", RequiredPfam = new() { "PF12345" } } } } } });
            Console.WriteLine("Panel editor: six native client captures, light/dark at three sizes: " + output);
            return 0;
        }
        finally { ThemeManager.Apply(old); }
    }
    private static int ComparePanelTemplates(string before, string after)
    {
        var first = FamilyPanelTemplate.Read(before);
        var second = FamilyPanelTemplate.Read(after);
        bool equal = JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
        Console.WriteLine(equal ? "PASS Excel workbook save preserved the complete panel" : "FAIL Excel workbook save changed the panel");
        return equal ? 0 : 1;
    }
}
