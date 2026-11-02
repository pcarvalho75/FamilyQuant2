using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record SecondaryDialogValue(string Role, string Kind, string Title, string Filter, bool Accepted, string Selected);

    private static void SecondaryDialogScreenRuntime(Assembly legacy, string output, Themes.AppTheme theme)
    {
        const string source = "FamilyQuant/ComputeXicWindow.xaml.cs";
        Type oldType = legacy.GetType("FamilyQuant.ComputeXicWindow", true)!;
        Window Frozen() => (Window)Activator.CreateInstance(oldType, new object?[] { "native dialog screen", null, null })!;
        var old = Frozen(); int calls = 0;
        var model = new ComputeXicViewModel("native dialog screen", compute: (_, _, _) => { calls++; return Task.FromResult(new FullXicComputeResult()); });
        var control = new QuantifyControl(model); var host = SecondaryHost(new Window { Content = control }); SecondaryHost(old);
        string folder = Path.Combine(output, "dialog-input"); Directory.CreateDirectory(folder);
        string package = Path.Combine(folder, "native.sepr2"); File.WriteAllText(package, "Synthetic marker. Refusals never parse this file.");
        var savedFile = ShellDialogs.FilePresenter; var savedFolder = ShellDialogs.FolderPresenter;
        var trace = new List<SecondaryInjection>(); var before = new List<SecondaryDialogValue>(); var actual = new List<SecondaryDialogValue>();
        var changed = new Dictionary<int, string>();
        try
        {
            Button Browse(QuantifyControl screen, int row) => WalkTree(screen).OfType<Button>().Single(button => button.Content as string == "Browse..." && Grid.GetRow(button) == row);
            string Input(bool frozen, int row) => frozen ? ((TextBox)old.FindName(new[] { "TextBoxRawDirectory", "TextBoxSeproPackage", "TextBoxDeNovoDirectory" }[row])).Text
                : new[] { model.RawDirectory, model.SeproPackage, model.DeNovoDirectory }[row];
            bool cancelKept = true;
            foreach (var spec in new[] { (0, "raw", "ButtonBrowseRaw_Click", 232), (1, "sepro", "ButtonBrowseSepro_Click", 246), (2, "denovo", "ButtonBrowseDeNovo_Click", 258) })
            {
                var copy = SecondaryInject(oldType.GetMethod(spec.Item3, BindingFlags.Instance | BindingFlags.NonPublic)!,
                    new Dictionary<MethodBase, MethodInfo> { [typeof(CommonDialog).GetMethod("ShowDialog", Type.EmptyTypes)!] = SecondaryBoundary(nameof(SecondaryFileBoundary)) }, trace);
                foreach (bool frozen in new[] { true, false })
                foreach (bool accept in new[] { true, false })
                {
                    SecondaryDialogValue? value = null;
                    bool? Choose(CommonDialog dialog)
                    {
                        if (dialog is FileDialog file) file.FileName = package; else ((OpenFolderDialog)dialog).FolderName = folder;
                        value = new(spec.Item2, dialog.GetType().Name, dialog is FileDialog f ? f.Title : ((OpenFolderDialog)dialog).Title,
                            dialog is FileDialog fd ? fd.Filter : "", accept, "");
                        return accept;
                    }
                    secondaryFrozenChooser = Choose;
                    ShellDialogs.FilePresenter = (_, id, dialog) => id == "quantify.sepro" ? Choose(dialog) : throw new InvalidOperationException("Unexpected file route.");
                    ShellDialogs.FolderPresenter = (_, id, dialog) => id == "quantify." + spec.Item2 ? Choose(dialog) : throw new InvalidOperationException("Unexpected folder route.");
                    string previous = Input(frozen, spec.Item1);
                    if (frozen) copy.Invoke(null, new object[] { old, old, new RoutedEventArgs() }); else SecondaryClick(Browse(control, spec.Item1));
                    PumpDispatcher();
                    if (value == null) throw new InvalidOperationException("The injected native chooser was not reached.");
                    string selected = Input(frozen, spec.Item1); cancelKept &= accept || selected == previous;
                    (frozen ? before : actual).Add(value with { Selected = Path.GetRelativePath(output, selected) });
                    if (!frozen) changed[spec.Item4] = value.Title;
                    if (!frozen && spec.Item2 == "sepro") changed[245] = value.Filter;
                }
            }
            bool samePaths = before.Select(value => (value.Role, value.Kind, value.Accepted, value.Selected))
                .SequenceEqual(actual.Select(value => (value.Role, value.Kind, value.Accepted, value.Selected)));
            string[] Patterns(IEnumerable<SecondaryDialogValue> values) => values.Where(value => value.Role == "sepro").Select(value => value.Filter.Split('|')[1]).ToArray();
            bool safe = !old.IsActive && !host.IsActive && old.Left == -30000 && host.Left == -30000 && !old.ShowActivated && !host.ShowActivated;
            Check("Quantify dialog screen: all three native chooser kinds, extension patterns, accepted inputs and cancelled inputs are preserved", safe && samePaths && cancelKept
                && actual.Count == 6 && Patterns(before).SequenceEqual(Patterns(actual)) && Patterns(actual).All(value => value == "*.sepr2"));

            var detachedOld = Frozen(); var detachedModel = new ComputeXicViewModel("disconnected dialog screen"); var detached = new QuantifyControl(detachedModel);
            try
            {
                int nativeCalls = 0; bool refused = true;
                secondaryFrozenChooser = _ => { nativeCalls++; return false; };
                ShellDialogs.FilePresenter = (_, _, _) => { nativeCalls++; return false; };
                ShellDialogs.FolderPresenter = (_, _, _) => { nativeCalls++; return false; };
                foreach (var spec in new[] { (0, "ButtonBrowseRaw", 229, 230), (1, "ButtonBrowseSepro", 240, 241), (2, "ButtonBrowseDeNovo", 255, 256) })
                {
                    SecondaryClick((Button)detachedOld.FindName(spec.Item2)); SecondaryClick(Browse(detached, spec.Item1));
                    refused &= ((TextBlock)detachedOld.FindName("TextBlockStatus")).Text.Length > 0 && detachedModel.Status.Length > 0;
                    changed[spec.Item3] = changed[spec.Item4] = detachedModel.Status;
                }
                changed[222] = changed[223] = detachedModel.Status;
                var entry = (TextBox)detached.FindName("TextBoxRawDirectory"); PumpDispatcher();
                entry.AppendText(folder);
                BindingOperations.GetBindingExpression(entry, TextBox.TextProperty)!.UpdateSource(); PumpDispatcher();
                Check("Quantify dialog screen: disconnected Browse actions open no native chooser, report refusal and retain manual path entry", nativeCalls == 0 && refused && detachedModel.RawDirectory == folder,
                    System.Text.Json.JsonSerializer.Serialize(new { NativeCalls = nativeCalls, Refused = refused, ModelPath = detachedModel.RawDirectory, Entry = entry.Text }));
            }
            finally { detachedOld.Close(); }

            var oldChoice = (ComboBox)old.FindName("ComboBoxDeNovoSource"); var filters = (FrameworkElement)control.FindName("FilterControl");
            var choice = (ComboBox)filters.FindName("ComboBoxDeNovoSource");
            var xmlWords = new Dictionary<int, string>(); int index = 71;
            foreach (var item in choice.Items.OfType<ComboBoxItem>()) xmlWords[index++] = item.Content?.ToString() ?? "";
            xmlWords[55] = WalkTree(control).OfType<Label>().Single(label => label.Content as string == "De novo folder (optional)").Content.ToString()!;
            xmlWords[93] = WalkTree(control).OfType<Label>().Single(label => label.Content as string == "Isotope ceiling").Content.ToString()!;
            SecondaryWording(output, "compute-xaml-" + theme, "FamilyQuant/ComputeXicWindow.xaml", xmlWords);
            model.DeNovoDirectory = ""; ((TextBox)old.FindName("TextBoxDeNovoDirectory")).Text = "";
            var errors = new List<string>(); bool cutoffRefused = true;
            foreach (string tag in new[] { "peaks-only", "deepnovo" })
            {
                oldChoice.SelectedItem = oldChoice.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tag);
                choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tag);
                foreach (string invalid in new[] { "invalid-native-cutoff", "-1", "101", "NaN" })
                {
                    ((TextBox)old.FindName("TextBoxPeaksMinimumAlc")).Text = invalid; ((TextBox)old.FindName("TextBoxDeepNovoMinimumScore")).Text = invalid;
                    model.Filters.PeaksMinimumAlc = invalid; model.Filters.DeepNovoMinimumScore = invalid; PumpDispatcher();
                    SecondaryClick((Button)old.FindName("ButtonCompute")); SecondaryClick((Button)control.FindName("ButtonCompute")); PumpDispatcher();
                    cutoffRefused &= calls == 0 && !(bool)SecondaryRead(old, "Computed")! && !model.Computed
                        && ((TextBlock)old.FindName("TextBlockStatus")).Text.Length > 0 && model.Status.Length > 0;
                    errors.Add(model.Status);
                }
            }
            Check("Quantify dialog screen: both caller cutoffs refuse malformed, negative, above-range and NaN values before engine entry", cutoffRefused);
            changed[149] = string.Join(" | ", errors.Distinct(StringComparer.Ordinal));
            string missing = Path.Combine(folder, "missing-native.sepr2"); ((TextBox)old.FindName("TextBoxSeproPackage")).Text = missing; model.SeproPackage = missing; PumpDispatcher();
            SecondaryClick((Button)old.FindName("ButtonCompute")); SecondaryClick((Button)control.FindName("ButtonCompute")); PumpDispatcher();
            Check("Quantify dialog screen: missing SEPro input is refused before engine entry and its typed path is retained", !File.Exists(missing) && calls == 0
                && !model.Computed && !(bool)SecondaryRead(old, "Computed")! && ((TextBox)control.FindName("TextBoxSeproPackage")).Text == missing);
            changed[283] = changed[284] = model.Status;
            SecondaryWording(output, "compute-cs-" + theme, source, changed);
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "dialog-screen-" + theme + ".json"), new { Frozen = before, Current = actual, CutoffErrors = errors, EngineCalls = calls, Injections = trace });
        }
        finally { secondaryFrozenChooser = null; ShellDialogs.FilePresenter = savedFile; ShellDialogs.FolderPresenter = savedFolder; host.Close(); old.Close(); }
    }
}
