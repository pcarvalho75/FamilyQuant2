using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int SecondaryWindowScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        foreach (var size in new[] { (1024, 768), (1366, 820) })
        {
            Themes.ThemeManager.Apply(theme);
            var file = new FamilyClusterFile { LockedUserIds = new() { 2, 7, 90 }, MyExtendedFastaItems = new()
            {
                new() { UserID = 2, Description = "Synthetic metalloproteinase", SequenceIdentifier = "synthetic_one" },
                new() { UserID = 7, Description = "Synthetic phospholipase", SequenceIdentifier = "synthetic_two" }
            } };
            var rows = new List<IdentityScanRow> { new() { Identity = 90, Groups = 500, TargetGroups = 260, MixedGroups = 0 },
                new() { Identity = 70, Groups = 320, TargetGroups = 168, MixedGroups = 1, Score = .003 },
                new() { Identity = 50, Groups = 170, TargetGroups = 92, MixedGroups = 8, Score = .061 } };
            string report = "Synthetic identity scan fixture\nIdentity\tFamilies\tMixed\tFDR\n90\t500\t0\t0\n70\t320\t1\t0.003\n50\t170\t8\t0.061\n";
            var windows = new (string Name, Window View)[]
            {
                ("locks", new LockManagerWindow(file)),
                ("spectrum", SpectrumComparisonWindow.Create(new List<SpectrumComparisonRow> { new() { FileName = "synthetic.raw", ScanNumber = 125, Charge = 2, PrecursorMz = 500.25 } })),
                ("identity", IdentityScanWindow.Create("Synthetic identity scan", rows, report, 70)),
                ("report", TextReportWindow.Create("Synthetic text report", report)),
                ("help", TextReportWindow.Create("FamilyQuant help", HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique), true))
            };
            foreach (var capture in windows)
            {
                var window = capture.View;
                window.Width = size.Item1; window.Height = size.Item2; window.Left = -30000; window.Top = -30000;
                window.WindowStartupLocation = WindowStartupLocation.Manual; window.ShowActivated = false; window.ShowInTaskbar = false;
                window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
                window.Show();
                try
                {
                    if (window is LockManagerWindow manager) manager.SelectFamilies(90);
                    PumpDispatcher();
                    ScreenHarness.Save(window, Path.Combine(output, $"{capture.Name}-{theme}-{size.Item1}.png"), size.Item1, size.Item2);
                }
                finally { window.Close(); }
            }
        }
        Console.WriteLine("Secondary windows: 20 native synthetic captures, dark/light at both sizes: " + output);
        return 0;
    }

    private static void SecondaryWindowTests(string workDir)
    {
        var shell = new MainWindow();
        try
        {
            string help = Flatten(HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique));
            string Header(string name) => ((MenuItem)shell.FindName(name)).Header.ToString()!;
            Check("Help paths name the current File commands", new[] { "MenuItemRunUSearch", "MenuItemLoadFQC", "MenuItemSaveProject" }
                .All(name => help.Contains("File > " + Header(name))));
            Check("Help paths name the current Analysis commands", new[] { "MenuItemComputeXic", "MenuItemReanotateSEPro" }
                .All(name => help.Contains("Analysis > " + Header(name))) && help.Contains(Header("MenuItemAnalytics")));
            var build = new USearchLib();
            Check("Help names the current Build action and exhaustive option", help.Contains(((Button)build.FindName("ButtonBuildFamilies")).Content.ToString()!)
                && help.Contains(((CheckBox)build.FindName("CheckBoxExhaustiveClustering")).Content.ToString()!));
            Check("Help describes the current status location without obsolete colour directions", help.Contains("bottom status line")
                && !Flatten(HelpContent.BuildDeNovo()).Contains("yellow line"));
            string workflow = Flatten((string)typeof(MainWindow).GetMethod("WorkflowText",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(shell, null)!);
            Check("workflow Help uses current menu paths and Build command names", workflow.Contains("File > " + Header("MenuItemRunUSearch"))
                && workflow.Contains("File > " + Header("MenuItemLoadFQC")) && workflow.Contains("File > " + Header("MenuItemSaveProject"))
                && !workflow.Contains("Menu: Run USearch") && !workflow.Contains("Load Family Cluster File"));
        }
        finally { shell.Close(); }
        var file = new FamilyClusterFile { LockedUserIds = new() { 7, 2, 90 }, MyExtendedFastaItems = new()
        {
            new() { UserID = 2, Description = "Target name", SequenceIdentifier = "target" },
            new() { UserID = 2, Description = "Decoy name", SequenceIdentifier = FamilyFilter.DecoyPrefix + "target" },
            new() { UserID = 7, Description = "Other target", SequenceIdentifier = "other" }
        } };
        var calls = new List<string>();
        var locks = new LockManagerViewModel(file, () => calls.Add("after:" + file.LockedUserIds.Count),
            () => calls.Add("before:" + file.LockedUserIds.Count));
        Check("lock model keeps fixed order, target naming, member counts and retired locks", locks.Rows.Select(r => r.UserId).SequenceEqual(new[] { 2, 7, 90 })
            && locks.Rows[0].Description == "Target name" && locks.Rows[0].Members == 2 && !locks.Rows[2].Exists && locks.Rows[2].Members == 0);
        Check("lock removal keeps one undo callback before change and notification after change", locks.Remove(new[] { 90, 7, 7 }) == 2
            && calls.SequenceEqual(new[] { "before:3", "after:1" }) && file.LockedUserIds.SetEquals(new[] { 2 }) && file.MyExtendedFastaItems.Count == 3);
        Check("an empty lock removal does not record undo or notify", locks.Remove(new[] { 999 }) == 0 && calls.Count == 2);
        file.LockedUserIds.Clear();
        Check("a displayed stale selection preserves the frozen removal count and callback order", locks.Remove(new[] { 2 }) == 1
            && calls.Skip(2).SequenceEqual(new[] { "before:0", "after:0" }) && locks.Rows.Count == 0 && file.MyExtendedFastaItems.Count == 3);
        var rows = new List<IdentityScanRow> { new() { Identity = 90, Groups = 3, TargetGroups = 3 }, new() { Identity = 70, Groups = 2, TargetGroups = 1, MixedGroups = 1 } };
        var scan = new IdentityScanViewModel("scan", rows, "unchanged\treport\n", 70);
        Check("identity model keeps the exact rows, report and current marker", ReferenceEquals(scan.Rows, rows) && scan.Report == "unchanged\treport\n" && scan.CurrentIdentity == 70);
        Check("identity Help retains the mixing caveat and decoy-free explanation", scan.HasDecoys && scan.Explanation.Contains("not an optimum")
            && new IdentityScanViewModel("empty", null, "", null).Explanation.Contains("no decoy sequences"));
        var sourceRows = new List<SpectrumComparisonRow> { new() { FileName = "fixture", ScanNumber = 5 } };
        var scatter = DeNovoAcceptedScoreScatterStudy.Build(sourceRows);
        scatter.PeaksMinimumAcceptedScore = 81.5;
        var spectrum = new SpectrumComparisonViewModel(sourceRows, "fixture.csv", scatter);
        string csv = Path.Combine(workDir, "secondary-spectrum.csv");
        spectrum.SaveCsv(csv);
        Check("spectrum model preserves exact rows and persisted score provenance", ReferenceEquals(spectrum.Rows, sourceRows)
            && ReferenceEquals(spectrum.ScoreScatter, scatter) && spectrum.ScoreScatter.PeaksMinimumAcceptedScore == 81.5 && spectrum.CanSave);
        Check("spectrum export delegates to the production CSV definition", File.ReadAllText(csv) == SpectrumComparisonTable.ToCsv(sourceRows));
        Check("empty spectrum stays visible with exports disabled", !new SpectrumComparisonViewModel(null, null, null).CanSave);
        var reportModel = new TextReportViewModel("Help", "selectable\nhelp text", true);
        var savedTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var windows = new Window[] { new LockManagerWindow(file), SpectrumComparisonWindow.Create(sourceRows, "fixture.csv", scatter),
                    IdentityScanWindow.Create("Identity scan", rows, scan.Report, 70), TextReportWindow.Create(reportModel.Title, reportModel.Text, reportModel.Wrap),
                    TextReportWindow.Create("Plain report", "a\tb\n", false) };
                foreach (var window in windows)
                {
                    window.Measure(new Size(1024, 768)); window.Arrange(new Rect(0, 0, 1024, 768));
                    PumpDispatcher();
                    Check(theme + " " + window.Title + " uses the shared window surface", Equals(window.Background, Application.Current.FindResource("CanvasBrush")));
                    foreach (var table in WalkTree(window).OfType<DataGrid>())
                    {
                        Check(theme + " secondary table uses the shared surface and readable text", Equals(table.Background, Application.Current.FindResource("SurfaceBrush"))
                            && ChartPalette.ContrastRatio(table.Foreground, table.Background) >= 4.5 && table.IsReadOnly && table.EnableRowVirtualization);
                    }
                    foreach (var text in WalkTree(window).OfType<TextBox>())
                        Check(theme + " secondary report keeps selection, scrolling and readable text", text.IsReadOnly && text.IsReadOnlyCaretVisible
                            && text.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && ChartPalette.ContrastRatio(text.Foreground, text.Background) >= 4.5);
                    if (WalkTree(window).OfType<IdentityScanChart>().SingleOrDefault() is { } chart)
                    {
                        window.Left = -30000; window.Top = -30000;
                        window.ShowInTaskbar = false; window.ShowActivated = false;
                        window.Show(); PumpDispatcher();
                        Themes.ThemeManager.Apply(theme == Themes.AppTheme.Dark ? Themes.AppTheme.Light : Themes.AppTheme.Dark);
                        PumpDispatcher();
                        Check(theme + " identity chart follows live theme tokens without changing its source rows", chart.Palette.Backdrop.IsFrozen
                            && chart.Palette.Backdrop.ToString() == Application.Current.FindResource("CanvasBrush").ToString()
                            && !ReferenceEquals(chart.Palette, ChartPalette.Dark) && !ReferenceEquals(chart.Palette, ChartPalette.Light)
                            && ReferenceEquals(((IdentityScanViewModel)window.DataContext).Rows, rows));
                        Themes.ThemeManager.Apply(theme);
                    }
                    window.Close();
                }
            }
        }
        finally { Themes.ThemeManager.Apply(savedTheme); }
    }
}
