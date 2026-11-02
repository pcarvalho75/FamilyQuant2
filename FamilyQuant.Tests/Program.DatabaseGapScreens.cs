using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int DatabaseGapScreens(string projectPath, string output)
    {
        EnsureApplication();
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Screen output must be fresh.");
        Directory.CreateDirectory(output);
        string inputHash = FeatureLedgerEvidence.Hash(projectPath);
        var project = ProjectStore.ReadFqproj(projectPath);
        var model = new DatabaseGapViewModel(project.manifest.FamilyWorldResult, project.fcf);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var control = new DatabaseGapControl(model);
                var host = new Window { Content = control, Width = 1024, Height = 1040, Left = -30000, Top = -30000,
                    ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                try
                {
                    host.Show(); host.UpdateLayout(); PumpDispatcher();
                    System.Threading.Thread.Sleep(350); PumpDispatcher();
                    ScreenHarness.Save(host, Path.Combine(output, "database-gap-" + theme + ".png"), 1024, 1040);
                }
                finally { host.Close(); PumpDispatcher(); }
            }
            if (model.HasData) File.WriteAllText(Path.Combine(output, "database_gap.csv"), model.BuildCsv());
            if (model.CanExportFasta) File.WriteAllText(Path.Combine(output, "suggested_additions.fasta"), model.BuildSuggestedFasta());
            File.WriteAllText(Path.Combine(output, "help.txt"), DatabaseGapHelp.Text);
            Check("Database gap capture leaves the supplied project bytes unchanged", inputHash == FeatureLedgerEvidence.Hash(projectPath));
            Console.WriteLine("Database gap: " + model.Rows.Count + " stored family rows; FASTA available: " + model.CanExportFasta
                + "; " + model.FastaState + "; " + Path.GetFullPath(projectPath));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
