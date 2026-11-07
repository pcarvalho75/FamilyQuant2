using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FamilyListImportHelpRouteTests(string? output = null)
    {
        EnsureApplication();
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var file = ManualChoiceFile(); string before = file.ToJsonText();
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(file)));
                var wizard = new FamilyListImportWindow(model)
                { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
                wizard.Show(); PumpDispatcher();
                try
                {
                    foreach (string route in new[] { "Help", "F1" })
                    {
                        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
                        if (route == "Help") WalkTree(wizard).OfType<Button>().Single(b => b.Content as string == "Help")
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        else wizard.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(wizard)!, 0, Key.F1)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                        PumpDispatcher();
                        var opened = Application.Current.Windows.Cast<Window>().Where(w => !existing.Contains(w)).ToArray();
                        try
                        {
                            var guide = opened.OfType<FamilyListImportGuideWindow>().SingleOrDefault();
                            Check(theme + " " + route + " opens the owned illustrated chapter and preserves the draft", guide != null && ReferenceEquals(guide.Owner, wizard)
                                && guide.IsVisible && model.Page == 0 && file.ToJsonText() == before);
                            if (guide != null && output != null)
                            {
                                guide.Left = -30000; guide.Top = -30000;
                                guide.Model.Current = guide.Model.Sections.First(s => s.Image.Length > 0); PumpDispatcher();
                                Directory.CreateDirectory(output);
                                ScreenHarness.Save(guide, Path.Combine(output, theme + "-" + route + "-guide.png"), 1000, 700);
                            }
                        }
                        finally { foreach (var openedWindow in opened) openedWindow.Close(); PumpDispatcher(); }
                    }
                    Check(theme + " plain-text import Help remains available", FamilyListImportHelp.Text.StartsWith("IMPORT FAMILY LIST", StringComparison.Ordinal)
                        && FamilyListImportHelp.Text.Contains(FamilyListImportHelp.DecisionsSummary, StringComparison.Ordinal));
                }
                finally { wizard.Close(); PumpDispatcher(); }
            }
            FamilyListImportHelpSectionTests();
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }

    private static void FamilyListImportHelpSectionTests()
    {
        var pages = new (int Page, string Key)[]
        {
            (0, "welcome"), (1, "file"), (2, "columns"), (3, "special"),
            (4, "matches"), (5, "summary"), (6, "done"),
        };
        var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())));
        var wizard = new FamilyListImportWindow(model)
        { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        wizard.Show(); PumpDispatcher();
        try
        {
            foreach (var (page, key) in pages)
            {
                GoToImportPage(model, page);
                using var opened = OpenImportHelp(wizard);
                Check("Help on page " + page + " opens " + key, opened.Guide.Model.Current.Key == key && opened.Guide.Owner == wizard);
            }
            GoToImportPage(model, 2);
            model.ReviewingDecisions = true; PumpDispatcher();
            using var decisions = OpenImportHelp(wizard);
            Check("Help while reviewing decisions opens that section", model.Page == 2 && model.ReviewingDecisions
                && decisions.Guide.Model.Current.Key == "decisions"
                && decisions.Guide.Model.Current.Title == "3. Review spreadsheet decisions");
            decisions.Guide.Model.Current = decisions.Guide.Model.Sections[0]; PumpDispatcher();
            var previous = (Button)decisions.Guide.FindName("previousButton");
            Check("disabled Previous says this is the first part", previous.IsEnabled == false
                && previous.ToolTip as string == "This is the first part of the chapter; choose Next or select another part in Sections.");
            decisions.Guide.Model.Current = decisions.Guide.Model.Sections[^1]; PumpDispatcher();
            var next = (Button)decisions.Guide.FindName("nextButton");
            Check("disabled Next says this is the last part", next.IsEnabled == false
                && next.ToolTip as string == "This is the last part of the chapter; choose Previous or Close.");
        }
        finally { wizard.Close(); PumpDispatcher(); }
    }

    private static void GoToImportPage(FamilyListImportViewModel model, int page) =>
        typeof(FamilyListImportViewModel).GetMethod("GoTo", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, new object[] { page });

    private static HelpScope OpenImportHelp(FamilyListImportWindow wizard)
    {
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        WalkTree(wizard).OfType<Button>().Single(b => b.Content as string == "Help").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpDispatcher();
        var guide = Application.Current.Windows.Cast<Window>().OfType<FamilyListImportGuideWindow>().Single(w => !existing.Contains(w));
        return new HelpScope(guide);
    }

    private sealed class HelpScope : IDisposable
    {
        public FamilyListImportGuideWindow Guide { get; }
        public HelpScope(FamilyListImportGuideWindow guide) => Guide = guide;
        public void Dispose() { Guide.Close(); PumpDispatcher(); }
    }
}
