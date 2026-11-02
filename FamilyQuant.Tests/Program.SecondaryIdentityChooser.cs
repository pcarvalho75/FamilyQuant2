using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static Func<CommonDialog, bool?>? secondaryFrozenChooser;
    private static bool? SecondaryFileBoundary(CommonDialog dialog) => secondaryFrozenChooser!(dialog);

    private static void SecondaryIdentityChooser(Assembly legacy, string output, Themes.AppTheme theme, SecondaryAdd add)
    {
        const string source = "FamilyQuant/IdentityScanWindow.cs";
        var rows = new List<IdentityScanRow> { new() { Identity = 90, Groups = 20, TargetGroups = 18, MixedGroups = 0 },
            new() { Identity = 70, Groups = 10, TargetGroups = 8, MixedGroups = 1 } };
        object oldRows = JsonSerializer.Deserialize(JsonSerializer.Serialize(rows), typeof(List<>).MakeGenericType(legacy.GetType("FamilyQuant.IdentityScanRow", true)!))!;
        Type oldType = legacy.GetType("FamilyQuant.IdentityScanWindow", true)!;
        var old = (Window)SecondaryCall(null, oldType, "Create", "Native chooser contract", oldRows, "Native chooser report", 70)!;
        var current = IdentityScanWindow.Create("Native chooser contract", rows, "Native chooser report", 70);
        SecondaryHost(old); SecondaryHost(current);
        var saved = ShellDialogs.FilePresenter;
        var savedContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var trace = new List<SecondaryInjection>();
        try
        {
            var method = oldType.GetMethod("SaveFigureFromDialog", BindingFlags.Static | BindingFlags.NonPublic)!;
            MethodInfo chooser = typeof(CommonDialog).GetMethod("ShowDialog", Type.EmptyTypes)!;
            var frozenSave = SecondaryInject(method, new Dictionary<MethodBase, MethodInfo> { [chooser] = SecondaryBoundary(nameof(SecondaryFileBoundary)) }, trace);
            var oldButton = WalkTree(old).OfType<Button>().Single(button => button.Content as string == "Save figure (PNG)");
            var button = WalkTree(current).OfType<Button>().Single(value => value.Content as string == "Save figure (PNG)");
            var oldStatus = WalkTree(old).OfType<TextBlock>().Single(block => block.Text == "");
            var status = WalkTree(current).OfType<TextBlock>().Single(block => block.Text == "");
            var before = new List<string>(); var actual = new List<string>();
            string Describe(FileDialog dialog) => JsonSerializer.Serialize(new { dialog.Filter, dialog.Title,
                FileName = Path.GetFileName(dialog.FileName), dialog.AddExtension, dialog.DefaultExt });
            string folder = Path.Combine(output, "chooser-" + theme); Directory.CreateDirectory(folder);
            foreach (bool frozen in new[] { true, false })
            {
                var captured = frozen ? before : actual;
                var activeStatus = frozen ? oldStatus : status;
                foreach (string scenario in new[] { "cancel", "success", "error" })
                {
                    string destination = Path.Combine(folder, scenario + ".png");
                    string blocker = Path.Combine(folder, "blocked-parent"); File.WriteAllText(blocker, "A native file, not a directory.");
                    if (scenario == "error") destination = Path.Combine(blocker, "error.png");
                    if (scenario == "cancel") File.WriteAllText(destination, "Existing destination must survive cancellation.");
                    int shown = 0; string configuration = "";
                    bool? Choose(FileDialog dialog)
                    { shown++; configuration = Describe(dialog); dialog.FileName = destination; return scenario != "cancel"; }
                    secondaryFrozenChooser = dialog => Choose((FileDialog)dialog);
                    ShellDialogs.FilePresenter = (_, id, dialog) => id == "identity.figure" ? Choose(dialog) : throw new InvalidOperationException("Unexpected dialog route: " + id);
                    string priorStatus = activeStatus.Text;
                    if (frozen) frozenSave.Invoke(null, new object?[] { oldButton, oldStatus, oldRows, 70 });
                    else SecondaryClick(button);
                    var watch = Stopwatch.StartNew();
                    while (scenario != "cancel" && !activeStatus.Text.StartsWith(scenario == "success" ? "Written to " : "The figure could not be written", StringComparison.Ordinal)
                        && watch.Elapsed < TimeSpan.FromSeconds(15)) { PumpDispatcher(); Thread.Sleep(2); }
                    if (scenario != "cancel" && watch.Elapsed >= TimeSpan.FromSeconds(15)) throw new TimeoutException("The identity figure operation did not finish.");
                    bool unchanged = scenario != "cancel" || File.ReadAllText(destination) == "Existing destination must survive cancellation." && activeStatus.Text == priorStatus;
                    bool exported = false;
                    if (scenario == "success")
                    {
                        var bitmap = BitmapFrame.Create(new Uri(destination), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        exported = bitmap.PixelWidth == 1920 && bitmap.PixelHeight == 1080;
                        File.Copy(destination, Path.Combine(folder, frozen ? "frozen-success.png" : "current-success.png"));
                    }
                    bool error = scenario != "error" || !File.Exists(destination) && activeStatus.Text.Contains("check that you can write to that folder", StringComparison.Ordinal)
                        && !Directory.EnumerateFileSystemEntries(folder).Any(path => Path.GetFileName(path).StartsWith(".fq-export-", StringComparison.Ordinal));
                    string normalized = activeStatus.Text.Replace(blocker, "<blocked-parent>", StringComparison.Ordinal);
                    captured.Add(JsonSerializer.Serialize(new { Scenario = scenario, Shown = shown, Configuration = configuration,
                        DestinationPreserved = unchanged, Png1920x1080 = exported, ErrorIsVisibleAndNoPartial = error, Status = normalized }));
                }
            }
            bool same = before.SequenceEqual(actual) && actual.Count == 3;
            using var cancel = JsonDocument.Parse(actual[0]); using var success = JsonDocument.Parse(actual[1]); using var errorCase = JsonDocument.Parse(actual[2]);
            bool contract = same && cancel.RootElement.GetProperty("Shown").GetInt32() == 1 && cancel.RootElement.GetProperty("DestinationPreserved").GetBoolean()
                && success.RootElement.GetProperty("Png1920x1080").GetBoolean() && errorCase.RootElement.GetProperty("ErrorIsVisibleAndNoPartial").GetBoolean();
            add(source, 175, 177, "displayed identity chooser preserves PNG filter, title, default name/extension and single cancel/success/error routing", before, actual,
                contract, "IdentityScanWindow Save click / frozen compiled SaveFigureFromDialog with ShowDialog boundary injected");
            add(source, 187, 192, "accepted identity Save writes a native 1920x1080 PNG and displays the same basename, N0 dimensions and unchanged-screen confirmation", before[1], actual[1],
                contract, "IdentityScanWindow real Save click -> AppOperationFiles -> SaveFigure -> visible status");
            add(source, 196, 198, "failed identity export displays reason and recovery advice, writes no destination and cleans staging files", before[2], actual[2],
                contract, "IdentityScanWindow real Save click; existing file blocks destination directory");
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "identity-chooser-injections-" + theme + ".json"), trace);
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "identity-chooser-values-" + theme + ".json"), new { Frozen = before, Current = actual });
        }
        finally { ShellDialogs.FilePresenter = saved; secondaryFrozenChooser = null; current.Close(); old.Close(); SynchronizationContext.SetSynchronizationContext(savedContext); }
    }
}
