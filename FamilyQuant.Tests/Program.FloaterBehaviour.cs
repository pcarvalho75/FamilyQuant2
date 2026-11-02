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
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record FloaterBehaviourFact(string[] Ids, string Assertion, object Values);
    private static object? BehaviourField(object owner, string name) => owner.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(owner);
    private static MethodInfo BehaviourMethod(object owner, string name) => owner.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!;
    private static void BehaviourSave(object file, string path)
    { if (file is FamilyClusterFile current) current.Save(path); else SecondaryCall(file, file.GetType(), "Save", path); }
    private static void BehaviourWait(Func<bool> complete)
    {
        var watch = Stopwatch.StartNew();
        while (!complete() && watch.Elapsed < TimeSpan.FromSeconds(35)) { PumpDispatcher(); Thread.Sleep(5); }
        PumpDispatcher();
        if (!complete()) throw new TimeoutException("Native behaviour did not finish within 35 seconds.");
    }

    private static int FloaterBehaviourCommand(string[] args)
    {
        if (args.Length is not (6 or 7) || args.Length == 7 && args[6] != "--negative-control")
            throw new ArgumentException("Supply frozen source/app, scope JSON, fresh output and date.");
        string output = Path.GetFullPath(args[4]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (!NativeProofDesktop.IsPrivate) return IsolatedNativeProof(new[] { "--isolated-native-proof" }.Concat(args).ToArray());
        if (Directory.Exists(output)) throw new IOException("Fresh behaviour output required.");
        Directory.CreateDirectory(output); EnsureApplication(); CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var legacy = Assembly.LoadFrom(Path.Combine(args[2], "FamilyQuant.dll"));
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        using var scope = JsonDocument.Parse(File.ReadAllText(args[3]));
        var assigned = scope.RootElement.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        if (assigned.Count != 22 || assigned.Any(id => !inventory.Observations.Any(row => row.Id == id)))
            throw new InvalidDataException("The fixed 22-row behaviour scope differs from the inventory.");
        foreach (var item in scope.RootElement.EnumerateObject())
        {
            var row = inventory.Observations.Single(row => row.Id == item.Name);
            if (item.Value.GetProperty("where").GetString() != row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("The assigned frozen location differs: " + item.Name);
        }
        foreach (var file in inventory.Files.Where(file => inventory.Observations.Any(row => assigned.Contains(row.Id) && row.Source == file.Path)))
            if (FeatureLedgerEvidence.Hash(Path.Combine(args[1], file.Path)) != file.Sha256) throw new InvalidDataException("Frozen source differs: " + file.Path);
        var facts = new List<FloaterBehaviourFact>(); var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        var original = Console.Out; using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            Check("behaviour uses the exact frozen build", legacy.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal) == true);
            Run("de novo prerequisites", new[] { "FQ15-14EF12F7CDE7", "FQ15-C38D751ECBB2", "FQ15-EEAE18669485" }, () => BehaviourDeNovo(legacy, output, facts, args.Length == 7));
            Run("Quantify identification chooser", new[] { "FQ15-1E839DFF85FC" }, () => BehaviourQuantifyChooser(legacy, facts));
            Run("native build and database chooser", new[] { "FQ15-54BB7A04AC1F", "FQ15-7AFF0BB0CB70", "FQ15-D498F9890B9B", "FQ15-9D2713E3D10D", "FQ15-22317F5CE7C2", "FQ15-66A98A8CF568", "FQ15-5A8FC191A0CF", "FQ15-86A889C7F876" },
                () => BehaviourBuild(legacy, output, facts));
            Run("lock persistence", new[] { "FQ15-F7CDF32FD8B8" }, () => BehaviourLocks(legacy, output, facts));
            Run("PatternLab mode isolation", new[] { "FQ15-A9B61BDC02D3", "FQ15-6EF740A445CE" }, () => BehaviourPlp(legacy, output, facts));
            Run("protein-index PatternLab", new[] { "FQ15-5BC9BADDBD2B" }, () => BehaviourProteinPlp(legacy, output, facts));
            Run("project open and save", new[] { "FQ15-A832A6193C72", "FQ15-D93D13EF5E70", "FQ15-5DE04A6489DF", "FQ15-9A649BDCD31D", "FQ15-A0A334D576C6" },
                () => BehaviourProjects(legacy, args[1], output, facts));
            reasons["FQ15-3C4644555B98"] = "The grouped-PLP/XIC2 partition source editor is retired; neither retained core readers nor another quantitation route proves that same desktop fallback workflow.";
            reasons["FQ15-5BC9BADDBD2B"] = "The peptide-level native load is proved by input-formats; no independently authored protein-level PatternLab export fixture with a known reader classification was available for the required paired refusal.";
            reasons["FQ15-6EF740A445CE"] = "The native profile check records available TIC modes, but does not prove that an arbitrary external re-export recovers missing normalization information.";
            reasons["FQ15-A0A334D576C6"] = "The native save/reopen fixture contains 1001 quant rows; the complete normal visible grouped-count status/details producer has not been compared.";
            Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        }
        finally { Console.SetOut(original); SynchronizationContext.SetSynchronizationContext(context); }
        File.WriteAllText(Path.Combine(output, "behaviour.log"), transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "behaviour-values.json"), facts);
        var proved = facts.SelectMany(fact => fact.Ids).ToHashSet(StringComparer.Ordinal);
        if (proved.Any(id => !assigned.Contains(id))) throw new InvalidDataException("A native fact falls outside the assigned scope.");
        var audit = assigned.OrderBy(id => id, StringComparer.Ordinal).Select(id => new { Id = id,
            Where = scope.RootElement.GetProperty(id).GetProperty("where").GetString(), Proved = proved.Contains(id),
            Reason = proved.Contains(id) ? "Actual native frozen/current comparison; see behaviour-values.json and behaviour.log."
                : reasons.GetValueOrDefault(id, "The native comparison did not establish this complete claim; see the retained exception details.") }).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "target-audit.json"), audit);
        File.Copy(args[3], Path.Combine(output, "scope.json"));
        if (failed == 0) BehaviourRegister(root, output, args[5], legacy, inventory, facts);
        Console.WriteLine(FormattableString.Invariant($"Behaviour: {proved.Count} of {assigned.Count} assigned rows proved; {assigned.Count - proved.Count} explicit reasons. No ledger activation."));
        return failed == 0 ? 0 : 1;

        void Run(string group, string[] ids, Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                skipped++;
                string detail = error.ToString();
                File.WriteAllText(Path.Combine(output, group.Replace(' ', '-') + "-unproved.txt"), detail);
                foreach (string id in ids.Where(id => !facts.SelectMany(fact => fact.Ids).Contains(id, StringComparer.Ordinal)))
                    reasons[id] = group + " could not establish the claim: " + error.GetBaseException().Message;
                Console.WriteLine("  UNPROVED  " + group + ": " + error.GetBaseException().Message);
            }
        }
    }

    private static void BehaviourFact(List<FloaterBehaviourFact> facts, string[] ids, string assertion, bool same, object values)
    {
        Check(assertion, same);
        if (same) facts.Add(new(ids, assertion, values));
    }

    private static void BehaviourDeNovo(Assembly legacy, string output, List<FloaterBehaviourFact> facts, bool negative)
    {
        var old = (UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.DeNovoControl", true)!)!;
        var current = new DeNovoControl(); var oldHost = SecondaryHost(new Window { Content = old }); var host = SecondaryHost(new Window { Content = current });
        try
        {
            var values = new List<object>(); var refused = new List<string>(); bool guards = true, disabled = true, empty = true;
            foreach (var side in new[] { (Screen: old, Frozen: true), (Screen: (UserControl)current, Frozen: false) })
            {
                foreach (string button in new[] { "ButtonReconcile", "ButtonExportSummary", "ButtonExportReviewQueue", "ButtonExportOverlapStudy" })
                    disabled &= !((Button)side.Screen.FindName(button)).IsEnabled;
                string path = Path.Combine(output, side.Frozen ? "frozen-absent-overlap.csv" : "current-absent-overlap.csv");
                try { BehaviourMethod(side.Screen, "ExportOverlapStudyCsv").Invoke(side.Screen, new object[] { path }); }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { refused.Add(error.InnerException.Message); }
                if (negative && !side.Frozen) File.WriteAllText(path, "PLANTED OUTPUT: the absent-reconciliation guard must not create a file.");
                empty &= !File.Exists(path);
                foreach (string handler in new[] { "ButtonExportSummary_Click", "ButtonExportReviewQueue_Click", "ButtonExportOverlapStudy_Click", "ButtonReconcile_Click" })
                {
                    BehaviourMethod(side.Screen, handler).Invoke(side.Screen, new object[] { side.Screen, new RoutedEventArgs() }); PumpDispatcher();
                    string status = ((TextBlock)side.Screen.FindName("TextBlockDeNovoStatus")).Text;
                    guards &= status.StartsWith(handler.Contains("Reconcile", StringComparison.Ordinal) ? "Nothing was reconciled:" : "Nothing was exported:", StringComparison.Ordinal);
                    values.Add(new { side.Frozen, Handler = handler, Status = status });
                }
            }
            BehaviourFact(facts, new[] { "FQ15-14EF12F7CDE7" }, "behaviour overlap writers refuse absent reconciliation identically and create no file", refused.Count == 2 && refused[0] == refused[1] && empty, refused);
            BehaviourFact(facts, new[] { "FQ15-C38D751ECBB2", "FQ15-EEAE18669485" }, "behaviour empty de novo controls disable prerequisite actions and actual bypassed handlers explain the refusal", disabled && guards && empty, values);
        }
        finally { oldHost.Close(); host.Close(); }
    }

    private static void BehaviourQuantifyChooser(Assembly legacy, List<FloaterBehaviourFact> facts)
    {
        var old = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.ComputeXicWindow", true)!, new object?[] { "native behaviour fixture", null, null })!;
        old = SecondaryHost(old);
        var current = new QuantifyControl(); var host = SecondaryHost(new Window { Content = current });
        var saved = ShellDialogs.FilePresenter; var trace = new List<SecondaryInjection>();
        try
        {
            var filters = new List<string>();
            var method = BehaviourMethod(old, "ButtonBrowseSepro_Click");
            var copy = SecondaryInject(method, new Dictionary<MethodBase, MethodInfo> { [typeof(CommonDialog).GetMethod("ShowDialog", Type.EmptyTypes)!] = SecondaryBoundary(nameof(SecondaryFileBoundary)) }, trace);
            secondaryFrozenChooser = dialog => { filters.Add(((FileDialog)dialog).Filter); return false; };
            copy.Invoke(null, new object[] { old, old, new RoutedEventArgs() });
            ShellDialogs.FilePresenter = (_, id, dialog) => { if (id != "quantify.sepro") throw new InvalidDataException(id); filters.Add(dialog.Filter); return false; };
            BehaviourMethod(current, "BrowseSepro").Invoke(current, new object[] { current, new RoutedEventArgs() });
            bool same = filters.Count == 2 && filters.All(filter => filter.Split('|').Where((_, index) => index % 2 == 1).Contains("*.sepr2", StringComparer.Ordinal))
                && ((TextBox)old.FindName("TextBoxSeproPackage")).Text.Length == 0 && current.ViewModel.SeproPackage.Length == 0;
            BehaviourFact(facts, new[] { "FQ15-1E839DFF85FC" }, "behaviour Quantify native identification choosers retain sepr2 acceptance and cancellation", same,
                new { FrozenFilter = filters.FirstOrDefault(), CurrentFilter = filters.LastOrDefault(), ExactCaptionEqual = filters.Count == 2 && filters[0] == filters[1], Injections = trace });
        }
        finally { ShellDialogs.FilePresenter = saved; secondaryFrozenChooser = null; old.Close(); host.Close(); }
    }

    private static void BehaviourBuild(Assembly legacy, string output, List<FloaterBehaviourFact> facts)
    {
        var old = (UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.USearchLib", true)!)!;
        var current = new USearchLib(); var oldHost = SecondaryHost(new Window { Content = old }); var host = SecondaryHost(new Window { Content = current });
        var saved = ShellDialogs.FilePresenter; var messages = ShellMessages.Presenter;
        string? tool = Environment.GetEnvironmentVariable("FAMILYQUANT_USEARCH");
        var values = new List<object>(); var trace = new List<SecondaryInjection>();
        try
        {
            ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
            bool gated = !((Button)old.FindName("ButtonCalculateFDR")).IsEnabled && !UiField<Button>(current, "ButtonCalculateFDR")!.IsEnabled;
            var oldGuard = BehaviourMethod(old, "ButtonCalculateFDR_Click");
            var show = typeof(MessageBox).GetMethod("Show", new[] { typeof(string), typeof(string), typeof(MessageBoxButton), typeof(MessageBoxImage) })!;
            CurationFileMessages.Clear();
            var copy = SecondaryInject(oldGuard, new Dictionary<MethodBase, MethodInfo> { [show] = SecondaryBoundary(nameof(CurationFileMessage)) }, trace,
                new Dictionary<MethodBase, int> { [show] = 2 });
            copy.Invoke(null, new object[] { old, old, new RoutedEventArgs() });
            BehaviourMethod(current, "ButtonCalculateFDR_Click").Invoke(current, new object[] { current, new RoutedEventArgs() });
            string before = CurationFileMessages.Single(), after = current.ViewModel.ResultSummary;
            BehaviourFact(facts, new[] { "FQ15-54BB7A04AC1F" }, "behaviour native mixing controls gate absent families and actual defensive handlers refuse without scoring", gated && before.Contains("Nothing was measured:", StringComparison.Ordinal)
                && after.Contains("Nothing was measured:", StringComparison.Ordinal) && current.fcf == null && BehaviourField(old, "<fcf>k__BackingField") == null,
                new { FrozenMessage = before, CurrentMessage = after, Gated = gated, Injections = trace });
            var oldInput = BehaviourMethod(old, "ButtonInput_Click");
            string frozenFilter = FileContractStrings(oldInput.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!)
                .Single(value => value.Contains("*.fasta", StringComparison.Ordinal));
            string currentFilter = ""; bool all = true, route = true;
            foreach (string extension in new[] { ".fasta", ".T-R" })
            {
                string[] outputs = new string[2];
                for (int side = 0; side < 2; side++)
                {
                    bool frozen = side == 0; string folder = Path.Combine(output, "build", extension[1..], frozen ? "frozen" : "current"); Directory.CreateDirectory(folder);
                    string input = Path.Combine(folder, "owned" + extension);
                    const string seq = "ACDEFGHIKLMNPQRSTVWYACDEFGHIKLMNPQRSTVWYACDEFGHIKLMNPQRSTVWY";
                    File.WriteAllText(input, ">owned_a\n" + seq + "\n>owned_b\n" + seq[..^1] + "F\n" + (extension == ".T-R" ? ">Reverse_owned_a\n" + new string(seq.Reverse().ToArray()) + "\n" : ""), new UTF8Encoding(false));
                    string inputHash = FeatureLedgerEvidence.Hash(input), binary = Path.Combine(folder, "usearch.exe");
                    var resources = (frozen ? legacy : typeof(USearchTools).Assembly).GetType("FamilyQuant.MyResources", true)!;
                    File.WriteAllBytes(binary, (byte[])resources.GetProperty("usearch11_0_667_win32", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!);
                    Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", binary);
                    var screen = frozen ? old : current;
                    ((CheckBox)screen.FindName("CheckBoxExhaustiveClustering")).IsChecked = false;
                    if (frozen)
                    {
                        using var driver = new NativeOwnedDialogDriver(input);
                        SecondaryClick((Button)old.FindName("ButtonInput"));
                        BehaviourWait(() => SecondaryRead(old, "fcf") != null && !(bool)BehaviourField(old, "busy")!);
                        // Acceptance is established by the actual adjacent FCF, read below, and its
                        // exact source-derived records. The generic driver's timeout also covers
                        // the later completion message, so it is diagnostic rather than acceptance.
                        route &= driver.SawDialog && driver.EnteredPath;
                        values.Add(new { Kind = "native-chooser", Extension = extension, driver.SawDialog, driver.EnteredPath, driver.TimedOut,
                            AcceptanceEvidence = "Actual successful native build, adjacent reopened FCF and enabled mixing control; no timeout-free dialog claim." });
                    }
                    else
                    {
                        ShellDialogs.FilePresenter = (_, id, dialog) => { if (id != "build.database") throw new InvalidDataException(id); currentFilter = dialog.Filter; dialog.FileName = input; return true; };
                        SecondaryClick(UiField<Button>(current, "ButtonInput")!);
                        SecondaryClick(UiField<Button>(current, "ButtonBuildFamilies")!);
                        BehaviourWait(() => current.fcf != null && !current.ViewModel.IsBusy);
                    }
                    string path = Path.ChangeExtension(input, ".fcf"); outputs[side] = path;
                    object reopened = frozen ? legacy.GetType("FamilyQuant.FamilyClusterFile", true)!.GetMethod("Load", new[] { typeof(string) })!.Invoke(null, new object[] { path })! : FamilyClusterFile.Load(path);
                    var items = ((IEnumerable)SecondaryRead(reopened, "MyExtendedFastaItems")!).Cast<object>().ToArray();
                    all &= items.Length == (extension == ".T-R" ? 3 : 2) && inputHash == FeatureLedgerEvidence.Hash(input)
                        && ((Button)screen.FindName("ButtonCalculateFDR")).IsEnabled;
                    values.Add(new { Frozen = frozen, Extension = extension, InputSha256 = inputHash, BundledEngineSha256 = FeatureLedgerEvidence.Hash(binary),
                        AdjacentFile = Path.GetRelativePath(output, path), Records = items.Length, MixingEnabled = ((Button)screen.FindName("ButtonCalculateFDR")).IsEnabled });
                    if (frozen) old.GetType().GetProperty("fcf")!.SetValue(old, null); else { /* fresh selection resets the current document */ }
                }
                var left = JsonNode.Parse(File.ReadAllText(outputs[0]))!; var right = JsonNode.Parse(File.ReadAllText(outputs[1]))!;
                foreach (var item in left["MyExtendedFastaItems"]!.AsArray()) item!.AsObject().Remove("XIC");
                all &= JsonNode.DeepEquals(left, right);
            }
            bool filters = new[] { "*.fasta", "*.T-R", "*.T" }.All(pattern => new[] { frozenFilter, currentFilter }.All(filter => filter.Split('|').Where((_, index) => index % 2 == 1).SelectMany(value => value.Split(';')).Contains(pattern, StringComparer.Ordinal)));
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "build-values.json"), new { Values = values, FrozenFilter = frozenFilter, CurrentFilter = currentFilter, Filters = filters, Route = route, EqualFamilies = all });
            BehaviourFact(facts, new[] { "FQ15-86A889C7F876" }, "behaviour database choosers select real FASTA and target-decoy inputs retaining every legacy pattern", route && filters,
                new { FrozenFilter = frozenFilter, CurrentFilter = currentFilter, ExactCaptionEqual = frozenFilter == currentFilter });
            BehaviourFact(facts, new[] { "FQ15-7AFF0BB0CB70", "FQ15-D498F9890B9B", "FQ15-9D2713E3D10D", "FQ15-22317F5CE7C2", "FQ15-66A98A8CF568", "FQ15-5A8FC191A0CF" },
                "behaviour actual frozen/current build actions write adjacent readable equal families for FASTA and target-decoy and enable mixing", all && route, values);
        }
        finally { Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", tool); ShellDialogs.FilePresenter = saved; ShellMessages.Presenter = messages; oldHost.Close(); host.Close(); }
    }

    private static FamilyClusterFile BehaviourCuration() => new() { LockedUserIds = new() { 7, 8 }, MyExtendedFastaItems = new() {
        new() { SequenceIdentifier = "sp|X|PROT1", Sequence = "PEPTIDEA", Description = "Owned behaviour fixture", UserID = 7, USearchID = 1, UserAnnotation = "Owned annotation" },
        new() { SequenceIdentifier = "owned_other", Sequence = "VVVVVVVVK", Description = "Other fixture", UserID = 8, USearchID = 2 } } };

    private static void BehaviourLocks(Assembly legacy, string output, List<FloaterBehaviourFact> facts)
    {
        string folder = Path.Combine(output, "locks"); Directory.CreateDirectory(folder);
        var values = new List<object>(); bool all = true;
        foreach (bool frozen in new[] { true, false })
        {
            var file = BehaviourCuration(); object held = frozen ? SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "FromJsonText", file.ToJsonText())! : file;
            string path = Path.Combine(folder, frozen ? "frozen.fcf" : "current.fcf"); BehaviourSave(held, path); string hash = FeatureLedgerEvidence.Hash(path);
            var manager = frozen ? (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.LockManagerWindow", true)!, new object?[] { held, null, null })! : new LockManagerWindow(file);
            manager = SecondaryHost(manager);
            try
            {
                BehaviourMethod(manager, "SelectFamilies").Invoke(manager, new object[] { new[] { 7 } });
                int removed = (int)BehaviourMethod(manager, "RemoveSelectedLocks").Invoke(manager, null)!;
                var locks = ((IEnumerable)SecondaryRead(held, "LockedUserIds")!).Cast<int>().OrderBy(value => value).ToArray();
                bool unchangedDisk = FeatureLedgerEvidence.Hash(path) == hash;
                BehaviourSave(held, path);
                object reopened = frozen ? legacy.GetType("FamilyQuant.FamilyClusterFile", true)!.GetMethod("Load", new[] { typeof(string) })!.Invoke(null, new object[] { path })! : FamilyClusterFile.Load(path);
                int[] saved = ((IEnumerable)SecondaryRead(reopened, "LockedUserIds")!).Cast<int>().ToArray();
                all &= removed == 1 && locks.SequenceEqual(new[] { 8 }) && unchangedDisk && saved.SequenceEqual(locks);
                values.Add(new { Frozen = frozen, Removed = removed, InMemoryLocks = locks, DiskUnchangedBeforeSave = unchangedDisk, ReopenedLocks = saved });
            }
            finally { manager.Close(); }
        }
        BehaviourFact(facts, new[] { "FQ15-F7CDF32FD8B8" }, "behaviour native lock managers change memory only and explicit FCF save/reopen persists the same removal", all, values);
    }

    private static void BehaviourPlp(Assembly legacy, string output, List<FloaterBehaviourFact> facts)
    {
        string folder = Path.Combine(output, "modes"); Directory.CreateDirectory(folder);
        string path = WriteDenominatorFixture(folder, "owned.plp", "NONE"), hash = FeatureLedgerEvidence.Hash(path);
        var file = BehaviourCuration(); object oldFile = SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "FromJsonText", file.ToJsonText())!;
        var old = (UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.ClusterGenerationControl", true)!, oldFile)!;
        var current = new ClusterGenerationControl(file); var oldHost = SecondaryHost(new Window { Content = old }); var host = SecondaryHost(new Window { Content = current });
        var saved = ShellDialogs.FilePresenter; var messages = ShellMessages.Presenter;
        try
        {
            ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
            using (var driver = new NativeOwnedDialogDriver(path))
            { SecondaryClick((Button)old.FindName("ButtonLoadQuantData")); BehaviourWait(() => SecondaryRead(old, "CurrentProfile") != null); }
            ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = path; return true; };
            SecondaryClick(UiField<Button>(current, "ButtonLoadQuantData")!); FinishBusyOperation(current.Operations);
            var rows = new[] { Qr("identified", "runA", 1, 2, "PEPTIDEA", 100, 300) };
            var set = BehaviourMethod(old, "SetFullXicQuant"); set.Invoke(old, new[] { LedgerLegacyValue(rows, set.GetParameters()[0].ParameterType), null }); current.SetFullXicQuant(rows, null); PumpDispatcher();
            var snapshots = new List<object>(); bool all = true, availability = true;
            foreach (var side in new[] { (Screen: old, File: oldFile, Frozen: true), (Screen: (UserControl)current, File: (object)file, Frozen: false) })
            {
                var items = ((IEnumerable)SecondaryRead(side.File, "MyExtendedFastaItems")!).Cast<object>().ToArray();
                double?[] full = items.Select(item => (double?)SecondaryRead(item, "FullXicShare")).ToArray();
                var mode = side.Frozen ? (ComboBox)old.FindName("ComboBoxNormalization") : UiField<ComboBox>(current, "ComboBoxNormalization")!;
                var recalc = side.Frozen ? (Button)old.FindName("ButtonRecalculateQuantitation") : UiField<Button>(current, "ButtonRecalculateQuantitation")!;
                var profiles = new List<object>();
                foreach (int selected in new[] { 0, 1, 2, 3 })
                {
                    bool enabled = ((ComboBoxItem)mode.Items[selected]).IsEnabled;
                    availability &= enabled == ((ComboBoxItem)UiField<ComboBox>(current, "ComboBoxNormalization")!.Items[selected]).IsEnabled;
                    if (!enabled) continue;
                    mode.SelectedIndex = selected; PumpDispatcher(); SecondaryClick(recalc);
                    if (!side.Frozen) FinishBusyOperation(current.Operations); PumpDispatcher();
                    double[] shares = items.Select(item => (double)SecondaryRead(item, "XICUser")!).ToArray();
                    all &= full.SequenceEqual(items.Select(item => (double?)SecondaryRead(item, "FullXicShare")));
                    profiles.Add(new { Mode = selected, Shares = shares, FullXic = full });
                }
                snapshots.Add(profiles);
            }
            all &= JsonSerializer.Serialize(snapshots[0]) == JsonSerializer.Serialize(snapshots[1]) && FeatureLedgerEvidence.Hash(path) == hash;
            BehaviourFact(facts, new[] { "FQ15-A9B61BDC02D3" }, "behaviour native PLP mode recalculation preserves Full XIC and matches frozen PLP shares in every available mode", all, snapshots);
            // Do not infer a promise of re-export repair merely from successful loading.
            Check("behaviour native PLP profile mode availability agrees without inferring external re-export recovery", availability
                && ((ComboBoxItem)((ComboBox)old.FindName("ComboBoxNormalization")).Items[1]).IsEnabled == ((ComboBoxItem)UiField<ComboBox>(current, "ComboBoxNormalization")!.Items[1]).IsEnabled,
                JsonSerializer.Serialize(new { FrozenTicEnabled = ((ComboBoxItem)((ComboBox)old.FindName("ComboBoxNormalization")).Items[1]).IsEnabled,
                    CurrentTicEnabled = ((ComboBoxItem)UiField<ComboBox>(current, "ComboBoxNormalization")!.Items[1]).IsEnabled, InputSha256 = hash,
                    Limit = "This proves profile availability only, not that an arbitrary external re-export can recover missing information." }));
        }
        finally { ShellDialogs.FilePresenter = saved; ShellMessages.Presenter = messages; oldHost.Close(); host.Close(); }
    }

    private static void BehaviourProteinPlp(Assembly legacy, string output, List<FloaterBehaviourFact> facts)
    {
        string folder = Path.Combine(output, "protein-plp"); Directory.CreateDirectory(folder);
        string peptide = WriteDenominatorFixture(folder, "peptides.plp", "NONE");
        string protein = Path.Combine(folder, "proteins.plp");
        File.WriteAllText(protein, File.ReadAllText(peptide).Replace("Matrix=Peptides", "Matrix=Proteins", StringComparison.Ordinal)
            .Replace("0\tPEPTIDEA\tsp|X|PROT1", "0\tsp|X|PROT1\tOwned protein alpha", StringComparison.Ordinal)
            .Replace("1\tPEPTIDEB\tsp|X|PROT1", "1\tsp|Y|PROT2\tOwned protein beta", StringComparison.Ordinal), new UTF8Encoding(false));
        var snapshots = new List<object>(); var results = new List<double[]>(); bool kinds = true;
        foreach (string path in new[] { peptide, protein })
        foreach (bool frozen in new[] { true, false })
        {
            var file = BehaviourCuration(); foreach (var item in file.MyExtendedFastaItems) item.XICUser = .75;
            object held = frozen ? SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "FromJsonText", file.ToJsonText())! : file;
            var lookup = held.GetType().GetMethod("SetLookupDictionary")!;
            object loaded = Activator.CreateInstance(lookup.GetParameters()[0].ParameterType, new object[] { new FileInfo(path) })!;
            var names = ((IEnumerable)SecondaryRead(SecondaryRead(loaded, "MyIndex")!, "TheIndexes")!).Cast<object>().Select(item => (string)SecondaryRead(item, "Name")!).ToArray();
            kinds &= names.SequenceEqual(path == peptide ? new[] { "PEPTIDEA", "PEPTIDEB" } : new[] { "sp|X|PROT1", "sp|Y|PROT2" });
            if (frozen)
            {
                lookup.Invoke(held, new[] { loaded });
                var method = held.GetType().GetMethods().Single(method => method.Name == "AttributeXICToGroups" && method.GetParameters().Length == 4);
                method.Invoke(held, new[] { loaded, Enum.Parse(method.GetParameters()[1].ParameterType, "User"),
                    Enum.Parse(method.GetParameters()[2].ParameterType, "IdentifiedSignal"), Enum.Parse(method.GetParameters()[3].ParameterType, "FamilyUnique") });
            }
            else
            {
                file.SetLookupDictionary((PatternTools.PLP.PatternLabProject)loaded);
                file.AttributeXICToGroups((PatternTools.PLP.PatternLabProject)loaded, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
            }
            var shares = ((IEnumerable)SecondaryRead(held, "MyExtendedFastaItems")!).Cast<object>().Select(item => (double)SecondaryRead(item, "XICUser")!).ToArray(); results.Add(shares);
            snapshots.Add(new { Frozen = frozen, Input = Path.GetFileName(path), InputSha256 = FeatureLedgerEvidence.Hash(path), NativeIndexNames = names, Shares = shares });
        }
        BehaviourFact(facts, new[] { "FQ15-5BC9BADDBD2B" }, "behaviour native PLP readers attribute the peptide index and clear stale shares for the protein index identically", kinds && results.Count == 4
            && results[0].SequenceEqual(results[1]) && results[0].Any(value => value > 0) && results[2].SequenceEqual(results[3]) && results[2].All(value => value == 0), snapshots);
    }

    private static void BehaviourProjects(Assembly legacy, string frozenSource, string output, List<FloaterBehaviourFact> facts)
    {
        string folder = Path.Combine(output, "projects"); Directory.CreateDirectory(folder);
        var file = BehaviourCuration(); string fcfPath = Path.Combine(folder, "frozen.fcf");
        object oldFile = SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "FromJsonText", file.ToJsonText())!; SecondaryCall(oldFile, oldFile.GetType(), "Save", fcfPath);
        var rows = Enumerable.Range(1, 1001).Select(scan => Qr("identified", "owned-run", scan, 2, "PEPTIDEA", 10, 30)).ToList();
        var manifest = new QuantManifest { FormatVersion = ProjectStore.CurrentFormatVersion, AppVersion = "Native synthetic fixture", CreatedUtc = DateTime.UnixEpoch,
            UpdatedUtc = DateTime.UnixEpoch, SpectrumComparisonRows = FamilyQuant.DeNovo.SpectrumComparisonTable.Build(rows) };
        string fixture = Path.Combine(folder, "input.fqproj"); ProjectStore.WriteFqproj(fixture, file, rows, manifest);
        var saved = ShellDialogs.FilePresenter; var messages = ShellMessages.Presenter; var settings = MainWindow.ShellSettingsPathOverride; var choice = MainWindow.UnsavedProjectChoiceOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(folder, "preferences.json"); MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        // The frozen Home XAML uses a retired brush key. Restore the exact frozen App brush
        // resources while constructing its window; do not substitute a current screen.
        var resources = Application.Current.Resources;
        string frozenApp = Path.Combine(frozenSource, "FamilyQuant/App.xaml");
        var frozenInventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(FindLedgerRoot()!, "docs/feature-ledger-scan.json")))!;
        if (FeatureLedgerEvidence.Hash(frozenApp) != frozenInventory.Files.Single(file => file.Path == "FamilyQuant/App.xaml").Sha256)
            throw new InvalidDataException("Frozen application resources differ from the inventory.");
        File.Copy(frozenApp, Path.Combine(folder, "frozen-App.xaml"));
        var brushes = System.Xml.Linq.XDocument.Load(frozenApp)
            .Descendants().Where(node => node.Name.LocalName == "SolidColorBrush").Select(node => new { Key = node.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value, Color = (string)node.Attribute("Color")! }).ToArray();
        var previous = brushes.ToDictionary(brush => brush.Key, brush => resources.Contains(brush.Key) ? resources[brush.Key] : null);
        Window old;
        try
        {
            foreach (var brush in brushes) resources[brush.Key] = new System.Windows.Media.BrushConverter().ConvertFromInvariantString(brush.Color)!;
            old = SecondaryHost((Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.MainWindow", true)!)!);
        }
        finally { foreach (var brush in brushes) { if (previous[brush.Key] is { } value) resources[brush.Key] = value; else resources.Remove(brush.Key); } }
        var current = new MainWindow(); SecondaryHost(current);
        try
        {
            ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
            var values = new List<object>(); bool savedData = true, legacyCleared = true, familyOpen = true;
            var spectra = new List<string>(); var countTexts = new List<string>();
            foreach (bool frozen in new[] { true, false })
            {
                Window window = frozen ? old : current;
                Open(fixture);
                string destination = Path.Combine(folder, frozen ? "frozen-saved.fqproj" : "current-saved.fqproj");
                if (frozen)
                {
                    using var observer = new NativeBehaviourMessages("Saved");
                    using (var driver = new NativeOwnedDialogDriver(destination))
                        BehaviourMethod(old, "MenuItemSaveProject_Click").Invoke(old, new object[] { old, new RoutedEventArgs() });
                    using (var dismiss = new NativeOwnedDialogDriver(null))
                        BehaviourWait(() => File.Exists(destination) && !(bool)BehaviourField(old, "saveInProgress")!);
                    countTexts.Add(string.Join("\n", observer.Texts));
                }
                else
                {
                    ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = destination; return true; };
                    var reported = new List<string>();
                    void Observe(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
                    { if (current.StatusModel.ContextHelp.Contains("stored quant row(s)", StringComparison.Ordinal)) reported.Add(current.StatusModel.ContextHelp); }
                    current.StatusModel.PropertyChanged += Observe;
                    try
                    {
                        var save = (MenuItem)current.FindName("MenuItemSaveAs");
                        ((System.Windows.Input.RoutedCommand)save.Command).Execute(save.CommandParameter, current);
                        FinishBusyOperation(current.Operations); BehaviourWait(() => File.Exists(destination) && !(bool)BehaviourField(current, "saveInProgress")!);
                    }
                    finally { current.StatusModel.PropertyChanged -= Observe; }
                    countTexts.Add(string.Join("\n", reported));
                }
                var reopened = ProjectStore.ReadFqproj(destination);
                FeatureLedgerEvidence.WriteJson(destination + ".quant.json", reopened.quant);
                FeatureLedgerEvidence.WriteJson(destination + ".spectrum.json", reopened.manifest.SpectrumComparisonRows);
                File.WriteAllText(destination + ".curation.json", reopened.fcf.ToJsonText(), new UTF8Encoding(false));
                savedData &= reopened.quant.Count == 1001 && reopened.manifest.SpectrumComparisonRows?.Count == manifest.SpectrumComparisonRows.Count
                    && JsonSerializer.Serialize(reopened.quant) == JsonSerializer.Serialize(rows)
                    && reopened.fcf.MyExtendedFastaItems.Select(item => (item.UserID, item.UserAnnotation)).SequenceEqual(file.MyExtendedFastaItems.Select(item => (item.UserID, item.UserAnnotation)));
                spectra.Add(JsonSerializer.Serialize(reopened.manifest.SpectrumComparisonRows));
                Open(destination);
                int quantCount = ((ICollection)BehaviourField(window, "loadedQuant")!).Count;
                int spectrumCount = ((ICollection)SecondaryRead(BehaviourField(window, "loadedManifest")!, "SpectrumComparisonRows")!).Count;
                Open(fcfPath);
                int emptyQuant = ((ICollection)BehaviourField(window, "loadedQuant")!).Count;
                var curation = BehaviourField(window, "loadedCuration")!;
                var items = ((IEnumerable)SecondaryRead(curation, "MyExtendedFastaItems")!).Cast<object>().ToArray();
                bool families = items.Select(item => ((int)SecondaryRead(item, "UserID")!, (string)SecondaryRead(item, "UserAnnotation")!))
                    .SequenceEqual(file.MyExtendedFastaItems.Select(item => (item.UserID, item.UserAnnotation)));
                legacyCleared &= quantCount == 1001 && spectrumCount == manifest.SpectrumComparisonRows.Count && emptyQuant == 0 && BehaviourField(window, "loadedManifest") == null;
                familyOpen &= families;
                values.Add(new { Frozen = frozen, SavedQuantRows = quantCount, SavedSpectrumRows = spectrumCount, LegacyQuantRows = emptyQuant, FamiliesAndAnnotationsEqual = families });

                void Open(string path)
                {
                    if (frozen)
                    {
                        using var driver = new NativeOwnedDialogDriver(path);
                        BehaviourMethod(old, "MenuItemCluster_Click").Invoke(old, new object[] { old, new RoutedEventArgs() }); PumpDispatcher();
                        if (!driver.SawDialog || !driver.EnteredPath) throw new InvalidDataException("Frozen File Load did not select its native path.");
                    }
                    else
                    {
                        ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = path; return true; };
                        var menu = (MenuItem)current.FindName("MenuItemLoadFQC");
                        if (menu.Command is System.Windows.Input.RoutedCommand command) command.Execute(menu.CommandParameter, current);
                        else menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        FinishBusyOperation(current.Operations); PumpDispatcher();
                    }
                    BehaviourWait(() => string.Equals((string?)BehaviourField(window, "lastFcfPath"), path, StringComparison.OrdinalIgnoreCase));
                }
            }
            File.WriteAllText(Path.Combine(folder, "frozen-save-message.txt"), countTexts[0], new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "current-save-message.txt"), countTexts[1], new UTF8Encoding(false));
            BehaviourFact(facts, new[] { "FQ15-D93D13EF5E70", "FQ15-9A649BDCD31D" }, "behaviour actual File Save and Open preserve 1001 native quant rows, equal complete refreshed spectrum tables and curation", savedData && legacyCleared && spectra.Count == 2 && spectra[0] == spectra[1], values);
            BehaviourFact(facts, new[] { "FQ15-A832A6193C72", "FQ15-5DE04A6489DF" }, "behaviour actual File Open reads frozen FCF families and annotations while clearing prior project quantitation", familyOpen && legacyCleared, values);
            string token = 1001.ToString("N0", CultureInfo.InvariantCulture) + " stored quant row(s)";
            BehaviourFact(facts, new[] { "FQ15-A0A334D576C6" }, "behaviour actual native File Save messages display the same grouped 1001-row count", countTexts.Count == 2 && countTexts.All(text => text.Contains(token, StringComparison.Ordinal)),
                new { CountToken = token, FrozenObservedInOwnedNativeMessageBox = countTexts[0].Contains(token, StringComparison.Ordinal), CurrentObservedInNormalStatusDetails = countTexts[1].Contains(token, StringComparison.Ordinal) });
        }
        finally { ShellDialogs.FilePresenter = saved; ShellMessages.Presenter = messages; MainWindow.ShellSettingsPathOverride = settings; MainWindow.UnsavedProjectChoiceOverride = choice; old.Close(); current.Close(); }
    }

    private static void BehaviourRegister(string root, string output, string date, Assembly legacy, FeatureLedger.Inventory inventory, List<FloaterBehaviourFact> facts)
    {
        const string step = "FL-behaviour";
        string[] paths = { "FamilyQuant.Tests/Program.FloaterBehaviour.cs", "FamilyQuant.Tests/Program.Commands.SecondaryLedger.cs", "FamilyQuant.Tests/Program.Commands.DeNovoLedger.cs",
            "FamilyQuant.Tests/NativeBehaviourMessages.cs",
            "FamilyQuant.Tests/NativeOwnedDialogDriver.cs", "FamilyQuant.Tests/NativeProofDesktop.cs", "FamilyQuant.Tests/Program.SecondaryInjectedMethod.cs",
            "FamilyQuant/DeNovoControl.Dialogs.cs", "FamilyQuant/DeNovoControl.Compatibility.cs", "FamilyQuant/DeNovoViewModel.cs", "FamilyQuant/QuantifyControl.xaml.cs",
            "FamilyQuant/USearch.Actions.cs", "FamilyQuant/USearch.Presentation.cs", "FamilyQuant/USearch.xaml.cs", "FamilyQuant/LockManagerWindow.cs", "FamilyQuant/LockManagerViewModel.cs",
            "FamilyQuant/ClusterGenerationControl.xaml.cs", "FamilyQuant/ClusterGenerationControl.Presentation.cs", "FamilyQuant/MainWindow.Documents.cs", "FamilyQuant/MainWindow.Saving.cs", "FamilyQuant.Core/ProjectStore.cs" };
        var implementations = paths.Where(path => File.Exists(Path.Combine(root, path))).Select(path => new FeatureLedgerEvidence.Implementation(path, "Actual paired native behaviour case", FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList();
        foreach (var implementation in implementations)
        { string target = Path.Combine(output, "executed", implementation.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Path.Combine(root, implementation.Path), target); }
        foreach (var item in new[] { (typeof(Program).Assembly.Location, "FamilyQuant.Tests.dll"), (typeof(DeNovoControl).Assembly.Location, "FamilyQuant2.dll"), (typeof(FamilyClusterFile).Assembly.Location, "FamilyQuant.Core.dll"), (legacy.Location, "frozen-FamilyQuant.dll") })
        { Directory.CreateDirectory(Path.Combine(output, "executed")); File.Copy(item.Item1, Path.Combine(output, "executed", item.Item2)); }
        var artifacts = Directory.GetFiles(output, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList();
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }; start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!; string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        var rows = facts.SelectMany(fact => fact.Ids.Select(id => new FeatureLedgerEvidence.Row(id, implementations,
            new() { new("native", "test", nameof(FloaterBehaviourCommand), fact.Assertion, Path.Combine(output, "behaviour.log"), "") },
            "Synthetic fixture; actual frozen/current native paths. Caption and workflow changes are disclosed, not exact wording parity.", null,
            inventory.Observations.Single(row => row.Id == id).Detail + "; " + fact.Assertion))).ToList();
        var register = new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { new("native", Environment.CommandLine, date, commit, artifacts, root) }, rows);
        var check = FeatureLedgerEvidence.Load(root, inventory, new[] { register });
        if (check.FatalProblems.Count > 0 || check.Entries.Values.Any(entry => !entry.Valid)) throw new InvalidDataException("Behaviour receipts invalid: " + string.Join("; ", check.FatalProblems.Concat(check.Entries.Values.SelectMany(entry => entry.Problems))));
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"), register);
    }
}
