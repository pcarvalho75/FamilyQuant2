using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyGridLayoutCommands() =>
    [new("--test-family-grid-layout", 10701, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-family-grid-layout", (args, _) =>
        {
            EnsureApplication(); FamilyGridLayoutTests(args.Length == 2 ? args[1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    new("--test-table-appearance", 10702, TestCommandPhase.Raw,
        args => args.Length == 1 && args[0] == "--test-table-appearance", (_, _) =>
        {
            EnsureApplication(); TableAppearanceTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static FamilyClusterFile FamilyGridFixture() => new() { MyExtendedFastaItems = new()
    {
        new FastaItemExtended { SequenceIdentifier = "grid_fixture_1", Description = "First fixture protein", Sequence = "PEPTIDEK", UserID = 1, USearchID = 4, UserAnnotation = "First family" },
        new FastaItemExtended { SequenceIdentifier = "grid_fixture_2", Description = "First fixture protein", Sequence = "PEPTIDER", UserID = 1, USearchID = 4, UserAnnotation = "First family" },
        new FastaItemExtended { SequenceIdentifier = "grid_fixture_3", Description = "Second fixture protein", Sequence = "PEPTIDEA", UserID = 2, USearchID = 8, UserAnnotation = "Second family" }
    } };

    private static void FamilyGridLayoutTests(string? fixtureOutput = null)
    {
        if (fixtureOutput != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fixtureOutput))!);
            FamilyGridFixture().Save(fixtureOutput);
        }
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        foreach (var size in new[] { (1024, 768), (1366, 820) })
        {
            Themes.ThemeManager.Apply(theme);
            var file = FamilyGridFixture(); string before = file.ToJsonText();
            var screen = new ClusterGenerationControl(file);
            var families = UiField<DataGrid>(screen, "DataGridFamilies")!;
            var proteins = UiField<DataGrid>(screen, "DataGridResultGrid")!;
            var title = UiField<TextBlock>(screen, "TextBlockProteinsTitle")!;
            var window = new Window { Content = screen, Width = size.Item1, Height = size.Item2, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
            window.Show(); PumpDispatcher(); window.UpdateLayout();
            try
            {
                string label = theme + " " + size.Item1 + "x" + size.Item2;
                var leftHeader = WalkTree(families).OfType<DataGridColumnHeadersPresenter>().Single();
                var rightHeader = WalkTree(proteins).OfType<DataGridColumnHeadersPresenter>().Single();
                double leftY = leftHeader.TransformToAncestor(screen).Transform(new Point()).Y;
                double rightY = rightHeader.TransformToAncestor(screen).Transform(new Point()).Y;
                Check("Z5a " + label + " grid header top edges align to the pixel", Math.Abs(leftY - rightY) < 0.01);
                Check("Z5a " + label + " names both tables", UiField<TextBlock>(screen, "TextBlockFamiliesTitle")!.Text == "Families"
                    && title.Text == "Proteins in this family");
                var idColumn = UiField<DataGridTextColumn>(screen, "ColumnFamilyId")!;
                var idText = idColumn.GetCellContent(screen.ViewModel.Families.First()) as TextBlock;
                var idHeader = WalkTree(families).OfType<DataGridColumnHeader>().Single(header => ReferenceEquals(header.Column, idColumn));
                Check("Z5c " + label + " family ID cells and header are centered", idText?.TextAlignment == TextAlignment.Center
                    && idHeader.HorizontalContentAlignment == HorizontalAlignment.Center);
                Check("Z5c " + label + " USearch IDs are visible with no saved choice", UiField<DataGridTextColumn>(screen, "ColumnUSearchId")!.Visibility == Visibility.Visible
                    && UiField<CheckBox>(screen, "CheckBoxShowSearchIds")!.IsChecked == true);
                screen.ViewModel.SelectedFamily = screen.ViewModel.Families.First(); PumpDispatcher();
                Check("Z5a " + label + " protein title follows the selected family name", title.Text == "Proteins in this family: First family"
                    && proteins.Items.Count == 2 && (string)title.ToolTip == title.Text);
                screen.ViewModel.SelectedFamily = screen.ViewModel.Families.Last(); PumpDispatcher();
                Check("Z5a " + label + " selection replaces the title without stale family text", title.Text == "Proteins in this family: Second family" && proteins.Items.Count == 1);
                var all = UiField<Button>(screen, "ButtonAllProteins")!;
                var buttonOrigin = all.TransformToAncestor(screen).Transform(new Point());
                var proteinOrigin = proteins.TransformToAncestor(screen).Transform(new Point());
                Check("Z5b " + label + " All proteins sits above the protein grid", buttonOrigin.X >= proteinOrigin.X
                    && buttonOrigin.X + all.ActualWidth <= proteinOrigin.X + proteins.ActualWidth + 0.01
                    && buttonOrigin.Y + all.ActualHeight <= rightY);
                all.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                Check("Z5b " + label + " native All proteins restores every row", screen.ViewModel.SelectedFamily == null
                    && proteins.Items.Count == 3 && file.ToJsonText() == before && screen.ViewModel.UndoStack.Count == 0);
                Check("Z5a " + label + " clearing selection clears its name without changing curation", title.Text == "Proteins in this family" && file.ToJsonText() == before);
            }
            finally { window.Close(); }
        }
        FamilyGridPreferenceTests();
    }

    private static void FamilyGridPreferenceTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "family-grid-preference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "fixture.fcf"), settings = Path.Combine(root, "shell.json");
        FamilyGridFixture().Save(source);
        File.WriteAllText(settings, "{}"); // Existing settings with no column choice adopt the new default.
        string? previousSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = settings;
        MainWindow? main = null;
        try
        {
            main = new MainWindow(); main.OpenProjectPath(source);
            var screen = UiField<ClusterGenerationControl>(main, "cgc")!;
            CheckBox SearchChoice() => UiField<CheckBox>(screen, "CheckBoxShowSearchIds")!;
            string before = screen.fcf.ToJsonText();
            Check("Z5c an old settings file with no column choice shows USearch ID", SearchChoice().IsChecked == true
                && UiField<DataGridTextColumn>(screen, "ColumnUSearchId")!.Visibility == Visibility.Visible);
            SearchChoice().IsChecked = false;
            Check("Z5c hiding USearch ID saves an explicit false without modifying the project", ShellSessionState.Load(settings).ShowSearchIds == false
                && !main.DocumentState.IsModified && screen.fcf.ToJsonText() == before);
            main.Close(); main = new MainWindow(); main.OpenProjectPath(source);
            screen = UiField<ClusterGenerationControl>(main, "cgc")!;
            Check("Z5c saved hidden choice wins over the new default after restart", SearchChoice().IsChecked == false
                && UiField<DataGridTextColumn>(screen, "ColumnUSearchId")!.Visibility == Visibility.Collapsed);
            SearchChoice().IsChecked = true;
            Check("Z5c visible choice is persisted", ShellSessionState.Load(settings).ShowSearchIds == true);
            main.OpenProjectPath(source);
            screen = UiField<ClusterGenerationControl>(main, "cgc")!;
            Check("Z5c opening another view retains the saved visible choice", SearchChoice().IsChecked == true
                && UiField<DataGridTextColumn>(screen, "ColumnUSearchId")!.Visibility == Visibility.Visible);
        }
        finally { main?.Close(); MainWindow.ShellSettingsPathOverride = previousSettings; }
    }
}
