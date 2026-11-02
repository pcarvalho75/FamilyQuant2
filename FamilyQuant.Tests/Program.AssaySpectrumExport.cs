using FamilyQuant.DeNovo;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssaySpectrumExportProbeCommand(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply a fresh lane-owned output folder.");
        string output = Path.GetFullPath(args[1]), lane = Path.GetFullPath("D:/FamilyQuant-data/out-assay") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(lane, StringComparison.OrdinalIgnoreCase)) throw new IOException("Fixture exports must remain under out-assay.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output); EnsureApplication();
        var oldCulture = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var oldPresenter = ShellDialogs.FilePresenter;
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var rows = Enumerable.Range(1, 1234).Select(scan => new SpectrumComparisonRow
        { FileName = "synthetic.raw", ScanNumber = scan, Charge = 2, PrecursorMz = 500.25, PeaksDeNovoSequence = "ACDEFGHIK" }).ToArray();
        var scatter = new DeNovoAcceptedScoreScatter();
        foreach (int scan in Enumerable.Range(1, 1234)) scatter.Points.Add(new DeNovoAcceptedScorePoint
        {
            FileName = "synthetic.raw", ScanNumber = scan, Charge = 2, PrecursorMz = 500.25,
            PeaksCleanSequence = "ACDEFGHIK", DeepNovoCleanSequence = scan == 1234 ? "ACDEFGHIL" : "ACDEFGHIK",
            PeaksAnnotatedSequence = "ACDEFGHIK", DeepNovoAnnotatedSequence = scan == 1234 ? "ACDEFGHIL" : "ACDEFGHIK",
            PeaksAlc = 90, DeepNovoScore = 85, StrictSequenceMatch = scan != 1234
        });
        scatter.Validate();
        values["inputs/rows"] = rows.Length.ToString(CultureInfo.InvariantCulture);
        values["inputs/total"] = scatter.Total.ToString(CultureInfo.InvariantCulture);
        values["inputs/matches"] = scatter.Matches.ToString(CultureInfo.InvariantCulture);
        values["inputs/mismatches"] = scatter.Mismatches.ToString(CultureInfo.InvariantCulture);
        var window = SpectrumComparisonWindow.Create(rows, Path.Combine(output, "previous.csv"), scatter);
        AssayViewSnapshot.ConfigureOffscreen(window);
        IEnumerable<DependencyObject> Children(DependencyObject root)
        {
            yield return root;
            foreach (object child in LogicalTreeHelper.GetChildren(root))
                if (child is DependencyObject dependency) foreach (var nested in Children(dependency)) yield return nested;
        }
        try
        {
            window.Show(); PumpDispatcher();
            var nodes = Children(window).ToArray();
            var blocks = nodes.OfType<TextBlock>().ToArray();
            foreach (string kind in new[] { "csv", "plot" })
            foreach (string mode in new[] { "dialog-cancel", "success", "failure", "operation-cancel" })
            {
                string test = kind + "/" + mode;
                var button = nodes.OfType<Button>().Single(item => item.Name == (kind == "csv" ? "ButtonSaveSpectrumComparisonCsv" : "ButtonSaveAcceptedScoreScatter"));
                var host = AppOperationHost.For(button);
                var connected = Stopwatch.StartNew();
                while (PresentationSource.FromVisual(button) == null && connected.Elapsed < TimeSpan.FromSeconds(5))
                { window.UpdateLayout(); PumpDispatcher(); Thread.Sleep(5); }
                if (PresentationSource.FromVisual(button) == null) throw new InvalidDataException("Owned off-screen fixture did not connect its button to a presentation source.");
                string folder = Path.Combine(output, kind + "-" + mode); Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, "fixture" + (kind == "csv" ? ".csv" : ".png"));
                if (mode == "failure")
                {
                    string blocker = Path.Combine(folder, "blocked"); File.WriteAllText(blocker, "fixture blocker");
                    target = Path.Combine(blocker, "fixture" + (kind == "csv" ? ".csv" : ".png"));
                }
                byte[] previous = Encoding.UTF8.GetBytes("previous fixture bytes");
                if (mode is "dialog-cancel" or "operation-cancel") File.WriteAllBytes(target, previous);
                int dialogs = 0; bool cancelled = false;
                ShellDialogs.FilePresenter = (owner, id, dialog) =>
                {
                    dialogs++; values[test + "/owner"] = ReferenceEquals(owner, window).ToString();
                    values[test + "/id"] = id; values[test + "/title"] = dialog.Title;
                    values[test + "/filter"] = dialog.Filter; values[test + "/defaultExt"] = dialog.DefaultExt;
                    values[test + "/suggested"] = dialog.FileName; values[test + "/addExtension"] = dialog.AddExtension.ToString();
                    values[test + "/selected"] = target.Replace(output, "<output>", StringComparison.OrdinalIgnoreCase);
                    dialog.FileName = target; return mode != "dialog-cancel";
                };
                PropertyChangedEventHandler cancel = (_, _) =>
                { if (mode == "operation-cancel" && host.IsBusy && !cancelled) { cancelled = true; host.Cancel(); } };
                host.PropertyChanged += cancel;
                var before = blocks.ToDictionary(block => block, block => block.Text);
                var context = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                }
                finally { SynchronizationContext.SetSynchronizationContext(context); }
                var watch = Stopwatch.StartNew();
                try
                {
                    while (host.IsBusy && watch.Elapsed < TimeSpan.FromSeconds(45)) { PumpDispatcher(); Thread.Sleep(5); }
                    if (host.IsBusy) throw new TimeoutException("Owned spectrum fixture export did not finish.");
                    PumpDispatcher();
                }
                finally { host.PropertyChanged -= cancel; }
                var changed = blocks.Where(block => before[block] != block.Text).ToArray();
                string status = changed.Length == 1 ? changed[0].Text : changed.Length == 0 ? "" : throw new InvalidDataException("Ambiguous spectrum status update.");
                values[test + "/status"] = status.Replace(output, "<output>", StringComparison.OrdinalIgnoreCase);
                Check("spectrum fixture " + test + " uses one owned native dialog configuration", dialogs == 1 && values[test + "/owner"] == "True");
                if (mode is "dialog-cancel" or "operation-cancel")
                    Check("spectrum fixture " + test + " preserves previous bytes", File.ReadAllBytes(target).SequenceEqual(previous)
                        && (mode == "dialog-cancel" ? status.Length == 0 : cancelled && status.StartsWith("Cancelled.", StringComparison.Ordinal)));
                else if (mode == "failure")
                    Check("spectrum fixture " + test + " reports the real write failure", !File.Exists(target)
                        && status.StartsWith(kind == "csv" ? "The CSV could not be written" : "The accepted-call score plot was not saved", StringComparison.Ordinal));
                else if (kind == "csv")
                    Check("spectrum actual routed CSV writes the native table bytes", File.ReadAllText(target) == SpectrumComparisonTable.ToCsv(rows)
                        && status.Contains("1,234 spectrum row(s)", StringComparison.Ordinal));
                else
                {
                    var image = BitmapFrame.Create(new Uri(target), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    string companion = Directory.GetFiles(folder, "*.csv").Single();
                    values[test + "/companion"] = companion.Replace(output, "<output>", StringComparison.OrdinalIgnoreCase);
                    Check("spectrum actual routed plot writes 1920x1080 and its exact native CSV", image.PixelWidth == 1920 && image.PixelHeight == 1080
                        && File.ReadAllText(Path.ChangeExtension(target, ".csv")) == DeNovoAcceptedScoreScatterStudy.ToCsv(scatter)
                        && status.Contains("1,234 jointly accepted", StringComparison.Ordinal) && status.Contains("1,233 strict matches", StringComparison.Ordinal));
                }
            }
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "export-actuals.json"), values);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }
        finally { window.Close(); ShellDialogs.FilePresenter = oldPresenter; CultureInfo.CurrentCulture = oldCulture; }
    }
}
