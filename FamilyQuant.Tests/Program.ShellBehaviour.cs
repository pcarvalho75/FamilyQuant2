using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FamilyQuant.Tests;
internal static partial class Program
{
    private sealed record ShellBehaviourFact(string Id, string Case, bool Passed, string Before, string Actual, string Reason);
    private static int ShellBehaviour(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("Supply frozen source/app, fixed behaviour JSON, fresh output and date.");
        string output = Path.GetFullPath(args[4]);
        string step = args[0] == "--prove-shell-behaviour2" ? "SH-behaviour2" : "SH-behaviour";
        if (!NativeProofDesktop.IsPrivate) return NativeProofDesktop.Run(args, output + ".isolated-process.log");
        if (Directory.Exists(output)) throw new IOException("Use a fresh behaviour output.");
        Directory.CreateDirectory(output);
        var outer = Console.Out; using var log = new StreamWriter(output + ".isolated-process.log") { AutoFlush = true };
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetOut(new LedgerProofWriter(log, transcript));
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var facts = new Dictionary<string, ShellBehaviourFact>(StringComparer.Ordinal);
        using var requested = JsonDocument.Parse(File.ReadAllText(args[3]));
        var ids = requested.RootElement.EnumerateObject().Select(value => value.Name).ToHashSet(StringComparer.Ordinal);
        try
        {
            EnsureApplication();
            var appXml = System.Xml.Linq.XDocument.Load(Path.Combine(args[1], "FamilyQuant/App.xaml"));
            var gold = appXml.Descendants().FirstOrDefault(node => node.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "GoldBrush"));
            if (gold?.Attribute("Color") is { } color) Application.Current.Resources["GoldBrush"] = new System.Windows.Media.BrushConverter().ConvertFromString(color.Value);
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            { if (sender is Window window) { window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = window.Top = -30000; } }), true);
            var legacy = Assembly.LoadFrom(Path.Combine(args[2], "FamilyQuant.dll"));
            void Record(string[] ids, string name, bool pass, object? before, object? actual, string reason = "")
            {
                string b = JsonSerializer.Serialize(before), a = JsonSerializer.Serialize(actual);
                foreach (string id in ids)
                {
                    string full = id.StartsWith("FQ15-", StringComparison.Ordinal) ? id : "FQ15-" + id;
                    if (!requested.RootElement.TryGetProperty(full, out _)) continue;
                    facts[full] = new(full, name, pass, b, a, reason);
                }
                if (pass) Check("SH behaviour " + name, true);
                else { skipped++; Console.WriteLine("  SKIP  SH behaviour " + name + ": " + reason); }
            }
            void Attempt(string[] ids, string name, Action action)
            {
                if (!ids.Any(id => requested.RootElement.TryGetProperty(id.StartsWith("FQ15-", StringComparison.Ordinal) ? id : "FQ15-" + id, out _))) return;
                try { action(); }
                catch (Exception ex)
                {
                    var pending = ids.Where(id => !facts.TryGetValue(id.StartsWith("FQ15-", StringComparison.Ordinal) ? id : "FQ15-" + id, out var recorded) || !recorded.Passed).ToArray();
                    if (pending.Length > 0) Record(pending, name, false, null, null, ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message);
                }
            }
            ShellBehaviourCuration(legacy, output, Record, Attempt);
            ShellBehaviourBuild(legacy, output, Record, Attempt);
            var oldDeNovo = (UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.DeNovoControl", true)!)!;
            var currentDeNovo = new DeNovoControl();
            var oldDnWindow = SecondaryHost(new Window { Content = oldDeNovo }); var dnWindow = SecondaryHost(new Window { Content = currentDeNovo });
            try
            {
                Attempt(["56EFD9B009FD"], "retained reconciliation and search filtering prerequisites", () =>
                {
                    string imports = Path.Combine(output, "prerequisite-import"); Directory.CreateDirectory(imports);
                    File.WriteAllText(Path.Combine(imports, "owned.raw.denovoOnly.csv"),
                        "Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,local confidence (%)\n" +
                        "1,ACDEFGHIKLMN,99,12,500,2,1,1000,99 99 99 99 99 99 99 99 99 99 99 99\n" +
                        "2,ACDEFGHIKLMN,1,12,500,2,1,1000,1 1 1 1 1 1 1 1 1 1 1 1\n");
                    object Run(UserControl view)
                    {
                        bool[] Gates() => new[] { "ButtonReconcile", "ButtonSearchHomology" }.Select(name => ((Button)view.FindName(name)).IsEnabled).ToArray();
                        var initial = Gates();
                        ((TextBox)view.FindName("TextBoxPeaksDirectory")).Text = imports;
                        SecondaryClick((Button)view.FindName("ButtonLoadAndFilter"));
                        var watch = Stopwatch.StartNew();
                        while (!((GroupBox)view.FindName("GroupBoxImportAndFilter")).IsEnabled && watch.Elapsed < TimeSpan.FromSeconds(20)) { PumpDispatcher(); Thread.Sleep(5); }
                        if (view is DeNovoControl native) FinishBusyOperation(native.ViewModel.Operations);
                        PumpDispatcher();
                        int count = ((IEnumerable)SecondaryRead(view, "FilteredRows")!).Cast<object>().Count();
                        return new { initial, after = Gates(), count };
                    }
                    object before = Run(oldDeNovo), actual = Run(currentDeNovo);
                    string b = JsonSerializer.Serialize(before), a = JsonSerializer.Serialize(actual);
                    Record(["56EFD9B009FD"], "retained reconciliation and search filtering prerequisites", b == a
                        && b.Contains("\"initial\":[false,false]", StringComparison.Ordinal) && b.Contains("\"after\":[true,true]", StringComparison.Ordinal)
                        && b.Contains("\"count\":1", StringComparison.Ordinal), before, actual,
                        "Retained reconciliation/search gates did not match the real native filtering outcome; retired stages are outside this comparison.");
                });
                Attempt(["48FB6F4B7D66"], "empty-import reconciliation guard", () =>
                {
                    bool disabled = !((Button)oldDeNovo.FindName("ButtonReconcile")).IsEnabled && !((Button)currentDeNovo.FindName("ButtonReconcile")).IsEnabled;
                    SecondaryCall(oldDeNovo, oldDeNovo.GetType(), "ButtonReconcile_Click", oldDeNovo, new RoutedEventArgs());
                    SecondaryCall(currentDeNovo, currentDeNovo.GetType(), "ButtonReconcile_Click", currentDeNovo, new RoutedEventArgs()); PumpDispatcher();
                    string before = ((TextBlock)oldDeNovo.FindName("TextBlockDeNovoStatus")).Text;
                    string actual = currentDeNovo.ViewModel.Status;
                    Record(["48FB6F4B7D66"], "empty-import reconciliation guard", disabled && before == actual && before.Contains("nothing to join", StringComparison.Ordinal), before, actual);
                });
                Attempt(["95D4A5E286C3"], "missing-run empty native contract", () =>
                {
                    string before = (string)SecondaryCall(oldDeNovo, oldDeNovo.GetType(), "MissingRunsSentence")!;
                    string actual = currentDeNovo.ViewModel.MissingRunsSentence();
                    Record(["95D4A5E286C3"], "missing-run empty native contract", before == "none." && actual == before, before, actual);
                });
                Attempt(["8504F0703DBD"], "empty reconciliation export writer guard", () =>
                {
                    string path = Path.Combine(output, "absent-review.csv"); string before = "", actual = "";
                    try { SecondaryCall(oldDeNovo, oldDeNovo.GetType(), "ExportReviewQueueCsv", path); }
                    catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException error) { before = error.Message; }
                    try { currentDeNovo.ExportReviewQueueCsv(path); }
                    catch (InvalidOperationException error) { actual = error.Message; }
                    bool disabled = !((Button)oldDeNovo.FindName("ButtonExportReviewQueue")).IsEnabled && !((Button)currentDeNovo.FindName("ButtonExportReviewQueue")).IsEnabled;
                    Record(["8504F0703DBD"], "empty reconciliation export writer guard", disabled && before.Length > 0
                        && before.Replace("Layer 2", "Step 2", StringComparison.Ordinal) == actual && !File.Exists(path), before, actual);
                });
                Attempt(["7497282283CE"], "empty-run native cache artifact fallback", () =>
                {
                    string oldRoot = Path.Combine(output, "cache-frozen"), newRoot = Path.Combine(output, "cache-current");
                    string oldPath = (string)SecondaryCall(null, oldDeNovo.GetType(), "CachePathFor", "", oldRoot)!;
                    string newPath = DeNovoScreenOperations.CachePathFor("", newRoot);
                    var value = new FamilyQuant.DeNovo.DeNovoXicResult { RunName = "", ScanNoMS2 = 17, Charge = 2, PrecursorMz = 500,
                        IsotopeAreas = new[] { 4.0, 2.0, 1.0 } };
                    var oldResultType = legacy.GetType("FamilyQuant.DeNovo.DeNovoXicResult", true)!;
                    object oldValue = JsonSerializer.Deserialize(JsonSerializer.Serialize(value), oldResultType)!;
                    var oldCacheType = legacy.GetType("FamilyQuant.DeNovo.DeNovoXicCache", true)!;
                    object oldCache = SecondaryCall(null, oldCacheType, "Load", oldPath, "", 5.0, 1, 3, 3, "owned-cache-fallback")!;
                    SecondaryCall(oldCache, oldCacheType, "Put", oldValue); SecondaryCall(oldCache, oldCacheType, "Save");
                    var cache = FamilyQuant.DeNovo.DeNovoXicCache.Load(newPath, "", 5, 1, 3, 3, "owned-cache-fallback"); cache.Put(value); cache.Save();
                    using var oldJson = JsonDocument.Parse(File.ReadAllText(oldPath)); using var newJson = JsonDocument.Parse(File.ReadAllText(newPath));
                    bool SameCache(JsonElement before, JsonElement actual) => before.ValueKind == actual.ValueKind && before.ValueKind switch
                    {
                        JsonValueKind.Object => before.EnumerateObject().Count() == actual.EnumerateObject().Count()
                            && before.EnumerateObject().All(property => actual.TryGetProperty(property.Name, out var other) && SameCache(property.Value, other)),
                        JsonValueKind.Array => before.GetArrayLength() == actual.GetArrayLength()
                            && before.EnumerateArray().Zip(actual.EnumerateArray()).All(pair => SameCache(pair.First, pair.Second)),
                        JsonValueKind.Number => BitConverter.DoubleToInt64Bits(before.GetDouble()) == BitConverter.DoubleToInt64Bits(actual.GetDouble()),
                        _ => before.GetRawText() == actual.GetRawText()
                    };
                    Record(["7497282283CE"], "empty-run native cache artifact fallback", Path.GetFileName(oldPath) == "denovo_xic_run.json"
                        && Path.GetFileName(newPath) == Path.GetFileName(oldPath) && SameCache(oldJson.RootElement, newJson.RootElement),
                        File.ReadAllText(oldPath), File.ReadAllText(newPath));
                });
            }
            finally { oldDnWindow.Close(); dnWindow.Close(); }
            Attempt(["E3939DBDC9B0"], "detached Quantify browse guard", () =>
            {
                var oldQuant = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.ComputeXicWindow", true)!, new object?[] { "native fixture", null, null })!;
                var current = new QuantifyControl(); int windows = Application.Current.Windows.Count;
                try
                {
                    SecondaryCall(oldQuant, oldQuant.GetType(), "ButtonBrowseRaw_Click", oldQuant, new RoutedEventArgs());
                    SecondaryCall(current, current.GetType(), "BrowseRaw", current, new RoutedEventArgs());
                    string before = string.Join("\n", WalkTree(oldQuant).OfType<TextBlock>().Select(value => value.Text));
                    string actual = current.ViewModel.Status;
                    Record(["E3939DBDC9B0"], "detached Quantify browse guard", before.Contains("not on a display", StringComparison.Ordinal)
                        && actual.Contains("Type the path", StringComparison.Ordinal) && Application.Current.Windows.Count == windows,
                        before, actual);
                }
                finally { oldQuant.Close(); }
            });
            string root = FindLedgerRoot()!;
            var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
            var cohort = inventory.Observations.Where(row => ids.Contains(row.Id)).ToArray();
            foreach (var source in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
                if (FeatureLedgerEvidence.Hash(Path.Combine(args[1], source.Path)) != source.Sha256) throw new InvalidDataException("Frozen source differs: " + source.Path);
            foreach (var row in cohort.Where(row => !facts.ContainsKey(row.Id)))
                facts[row.Id] = new(row.Id, "not-executed", false, "", "", "No complete frozen/current execution comparison for this branch in this packet.");
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "facts.json"), facts.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray());
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            string proof = Path.Combine(output, "proofs.log"); File.WriteAllText(proof, transcript.ToString(), new UTF8Encoding(false));
            string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
            foreach (string file in new[] { "Program.ShellBehaviour.cs", "Program.ShellBehaviour.Curation.cs", "Program.ShellBehaviour.Build.cs", "Program.Commands.ShellBehaviour.cs", "Program.SecondarySuperRuntime.cs", "NativeOwnedDialogDriver.cs" })
                File.Copy(Path.Combine(root, "FamilyQuant.Tests", file), Path.Combine(archive, file));
            foreach (string file in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(FamilyClusterFile).Assembly.Location, legacy.Location })
                File.Copy(file, Path.Combine(archive, Path.GetFileName(file)));
            File.Copy(args[3], Path.Combine(archive, "scope.json"));
            var implementation = Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*.cs").Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*.xaml"))
                .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core"), "*.cs"))
                .Concat(new[] { "FamilyQuant.Tests/Program.ShellBehaviour.cs", "FamilyQuant.Tests/Program.ShellBehaviour.Curation.cs", "FamilyQuant.Tests/Program.ShellBehaviour.Build.cs", "FamilyQuant.Tests/Program.Commands.ShellBehaviour.cs", "FamilyQuant.Tests/Program.SecondarySuperRuntime.cs", "FamilyQuant.Tests/NativeOwnedDialogDriver.cs" }.Select(path => Path.Combine(root, path)))
                .Select(path => new FeatureLedgerEvidence.Implementation(Path.GetRelativePath(root, path).Replace('\\', '/'), "native behaviour fixture", FeatureLedgerEvidence.Hash(path))).ToList();
            var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD"); using var git = Process.Start(start)!;
            string commit = git.StandardOutput.ReadToEnd().Trim(); git.WaitForExit();
            var artifacts = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList();
            var run = new FeatureLedgerEvidence.Run(step, Environment.CommandLine, args[5], commit, artifacts, root);
            var rows = cohort.Select(row =>
            {
                var fact = facts[row.Id]; return new FeatureLedgerEvidence.Row(row.Id, fact.Passed ? implementation : new(), fact.Passed
                    ? new() { new(step, "test", nameof(ShellBehaviour), "SH behaviour " + fact.Case, proof, "") } : new(),
                    fact.Passed ? "Native frozen/current comparison; no product change." : "OPEN: " + fact.Reason,
                    Compared: fact.Passed ? row.Source + ":" + row.Line + " " + row.Detail + "; before=" + fact.Before + "; actual=" + fact.Actual : null);
            }).ToList();
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"), new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { run }, rows));
            Console.WriteLine(step + ": " + rows.Count(row => row.Proofs.Count > 0) + " supported; " + rows.Count(row => row.Proofs.Count == 0) + " OPEN.");
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
        finally { Console.SetOut(outer); SynchronizationContext.SetSynchronizationContext(oldContext); foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray()) window.Close(); }
    }
}
