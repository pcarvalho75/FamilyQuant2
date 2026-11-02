using FamilyQuant.DeNovo;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void SecondaryComputeInteractions(Assembly legacy, string output, Themes.AppTheme theme, SecondaryAdd add)
    {
        const string cs = "FamilyQuant/ComputeXicWindow.xaml.cs", xaml = "FamilyQuant/ComputeXicWindow.xaml";
        Type oldType = legacy.GetType("FamilyQuant.ComputeXicWindow", true)!;
        var old = (Window)Activator.CreateInstance(oldType, new object?[] { "native interaction fixture", null, null })!;
        int engineCalls = 0; var pending = new TaskCompletionSource<FullXicComputeResult>();
        ComputeXicRequest? capturedRequest = null;
        var model = new ComputeXicViewModel("native interaction fixture", compute: (request, _, _) => { engineCalls++; capturedRequest = request; return pending.Task; });
        var control = new QuantifyControl(model); var host = SecondaryHost(new Window { Content = control }); SecondaryHost(old);
        string folder = Path.Combine(output, "interaction-input"); Directory.CreateDirectory(folder);
        string package = Path.Combine(folder, "native.sepr2"); File.WriteAllText(package, "Synthetic marker. Injected engine does not parse it.");
        try
        {
            var compute = (Button)control.FindName("ButtonCompute");
            foreach (var scenario in new[] { ("", "", "", 272, 272), (folder, "", "", 281, 282), (folder, package, "missing-native-de-novo", 292, 292) })
            {
                ((TextBox)old.FindName("TextBoxRawDirectory")).Text = scenario.Item1;
                ((TextBox)old.FindName("TextBoxSeproPackage")).Text = scenario.Item2;
                ((TextBox)old.FindName("TextBoxDeNovoDirectory")).Text = scenario.Item3;
                model.RawDirectory = scenario.Item1; model.SeproPackage = scenario.Item2; model.DeNovoDirectory = scenario.Item3;
                SecondaryClick((Button)old.FindName("ButtonCompute")); SecondaryClick(compute); PumpDispatcher();
                string oldStatus = ((TextBlock)old.FindName("TextBlockStatus")).Text;
                bool refused = !(bool)SecondaryRead(old, "Computed")! && !model.Computed && engineCalls == 0 && oldStatus.Length > 0 && model.Status.Length > 0;
                if (scenario.Item4 == 292) refused &= oldStatus.Contains("clear", StringComparison.Ordinal) && model.Status.Contains("clear", StringComparison.Ordinal);
                add(cs, scenario.Item4, scenario.Item5, "actual Compute refuses missing input before engine entry and shows the input-specific next action",
                    new { EngineCalls = 0, Computed = false, Status = oldStatus }, new { EngineCalls = engineCalls, model.Computed, model.Status }, refused,
                    "QuantifyControl Compute command validation; displayed wording may change");
            }
            foreach (var field in new[] { (40, "ButtonBrowseRaw", 0), (50, "ButtonBrowseSepro", 1), (60, "ButtonBrowseDeNovo", 2) })
            {
                var oldButton = (Button)old.FindName(field.Item2);
                var button = WalkTree(control).OfType<Button>().Single(value => value.Content as string == "Browse..." && Grid.GetRow(value) == field.Item3);
                add(xaml, field.Item1, field.Item1, "native Browse control and caption remain reachable beside the corresponding input", oldButton.Content, button.Content,
                    oldButton.IsVisible && button.IsVisible && Equals(oldButton.Content, button.Content), "QuantifyControl Browse row " + field.Item3,
                    row => row.Kind is "control" or "option/copy");
            }
            foreach (var field in new[] { (35, "RAW folder"), (45, "Identifications (.sepr2)"), (79, "PEAKS minimum ALC (%)"), (84, "DeepNovo minimum SCORE (%)"), (99, "Minimum points per peak"), (65, "De novo source") })
            {
                var oldLabel = WalkTree(old).OfType<Label>().Single(label => label.Content as string == field.Item2);
                var label = WalkTree(control).OfType<Label>().Single(value => value.Content as string == field.Item2);
                add(xaml, field.Item1, field.Item1, "actual visible input label preserves its native source or parameter meaning", oldLabel.Content, label.Content,
                    oldLabel.IsVisible && label.IsVisible && Equals(oldLabel.Content, label.Content), "QuantifyControl / DeNovoFilterControl visible input label");
            }
            var oldSource = (ComboBox)old.FindName("ComboBoxDeNovoSource"); var filter = (FrameworkElement)control.FindName("FilterControl");
            var source = (ComboBox)filter.FindName("ComboBoxDeNovoSource");
            string Describe(object value) => string.Join(";", ((IEnumerable)value).Cast<object>().Select(selection =>
                SecondaryRead(SecondaryRead(selection, "Source")!, "Id") + "/" + Convert.ToString(SecondaryRead(selection, "MinimumConfidence"), CultureInfo.InvariantCulture)));
            var beforeModes = new List<string>(); var modes = new List<string>(); var captions = new List<object>();
            foreach (string tag in new[] { "peaks-only", "peaks-all", "deepnovo", "union-only-deepnovo", "union-all-deepnovo" })
            {
                oldSource.SelectedItem = oldSource.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tag);
                source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tag); PumpDispatcher();
                object?[] arguments = { null }; bool oldBuilt = (bool)oldType.GetMethod("TryBuildDeNovoSelections", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(old, arguments)!;
                bool built = model.Filters.TryBuild(out _, out var selected, out _);
                beforeModes.Add(tag + "/" + oldBuilt + "/" + Describe(arguments[0]!)); modes.Add(tag + "/" + built + "/" + Describe(selected));
                // Selected-item text is captured from the displayed native selector. Source globs removed
                // from this copy are not proved by matching hidden tags or the injected engine request.
                captions.Add(new { Tag = tag, Frozen = ((ComboBoxItem)oldSource.SelectedItem).Content, Current = ((ComboBoxItem)source.SelectedItem).Content });
            }
            add(cs, 156, 158, "native five-way selector keeps PEAKS-only/all and union branches with the same source-specific cutoffs", beforeModes, modes,
                beforeModes.SequenceEqual(modes), "DeNovoFilterViewModel.TryBuild reached through native source selector");
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "source-choice-visible-copy-" + theme + ".json"), captions);
            model.RawDirectory = folder; model.SeproPackage = package; model.DeNovoDirectory = "";
            int published = 0; control.Completed += _ => published++;
            SecondaryClick(compute); PumpDispatcher();
            bool busy = model.IsBusy && !compute.IsEnabled && engineCalls == 1;
            var result = new FullXicComputeResult { Rows = new() { new() { Side = "identified", Run = "native", Scan = 1 } }, Manifest = new() };
            pending.SetResult(result);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (model.IsBusy && watch.Elapsed < TimeSpan.FromSeconds(10)) { PumpDispatcher(); Thread.Sleep(2); }
            bool complete = busy && !model.IsBusy && model.Computed && published == 1 && ReferenceEquals(model.Result, result) && capturedRequest?.DeNovoDirectory == null;
            add(xaml, 112, 112, "real Compute click executes exactly one request and one completed-result publication", true,
                new { BusyBeforeCompletion = busy, EngineCalls = engineCalls, CompletedEvents = published, model.Computed, NoDeNovoFolder = capturedRequest?.DeNovoDirectory == null },
                complete, "QuantifyControl ButtonCompute -> ComputeCommand -> ComputeAsync -> Completed");
            add(xaml, 111, 111, "visible Compute caption names the same full-XIC action and executes its bound command", "Compute / update full XIC", compute.Content,
                complete && compute.Content as string == "Compute full XIC" && compute.IsVisible, "QuantifyControl.ButtonCompute.Content", row => row.Kind == "option/copy");
        }
        finally { host.Close(); old.Close(); }
        SecondaryRestoredCutoffs(legacy, add);
        SecondaryComputeClose(legacy, add);
    }

    private static void SecondaryRestoredCutoffs(Assembly legacy, SecondaryAdd add)
    {
        var manifest = new QuantManifest { DeNovoInputs = new() {
            new() { SourceId = DeNovoSource.DenovoOnly.Id, MinimumConfidence = 81.12345 },
            new() { SourceId = DeNovoSource.DeepNovo.Id, MinimumConfidence = 77.98765 } } };
        object oldManifest = JsonSerializer.Deserialize(JsonSerializer.Serialize(manifest), legacy.GetType("FamilyQuant.QuantManifest", true)!)!;
        var old = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.ComputeXicWindow", true)!, new object?[] { "native restore", oldManifest, null })!;
        var current = new ComputeXicWindow("native restore", manifest); SecondaryHost(old); SecondaryHost(current);
        try
        {
            var before = new[] { ((TextBox)old.FindName("TextBoxPeaksMinimumAlc")).Text, ((TextBox)old.FindName("TextBoxDeepNovoMinimumScore")).Text };
            var actual = new[] { current.ViewModel.Filters.PeaksMinimumAlc, current.ViewModel.Filters.DeepNovoMinimumScore };
            add("FamilyQuant/ComputeXicWindow.xaml.cs", 121, 123, "reopening source-specific saved cutoffs displays the same invariant three-decimal values", before, actual,
                before.SequenceEqual(actual) && actual.SequenceEqual(new[] { "81.123", "77.988" }), "ComputeXicWindow manifest -> DeNovoFilterViewModel restored confidence display");
        }
        finally { current.Close(); old.Close(); }
    }

    private static void SecondaryComputeClose(Assembly legacy, SecondaryAdd add)
    {
        var before = new List<QuantifyCloseReading>(); var actual = new List<QuantifyCloseReading>();
        foreach (bool modal in new[] { false, true })
        {
            var old = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.ComputeXicWindow", true)!, new object?[] { "native close", null, null })!;
            before.Add(ReadQuantifyClose(old, modal));
            actual.Add(ReadQuantifyClose(new ComputeXicWindow("native close"), modal));
        }
        bool safe = before.Concat(actual).All(value => value.Offscreen && !value.TookFocus);
        bool control = safe && before.Concat(actual).All(value => value.HasButton && value.Displayed) && actual.All(value => value.HasStyle);
        bool caption = control && before.Concat(actual).All(value => value.Caption == "Close");
        bool action = caption && before.Concat(actual).All(value => value.Clicks == 1 && value.Closed)
            && before[1].ModalResult == true && actual[1].ModalResult == before[1].ModalResult;
        add("FamilyQuant/ComputeXicWindow.xaml", 137, 137, "displayed native Close control is present in both host modes and uses the current button style", before, actual,
            control, "ComputeXicWindow.ButtonClose", row => row.Kind == "control");
        add("FamilyQuant/ComputeXicWindow.xaml", 137, 137, "displayed native Close caption preserves the frozen action", before, actual,
            caption, "ComputeXicWindow.ButtonClose.Content", row => row.Kind == "option/copy");
        add("FamilyQuant/ComputeXicWindow.xaml", 138, 138, "actual Close click closes nonmodal hosts and returns true to modal callers", before, actual,
            action, "ComputeXicWindow.ButtonClose_Click", row => row.Kind == "handler");
        add("FamilyQuant/ComputeXicWindow.xaml.cs", 397, 397, "frozen and current Close handlers preserve modal true and nonmodal close through actual button clicks", before, actual,
            action, "ComputeXicWindow.ButtonClose_Click; offscreen, no activation or taskbar entry");
    }
}
