using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void QuantitationPanelTests(string output)
    {
        Section("Quantitation panel - Full XIC first, optional PatternLab and applicable choices");
        EnsureApplication(); Directory.CreateDirectory(output);
        string plpPath = WriteDenominatorFixture(output, "panel-fixture.plp", "TIC");
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var file = new FamilyClusterFile { MyExtendedFastaItems = new()
                {
                    new() { SequenceIdentifier = "sp|X|PROT1", Description = "Synthetic panel member",
                        Sequence = "MAWNDYEGHKMRGG", UserID = 1, USearchID = 1 }
                } };
                var control = new ClusterGenerationControl(file);
                var panel = (CurationQuantitationPanel)control.FindName("QuantitationPanel");
                var model = control.ViewModel.Quantitation;
                var shared = (ComboBox)panel.FindName("ComboBoxSharedPeptides");
                var mode = (ComboBox)panel.FindName("ComboBoxNormalization");
                var basis = (ComboBox)panel.FindName("ComboBoxFullXicBasis");
                var recalculate = (Button)panel.FindName("ButtonRecalculateQuantitation");
                Check(theme + " every normalization, shared policy and Full XIC basis remains in its original index",
                    mode.Items.Count == Enum.GetValues<NormalizationMode>().Length
                    && shared.Items.Count == Enum.GetValues<SharedPeptidePolicy>().Length && basis.Items.Count == 2);
                bool Shown(string name) => panel.FindName(name) is FrameworkElement element && element.Visibility == Visibility.Visible;
                Check(theme + " no-data state hides every inapplicable choice", !Shown("SharedOptions") && !Shown("FullXicOptions")
                    && !Shown("PatternLabOptions") && recalculate.Visibility == Visibility.Collapsed);
                var visualParent = VisualTreeHelper.GetParent(panel) as Panel;
                visualParent?.Children.Remove(panel);
                var host = new Window { Content = panel, Width = 360, Height = 760, Left = -30000, Top = -30000,
                    ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize };
                try
                {
                    host.Show(); PumpDispatcher();
                    var fullSection = panel.FindName("FullXicSection") as FrameworkElement;
                    var plpSection = panel.FindName("PatternLabSection") as FrameworkElement;
                    Check(theme + " Full XIC precedes the optional PatternLab source on one surface", fullSection != null && plpSection != null
                        && fullSection.TranslatePoint(new Point(), panel).Y < plpSection.TranslatePoint(new Point(), panel).Y
                        && !WalkTree(panel).OfType<GroupBox>().Any());
                    Capture("empty");
                    var rows = new[] { Qr("identified", "r1", 10, 2, "WNDYEGHKMR", 100, 200) };
                    control.SetFullXicQuant(rows, new Dictionary<string, double> { ["r1"] = 1000 }); PumpDispatcher();
                    Check(theme + " Full XIC alone exposes only its basis and the shared rule", Shown("FullXicOptions")
                        && Shown("SharedOptions") && !Shown("PatternLabOptions") && recalculate.Visibility == Visibility.Collapsed);
                    double? originalShare = file.MyExtendedFastaItems[0].FullXicShare;
                    basis.SelectedIndex = 1; PumpDispatcher();
                    Check(theme + " the native basis event still updates the family column immediately", originalShare == 1
                        && file.MyExtendedFastaItems[0].FullXicShare == .2);
                    basis.SelectedIndex = 0;
                    Capture("full-xic");
                    model.CurrentPLP = new PatternLabProject(new FileInfo(plpPath));
                    model.CurrentProfile = QuantitationProfile.Describe(model.CurrentPLP);
                    typeof(ClusterGenerationControl).GetMethod("UpdateReportingModeAvailability", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(control, new object[] { model.CurrentProfile });
                    PumpDispatcher();
                    Check(theme + " both sources keep separate denominators and one shared rule", Shown("FullXicOptions")
                        && Shown("PatternLabOptions") && Shown("SharedOptions") && recalculate.Visibility == Visibility.Visible);
                    Check(theme + " recorded divisor availability keeps the production mode gate",
                        ((ComboBoxItem)mode.Items[(int)NormalizationMode.TotalChromatogram]).IsEnabled == model.CurrentProfile.CanReportTotalChromatogram);
                    mode.SelectedIndex = (int)NormalizationMode.TotalChromatogram;
                    Capture("both-sources");
                    control.SetFullXicQuant(null, null); PumpDispatcher();
                    Check(theme + " PatternLab alone hides the Full XIC divisor and keeps every PatternLab choice", !Shown("FullXicOptions")
                        && Shown("PatternLabOptions") && Shown("SharedOptions") && mode.Items.Count == 4);
                    Capture("patternlab");
                    control.SetFullXicQuant(rows, new Dictionary<string, double> { ["other-run"] = 1000 }); PumpDispatcher();
                    var tic = (ComboBoxItem)panel.FindName("FullXicBasisTic");
                    Check(theme + " missing run TIC is refused with the recorded reason, never narrowed", !tic.IsEnabled
                        && basis.SelectedIndex == 0 && model.Summary.Contains("r1", StringComparison.Ordinal)
                        && model.Summary.Contains("TIC basis is unavailable", StringComparison.Ordinal));
                    control.SetFullXicQuant(new RefusedPanelRows(), null); PumpDispatcher();
                    Check(theme + " an attribution failure stays visible with the program's reason", model.HasFullXicError
                        && Shown("FullXicFailureText") && ((TextBlock)panel.FindName("FullXicFailureText")).Text == "Synthetic input refusal"
                        && model.FullXicStateLabel == "Unavailable");
                    control.SetFullXicQuant(null, null); PumpDispatcher();
                    Check(theme + " detaching a failed quantitation clears its old failure state", !model.HasFullXicError
                        && !Shown("FullXicFailureText") && !model.Summary.Contains("Synthetic input refusal", StringComparison.Ordinal));
                    Check(theme + " source options retain six-word tooltips", WalkTree(panel).OfType<FrameworkElement>()
                        .Where(e => e.TemplatedParent == null && e is Button or ComboBox or Label)
                        .All(e => e.ToolTip is string text && text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 6));
                }
                finally { host.Close(); PumpDispatcher(); }

                void Capture(string state)
                {
                    System.Threading.Thread.Sleep(350); PumpDispatcher();
                    ScreenHarness.Save(host, Path.Combine(output, $"panel-{theme}-{state}.png"), 360, 760);
                }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }

    private sealed class RefusedPanelRows : IReadOnlyList<QuantRow>
    {
        public int Count => 1;
        public QuantRow this[int index] => throw new InvalidOperationException("Synthetic input refusal");
        public IEnumerator<QuantRow> GetEnumerator() => throw new InvalidOperationException("Synthetic input refusal");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
