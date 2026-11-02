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
            string diagnosticTip = ((MenuItem)main.FindName("MenuItemClusteringDiagnostic")).ToolTip.ToString()!;
            Check("clustering diagnostic tooltip retains saved input, identity and unchanged-family facts",
                new[] { "diagnostic report", "saved .fcf or .fqproj", "clustering identity", "Save changes first", "families stay as they are" }
                    .All(fact => diagnosticTip.Contains(fact, StringComparison.OrdinalIgnoreCase)));
            Check("clustering diagnostic tooltip uses at most two sentences",
                System.Text.RegularExpressions.Regex.Matches(diagnosticTip, @"[.!?](?:\s|$)").Count <= 2);
            var guide = (TextBlock)main.FindName("TextBlockGuide");
            Check("the shell owns one quiet bottom status bar", guide.TextWrapping == TextWrapping.NoWrap
                && Grid.GetRow((Border)main.FindName("BorderGuide")) > Grid.GetRow((Grid)main.FindName("GridApp")));
            ApplicationCommands.New.Execute(null, main);
            var build = ((Grid)main.FindName("GridApp")).Children.OfType<USearchLib>().Single();
            Check("the shell suppresses the duplicate Build banner", build.UseShellStatusBar
                && guide.Text.Contains("Build families") && guide.Text.Contains("Choose a database"), guide.Text);
            var begin = typeof(USearchLib).GetMethod("BeginRun", any)!;
            var end = typeof(USearchLib).GetMethod("EndRun", any)!;
            try
            {
                begin.Invoke(build, new object[] { "fixture status notification" });
                Check("Build status changes reach the shell without starting computation", guide.Text.Contains("Running USEARCH")
                    && main.StatusModel.ContextHelp.Contains("fixture status notification")
                    && main.StatusModel.ContextHelp.Contains("Cancel stops after the current phase"), guide.Text);
            }
            finally { end.Invoke(build, null); }
            string workflow = (string)typeof(MainWindow).GetMethod("WorkflowText", any)!.Invoke(main, null)!;
            int quantitationStart = workflow.IndexOf("STEP 3", StringComparison.Ordinal);
            int quantitationEnd = workflow.IndexOf("STEP 4", StringComparison.Ordinal);
            string quantitation = quantitationStart >= 0 && quantitationEnd > quantitationStart
                ? Flatten(workflow[quantitationStart..quantitationEnd]) : "";
            Check("workflow quantitation directions name the current screens and optional PatternLab source",
                quantitation.Contains("Quantify", StringComparison.Ordinal)
                && quantitation.Contains("Families > Quantitation", StringComparison.Ordinal)
                && quantitation.Contains("PatternLab (optional)", StringComparison.Ordinal)
                && quantitation.Contains("Load PatternLab project", StringComparison.Ordinal));
            Check("workflow quantitation directions retire the Layer 2 and Layer 3 steps",
                !quantitation.Contains("Layer 2", StringComparison.Ordinal) && !quantitation.Contains("Layer 3", StringComparison.Ordinal));
            Check("workflow keeps the peptide-level input, denominator, shared-peptide and unsupported-mode facts",
                quantitation.Contains("PEPTIDE level", StringComparison.Ordinal) && quantitation.Contains("protein-level", StringComparison.Ordinal)
                && quantitation.Contains("every row will read 0", StringComparison.Ordinal) && quantitation.Contains("share of", StringComparison.Ordinal)
                && quantitation.Contains("peptides that belong to several families", StringComparison.Ordinal)
                && quantitation.Contains("cannot support", StringComparison.Ordinal) && quantitation.Contains("tooltip", StringComparison.Ordinal));
            Check("the former Build banner facts remain reachable in Help", workflow.Contains("Family Cluster File")
                && workflow.Contains("Build families") && workflow.Contains(build.ShellHelpText));
            typeof(MainWindow).GetMethod("MenuItemDeNovo_Click", any)!.Invoke(main, new object[] { main, new RoutedEventArgs() });
            Check("de novo status is short while its detailed navigation facts stay in Help", guide.Text.Contains("De novo")
                && main.StatusModel.ContextHelp.Contains("View > Families") && main.StatusModel.ContextHelp.Contains("File > Open")
                && main.StatusModel.ContextHelp.Contains("scan, charge"));

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
