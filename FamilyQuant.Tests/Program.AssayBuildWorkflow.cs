using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static Dictionary<string, string> AssayBuildWorkflow(string output, IReadOnlyDictionary<string, string> expected)
    {
        Directory.CreateDirectory(output); EnsureApplication();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var oldCulture = CultureInfo.CurrentCulture;
        var oldSelect = USearchLib.SelectDatabasePathOverride;
        var oldBuild = USearchLib.BuildOverride;
        var oldConfirm = USearchLib.ConfirmOverwriteOverride;
        var oldFiles = ShellDialogs.FilePresenter;
        var oldMessages = ShellMessages.Presenter;
        string? oldTool = Environment.GetEnvironmentVariable("FAMILYQUANT_USEARCH");
        string input = Path.Combine(output, "fixture.fasta"), fcf = Path.ChangeExtension(input, ".fcf");
        string reduced = Path.Combine(output, "fixture_NO_REDUNDANCY.fasta");
        File.WriteAllText(input, AssayBuildOutputSnapshot.Fasta, new UTF8Encoding(false));
        var view = new USearchLib();
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        AssayViewSnapshot.ConfigureOffscreen(window);
        var host = AppOperationHost.For(view);
        var messages = new List<ShellMessage>();
        void Report(ShellMessage message) => messages.Add(message);
        ShellMessages.Reported += Report;
        string mode = "cancel";
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", null);
            USearchLib.SelectDatabasePathOverride = null;
            USearchLib.BuildOverride = null;
            USearchLib.ConfirmOverwriteOverride = null;
            ShellMessages.Presenter = (_, _) => MessageBoxResult.No;
            ShellDialogs.FilePresenter = (owner, id, dialog) =>
            {
                values["dialog/id"] = id; values["dialog/title"] = dialog.Title;
                values["dialog/filter"] = dialog.Filter; values["dialog/type"] = dialog.GetType().Name;
                Check("Build file chooser is owned by the hidden fixture window", ReferenceEquals(owner, window));
                if (mode == "cancel") return false;
                dialog.FileName = input; return true;
            };
            window.Show(); PumpDispatcher();
            void Click(string name)
            {
                var previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                try { ((Button)view.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
            }
            void Finish()
            {
                var watch = Stopwatch.StartNew();
                while ((host.IsBusy || view.ViewModel.IsBusy) && watch.Elapsed < TimeSpan.FromSeconds(60))
                { PumpDispatcher(); Thread.Sleep(1); }
                if (host.IsBusy || view.ViewModel.IsBusy) throw new TimeoutException("Owned Build fixture did not finish.");
                PumpDispatcher();
            }
            void Record(string key, bool passedCheck)
            {
                Check("USearch workflow " + key, passedCheck);
                values["workflow/" + key] = passedCheck.ToString();
            }
            Record("defaults", view.ViewModel.Identity == 85 && view.ViewModel.Exhaustive
                && view.ViewModel.MaximumMixing == 0 && view.ViewModel.RedundancyIdentity == 100);
            foreach (string name in new[] { "ButtonDivideAndConquer", "ButtonScanIdentities", "ButtonRemoveRedundancy" })
            {
                Click(name); Finish();
                Record(name + "/missing", !host.IsBusy && !File.Exists(fcf)
                    && view.ViewModel.Status == "Choose an existing database before running this action.");
            }
            Click("ButtonCalculateFDR");
            Record("ButtonCalculateFDR/missing", messages.Last().Title == "Generate the families first");
            Click("ButtonInput");
            Record("select/cancel", view.ViewModel.DatabasePath.Length == 0 && !File.Exists(fcf));
            mode = "accept"; Click("ButtonInput");
            Record("select/accept", view.ViewModel.DatabasePath == input && view.fcf == null && !File.Exists(fcf)
                && ((Button)view.FindName("ButtonBuildFamilies")).IsEnabled);

            File.WriteAllText(fcf, "previous curated fixture");
            byte[] prior = File.ReadAllBytes(fcf);
            Click("ButtonBuildFamilies"); Finish();
            Record("build/overwrite-no", messages.Last().Title == "Replace the existing Family Cluster File?"
                && messages.Last().DefaultResult == MessageBoxResult.No && File.ReadAllBytes(fcf).SequenceEqual(prior));
            USearchLib.ConfirmOverwriteOverride = _ => true;
            var pending = new TaskCompletionSource<FamilyClusterFile>();
            USearchLib.BuildOverride = (path, identity, exhaustive) =>
            {
                values["build/input"] = Path.GetFileName(path);
                values["build/identity"] = identity.ToString(CultureInfo.InvariantCulture);
                values["build/exhaustive"] = exhaustive.ToString(); return pending.Task;
            };
            Click("ButtonBuildFamilies");
            Record("build/busy", host.IsBusy && view.ViewModel.IsBusy && new[] { "ButtonInput", "ButtonBuildFamilies",
                "ButtonCalculateFDR", "ButtonDivideAndConquer", "ButtonScanIdentities", "ButtonRemoveRedundancy" }
                .All(name => !((Button)view.FindName(name)).IsEnabled));
            view.CancelOperation(); pending.SetResult(new FamilyClusterFile()); Finish();
            Record("build/cancel", view.fcf == null && File.ReadAllBytes(fcf).SequenceEqual(prior)
                && view.ViewModel.Status.StartsWith("Cancelled.", StringComparison.Ordinal));
            USearchLib.BuildOverride = (_, _, _) => Task.FromException<FamilyClusterFile>(new IOException("owned fixture failure"));
            Click("ButtonBuildFamilies"); Finish();
            Record("build/failure", view.fcf == null && File.ReadAllBytes(fcf).SequenceEqual(prior)
                && messages.Last().Title == "Could not cluster the database");

            File.Delete(fcf); USearchLib.BuildOverride = null;
            Click("ButtonBuildFamilies"); Finish();
            values["build/members"] = AssayBuildOutputSnapshot.Members(FamilyClusterFile.Load(fcf));
            Record("build/success", view.fcf != null && values["build/members"] == expected["build/members"]
                && File.ReadAllText(input) == AssayBuildOutputSnapshot.Fasta && ((Button)view.FindName("ButtonCalculateFDR")).IsEnabled);
            Click("ButtonCalculateFDR");
            Record("ButtonCalculateFDR/success", messages.Last().Title == "Target/decoy mixing"
                && messages.Last().Text.Contains("Mixing score: " + view.fcf!.CalculateGroupingScore().Score.ToString("P2", CultureInfo.InvariantCulture), StringComparison.Ordinal));

            Click("ButtonRemoveRedundancy"); Finish();
            values["redundancy/fasta"] = File.ReadAllText(reduced);
            Record("ButtonRemoveRedundancy/success", values["redundancy/fasta"] == expected["redundancy/fasta"]
                && messages.Last().Title == "Redundancy removed" && File.ReadAllText(input) == AssayBuildOutputSnapshot.Fasta);
            view.ViewModel.DatabasePath = input + ".missing";
            Record("select/invalidates", view.fcf != null && !view.ViewModel.CanScore && !view.ViewModel.CanRun);

            string duplicate = Path.Combine(output, "duplicate.fasta");
            File.WriteAllText(duplicate, ">duplicate\n" + AssayBuildOutputSnapshot.Sequence + "\n>duplicate\n" + AssayBuildOutputSnapshot.Sequence + "\n");
            view.ViewModel.DatabasePath = duplicate;
            foreach (var action in new[] { ("ButtonDivideAndConquer", "Could not finish the search"), ("ButtonScanIdentities", "Could not scan the identity range") })
            {
                int count = messages.Count;
                Click(action.Item1); Finish();
                Record(action.Item1 + "/failure", messages.Count == count + 1 && messages.Last().Title == action.Item2
                    && !File.Exists(Path.ChangeExtension(duplicate, ".fcf")));
            }
            values["output/fcfExtension"] = Path.GetExtension(fcf);
            values["output/redundancySuffix"] = Path.GetFileName(reduced)[Path.GetFileNameWithoutExtension(input).Length..];
            foreach (var value in messages.Select((message, index) => (message, index)))
            {
                string prefix = "messages/" + value.index.ToString("D2", CultureInfo.InvariantCulture) + "/";
                values[prefix + "title"] = value.message.Title;
                values[prefix + "text"] = value.message.Text.Replace(output, "<output>", StringComparison.Ordinal);
            }
            Record("hidden-windows", window.Left == -30000 && window.Top == -30000 && !window.ShowActivated && !window.ShowInTaskbar
                && Application.Current.Windows.Cast<Window>().All(item => ReferenceEquals(item, window)));
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "workflow-values.json"), values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            return values;
        }
        finally
        {
            ShellMessages.Reported -= Report; ShellMessages.Presenter = oldMessages; ShellDialogs.FilePresenter = oldFiles;
            USearchLib.SelectDatabasePathOverride = oldSelect; USearchLib.BuildOverride = oldBuild; USearchLib.ConfirmOverwriteOverride = oldConfirm;
            CultureInfo.CurrentCulture = oldCulture; Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", oldTool);
            window.Close();
        }
    }
}
