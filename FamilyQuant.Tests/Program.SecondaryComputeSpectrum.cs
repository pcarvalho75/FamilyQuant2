using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Label = System.Windows.Controls.Label;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static readonly List<Window> SecondarySpectrumShown = new();
    private static void SecondarySpectrumPresentation(Window window) { SecondaryHost(window); SecondarySpectrumShown.Add(window); }

    private static void SecondaryComputeSpectrumRuntime(Assembly legacy, string output, Themes.AppTheme theme, SecondaryAdd add)
    {
        const string cs = "FamilyQuant/ComputeXicWindow.xaml.cs", xaml = "FamilyQuant/ComputeXicWindow.xaml";
        var old = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.ComputeXicWindow", true)!, new object?[] { "native spectrum link", null, null })!;
        string input = Path.Combine(output, "spectrum-link-input"); Directory.CreateDirectory(input);
        string package = Path.Combine(input, "native.sepr2"); File.WriteAllText(package, "Synthetic marker; injected engine does not parse it.");
        var rows = new List<SpectrumComparisonRow> { new() { FileName = "native.raw", ScanNumber = 17, Charge = 2, PrecursorMz = 500.25 } };
        var result = new FullXicComputeResult { Rows = new(), Manifest = new(), SpectrumComparisonRows = rows, SpectrumComparisonCsvPath = "native-spectrum.csv" };
        var model = new ComputeXicViewModel("native spectrum link", compute: (_, _, _) => Task.FromResult(result)) { RawDirectory = input, SeproPackage = package };
        var control = new QuantifyControl(model); var host = SecondaryHost(new Window { Content = control }); SecondaryHost(old);
        var oldButton = (Button)old.FindName("ButtonViewSpectrumComparison"); var button = (Button)control.FindName("ButtonViewSpectrumComparison");
        var trace = new List<SecondaryInjection>();
        try
        {
            var oldShow = legacy.GetType("FamilyQuant.SpectrumComparisonWindow", true)!.GetMethod("Show")!;
            var currentShow = typeof(SpectrumComparisonWindow).GetMethod("Show")!;
            var boundary = new Dictionary<MethodBase, MethodInfo> { [typeof(Window).GetMethod("Show", Type.EmptyTypes)!] = SecondaryBoundary(nameof(SecondarySpectrumPresentation)) };
            var oldPresentation = SecondaryInject(oldShow, boundary, trace); var presentation = SecondaryInject(currentShow, boundary, trace);
            var oldMethod = old.GetType().GetMethod("ButtonViewSpectrumComparison_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var method = typeof(QuantifyControl).GetMethod("ViewSpectrum", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var oldClick = SecondaryInject(oldMethod, new Dictionary<MethodBase, MethodInfo> { [oldShow] = oldPresentation }, trace);
            var click = SecondaryInject(method, new Dictionary<MethodBase, MethodInfo> { [currentShow] = presentation }, trace);
            void Click(Button target, MethodInfo originalMethod, DynamicMethod injected)
            {
                object store = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
                var handlers = (Array)store.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(store, new object[] { Button.ClickEvent })!;
                var original = handlers.Cast<object>().Select(info => (Delegate)info.GetType().GetProperty("Handler")!.GetValue(info)!).Single(value => value.Method == originalMethod);
                var replacement = (RoutedEventHandler)injected.CreateDelegate(typeof(RoutedEventHandler), original.Target);
                target.RemoveHandler(Button.ClickEvent, original); target.AddHandler(Button.ClickEvent, replacement);
                try { SecondaryClick(target); PumpDispatcher(); }
                finally { target.RemoveHandler(Button.ClickEvent, replacement); target.AddHandler(Button.ClickEvent, original); }
            }
            SecondarySpectrumShown.Clear();
            bool initiallyDisabled = oldButton.IsVisible && button.IsVisible && !oldButton.IsEnabled && !button.IsEnabled;
            string initial = model.Status;
            var copyWords = new Dictionary<int, string>();
            for (int line = 81; line <= 84; line++) copyWords[line] = initial;
            for (int line = 202; line <= 212; line++) copyWords[line] = model.Basis + " " + model.BasisDetails;
            SecondaryWording(output, "compute-basis-" + theme, cs, copyWords, includeUnchanged: true);
            string Tip(FrameworkElement element) => element.ToolTip?.ToString() ?? "[No tooltip on this current control]";
            var xmlWords = new Dictionary<int, string> {
                [39] = Tip((FrameworkElement)control.FindName("TextBoxRawDirectory")), [49] = Tip((FrameworkElement)control.FindName("TextBoxSeproPackage")),
                [59] = Tip((FrameworkElement)control.FindName("TextBoxDeNovoDirectory")), [113] = Tip((FrameworkElement)control.FindName("ButtonCompute")),
                [122] = Tip(button), [97] = Tip((FrameworkElement)control.FindName("IntegerUpDownIsotopes")), [103] = Tip((FrameworkElement)control.FindName("IntegerUpDownMinPoints")),
                [109] = model.BasisDetails, [133] = "[No status tooltip; current status line: " + initial + "]" };
            var filters = (FrameworkElement)control.FindName("FilterControl");
            xmlWords[70] = Tip((FrameworkElement)filters.FindName("ComboBoxDeNovoSource"));
            foreach (var confidence in new[] { (83, "PeaksMinimumAlc"), (88, "DeepNovoMinimumScore") })
                xmlWords[confidence.Item1] = Tip(WalkTree(filters).OfType<TextBox>().Single(box => BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path?.Path == confidence.Item2));
            foreach (var browse in new[] { (42, 0), (52, 1), (62, 2) })
                xmlWords[browse.Item1] = Tip(WalkTree(control).OfType<Button>().Single(value => value.Content as string == "Browse..." && Grid.GetRow(value) == browse.Item2));
            foreach (var label in new[] { (36, "RAW folder"), (46, "Identifications (.sepr2)"), (56, "De novo folder (optional)"), (66, "De novo source"),
                (80, "PEAKS minimum ALC (%)"), (85, "DeepNovo minimum SCORE (%)"), (94, "Isotope ceiling"), (100, "Minimum points per peak") })
                xmlWords[label.Item1] = Tip(WalkTree(control).OfType<Label>().Single(value => value.Content as string == label.Item2));
            xmlWords[32] = WalkTree(control).OfType<TextBlock>().Single(block => block.Text == "Measure the full isotope envelope from matched RAW acquisitions.").Text;
            var compatibility = new ComputeXicWindow("copy only");
            try { xmlWords[139] = Tip((Button)compatibility.FindName("ButtonClose")); } finally { compatibility.Close(); }
            SecondaryWording(output, "compute-tooltips-" + theme, xaml, xmlWords, includeUnchanged: true, tooltips: true);
            Click(oldButton, oldMethod, oldClick); Click(button, method, click);
            bool emptyRefused = SecondarySpectrumShown.Count == 0;
            string currentEmptyStatus = model.Status;
            object oldRows = JsonSerializer.Deserialize(JsonSerializer.Serialize(rows), typeof(List<>).MakeGenericType(legacy.GetType(typeof(SpectrumComparisonRow).FullName!, true)!))!;
            old.GetType().GetProperty("SpectrumComparisonRows")!.SetValue(old, oldRows);
            old.GetType().GetProperty("SpectrumComparisonCsvPath")!.SetValue(old, "native-spectrum.csv"); oldButton.IsEnabled = true;
            model.ComputeAsync().GetAwaiter().GetResult(); PumpDispatcher();
            Click(oldButton, oldMethod, oldClick); Click(button, method, click);
            var displays = SecondarySpectrumShown.Select(window => new { Rows = WalkTree(window).OfType<DataGrid>().Single().Items.Count,
                Safe = HasProofWindowPlacement(window) && !window.IsActive, Title = window.Title }).ToArray();
            bool linked = initiallyDisabled && emptyRefused && button.IsEnabled && displays.Length == 2 && displays.All(value => value.Rows == 1 && value.Safe);
            Check("Compute screen: spectrum link refuses empty state and opens the completed native table with the same rows offscreen", linked);
            add(xaml, 120, 120, "actual completed Compute spectrum button opens the native table through its attached event", true, linked, linked,
                "ComputeXicWindow / QuantifyControl spectrum link; only final Window.Show presentation injected", row => row.Kind == "control");
            add(xaml, 121, 121, "actual spectrum button event reaches the completed stored rows and starts disabled", true, linked, linked,
                "QuantifyControl.ViewSpectrum linked Click; native table constructed from completed result", row => row.Kind == "handler" || row.Kind == "option/copy" && row.Detail.Contains(".IsEnabled", StringComparison.Ordinal));
            add(cs, 384, 384, "compiled attached spectrum handler retains empty-state refusal and completed-table behavior", true, linked, linked,
                "QuantifyControl.ViewSpectrum / SpectrumComparisonWindow.Show; injected final presentation only", row => row.Kind == "handler definition");
            SecondaryWording(output, "compute-spectrum-xaml-" + theme, xaml, new Dictionary<int, string> { [120] = button.Content?.ToString() ?? "", [125] = model.Details });
            SecondaryWording(output, "compute-spectrum-tooltip-" + theme, xaml, new Dictionary<int, string> { [126] = model.Details }, tooltips: true);
            SecondaryWording(output, "compute-spectrum-cs-" + theme, cs, new Dictionary<int, string> { [388] = currentEmptyStatus, [389] = currentEmptyStatus });
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "compute-spectrum-link-" + theme + ".json"), new { InitiallyDisabled = initiallyDisabled, EmptyRefused = emptyRefused, Displays = displays, Injections = trace });
        }
        finally { foreach (var window in SecondarySpectrumShown) window.Close(); SecondarySpectrumShown.Clear(); host.Close(); old.Close(); }
    }
}
