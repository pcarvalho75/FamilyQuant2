using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int NativeDialogProbe(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply a fresh lane-owned fixture directory.");
        if (!NativeProofDesktop.IsPrivate) return IsolatedNativeProof(new[] { "--isolated-native-proof" }.Concat(args).ToArray());
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(output); string input = Path.Combine(output, "input.txt"); File.WriteAllText(input, "native chooser fixture");
        EnsureApplication(); var owner = new Window { Left = -30000, Top = -30000, Width = 500, Height = 300, ShowActivated = false, ShowInTaskbar = false };
        owner.Show();
        try
        {
            var reports = new System.Collections.Generic.List<object>();
            using (var driver = new NativeOwnedDialogDriver(input))
            {
                var dialog = new OpenFileDialog { Title = "Owned native test open", Filter = "Text (*.txt)|*.txt" };
                bool accepted = dialog.ShowDialog(owner) == true;
                Check("owned native Open chooser accepts the fixture file", accepted && dialog.FileName == input && driver.SawDialog && driver.EnteredPath && !driver.TimedOut);
                reports.Add(new { kind = "open", accepted, dialog.FileName, driver.SawDialog, driver.EnteredPath, driver.TimedOut, driver.Controls });
            }
            using (var driver = new NativeOwnedDialogDriver(output))
            {
                var dialog = new OpenFolderDialog { Title = "Owned native test folder" };
                bool accepted = dialog.ShowDialog(owner) == true;
                Check("owned native Folder chooser accepts the fixture directory", accepted && dialog.FolderName == output && !driver.TimedOut);
                reports.Add(new { kind = "folder", accepted, path = dialog.FolderName, driver.SawDialog, driver.EnteredPath, driver.TimedOut, driver.Controls });
            }
            string save = Path.Combine(output, "chosen.txt");
            using (var driver = new NativeOwnedDialogDriver(save))
            {
                var dialog = new SaveFileDialog { Title = "Owned native test save", Filter = "Text (*.txt)|*.txt" };
                bool accepted = dialog.ShowDialog(owner) == true;
                Check("owned native Save chooser accepts the output path", accepted && dialog.FileName == save && !driver.TimedOut);
                reports.Add(new { kind = "save", accepted, dialog.FileName, driver.SawDialog, driver.EnteredPath, driver.TimedOut, driver.Controls });
            }
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "native-chooser-probe.json"), reports);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
        }
        finally { owner.Close(); }
    }
}
