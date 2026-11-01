using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int HomeStartScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        string fixture = Path.Combine(output, "Synthetic navigation fixture.fqproj");
        var curation = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new FastaItemExtended { SequenceIdentifier = "navigation_fixture", Description = "Synthetic navigation fixture",
                Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 }
        } };
        ProjectStore.WriteFqproj(fixture, curation, SyntheticAnalyticsRows(), new QuantManifest
        {
            FormatVersion = ProjectStore.CurrentFormatVersion,
            PerRunTic = new System.Collections.Generic.Dictionary<string, double> { ["ui_01"] = 5e8, ["ui_02"] = 4e8 }
        });
        string? original = MainWindow.ShellSettingsPathOverride;
        try
        {
            foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
            {
                FamilyQuant.Themes.ThemeManager.Apply(theme);
                foreach (var size in new[] { (1024, 768), (1366, 820) })
                {
                    string folder = Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Item1 + "x" + size.Item2);
                    Directory.CreateDirectory(folder);
                    MainWindow.ShellSettingsPathOverride = Path.Combine(folder, "fixture-preferences.json");
                    if (File.Exists(MainWindow.ShellSettingsPathOverride)) File.Delete(MainWindow.ShellSettingsPathOverride);
                    var main = new MainWindow { Width = size.Item1, Height = size.Item2, Left = -30000, Top = -30000,
                        WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
                    main.Show();
                    try
                    {
                        PumpDispatcher();
                        ScreenHarness.Save(main, Path.Combine(folder, "start-empty.png"), size.Item1, size.Item2);
                        if (!main.OpenProjectPath(fixture)) throw new InvalidOperationException("Synthetic navigation fixture did not open.");
                        typeof(MainWindow).GetMethod("ShowHome", System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance)!.Invoke(main, null);
                        var home = ((Grid)main.FindName("GridApp")).Children.OfType<HomeControl>().Single();
                        home.SetRecentProjects(new[] { fixture, Path.Combine(output, "Unavailable project.fqproj") });
                        PumpDispatcher();
                        ScreenHarness.Save(main, Path.Combine(folder, "start-project.png"), size.Item1, size.Item2);
                    }
                    finally { main.Close(); }
                }
            }
            Console.WriteLine("Start page: 8 real captures, empty and synthetic project states, dark/light at both sizes: " + output);
            return 0;
        }
        finally { MainWindow.ShellSettingsPathOverride = original; }
    }

    private static void HomeStartTests(string workDir)
    {
        var model = new HomeViewModel();
        model.SetProjectState(true, "project.fqproj\nwith stored results");
        Check("start model keeps project state and a one-line summary", model.HasProject
            && model.ProjectSummary.Contains("stored results") && !model.ProjectSummary.Contains('\n'));
        model.SetProjectState(false, "No project open.");
        Check("start model explains disabled project actions", !model.HasProject && model.ProjectReason.Contains("Requires"));
        string available = Path.Combine(workDir, "start-available.fcf");
        string unavailable = Path.Combine(workDir, "start-missing.fqproj");
        new FamilyClusterFile().Save(available);
        if (File.Exists(unavailable)) File.Delete(unavailable);
        model.SetRecentProjects(new[] { available, unavailable, available }, path => path == available);
        Check("start model keeps recent order, availability and distinct paths", model.RecentProjects.Count == 2
            && model.RecentProjects[0].Exists && !model.RecentProjects[1].Exists && model.RecentProjects[1].ToolTip.Contains(unavailable));
        model.SetLastResult(unavailable, false, _ => false);
        Check("a missing saved result stays readable and disabled", !model.HasLastResult && model.LastResultText.Contains("unavailable"));
        model.SetLastResult(null, true);
        Check("stored results in the current session can open without a saved result path", model.HasLastResult
            && model.LastResultText.Contains("open project"));

        string settings = Path.Combine(workDir, "start-state.json");
        var state = new ShellSessionState();
        state.Remember(available, hasStoredResults: true);
        Check("start result preferences save without scientific data", state.Save(settings) == null);
        var restored = ShellSessionState.Load(settings);
        Check("last result and recent paths survive restart", restored.LastResultProject == Path.GetFullPath(available)
            && restored.RecentProjects.SequenceEqual(state.RecentProjects));
        restored.Remember(available, hasStoredResults: false);
        Check("a known empty project cannot remain the saved last result", restored.LastResultProject == null);
        File.WriteAllText(settings, "{bad json");
        Check("damaged navigation preferences do not prevent starting", ShellSessionState.Load(settings).RecentProjects.Count == 0);

        var home = new HomeControl();
        home.SetProjectState(false, "No project open.");
        var quantify = (Button)home.FindName("ButtonHomeCompute");
        var results = (Button)home.FindName("ButtonHomeAnalytics");
        Check("start disabled actions retain the existing headless state seam", !quantify.IsEnabled && !results.IsEnabled);
        home.SetProjectState(true, "fixture.fqproj (12 stored rows)");
        Check("start project actions enable and preserve their summary", quantify.IsEnabled && results.IsEnabled
            && ((TextBlock)home.FindName("TextBlockHomeProject")).Text.Contains("fixture.fqproj"));
        int opens = 0, builds = 0, lastResults = 0;
        home.OpenRequested += () => opens++;
        home.BuildFamiliesRequested += () => builds++;
        home.LastResultRequested += () => lastResults++;
        ((Button)home.FindName("ButtonHomeOpen")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)home.FindName("ButtonHomeBuild")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        home.SetLastResult(null, true);
        ((Button)home.FindName("ButtonHomeLastResult")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check("start actions raise navigation requests without opening dialogs", opens == 1 && builds == 1 && lastResults == 1);
        home.SetRecentProjects(new[] { available, unavailable });
        home.Measure(new Size(1024, 768));
        home.Arrange(new Rect(0, 0, 1024, 768));
        home.UpdateLayout();
        PumpDispatcher();
        string? chosen = null;
        home.OpenPathRequested += path => chosen = path;
        var recentButtons = WalkTree(home).OfType<Button>().Where(b => b.DataContext is HomeRecentProject).ToArray();
        Check("recent project rows are real keyboard-focusable buttons", recentButtons.Length == 2 && recentButtons.All(b => b.Focusable));
        foreach (var button in recentButtons) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check("only an available recent row raises an open request", chosen == available);
        CheckTooltipCoverage("Start page", home);
    }
}
