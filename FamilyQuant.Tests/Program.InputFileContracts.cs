using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int InputFileCommand(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Supply frozen app, trusted SEPro, fresh output and date.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
        if (!NativeProofDesktop.IsPrivate) return IsolatedNativeProof(new[] { "--isolated-native-proof" }.Concat(args).ToArray());
        string output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output); EnsureApplication(); CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var legacy = Assembly.LoadFrom(Path.Combine(Path.GetFullPath(args[1]), "FamilyQuant.dll"));
        var original = Console.Out; using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var proved = new List<string>(); var notes = new List<object>();
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            Check("input files identify exact frozen build", legacy.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal) == true);
            int readerResult = DeNovoCommandProbe(new[] { "--denovo-command-probe", args[1], args[2], Path.Combine(output, "readers") });
            if (readerResult != 0) throw new InvalidDataException("Native SEPro/FCF command reader comparison failed.");
            InputDiscoveryChoosers(legacy, args[2], Path.Combine(output, "readers/command-fixture.fcf"), output, notes);
            proved.AddRange(new[] { "FQ15-C720AF6E4572", "FQ15-1DB39E9F341A" });
            try { InputPlpScreen(legacy, output, notes); proved.Add("FQ15-B3488AD4ABFA"); }
            catch (Exception error) { skipped++; notes.Add(new { Group = "PLP", Reason = error.ToString() }); Console.WriteLine("  SKIP  PLP input screen: " + error.Message); }
            Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        }
        catch (Exception error) { Check("input chooser/reader packet completed", false, error.ToString()); Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped =====")); }
        finally { Console.SetOut(original); }
        File.WriteAllText(Path.Combine(output, "inputs.log"), transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "chooser-values.json"), notes);
        var captions = new List<string> { SecondaryWordingHeader };
        foreach (object note in notes)
        {
            var value = JsonSerializer.SerializeToElement(note, FeatureLedgerEvidence.JsonOptions);
            if (!value.TryGetProperty("frozenFilter", out var before) || !value.TryGetProperty("currentFilter", out var after) || before.GetString() == after.GetString()) continue;
            string id = value.GetProperty("group").GetString() == "PLP" ? "FQ15-B3488AD4ABFA" : "FQ15-1DB39E9F341A";
            captions.Add(string.Join('\t', new[] { "FQFloater", id, before.GetString()!, after.GetString()!,
                "Input format and reader compatibility executed; friendly caption differs or project support extends the filter. No exact caption equality claimed." }.Select(SecondaryWordingCell)));
        }
        File.WriteAllLines(Path.Combine(output, "wording-input-captions.tsv"), captions, new UTF8Encoding(false));
        if (failed == 0) InputFileRegister(output, args[4], legacy, proved);
        return failed == 0 ? 0 : 1;
    }

    private static void InputDiscoveryChoosers(Assembly legacy, string sepro, string fcf, string output, List<object> notes)
    {
        var old = (UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.DeNovoControl", true)!)!;
        var current = new DeNovoControl(); var oldHost = SecondaryHost(new Window { Content = old }); var host = SecondaryHost(new Window { Content = current });
        var saved = ShellDialogs.FilePresenter; var trace = new List<SecondaryInjection>();
        try
        {
            foreach (var item in new[] { ("SEPro", "ButtonBrowseSeproPackage", "TextBoxSeproPackage", "denovo.sepro", Path.GetFullPath(sepro), "*.sepr2"),
                ("FCF", "ButtonBrowseHomologyFcf", "TextBoxHomologyFcf", "denovo.curation", Path.GetFullPath(fcf), "*.fcf") })
            {
                var method = old.GetType().GetMethod(item.Item2 + "_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var copy = SecondaryInject(method, new Dictionary<MethodBase, MethodInfo> {
                    [typeof(CommonDialog).GetMethod("ShowDialog", Type.EmptyTypes)!] = SecondaryBoundary(nameof(SecondaryFileBoundary)) }, trace);
                var filters = new List<string>(); bool paths = true;
                foreach (bool frozen in new[] { true, false })
                foreach (bool accept in new[] { true, false })
                {
                    var screen = frozen ? old : current; string before = ((TextBox)screen.FindName(item.Item3)).Text; int calls = 0;
                    bool? Choose(FileDialog dialog) { calls++; filters.Add(dialog.Filter); dialog.FileName = item.Item5; return accept; }
                    secondaryFrozenChooser = dialog => Choose((FileDialog)dialog);
                    ShellDialogs.FilePresenter = (_, id, dialog) => id == item.Item4 ? Choose(dialog) : throw new InvalidOperationException("Unexpected chooser.");
                    if (frozen) copy.Invoke(null, new object[] { old, old.FindName(item.Item2), new RoutedEventArgs() });
                    else SecondaryClick((Button)current.FindName(item.Item2));
                    PumpDispatcher(); string after = ((TextBox)screen.FindName(item.Item3)).Text;
                    paths &= calls == 1 && after == (accept ? item.Item5 : before);
                }
                bool patterns = filters.Count == 4 && filters.All(filter => filter.Split('|').Where((_, index) => index % 2 == 1)
                    .SelectMany(pattern => pattern.Split(';')).Contains(item.Item6, StringComparer.Ordinal));
                Check("input " + item.Item1 + " chooser retains legacy extension and accept/cancel path with actual readers", paths && patterns && !host.IsActive && !oldHost.IsActive
                    && HasProofWindowPlacement(host) && HasProofWindowPlacement(oldHost));
                notes.Add(new { Group = item.Item1, FrozenFilter = filters[0], CurrentFilter = filters[2], LegacyPattern = item.Item6, AcceptedAndCancelledPaths = paths,
                    ReaderProof = "Actual ButtonReconcile/ButtonSearchHomology native data comparison in readers/command-probe.json" });
            }
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "chooser-injections.json"), trace);
        }
        finally { ShellDialogs.FilePresenter = saved; secondaryFrozenChooser = null; oldHost.Close(); host.Close(); }
    }

    private static void InputPlpScreen(Assembly legacy, string output, List<object> notes)
    {
        string folder = Path.Combine(output, "plp"); Directory.CreateDirectory(folder);
        string package = WriteDenominatorFixture(folder, "input.plp", "NONE"), hash = FeatureLedgerEvidence.Hash(package);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "sp|X|PROT1", Description = "Native PLP input", Sequence = "PEPTIDEA", UserID = 1, USearchID = 1 } } };
        object oldFile = SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "FromJsonText", file.ToJsonText())!;
        var old = (UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.ClusterGenerationControl", true)!, oldFile)!;
        var current = new ClusterGenerationControl(file); var oldHost = SecondaryHost(new Window { Content = old, Width = 1366, Height = 820 }); var host = SecondaryHost(new Window { Content = current, Width = 1366, Height = 820 });
        var saved = ShellDialogs.FilePresenter; var context = SynchronizationContext.Current; var trace = new List<SecondaryInjection>();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var method = old.GetType().GetMethod("ButtonLoadQuantData_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var machine = method.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            string oldFilter = FileContractStrings(machine).Single(value => value.Contains("*.plp", StringComparison.Ordinal));
            bool cancel = false, accepted = false; string currentFilter = "";
            foreach (bool accept in new[] { false, true })
            {
                using var driver = new NativeOwnedDialogDriver(accept ? package : null);
                SecondaryClick((Button)old.FindName("ButtonLoadQuantData"));
                var watch = Stopwatch.StartNew();
                while (accept && (SecondaryRead(old, "CurrentProfile") == null || ((IEnumerable)SecondaryRead(oldFile, "MyExtendedFastaItems")!).Cast<object>()
                    .All(item => (double)SecondaryRead(item, "XICUser")! <= 0)) && watch.Elapsed < TimeSpan.FromSeconds(25)) { PumpDispatcher(); Thread.Sleep(5); }
                PumpDispatcher();
                if (!accept) cancel = driver.SawDialog && !driver.TimedOut && SecondaryRead(old, "CurrentPLP") == null;
                else accepted = driver.SawDialog && driver.EnteredPath && !driver.TimedOut && SecondaryRead(old, "CurrentProfile") != null;
            }
            bool currentCancel = false;
            foreach (bool accept in new[] { false, true })
            {
                ShellDialogs.FilePresenter = (_, id, dialog) => { if (id != "curation.patternlab") throw new InvalidOperationException("Unexpected PLP chooser."); currentFilter = dialog.Filter; dialog.FileName = package; return accept; };
                SecondaryClick(UiField<Button>(current, "ButtonLoadQuantData")!); FinishBusyOperation(current.Operations);
                if (!accept) currentCancel = !current.ViewModel.Quantitation.HasPatternLab;
            }
            string oldPath = Path.Combine(folder, "frozen.csv"), currentPath = Path.Combine(folder, "current.csv");
            var save = old.GetType().GetMethod("ButtonSaveReport_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var choose = typeof(CommonDialog).GetMethod("ShowDialog", Type.EmptyTypes)!;
            var message = typeof(MessageBox).GetMethod("Show", new[] { typeof(string), typeof(string), typeof(MessageBoxButton), typeof(MessageBoxImage) })!;
            var copied = SecondaryInject(save, new Dictionary<MethodBase, MethodInfo> { [choose] = SecondaryBoundary(nameof(SecondaryFileBoundary)), [message] = SecondaryBoundary(nameof(CurationFileMessage)) }, trace,
                new Dictionary<MethodBase, int> { [choose] = 1, [message] = 3 });
            secondaryFrozenChooser = dialog => { ((FileDialog)dialog).FileName = oldPath; return true; };
            copied.Invoke(null, new object[] { old, old.FindName("ButtonSaveReport"), new RoutedEventArgs() });
            ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = currentPath; return true; };
            typeof(ClusterGenerationControl).GetMethod("ExportVisibleProteinTable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(current, new object[] { current }); FinishBusyOperation(current.Operations);
            string[] Patterns(string filter) => filter.Split('|').Where((_, index) => index % 2 == 1).SelectMany(value => value.Split(';')).ToArray();
            bool same = cancel && currentCancel && accepted && current.ViewModel.Quantitation.HasPatternLab
                && Patterns(oldFilter).SequenceEqual(Patterns(currentFilter)) && Patterns(currentFilter).Contains("*.plp", StringComparer.Ordinal)
                && file.MyExtendedFastaItems[0].XICUser > 0 && File.ReadAllBytes(oldPath).SequenceEqual(File.ReadAllBytes(currentPath)) && hash == FeatureLedgerEvidence.Hash(package);
            Check("input PLP chooser and actual loaded/exported quantitation agree", same);
            notes.Add(new { Group = "PLP", FrozenFilter = oldFilter, CurrentFilter = currentFilter, CaptionEqual = oldFilter == currentFilter,
                CaptionNote = "Friendly caption shortened; legacy extension and actual input/output data agree. No exact caption equality claimed.", Cancelled = cancel && currentCancel, NativeReaderLoaded = accepted,
                FrozenShare = SecondaryRead(((IEnumerable)SecondaryRead(oldFile, "MyExtendedFastaItems")!).Cast<object>().First(), "XICUser"), CurrentShare = file.MyExtendedFastaItems[0].XICUser, InputSha256 = hash });
            FeatureLedgerEvidence.WriteJson(Path.Combine(folder, "injections.json"), trace);
        }
        finally { ShellDialogs.FilePresenter = saved; secondaryFrozenChooser = null; SynchronizationContext.SetSynchronizationContext(context); oldHost.Close(); host.Close(); }
    }

    private static void InputFileRegister(string output, string date, Assembly legacy, List<string> proved)
    {
        string root = FindLedgerRoot()!; const string step = "FL-input-file-contracts";
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var labels = new Dictionary<string, string> { ["FQ15-C720AF6E4572"] = "input SEPro chooser retains legacy extension and accept/cancel path with actual readers",
            ["FQ15-1DB39E9F341A"] = "input FCF chooser retains legacy extension and accept/cancel path with actual readers", ["FQ15-B3488AD4ABFA"] = "input PLP chooser and actual loaded/exported quantitation agree" };
        string[] sources = { "FamilyQuant/DeNovoControl.Dialogs.cs", "FamilyQuant/DeNovoViewModel.Commands.cs", "FamilyQuant/ClusterGenerationControl.xaml.cs", "FamilyQuant.Core/FamilyQuantFile.cs", "FamilyQuant.Core/DeNovo/SeproPsmAdapter.cs" };
        var implementations = sources.Where(source => File.Exists(Path.Combine(root, source))).Select(source => new FeatureLedgerEvidence.Implementation(source, "Executed native input chooser/reader path", FeatureLedgerEvidence.Hash(Path.Combine(root, source)))).ToList();
        var rows = proved.Select(id => new FeatureLedgerEvidence.Row(id, implementations, new() { new("native", "test", nameof(InputFileCommand), labels[id], Path.Combine(output, "inputs.log"), "") },
            "Actual native input acceptance/cancellation plus loaded reader data; current FCF filter also accepts fqproj.", null, inventory.Observations.Single(row => row.Id == id).Detail)).ToList();
        string previousAudit = "D:/FamilyQuant-data/out-floater/curation-formats/after-y6/target-audit.json";
        var previous = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(previousAudit))!;
        var remaining = previous["remaining"]!.AsArray().Where(item => !proved.Contains(item!["row"]!["id"]!.GetValue<string>(), StringComparer.Ordinal))
            .Select(item => item!.DeepClone()).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "target-audit.json"), new { Proved = proved, Remaining = remaining });
        File.Copy(previousAudit, Path.Combine(output, "previous-target-audit.json"));
        foreach (string source in sources.Concat(new[] { "FamilyQuant.Tests/Program.InputFileContracts.cs", "FamilyQuant.Tests/Program.DeNovoCommandProbe.cs", "FamilyQuant.Tests/Program.SecondaryInjectedMethod.cs", "FamilyQuant.Tests/NativeOwnedDialogDriver.cs", "FamilyQuant.Tests/NativeProofDesktop.cs" }).Where(source => File.Exists(Path.Combine(root, source))))
        { string target = Path.Combine(output, "executed", source); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Path.Combine(root, source), target); }
        foreach (var item in new[] { (typeof(Program).Assembly.Location, "FamilyQuant.Tests.dll"), (typeof(DeNovoControl).Assembly.Location, "FamilyQuant2.dll"), (typeof(FamilyClusterFile).Assembly.Location, "FamilyQuant.Core.dll"), (legacy.Location, "frozen-FamilyQuant.dll") }) File.Copy(item.Item1, Path.Combine(output, "executed", item.Item2));
        var artifacts = Directory.GetFiles(output, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList();
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }; start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!; string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        var register = new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { new("native", Environment.CommandLine, date, commit, artifacts, root) }, rows);
        var check = FeatureLedgerEvidence.Load(root, inventory, new[] { register });
        if (check.FatalProblems.Count > 0 || check.Entries.Values.Any(entry => !entry.Valid)) throw new InvalidDataException("Input receipts are invalid: " + string.Join("; ", check.FatalProblems.Concat(check.Entries.Values.SelectMany(entry => entry.Problems))));
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"), register);
    }
}
