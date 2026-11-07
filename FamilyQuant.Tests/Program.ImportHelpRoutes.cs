using System;
using System.IO;
using System.Linq;
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
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }
}
