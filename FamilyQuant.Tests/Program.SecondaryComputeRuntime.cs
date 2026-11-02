using FamilyQuant.DeNovo;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void SecondaryComputeRuntime(Assembly legacy, string output, Themes.AppTheme theme, SecondaryAdd add)
    {
        const string cs = "FamilyQuant/ComputeXicWindow.xaml.cs", xaml = "FamilyQuant/ComputeXicWindow.xaml";
        var old = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.ComputeXicWindow", true)!, new object?[] { "native fixture", null, null })!;
        int engineCalls = 0; ComputeXicRequest? request = null; IProgress<string>? progress = null;
        var pending = new TaskCompletionSource<FullXicComputeResult>();
        Func<ComputeXicRequest, IProgress<string>, CancellationToken, Task<FullXicComputeResult>> execute = (next, reporter, _) =>
        { engineCalls++; request = next; progress = reporter; return pending.Task; };
        var model = new ComputeXicViewModel("native fixture", compute: (next, reporter, token) => execute(next, reporter, token));
        var control = new QuantifyControl(model); var host = SecondaryHost(new Window { Content = control }); SecondaryHost(old);
        string input = Path.Combine(output, "compute-input"); Directory.CreateDirectory(input);
        string package = Path.Combine(input, "native.sepr2"); File.WriteAllText(package, "Synthetic input; injected engine never parses it.");
        var savedFile = ShellDialogs.FilePresenter; var savedFolder = ShellDialogs.FolderPresenter;
        try
        {
            void Wait(Func<bool> complete)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!complete() && watch.Elapsed < TimeSpan.FromSeconds(10)) { PumpDispatcher(); Thread.Sleep(2); }
                if (!complete()) throw new TimeoutException("A native Quantify operation did not reach its expected state.");
            }
            var compute = (Button)control.FindName("ButtonCompute");
            string OldStatus() => ((TextBlock)old.FindName("TextBlockStatus")).Text;
            void OldPaths(string raw, string sepro, string deNovo)
            {
                ((TextBox)old.FindName("TextBoxRawDirectory")).Text = raw;
                ((TextBox)old.FindName("TextBoxSeproPackage")).Text = sepro;
                ((TextBox)old.FindName("TextBoxDeNovoDirectory")).Text = deNovo;
            }
            var refusals = new List<string>();
            foreach (var scenario in new[] { ("", "", "", 273, 279), (input, "", "", 285, 289), (input, package, "missing-native-de-novo", 290, 291) })
            {
                OldPaths(scenario.Item1, scenario.Item2, scenario.Item3);
                model.RawDirectory = scenario.Item1; model.SeproPackage = scenario.Item2; model.DeNovoDirectory = scenario.Item3;
                SecondaryClick((Button)old.FindName("ButtonCompute")); SecondaryClick(compute); PumpDispatcher();
                bool refused = !(bool)SecondaryRead(old, "Computed")! && !model.Computed && engineCalls == 0
                    && !string.IsNullOrWhiteSpace(OldStatus()) && !string.IsNullOrWhiteSpace(model.Status);
                refusals.Add(refused.ToString(CultureInfo.InvariantCulture));
                add(cs, scenario.Item4, scenario.Item5, "native old/current Compute rejects the same invalid input category before the engine",
                    new { Refused = !(bool)SecondaryRead(old, "Computed")!, HasStatus = OldStatus().Length > 0 },
                    new { Refused = !model.Computed, HasStatus = model.Status.Length > 0 }, refused,
                    "Compute button input validation; displayed wording may be improved");
            }
            var oldSource = (ComboBox)old.FindName("ComboBoxDeNovoSource");
            var filters = (FrameworkElement)control.FindName("FilterControl");
            var source = (ComboBox)filters.FindName("ComboBoxDeNovoSource");
            var sourceBefore = new SortedDictionary<string, string>(StringComparer.Ordinal); var sourceAfter = new SortedDictionary<string, string>(StringComparer.Ordinal);
            string Selection(object selections) => string.Join(";", ((IEnumerable)selections).Cast<object>().Select(selection =>
                SecondaryRead(SecondaryRead(selection, "Source")!, "Id") + "/" + Convert.ToString(SecondaryRead(selection, "MinimumConfidence"), CultureInfo.InvariantCulture)
                + "/" + Convert.ToString(SecondaryRead(selection, "Priority"), CultureInfo.InvariantCulture)));
            foreach (string tag in new[] { "peaks-only", "peaks-all", "deepnovo", "union-only-deepnovo", "union-all-deepnovo" })
            {
                oldSource.SelectedItem = oldSource.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tag);
                source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tag);
                ((TextBox)old.FindName("TextBoxPeaksMinimumAlc")).Text = "81.125";
                ((TextBox)old.FindName("TextBoxDeepNovoMinimumScore")).Text = "77.375";
                model.Filters.PeaksMinimumAlc = "81.125"; model.Filters.DeepNovoMinimumScore = "77.375"; PumpDispatcher();
                object?[] args = { null }; bool before = (bool)old.GetType().GetMethod("TryBuildDeNovoSelections", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(old, args)!;
                bool after = model.Filters.TryBuild(out _, out var selected, out _);
                sourceBefore[tag] = before + "/" + Selection(args[0]!); sourceAfter[tag] = after + "/" + Selection(selected);
            }
            bool selectionsMatch = sourceBefore.SequenceEqual(sourceAfter);
            add(cs, 87, 112, "actual source modes and ordered selections preserve each legacy source identity", sourceBefore, sourceAfter,
                selectionsMatch, "ComputeXicWindow source tags -> DeNovoFilterViewModel.ModeTag/Sources");
            add(cs, 159, 175, "native selection builder keeps source-specific fractional cutoffs without scale conversion", sourceBefore, sourceAfter,
                selectionsMatch, "DeNovoFilterViewModel.TryBuild native source selections");
            var oldPeaks = (TextBox)old.FindName("TextBoxPeaksMinimumAlc"); var oldDeep = (TextBox)old.FindName("TextBoxDeepNovoMinimumScore");
            var invalidBefore = new List<bool>(); var invalidAfter = new List<bool>();
            foreach (string value in new[] { "bad", "-1", "101", "NaN" })
            {
                oldPeaks.Text = value; oldDeep.Text = value; model.Filters.PeaksMinimumAlc = value; model.Filters.DeepNovoMinimumScore = value;
                object?[] args = { null }; invalidBefore.Add(!(bool)old.GetType().GetMethod("TryBuildDeNovoSelections", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(old, args)!);
                invalidAfter.Add(!model.Filters.TryBuild(out _, out _, out _));
            }
            add(cs, 150, 151, "native old/current cutoff builders refuse malformed, out-of-range and NaN scores", invalidBefore, invalidAfter,
                invalidBefore.SequenceEqual(invalidAfter) && invalidAfter.All(value => value), "source-specific confidence refusal; UI copy may be improved");
            model.Filters.PeaksMinimumAlc = "81.125"; model.Filters.DeepNovoMinimumScore = "77.375";
            model.RawDirectory = input; model.SeproPackage = package; model.DeNovoDirectory = ""; model.IsotopeCeiling = 4; model.MinimumPoints = 2;
            var chosen = new List<string>();
            ShellDialogs.FolderPresenter = (_, id, dialog) => { chosen.Add(id); dialog.FolderName = input; return true; };
            ShellDialogs.FilePresenter = (_, id, dialog) => { chosen.Add(id); dialog.FileName = package; return true; };
            Button Browse(int row) => WalkTree(control).OfType<Button>().Single(button => button.Content as string == "Browse..." && Grid.GetRow(button) == row);
            SecondaryClick(Browse(0)); SecondaryClick(Browse(1)); SecondaryClick(Browse(2)); PumpDispatcher();
            bool browse = model.RawDirectory == input && model.SeproPackage == package && model.DeNovoDirectory == input
                && chosen.SequenceEqual(new[] { "quantify.raw", "quantify.sepro", "quantify.denovo" });
            foreach (var mapping in new[] { (41, 227), (51, 238), (61, 253) })
            {
                add(xaml, mapping.Item1, mapping.Item1, "actual Browse Click publishes the selected path through the native dialog seam", true, browse, browse, "QuantifyControl Browse handlers", row => row.Kind == "handler");
                add(cs, mapping.Item2, mapping.Item2, "replacement Browse handler executes and keeps the chosen path", true, browse, browse, "QuantifyControl Browse handlers", row => row.Kind == "handler definition");
            }
            model.DeNovoDirectory = "";
            SecondaryClick(compute); Wait(() => model.IsBusy);
            bool busy = !model.CanEdit && !compute.IsEnabled && !model.ComputeCommand.CanExecute(null) && engineCalls == 1;
            progress!.Report("Native synthetic progress phase"); Wait(() => model.Status.Contains("Native synthetic progress phase", StringComparison.Ordinal));
            add(cs, 313, 315, "actual Compute command enters busy state and preserves the previous result until completion", true, busy, busy,
                "QuantifyControl bound ComputeCommand / IsBusy");
            add(cs, 318, 318, "native engine progress reaches the bound status", "Native synthetic progress phase", model.Status,
                model.Status.Contains("Native synthetic progress phase", StringComparison.Ordinal), "ComputeXicViewModel progress to Status; prefix may be improved");
            var outcome = new FullXicComputeResult { Rows = Enumerable.Range(0, 1050).Select(index => new QuantRow { Side = index < 1030 ? "identified" : "denovo", Run = "native-run", Scan = index + 1 }).ToList(),
                Manifest = new() { IsotopeCount = 4, MinPointsForQuant = 2, PerRunTic = new() { ["native-a"] = 100, ["native-b"] = 200 },
                    DeNovoInputs = new() { new() { SourceId = "native-caller", SourceDisplayName = "Native caller", ConfidenceField = "Native score", MinimumConfidence = 81.125 },
                        new() { SourceId = "native-caller", SourceDisplayName = "Native caller", ConfidenceField = "Native score", MinimumConfidence = 81.125 } } },
                TicShareSummary = new() { "Native primary share" }, Notes = new() { "Native diagnostic detail" }, SpectrumComparisonCsvPath = "native-spectrum.csv",
                SpectrumComparisonRows = new() { new() { FileName = "native.raw", ScanNumber = 7, Charge = 2, PrecursorMz = 500.25 } } };
            outcome.Manifest.SpectrumComparisonRows = outcome.SpectrumComparisonRows;
            pending.SetResult(outcome); Wait(() => model.Computed && !model.IsBusy);
            bool completed = ReferenceEquals(model.Result, outcome) && model.RunSummary.Contains(1050.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                && model.RunSummary.Contains(1030.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                && model.RunSummary.Contains(20.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                && request!.Options.IsotopeCount == 4 && request.Options.MinPointsForQuant == 2 && request.DeNovoDirectory == null;
            add(xaml, 113, 113, "actual Compute event/command executes one captured request and publishes once", true, completed, completed, "ComputeCommand -> ComputeAsync", row => row.Kind == "handler");
            add(cs, 264, 264, "replacement Compute handler executes the actual bound command", true, completed, completed, "ComputeXicViewModel.ComputeAsync", row => row.Kind == "handler definition");
            add(cs, 337, 344, "native result summary and visible basis preserve row/run counts and chosen isotope/point rules", new { Rows = 1050, Identified = 1030, DeNovo = 20, Runs = 2, Isotopes = 4, Points = 2 },
                new { Rows = outcome.Rows.Count, Identified = outcome.IdentifiedRows, DeNovo = outcome.DeNovoRows, Runs = outcome.Manifest.PerRunTic.Count, Isotopes = request!.Options.IsotopeCount, Points = request.Options.MinPointsForQuant },
                completed && model.RunSummary.Contains("2 runs", StringComparison.Ordinal) && model.Basis.Contains("4 isotopes", StringComparison.Ordinal), "Quantify visible RunSummary/Basis; wording may be improved");
            add(cs, 348, 356, "native details preserve share-before-diagnostic order and grouped caller cutoff/file count", true,
                new { PrimaryFirst = model.Details.IndexOf("Native primary share", StringComparison.Ordinal) < model.Details.IndexOf("Native diagnostic detail", StringComparison.Ordinal),
                    Caller = model.Details.Contains("Native caller", StringComparison.Ordinal), Cutoff = model.Details.Contains("81.125", StringComparison.Ordinal), Files = model.Details.Contains("2 files", StringComparison.Ordinal) },
                model.Details.StartsWith("Native primary share", StringComparison.Ordinal) && model.Details.Contains("Native diagnostic detail", StringComparison.Ordinal)
                && model.Details.Contains("Native caller", StringComparison.Ordinal) && model.Details.Contains("81.125", StringComparison.Ordinal) && model.Details.Contains("2 files", StringComparison.Ordinal),
                "ComputeXicViewModel.Details native retained result fields");
            add(cs, 333, 334, "native completed result exposes its spectrum table and automatic CSV path", true,
                new { Table = model.Result!.SpectrumComparisonRows.Count == 1, Csv = model.Details.Contains("native-spectrum.csv", StringComparison.Ordinal) },
                model.Result.SpectrumComparisonRows.Count == 1 && model.Details.Contains("native-spectrum.csv", StringComparison.Ordinal), "Quantify completed spectrum/CSV state");
            string project = Path.Combine(output, "computed-result-" + theme + ".fqproj");
            ProjectStore.WriteFqproj(project, new FamilyClusterFile(), outcome.Rows, outcome.Manifest);
            // Canonical metadata belongs to this authored fixture, before the reopen probe.
            using (var archive = ZipFile.Open(project, ZipArchiveMode.Update))
                foreach (var entry in archive.Entries) entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var reopened = ProjectStore.ReadFqproj(project);
            bool stored = reopened.quant.Count == outcome.Rows.Count && reopened.manifest.IsotopeCount == 4 && reopened.manifest.MinPointsForQuant == 2
                && reopened.manifest.SpectrumComparisonRows.Count == outcome.SpectrumComparisonRows.Count;
            add(cs, 360, 362, "completed native result stays held, directs Save, and survives a real FQPROJ write/read", true,
                new { Held = ReferenceEquals(model.Result, outcome), Save = model.Status.Contains("Save the project", StringComparison.Ordinal), Stored = stored },
                ReferenceEquals(model.Result, outcome) && model.Status.Contains("Save the project", StringComparison.Ordinal) && stored,
                "Quantify result retention -> ProjectStore.WriteFqproj/ReadFqproj");
            execute = (_, _, _) => Task.FromException<FullXicComputeResult>(new IOException("Native synthetic engine refusal."));
            SecondaryClick(compute); Wait(() => !model.IsBusy && model.Status.Contains("Native synthetic engine refusal.", StringComparison.Ordinal));
            add(cs, 373, 376, "actual failed retry preserves prior result and leaves a visible error", true,
                new { Retained = ReferenceEquals(model.Result, outcome), Editable = model.CanEdit, Error = model.Status.Contains("Native synthetic engine refusal.", StringComparison.Ordinal) },
                ReferenceEquals(model.Result, outcome) && model.CanEdit && model.Status.Contains("Native synthetic engine refusal.", StringComparison.Ordinal), "Quantify failure publication guard; wording may be improved");
        }
        finally { ShellDialogs.FilePresenter = savedFile; ShellDialogs.FolderPresenter = savedFolder; host.Close(); old.Close(); }
    }
}
