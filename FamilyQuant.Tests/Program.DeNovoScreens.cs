using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int DeNovoScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        string root = Path.Combine(output, "synthetic-sources");
        Directory.CreateDirectory(root);
        const string row = "101,PEPTIDEK,{0},8,500.25000,2,12.5,1000000,99 99 99 99 99 99 99 99\n";
        File.WriteAllText(Path.Combine(root, "fixture.raw.denovoOnly.csv"),
            "Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,local confidence (%)\n" + string.Format(row, 85));
        File.WriteAllText(Path.Combine(root, "fixture.raw.deepnovo.csv"),
            "Scan,Peptide,SCORE (%),Length,m/z,z,RT,Area,local confidence (%)\n" + string.Format(row, 77));
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        foreach (var size in new[] { (1024, 768), (1366, 820) })
        {
            Themes.ThemeManager.Apply(theme);
            var filters = new DeNovoFilterViewModel { ModeTag = "union-only-deepnovo", DeepNovoMinimumScore = "75" };
            var control = new DeNovoControl(filters);
            var window = new Window { Content = control, Width = size.Item1, Height = size.Item2,
                Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
            window.Show();
            try
            {
                PumpDispatcher();
                Capture("inputs");
                filters.TryBuild(out var criteria, out var selected, out _);
                var imported = DeNovoSourceImport.Run(root, criteria, selected);
                control.ViewModel.ImportDirectory = root;
                control.ApplyImport(imported);
                var psms = new List<PatternLabPsm> { new() { FileName = "fixture.raw", ScanNumber = 101,
                    PrecursorMz = 500.25, ChargeState = 2, Sequence = "AAAAAAAA", XCorr = 4 } };
                control.ApplyReconciliation(DeNovoInspectionAudit.AttachSources(
                    DeNovoScreenOperations.Reconcile(imported.Kept, psms, "synthetic", imported.RowsRead, criteria), imported));
                PumpDispatcher();
                Capture("imported");
                var scroll = WalkTree(control).OfType<ScrollViewer>().First();
                scroll.ScrollToVerticalOffset(300);
                PumpDispatcher();
                Capture("review");
                WalkTree(control).OfType<Expander>().Single().IsExpanded = true;
                PumpDispatcher();
                System.Threading.Thread.Sleep(350);
                scroll.ScrollToBottom();
                PumpDispatcher();
                Capture("advanced");
            }
            finally { window.Close(); }
            void Capture(string state)
            {
                System.Threading.Thread.Sleep(350);
                PumpDispatcher();
                ScreenHarness.Save(window, Path.Combine(output, $"denovo-{theme}-{size.Item1}-{state}.png"), size.Item1, size.Item2);
            }
        }
        Console.WriteLine("De novo: 16 synthetic captures across both themes, sizes and inspection states: " + output);
        return 0;
    }
}
