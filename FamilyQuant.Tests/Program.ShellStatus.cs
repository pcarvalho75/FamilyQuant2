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
    private static void ShellStatusTests(string workDir)
    {
        var model = new ShellStatusViewModel();
        int notifications = 0;
        model.PropertyChanged += (_, _) => notifications++;
        model.Update("Build\nfamilies", "project.fqproj", "Ready\r\nto build", "Full context\nwith details");
        Check("status model keeps the bar on one line and retains full Help context", !model.Text.Contains('\n')
            && !model.Text.Contains('\r') && model.ContextHelp == "Full context\nwith details" && notifications == 2);
        model.ShowMessage("Saved\nFull destination and details");
        Check("status messages retain their details for Help", model.Text.EndsWith("Saved", StringComparison.Ordinal)
            && model.ContextHelp.Contains("Full destination and details"));
        string? originalSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(workDir, "status-settings.json");
        var main = new MainWindow();
        PumpDispatcher();
        const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            var guide = (TextBlock)main.FindName("TextBlockGuide");
            Check("the shell owns one quiet bottom status bar", guide.TextWrapping == TextWrapping.NoWrap
                && Grid.GetRow((Border)main.FindName("BorderGuide")) > Grid.GetRow((Grid)main.FindName("GridApp")));
            ApplicationCommands.New.Execute(null, main);
            var build = ((Grid)main.FindName("GridApp")).Children.OfType<USearchLib>().Single();
            Check("the shell suppresses the duplicate Build banner", build.UseShellStatusBar
                && guide.Text.Contains("Build families") && guide.Text.Contains("Ready to build"), guide.Text);
            var begin = typeof(USearchLib).GetMethod("BeginRun", any)!;
            var end = typeof(USearchLib).GetMethod("EndRun", any)!;
            try
            {
                begin.Invoke(build, new object[] { "fixture status notification" });
                Check("Build status changes reach the shell without starting computation", guide.Text.Contains("Running USEARCH")
                    && main.StatusModel.ContextHelp.Contains("fixture status notification")
                    && main.StatusModel.ContextHelp.Contains("cannot be interrupted"), guide.Text);
            }
            finally { end.Invoke(build, null); }
            string workflow = (string)typeof(MainWindow).GetMethod("WorkflowText", any)!.Invoke(main, null)!;
            Check("the former Build banner facts remain reachable in Help", workflow.Contains("Family Cluster File")
                && workflow.Contains("Input a FASTA file") && workflow.Contains(build.ShellHelpText));
            typeof(MainWindow).GetMethod("MenuItemDeNovo_Click", any)!.Invoke(main, new object[] { main, new RoutedEventArgs() });
            Check("de novo status is short while its detailed navigation facts stay in Help", guide.Text.Contains("De novo")
                && main.StatusModel.ContextHelp.Contains("Load Family") && main.StatusModel.ContextHelp.Contains("scan, charge"));

            bool curationContract = typeof(ClusterGenerationControl).GetProperty("ShellStatusText") != null
                && typeof(ClusterGenerationControl).GetProperty("UseShellStatusBar") != null
                && typeof(ClusterGenerationControl).GetEvent("ShellStatusChanged") != null;
            if (!curationContract)
            {
                Console.WriteLine("  SKIP  curation status seam integration - waiting for LK contract merge");
                skipped++;
            }
            else
            {
                string fcf = Path.Combine(workDir, "status-project.fcf");
                new FamilyClusterFile { MyExtendedFastaItems = new()
                {
                    new FastaItemExtended { SequenceIdentifier = "status_fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 }
                } }.Save(fcf);
                Check("curation status source opens for integration check", main.OpenProjectPath(fcf));
                var curation = ((Grid)main.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
                Check("the shell enables curation's status seam", (bool)typeof(ClusterGenerationControl)
                    .GetProperty("UseShellStatusBar")!.GetValue(curation)!);
                string initialStatus = curation.ShellStatusText;
                PumpDispatcher();
                Check("the shell displays curation's authoritative concise summary",
                    guide.Text.EndsWith(initialStatus, StringComparison.Ordinal), guide.Text);
                // Match the producer's fixture: commit a displayed calculation mode, then refresh.
                // Changing a legacy TextBlock alone is not a change to the status contract.
                typeof(ClusterGenerationControl).GetProperty("DisplayedMode", any)!
                    .SetValue(curation, NormalizationMode.IdentifiedSignal);
                typeof(ClusterGenerationControl).GetMethod("UpdateStatusLine", any)!.Invoke(curation, null);
                PumpDispatcher();
                string changedStatus = curation.ShellStatusText;
                Check("curation status transitions reach the shell verbatim", changedStatus != initialStatus
                    && guide.Text.EndsWith(changedStatus, StringComparison.Ordinal), guide.Text);
                Check("curation status facts remain available in shell Help", main.StatusModel.ContextHelp.Contains(changedStatus));
            }
        }
        finally { main.Close(); MainWindow.ShellSettingsPathOverride = originalSettings; }
    }
}
