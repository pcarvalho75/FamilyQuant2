using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;
internal static partial class Program
{
    private static void ShellBehaviourBuild(Assembly legacy, string output,
        Action<string[], string, bool, object?, object?, string> record, Action<string[], string, Action> attempt)
    {
        void R(string[] ids, string name, bool pass, object? before, object? actual) => record(ids, name, pass, before, actual, pass ? "" : "Native values do not establish the requested branch.");
        var old = (UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.USearchLib", true)!)!; var current = new USearchLib();
        var oldWindow = SecondaryHost(new Window { Content = old }); var window = SecondaryHost(new Window { Content = current });
        string[] buttons = ["ButtonInput", "ButtonDivideAndConquer", "ButtonScanIdentities", "ButtonRemoveRedundancy"];
        bool[] State(UserControl view) => buttons.Select(name => ((Button)view.FindName(name)).IsEnabled).ToArray();
        try
        {
            attempt(["3385D4BF21BC", "7AC0BC9F0603"], "native redundancy completion returns controls and preserves headers", () =>
            {
                string path = Path.Combine(output, "redundancy.fasta");
                const string fasta = ">first owned header\nACDEFGHIKLMNPQRSTVWY\n>duplicate second header\nACDEFGHIKLMNPQRSTVWY\n>third owned header\nVVVVVVVVVVVVVVVVVVK\n";
                File.WriteAllText(path, fasta); string hash = FeatureLedgerEvidence.Hash(path);
                string target = Path.Combine(output, "redundancy_NO_REDUNDANCY.fasta");
                var oldBefore = State(old);
                using (var driver = new NativeOwnedDialogDriver(path))
                {
                    SecondaryCall(old, old.GetType(), "ButtonRemoveRedundancy_Click", old, new RoutedEventArgs());
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while ((old.GetType().GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(old) is true) && watch.Elapsed < TimeSpan.FromSeconds(30))
                    { PumpDispatcher(); System.Threading.Thread.Sleep(5); }
                }
                string before = File.ReadAllText(target); File.Copy(target, Path.Combine(output, "frozen-redundancy.fasta")); File.Delete(target);
                current.ViewModel.DatabasePath = path; PumpDispatcher(); var currentBefore = State(current);
                SecondaryClick((Button)current.FindName("ButtonRemoveRedundancy")); FinishBusyOperation(AppOperationHost.For(current)); PumpDispatcher();
                string actual = File.ReadAllText(target); File.Copy(target, Path.Combine(output, "current-redundancy.fasta"));
                R(["3385D4BF21BC", "7AC0BC9F0603"], "native redundancy completion returns controls and preserves headers", before == actual
                    && before.IndexOf("first owned header", StringComparison.Ordinal) < before.IndexOf(">third owned header", StringComparison.Ordinal)
                    && before.Contains("first owned header", StringComparison.Ordinal) && before.Contains("duplicate second header", StringComparison.Ordinal)
                    && before.Contains(">third owned header", StringComparison.Ordinal)
                    && FeatureLedgerEvidence.Hash(path) == hash && State(old).SequenceEqual(oldBefore) && State(current).SequenceEqual(currentBefore),
                    new { fasta = before, controls = State(old) }, new { fasta = actual, controls = State(current) });
            });
            attempt(["94CC1A1E09A7", "5F8DD620BA18", "ABE30F08F684"], "native busy reentry and return of controls", () =>
            {
                var a = State(old); var b = State(current);
                bool firstOld = (bool)SecondaryCall(old, old.GetType(), "BeginRun", "owned native phase")!;
                bool firstCurrent = (bool)SecondaryCall(current, current.GetType(), "BeginRun", "owned native phase")!;
                PumpDispatcher(); bool disabled = State(old).All(value => !value) && State(current).All(value => !value);
                bool secondOld; using (var driver = new NativeOwnedDialogDriver(null)) secondOld = (bool)SecondaryCall(old, old.GetType(), "BeginRun", "reentry")!;
                bool secondCurrent = (bool)SecondaryCall(current, current.GetType(), "BeginRun", "reentry")!;
                using (var driver = new NativeOwnedDialogDriver(null)) SecondaryCall(old, old.GetType(), "ButtonRemoveRedundancy_Click", old, new RoutedEventArgs());
                SecondaryCall(current, current.GetType(), "ButtonRemoveRedundancy_Click", current, new RoutedEventArgs()); PumpDispatcher();
                SecondaryCall(old, old.GetType(), "EndRun"); SecondaryCall(current, current.GetType(), "EndRun"); PumpDispatcher();
                R(["94CC1A1E09A7", "5F8DD620BA18", "ABE30F08F684"], "native busy reentry and return of controls",
                    firstOld && firstCurrent && disabled && !secondOld && !secondCurrent && State(old).SequenceEqual(a) && State(current).SequenceEqual(b),
                    new { a, firstOld, secondOld, after = State(old) }, new { b, firstCurrent, secondCurrent, after = State(current) });
            });
            attempt(["2B8F9F3F57E3"], "native no-family mixing refusal", () =>
            {
                bool observed; using (var driver = new NativeOwnedDialogDriver(null))
                { SecondaryCall(old, old.GetType(), "ButtonCalculateFDR_Click", old, new RoutedEventArgs()); observed = driver.SawDialog; }
                ShellMessage? message = null; var save = ShellMessages.Presenter; ShellMessages.Presenter = (_, value) => { message = value; return MessageBoxResult.OK; };
                void Observe(ShellMessage value) => message = value;
                ShellMessages.Reported += Observe;
                try { SecondaryCall(current, current.GetType(), "ButtonCalculateFDR_Click", current, new RoutedEventArgs()); }
                finally { ShellMessages.Presenter = save; ShellMessages.Reported -= Observe; }
                R(["2B8F9F3F57E3"], "native no-family mixing refusal", observed && current.fcf == null
                    && message?.Text.Contains("no families to score", StringComparison.Ordinal) == true,
                    new { modalObserved = observed, result = SecondaryRead(old, "fcf") }, message?.Text);
            });
            attempt(["E4D5EFE6FD95"], "native identity curve lowest-clean report", () =>
            {
                string path = Path.Combine(output, "curve.fasta"); File.WriteAllText(path, BuildTargetDecoyFasta("Reverse_"));
                object oldRows = SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "ScanIdentities", path, 95, 90, 5, "Reverse_", true)!;
                var rows = FamilyClusterFile.ScanIdentities(path, 95, 90, 5);
                string before = (string)SecondaryCall(null, old.GetType(), "FormatScanReport", oldRows)!;
                string actual = BuildReportFormatter.Format(rows);
                var clean = rows.Where(row => row.MixedGroups == 0).OrderBy(row => row.Identity).First();
                string line = $"Lowest identity with no target/decoy mixing: {clean.Identity}% ({clean.Groups} families).";
                string[] Values(IEnumerable values) => values.Cast<object>().Select(row => string.Join("/", new[] { "Identity", "Groups", "TargetGroups", "MixedGroups" }.Select(name => SecondaryRead(row, name)))).ToArray();
                R(["E4D5EFE6FD95"], "native identity curve lowest-clean report", Values((IEnumerable)oldRows).SequenceEqual(Values(rows))
                    && before.Contains(line, StringComparison.Ordinal) && actual.Contains(line, StringComparison.Ordinal), before, actual);
            });
        }
        finally
        {
            if (current.ViewModel.IsBusy) SecondaryCall(current, current.GetType(), "EndRun");
            if (old.GetType().GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(old) is true) SecondaryCall(old, old.GetType(), "EndRun");
            oldWindow.Close(); window.Close();
        }
    }
}
