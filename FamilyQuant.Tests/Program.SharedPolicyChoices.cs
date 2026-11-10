using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SharedPolicyChoiceCommands() =>
    [new("--test-shared-policy-choices", 84, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-shared-policy-choices", (args, _) =>
        {
            EnsureApplication();
            SharedPolicyChoiceTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "shared-policy-choices"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void SharedPolicyChoiceTests(string output)
    {
        Directory.CreateDirectory(output);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "MEMBER_A", Description = "Synthetic member A", Sequence = "MMAAAAAAAKDDDDDDDK", UserID = 1, USearchID = 1 },
            new() { SequenceIdentifier = "MEMBER_B", Description = "Synthetic member B", Sequence = "LLAAAAAAAKCCCCCCCK", UserID = 2, USearchID = 2 },
        } };
        var rows = new[] { Qr("identified", "run-a", 1, 2, "AAAAAAAK", 100, 200) };
        var previousChoice = MainWindow.UnsavedProjectChoiceOverride;
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        try
        {
            foreach (var policy in new[] { SharedPeptidePolicy.CountInEveryFamily, SharedPeptidePolicy.SplitEqually })
            {
                string path = Path.Combine(output, policy + ".fqproj");
                ProjectStore.WriteFqproj(path, file, rows, new QuantManifest
                {
                    PerRunTic = new() { ["run-a"] = 1000 }, FamilyRunShares = new() { Policy = policy },
                    Provenance = QuantManifest.DefaultProvenance(),
                });
                string before = FileFingerprint.Sha256(path);
                var shell = new MainWindow();
                try
                {
                    Check("saved " + policy + " project opens through the native shell", shell.OpenProjectPath(path));
                    WaitForReportFixture(shell);
                    var control = ((Grid)shell.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();
                    var panel = (CurationQuantitationPanel)control.FindName("QuantitationPanel");
                    var shared = (ComboBox)panel.FindName("ComboBoxSharedPeptides");
                    Check("the quantitation panel retains only family-unique and split", shared.Items.Count == 2
                        && shared.Items[0] == panel.FindName("SharedItemUnique") && shared.Items[1] == panel.FindName("SharedItemSplit")
                        && panel.FindName("SharedItemEvery") == null);
                    Check("saved " + policy + " restores the supported active policy", shared.SelectedIndex
                        == (policy == SharedPeptidePolicy.SplitEqually ? 1 : 0));
                    Check("saved " + policy + " applies the supported rule to the displayed shared signal",
                        control.fcf.MyExtendedFastaItems.All(p => p.FullXicShare == (policy == SharedPeptidePolicy.SplitEqually ? .5 : 0)));
                    if (policy == SharedPeptidePolicy.CountInEveryFamily)
                        Check("legacy every-family project shows the one-time status notice", control.ShellStatusText.Contains(
                            "replaced with family-unique", StringComparison.Ordinal) && control.ShellStatusText.Contains("Run Quantify again", StringComparison.Ordinal));
                    else
                        Check("saved split policy needs no migration notice", !control.ShellStatusText.Contains("replaced", StringComparison.Ordinal));
                    Check("opening does not rewrite stored measurements or policy", before == FileFingerprint.Sha256(path)
                        && ProjectStore.ReadFqproj(path).manifest!.FamilyRunShares!.Policy == policy);
                    ScreenHarness.Save(shell, Path.Combine(output, "policy-" + policy + ".png"), 1180, 740);
                }
                finally { shell.Close(); PumpDispatcher(); }
            }
            var once = new ClusterGenerationControl(file);
            int notices = 0;
            once.ShellStatusChanged += (_, _) => { if (once.ShellStatusText.Contains("replaced with family-unique", StringComparison.Ordinal)) notices++; };
            once.RestoreStoredSharedPeptidePolicy(SharedPeptidePolicy.CountInEveryFamily);
            FinishBusyTask(once.ShowSharedPeptidePolicyNoticeAsync(Task.CompletedTask));
            once.RestoreStoredSharedPeptidePolicy(SharedPeptidePolicy.CountInEveryFamily);
            FinishBusyTask(once.ShowSharedPeptidePolicyNoticeAsync(Task.CompletedTask));
            Check("legacy policy notice is emitted once per opened curation", notices == 1);
        }
        finally { MainWindow.UnsavedProjectChoiceOverride = previousChoice; }
    }
}
