using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record DeNovoCommandTrace(string Text, int FrozenLine, string Member);
    private static int DeNovoCommandProbe(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply frozen app folder, trusted SEPro package and fresh output.");
        string output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output); CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string packageHash = TestFileHash.Upper(args[2]);
        var packagePsms = SeproPsmAdapter.Load(args[2]);
        var psms = packagePsms.Where(psm => psm.ChargeState > 0 && psm.PrecursorMz > 0
            && DeNovoReconciler.NormalizeSequenceIgnoringPtm(psm.Sequence, false).Length >= 12)
            .OrderBy(psm => psm.FileName, StringComparer.Ordinal).ThenBy(psm => psm.ScanNumber).Take(5).ToArray();
        if (psms.Length == 0) throw new InvalidDataException("No usable fixture PSM in the supplied package.");
        string syntheticRun = DeNovoReconciler.NormalizeRunName(psms[0].FileName);
        var fixtureRows = psms.Concat(new[] { new PatternLabPsm { FileName = syntheticRun,
            ScanNumber = checked(packagePsms.Max(psm => psm.ScanNumber) + 1), PrecursorMz = 750, ChargeState = 2,
            Sequence = "ACDEFGHIKLMNPQRSTVWY", XCorr = 0 } }).ToArray();
        string imports = Path.Combine(output, "imports"); Directory.CreateDirectory(imports);
        foreach (var group in fixtureRows.GroupBy(psm => DeNovoReconciler.NormalizeRunName(psm.FileName), StringComparer.Ordinal))
        {
            var csv = new StringBuilder("Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,local confidence (%)\n");
            foreach (var psm in group)
            {
                string sequence = DeNovoReconciler.NormalizeSequenceIgnoringPtm(psm.Sequence, false);
                csv.AppendLine(string.Join(",", psm.ScanNumber.ToString(CultureInfo.InvariantCulture), sequence, "99", sequence.Length.ToString(CultureInfo.InvariantCulture),
                    psm.PrecursorMz.ToString("R", CultureInfo.InvariantCulture), psm.ChargeState.ToString(CultureInfo.InvariantCulture), "1", "1000", string.Join(" ", Enumerable.Repeat("99", sequence.Length))));
            }
            File.WriteAllText(Path.Combine(imports, group.Key + ".raw.denovoOnly.csv"), csv.ToString(), new UTF8Encoding(false));
        }
        string curation = Path.Combine(output, "command-fixture.fcf");
        new FamilyClusterFile { MyExtendedFastaItems = fixtureRows.Select((psm, index) => new FastaItemExtended
        { SequenceIdentifier = "fixture" + index.ToString(CultureInfo.InvariantCulture), Description = "Native command fixture", Sequence = DeNovoReconciler.NormalizeSequenceIgnoringPtm(psm.Sequence, false), UserID = index + 1, USearchID = index + 1 }).ToList() }.Save(curation);
        var legacyAssembly = Assembly.LoadFrom(Path.Combine(Path.GetFullPath(args[1]), "FamilyQuant.dll"));
        var legacyType = legacyAssembly.GetType("FamilyQuant.DeNovoControl", true)!;
        EnsureApplication();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var legacy = (UserControl)Activator.CreateInstance(legacyType)!; var current = new DeNovoControl();
        var oldWindow = LedgerDeNovoWindow(legacy); var newWindow = LedgerDeNovoWindow(current);
        var oldTrace = new List<DeNovoCommandTrace>(); var newTrace = new List<DeNovoCommandTrace>();
        var snapshots = new List<object>();
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        EventHandler Watch(UserControl control, List<DeNovoCommandTrace> target, Assembly assembly) => (_, _) =>
        {
            var frame = new StackTrace(true).GetFrames()?.FirstOrDefault(frame => frame.GetMethod()?.Module.Assembly == assembly
                && frame.GetMethod()?.Name != "SetStatus" && frame.GetFileLineNumber() > 0);
            target.Add(new(((TextBlock)control.FindName("TextBlockDeNovoStatus")).Text, frame?.GetFileLineNumber() ?? 0, frame?.GetMethod()?.Name ?? ""));
        };
        var oldWatch = Watch(legacy, oldTrace, legacyAssembly); var newWatch = Watch(current, newTrace, typeof(App).Assembly);
        descriptor.AddValueChanged(legacy.FindName("TextBlockDeNovoStatus"), oldWatch); descriptor.AddValueChanged(current.FindName("TextBlockDeNovoStatus"), newWatch);
        var oldFilePresenter = ShellDialogs.FilePresenter; var oldFolderPresenter = ShellDialogs.FolderPresenter;
        var chooserCalls = new List<object>();
        try
        {
            oldWindow.Show(); newWindow.Show(); PumpDispatcher();
            foreach (var expander in WalkTree(current).OfType<Expander>()) expander.IsExpanded = true;
            foreach (var control in new[] { legacy, current })
            {
                ((TextBox)control.FindName("TextBoxPeaksDirectory")).Text = imports;
                ((TextBox)control.FindName("TextBoxSeproPackage")).Text = Path.GetFullPath(args[2]);
                ((TextBox)control.FindName("TextBoxHomologyFcf")).Text = curation;
            }
            foreach (string button in new[] { "ButtonLoadAndFilter", "ButtonReconcile", "ButtonSearchHomology" })
            {
                Run(legacy, button, "frozen"); Run(current, button, "current");
            }
            var paired = JsonSerializer.SerializeToElement(snapshots);
            foreach (string command in new[] { "ButtonLoadAndFilter", "ButtonReconcile", "ButtonSearchHomology" })
            {
                var oldState = paired.EnumerateArray().Single(state => state.GetProperty("side").GetString() == "frozen" && state.GetProperty("command").GetString() == command);
                var newState = paired.EnumerateArray().Single(state => state.GetProperty("side").GetString() == "current" && state.GetProperty("command").GetString() == command);
                Check("Actual command frozen/current data agreement " + command, LedgerCommandDataAgrees(oldState, newState));
            }
            ShellDialogs.FolderPresenter = null; ShellDialogs.FilePresenter = null;
            foreach (var side in new[] { (View: legacy, Name: "frozen"), (View: (UserControl)current, Name: "current") })
            {
                foreach (var choice in new[] { ("ButtonBrowsePeaksDirectory", imports, "TextBoxPeaksDirectory"),
                    ("ButtonBrowseSeproPackage", Path.GetFullPath(args[2]), "TextBoxSeproPackage"), ("ButtonBrowseHomologyFcf", curation, "TextBoxHomologyFcf") })
                {
                    using var driver = new NativeOwnedDialogDriver(choice.Item2);
                    Run(side.View, choice.Item1, side.Name + "-chooser");
                    bool accepted = ((TextBox)side.View.FindName(choice.Item3)).Text == choice.Item2 && driver.SawDialog && driver.EnteredPath && !driver.TimedOut;
                    Check("Actual native chooser selection reached the screen " + side.Name + "/" + choice.Item1, accepted);
                    chooserCalls.Add(new { side = side.Name, command = choice.Item1, selected = choice.Item2, accepted, driver.SawDialog, driver.EnteredPath, driver.TimedOut, driver.Controls });
                }
                foreach (var export in new[] { ("ButtonExportSummary", "summary.csv"), ("ButtonExportReviewQueue", "review.csv"), ("ButtonExportOverlapStudy", "overlap.csv") })
                {
                    string path = Path.Combine(output, side.Name + "-" + export.Item2);
                    using var driver = new NativeOwnedDialogDriver(path);
                    Run(side.View, export.Item1, side.Name + "-export");
                    bool written = File.Exists(path) && new FileInfo(path).Length > 0 && driver.SawDialog && driver.EnteredPath && !driver.TimedOut;
                    Check("Actual native export wrote the chosen file " + side.Name + "/" + export.Item1, written);
                    chooserCalls.Add(new { side = side.Name, command = export.Item1, selected = path, written, driver.SawDialog, driver.EnteredPath, driver.TimedOut, driver.Controls });
                }
            }
            foreach (string file in new[] { "summary.csv", "review.csv", "overlap.csv" })
                Check("Actual chosen export frozen/current bytes " + file,
                    File.ReadAllBytes(Path.Combine(output, "frozen-" + file)).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "current-" + file))));
            foreach (var side in new[] { (View: legacy, Name: "frozen"), (View: (UserControl)current, Name: "current") })
            {
                foreach (string button in new[] { "ButtonBrowsePeaksDirectory", "ButtonBrowseSeproPackage", "ButtonBrowseHomologyFcf", "ButtonExportSummary" })
                {
                    var before = LedgerCommandStableState(side.View);
                    using var driver = new NativeOwnedDialogDriver(null);
                    Run(side.View, button, side.Name + "-cancel");
                    Check("Actual native cancel preserves data and paths " + side.Name + "/" + button, driver.SawDialog && !driver.TimedOut && before == LedgerCommandStableState(side.View));
                    chooserCalls.Add(new { side = side.Name, command = button, cancelled = true, driver.SawDialog, driver.TimedOut });
                }
                ((TextBox)side.View.FindName("TextBoxPeaksDirectory")).Text = "";
                RunFailure(side.View, "ButtonLoadAndFilter", side.Name + "-empty-import-directory");
                string missing = Path.Combine(output, "absent-import-directory");
                ((TextBox)side.View.FindName("TextBoxPeaksDirectory")).Text = missing;
                RunFailure(side.View, "ButtonLoadAndFilter", side.Name + "-missing-import");
                ((TextBox)side.View.FindName("TextBoxPeaksDirectory")).Text = imports;
                var seam = side.View.GetType().GetField("ImportOverride", BindingFlags.Public | BindingFlags.Static)!;
                object? previousSeam = seam.GetValue(null);
                var invoke = seam.FieldType.GetMethod("Invoke")!;
                var fail = typeof(Program).GetMethod(nameof(LedgerFailNativeImport), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(invoke.GetParameters()[1].ParameterType, invoke.ReturnType.GetGenericArguments()[0]);
                try { seam.SetValue(null, Delegate.CreateDelegate(seam.FieldType, fail)); RunFailure(side.View, "ButtonLoadAndFilter", side.Name + "-reader-failure"); }
                finally { seam.SetValue(null, previousSeam); }
                string badPackage = Path.Combine(output, "broken.sepr2"); File.WriteAllBytes(badPackage, [0, 1, 2]);
                ((TextBox)side.View.FindName("TextBoxSeproPackage")).Text = badPackage;
                RunFailure(side.View, "ButtonReconcile", side.Name + "-broken-reconcile");
                string badCuration = Path.Combine(output, "broken.fcf"); File.WriteAllText(badCuration, "{invalid");
                ((TextBox)side.View.FindName("TextBoxHomologyFcf")).Text = badCuration;
                RunFailure(side.View, "ButtonSearchHomology", side.Name + "-broken-search");
            }
            foreach (var side in new[] { (View: legacy, Name: "frozen"), (View: (UserControl)current, Name: "current") })
            {
                var source = (ComboBox)side.View.FindName("ComboBoxDeNovoSource");
                source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == DeNovoSource.DeepNovo.Id);
                PumpDispatcher(); Snapshot(side.View, "SourceChanged", side.Name);
                string empty = Path.Combine(output, "empty"); Directory.CreateDirectory(empty);
                ((TextBox)side.View.FindName("TextBoxPeaksDirectory")).Text = empty;
                Run(side.View, "ButtonLoadAndFilter", side.Name + "-empty", allowEmptyImport: true);
            }
            if (TestFileHash.Upper(args[2]) != packageHash) throw new InvalidDataException("SEPro input bytes changed.");
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "command-probe.json"), new { sepro = Path.GetFullPath(args[2]), packageHash, fixturePsms = psms.Length,
                snapshots, oldTrace, newTrace, chooserCalls, csvFiles = Directory.GetFiles(output, "*.csv").Select(path => new { path, sha256 = TestFileHash.Upper(path) }) });
            DeNovoChangedCopyFacts(output, JsonSerializer.SerializeToElement(snapshots));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }
        finally
        {
            ShellDialogs.FilePresenter = oldFilePresenter; ShellDialogs.FolderPresenter = oldFolderPresenter;
            descriptor.RemoveValueChanged(legacy.FindName("TextBlockDeNovoStatus"), oldWatch); descriptor.RemoveValueChanged(current.FindName("TextBlockDeNovoStatus"), newWatch);
            newWindow.Close(); oldWindow.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        void RunFailure(UserControl control, string name, string side)
        {
            string before = LedgerCommandStableState(control);
            Run(control, name, side, failureExpected: true);
            Check("Actual failed command retains prior data " + side, before == LedgerCommandStableState(control));
        }
        void Run(UserControl control, string name, string side, bool allowEmptyImport = false, bool failureExpected = false)
        {
            var button = (Button)control.FindName(name); Check("Actual command is reachable " + side + "/" + name, button.IsEnabled);
            if (!button.IsEnabled) throw new InvalidOperationException("Command is not reachable: " + name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var watch = Stopwatch.StartNew();
            while (!((GroupBox)control.FindName("GroupBoxImportAndFilter")).IsEnabled
                || ReferenceEquals(control, current) && current.ViewModel.Operations.IsBusy || ReferenceEquals(control, current) && AppOperationHost.For(current).IsBusy)
            {
                PumpDispatcher(); Thread.Sleep(10);
                if (watch.Elapsed > TimeSpan.FromMinutes(5)) throw new TimeoutException("Owned command did not finish: " + name);
            }
            PumpDispatcher();
            object? Value(string property) => control.GetType().GetProperty(property)?.GetValue(control);
            int Size(string property) => Value(property) is System.Collections.IEnumerable values ? values.Cast<object>().Count() : 0;
            bool success = failureExpected || name switch
            {
                "ButtonLoadAndFilter" => Value("LastImport") != null && Size("FilteredRows") == (allowEmptyImport ? 0 : fixtureRows.Length),
                "ButtonReconcile" => Value("LastReconciliation") != null && Size("Reconciled") == fixtureRows.Length,
                "ButtonSearchHomology" => Value("LastHomology") is object result && (int)result.GetType().GetProperty("MappedToTargets")!.GetValue(result)! > 0,
                _ => true,
            };
            Snapshot(control, name, side);
            Check("Actual command completed with expected data " + side + "/" + name, success);
        }
        void Snapshot(UserControl control, string name, string side)
        {
            object? Value(string property) => control.GetType().GetProperty(property)?.GetValue(control);
            int Size(string property) => Value(property) is System.Collections.IEnumerable values ? values.Cast<object>().Count() : 0;
            var visible = new Dictionary<string, string?>();
            foreach (string field in new[] { "LabelRowsRead", "LabelRowsKept", "LabelFilesFound", "TextBlockPerRunBreakdown", "TextBlockNativeConfidence",
                "LabelHomologyPeptides", "LabelHomologyMapped", "LabelHomologyDecoyOnly", "LabelHomologyUnmatched" })
                visible[field] = control.FindName(field) is TextBlock text ? text.Text : (control.FindName(field) as ContentControl)?.Content?.ToString();
            object? reconciliation = Value("LastReconciliation");
            JsonElement? Rec(string property) => reconciliation == null ? null : JsonSerializer.SerializeToElement(
                reconciliation.GetType().GetProperty(property)!.GetValue(reconciliation), DeNovoBridgeJson);
            snapshots.Add(new { side, command = name, status = ((TextBlock)control.FindName("TextBlockDeNovoStatus")).Text, visible,
                canReconcile = ((Button)control.FindName("ButtonReconcile")).IsEnabled,
                canImport = ((Button)control.FindName("ButtonLoadAndFilter")).IsEnabled,
                criteria = JsonSerializer.SerializeToElement(Value("Criteria"), Value("Criteria")!.GetType()),
                hasReconciliation = Value("LastReconciliation") != null, hasHomology = Value("LastHomology") != null,
                filtered = Size("FilteredRows"), reconciled = Size("Reconciled"), import = Value("LastImport") == null ? null : (JsonElement?)JsonSerializer.SerializeToElement(Value("LastImport"), Value("LastImport")!.GetType(), DeNovoBridgeJson),
                reconciliation = reconciliation == null ? null : new { counts = Rec("Counts"), rows = Rec("Rows"), review = Rec("ReviewQueue"), overlap = Rec("Overlap") },
                homology = Value("LastHomology") == null ? null : (JsonElement?)JsonSerializer.SerializeToElement(Value("LastHomology"), Value("LastHomology")!.GetType(), DeNovoBridgeJson) });
        }
    }

    private static bool LedgerCommandDataAgrees(JsonElement before, JsonElement after)
    {
        foreach (string scalar in new[] { "filtered", "reconciled" })
            if (before.GetProperty(scalar).GetInt32() != after.GetProperty(scalar).GetInt32()) return false;
        foreach (string property in new[] { "import", "reconciliation", "homology" })
            if (!LedgerCommandJsonAgrees(before.GetProperty(property), after.GetProperty(property))) return false;
        return true;
    }

    private static bool LedgerCommandJsonAgrees(JsonElement before, JsonElement after) => before.ValueKind == after.ValueKind && (before.ValueKind switch
    {
        JsonValueKind.Object => before.EnumerateObject().Where(property => property.Name != "ElapsedSeconds")
            .All(property => after.TryGetProperty(property.Name, out var value) && LedgerCommandJsonAgrees(property.Value, value)),
        JsonValueKind.Array => before.GetArrayLength() == after.GetArrayLength() && before.EnumerateArray().Zip(after.EnumerateArray()).All(pair => LedgerCommandJsonAgrees(pair.First, pair.Second)),
        _ => before.GetRawText() == after.GetRawText(),
    });

    private static string LedgerCommandStableState(UserControl control)
    {
        object? Value(string name) => control.GetType().GetProperty(name)!.GetValue(control);
        object? reconciliation = Value("LastReconciliation");
        object? Rec(string name) => reconciliation?.GetType().GetProperty(name)!.GetValue(reconciliation);
        return JsonSerializer.Serialize(new { import = Value("LastImport"),
            reconciliation = reconciliation == null ? null : new { rows = Rec("Rows"), counts = Rec("Counts"), queue = Rec("ReviewQueue"), overlap = Rec("Overlap"),
                psms = Rec("Psms") is System.Collections.ICollection psms ? psms.Count : 0 },
            homology = Value("LastHomology"), criteria = Value("Criteria"),
            paths = new[] { "TextBoxPeaksDirectory", "TextBoxSeproPackage", "TextBoxHomologyFcf" }.Select(name => ((TextBox)control.FindName(name)).Text).ToArray() }, DeNovoBridgeJson);
    }

    private static System.Threading.Tasks.Task<TImport> LedgerFailNativeImport<TCriteria, TImport>(string directory, TCriteria criteria) =>
        System.Threading.Tasks.Task.FromException<TImport>(new IOException("Native command fixture reader failure"));
}
