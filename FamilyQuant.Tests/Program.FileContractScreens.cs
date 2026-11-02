using FamilyQuant.DeNovo;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record FileContractDialog(string Scenario, string Filter, string FileName, bool AddExtension,
        string DefaultExt, bool DestinationPreserved, bool FileWritten);

    private static void FileContractScreens(Assembly legacy, string output, List<ThreeWayPartitionRow> rows,
        IList oldRows, object oldDeNovo, DeNovoHomology homology, Action<string, string, string> compare)
    {
        var injections = new List<SecondaryInjection>();
        var savedPresenter = ShellDialogs.FilePresenter;
        var savedContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Type partitionType = legacy.GetType("FamilyQuant.DeNovo.ThreeWayWindow", true)!;
        var oldWindow = (Window)partitionType.GetMethod("Create")!.Invoke(null, new object?[] { "Native partition export", oldRows, null })!;
        var window = ThreeWayWindow.Create("Native partition export", rows);
        var oldDeNovoWindow = new Window { Content = oldDeNovo };
        var results = new AnalyticsControl(null, null, null, null, homology);
        var resultsWindow = new Window { Content = results };
        var captures = new Dictionary<string, List<FileContractDialog>>(StringComparer.Ordinal);
        try
        {
            foreach (var host in new[] { oldWindow, window, oldDeNovoWindow, resultsWindow }) SecondaryHost(host);
            var oldPartitionButton = WalkTree(oldWindow).OfType<Button>().Single(button => button.Content as string == "Save CSV...");
            var partitionButton = WalkTree(window).OfType<Button>().Single(button => button.Content as string == "Save CSV...");
            MethodInfo chooser = typeof(CommonDialog).GetMethod("ShowDialog", Type.EmptyTypes)!;
            var oldSave = partitionType.GetMethod("SaveCsv", BindingFlags.Static | BindingFlags.NonPublic)!;
            var injectedSave = SecondaryInject(oldSave, new Dictionary<MethodBase, MethodInfo> {
                [chooser] = SecondaryBoundary(nameof(SecondaryFileBoundary)) }, injections);
            var oldExport = oldDeNovo.GetType().GetMethod("Export", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var injectedExport = SecondaryInject(oldExport, new Dictionary<MethodBase, MethodInfo> {
                [chooser] = SecondaryBoundary(nameof(SecondaryFileBoundary)) }, injections);
            var oldHomologyClick = oldDeNovo.GetType().GetMethod("ButtonExportHomologyCsv_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var injectedHomologyClick = SecondaryInject(oldHomologyClick, new Dictionary<MethodBase, MethodInfo> {
                [oldExport] = injectedExport }, injections);

            foreach (string group in new[] { "partition-screen", "homology-screen" })
            {
                foreach (bool frozen in new[] { true, false })
                {
                    var captured = new List<FileContractDialog>(); captures[group + (frozen ? "/frozen" : "/current")] = captured;
                    foreach (bool accept in new[] { false, true })
                    {
                        string path = Path.Combine(output, group + (frozen ? "-frozen.csv" : "-current.csv"));
                        const string marker = "Existing destination must survive cancellation.";
                        File.WriteAllText(path, marker);
                        FileDialog? observed = null;
                        FileContractDialog? value = null;
                        bool? Choose(CommonDialog dialog)
                        {
                            observed = (FileDialog)dialog;
                            value = new(accept ? "accept" : "cancel", observed.Filter, Path.GetFileName(observed.FileName),
                                observed.AddExtension, observed.DefaultExt, false, false);
                            observed.FileName = path;
                            return accept;
                        }
                        secondaryFrozenChooser = Choose;
                        ShellDialogs.FilePresenter = (_, id, dialog) => id == (group == "partition-screen" ? "partition.csv" : "results.Homology")
                            ? Choose(dialog) : throw new InvalidOperationException("Unexpected export route: " + id);
                        if (group == "partition-screen")
                        {
                            if (frozen) FileContractInjectedClick(oldPartitionButton, oldSave, injectedSave, injections);
                            else SecondaryClick(partitionButton);
                        }
                        else if (frozen) injectedHomologyClick.Invoke(null, new[] { oldDeNovo, oldDeNovo, new RoutedEventArgs() });
                        else SecondaryClick((Button)results.FindName("ButtonExport"));
                        if (observed == null || value == null) throw new InvalidOperationException("The native export chooser was not reached.");
                        if (accept && !frozen && group == "homology-screen")
                        {
                            var watch = Stopwatch.StartNew();
                            while (results.ViewModel.Status.Length == 0 && watch.Elapsed < TimeSpan.FromSeconds(15)) { PumpDispatcher(); Thread.Sleep(2); }
                            if (results.ViewModel.Status.Length == 0) throw new TimeoutException("The native homology export did not finish.");
                        }
                        bool preserved = File.ReadAllText(path) == marker;
                        captured.Add(value with { DestinationPreserved = !accept && preserved, FileWritten = accept && !preserved });
                    }
                }
                var before = captures[group + "/frozen"]; var current = captures[group + "/current"];
                bool equalConfiguration = before.Select(value => (value.Filter, value.FileName)).SequenceEqual(current.Select(value => (value.Filter, value.FileName)));
                bool values = before.Concat(current).All(value => value.Scenario == "cancel" ? value.DestinationPreserved : value.FileWritten);
                bool placement = new[] { oldWindow, window, oldDeNovoWindow, resultsWindow }.All(host => HasProofWindowPlacement(host) && !host.IsActive);
                Check("native " + group + ": chooser filename/filter, cancelled destination and accepted file route agree " + System.Globalization.CultureInfo.CurrentCulture.Name,
                    equalConfiguration && values && placement && (group != "partition-screen" || before.SequenceEqual(current)));
                compare(group, Path.Combine(output, group + "-frozen.csv"), Path.Combine(output, group + "-current.csv"));
            }
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "screen-dialogs.json"), captures);
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "screen-injections.json"), injections);
        }
        finally
        {
            secondaryFrozenChooser = null; ShellDialogs.FilePresenter = savedPresenter;
            foreach (var host in new[] { oldWindow, window, oldDeNovoWindow, resultsWindow }) host.Close();
            SynchronizationContext.SetSynchronizationContext(savedContext);
        }
    }

    private static void FileContractInjectedClick(Button button, MethodInfo called, DynamicMethod replacement, List<SecondaryInjection> trace)
    {
        object store = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(button)!;
        var handlers = (Array)store.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(store, new object[] { Button.ClickEvent })!;
        var original = handlers.Cast<object>().Select(info => (Delegate)info.GetType().GetProperty("Handler")!.GetValue(info)!).Single();
        var injected = SecondaryInject(original.Method, new Dictionary<MethodBase, MethodInfo> { [called] = replacement }, trace);
        var handler = (RoutedEventHandler)injected.CreateDelegate(typeof(RoutedEventHandler), original.Target);
        button.RemoveHandler(Button.ClickEvent, original); button.AddHandler(Button.ClickEvent, handler);
        try { SecondaryClick(button); PumpDispatcher(); }
        finally { button.RemoveHandler(Button.ClickEvent, handler); button.AddHandler(Button.ClickEvent, original); }
    }
}
