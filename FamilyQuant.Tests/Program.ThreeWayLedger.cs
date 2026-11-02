using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ThreeWayLedgerCommands() =>
    [new("--prove-threeway", 10410, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--prove-threeway", (args, _) => ThreeWayLedgerProof(args))];

    private static int ThreeWayLedgerProof(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Supply frozen source, frozen app, fresh output and date.");
        if (!NativeProofDesktop.IsPrivate) return IsolatedNativeProof(new[] { "--isolated-native-proof" }.Concat(args).ToArray());
        string root = FindLedgerRoot()!, frozen = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Use a fresh output.");
        Directory.CreateDirectory(output); EnsureApplication();
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row) && row.Source.StartsWith("FamilyQuant/DeNovo/ThreeWay", StringComparison.Ordinal)).ToArray();
        foreach (var file in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
            if (FeatureLedgerEvidence.Hash(Path.Combine(frozen, file.Path)) != file.Sha256) throw new InvalidDataException("Frozen source changed.");
        var oldAssembly = Assembly.LoadFrom(Path.Combine(args[2], "FamilyQuant.dll"));
        var original = Console.Out; using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var data = new List<object>(); var artifacts = new List<string>();
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            Check("Three-way probe uses exact frozen application", oldAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion.EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal));
            foreach (string culture in new[] { "en-US", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var sideData = new List<JsonElement>(); var written = new List<string>();
                foreach (var side in new[] { (Name: "frozen", Assembly: oldAssembly), (Name: "current", Assembly: typeof(FamilyQuant.DeNovo.ThreeWayWindow).Assembly) })
                {
                    var partition = side.Name == "frozen" ? oldAssembly.GetType("FamilyQuant.DeNovo.ThreeWayPartition", true)!
                        : typeof(FamilyQuant.DeNovo.ThreeWayPartition);
                    var valueType = partition.Assembly.GetType("FamilyQuant.DeNovo.PartitionValue", true)!;
                    object Values(string method, double one, double two)
                    {
                        var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType))!;
                        dictionary.Add("r,\"one\"", valueType.GetMethod(method)!.Invoke(null, [one, "fixture,\"source\""]));
                        dictionary.Add("r2", valueType.GetMethod(method)!.Invoke(null, [two, "fixture-source"]));
                        return dictionary;
                    }
                    object rows = partition.GetMethod("Build")!.Invoke(null, [Values("Measured", 1000.25, 2000),
                        Values("Recorded", 250.5, 1900.5), Values("Measured", 125.25, 250.75),
                        new Dictionary<string, double> { ["r,\"one\""] = 751.5, ["r2"] = 1000 }])!;
                    sideData.Add(JsonSerializer.SerializeToElement(rows, rows.GetType(), DeNovoBridgeJson));
                    string csv = (string)partition.GetMethod("BuildCsv")!.Invoke(null, [rows])!;
                    string api = Path.Combine(output, culture + "-" + side.Name + "-api.csv"); File.WriteAllText(api, csv); artifacts.Add(api);
                    var windowType = side.Assembly.GetType("FamilyQuant.DeNovo.ThreeWayWindow", true)!;
                    foreach (bool empty in new[] { false, true })
                    {
                        object windowRows = empty ? Activator.CreateInstance(rows.GetType())! : rows;
                        var window = (Window)windowType.GetMethod("Create")!.Invoke(null, ["Native three-way fixture", windowRows, "Fixture provenance"] )!;
                        window.Left = window.Top = -30000; window.ShowActivated = false; window.ShowInTaskbar = false;
                        try
                        {
                            window.Show(); PumpDispatcher();
                            var grid = (Grid)window.Content; var button = grid.Children.OfType<Button>().Single();
                            Check("Three-way native window/chart and export state " + culture + "/" + side.Name + "/" + empty,
                                grid.Children.OfType<FrameworkElement>().Any(child => child.GetType().Name == "ThreeWayChart")
                                && button.IsVisible && button.IsEnabled == !empty);
                            string? caption = grid.Children.OfType<TextBlock>().SingleOrDefault()?.Text;
                            if (!empty)
                            {
                                string path = Path.Combine(output, culture + "-" + side.Name + "-chosen.csv");
                                using (var driver = new NativeOwnedDialogDriver(path))
                                {
                                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                                    Check("Three-way actual native Save writes identical API bytes " + culture + "/" + side.Name,
                                        driver.SawDialog && driver.EnteredPath && !driver.TimedOut && File.Exists(path) && File.ReadAllText(path) == csv);
                                }
                                artifacts.Add(path); written.Add(path);
                                using var cancel = new NativeOwnedDialogDriver(null);
                                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                                Check("Three-way actual native cancellation preserves chosen bytes " + culture + "/" + side.Name,
                                    cancel.SawDialog && !cancel.TimedOut && File.ReadAllText(path) == csv);
                            }
                            var bitmap = new RenderTargetBitmap(1020, 620, 96, 96, PixelFormats.Pbgra32); bitmap.Render(grid);
                            string png = Path.Combine(output, culture + "-" + side.Name + "-" + (empty ? "empty" : "values") + ".png");
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using (var stream = File.Create(png)) encoder.Save(stream); artifacts.Add(png);
                            data.Add(new { culture, side = side.Name, empty, caption, tooltip = button.ToolTip?.ToString(), buttonText = button.Content?.ToString() });
                        }
                        finally { window.Close(); }
                    }
                }
                Check("Three-way frozen/current numeric rows including signed remainder agree " + culture,
                    LedgerLegacyJsonAgrees(sideData[0], sideData[1]));
                Check("Three-way negative remainder is retained " + culture,
                    sideData[1].EnumerateArray().Any(row => row.GetProperty("Remainder").GetProperty("Value").GetDouble() < 0));
                Check("Three-way frozen/current chosen CSV byte agreement " + culture,
                    File.ReadAllBytes(written[0]).SequenceEqual(File.ReadAllBytes(written[1])));
                data.Add(new { culture, rows = sideData });
            }
            if (failed == 0) Check("Whole three-way numeric, CSV and native window Save/cancel contract passed in both cultures", true);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; }
        string log = Path.Combine(output, "comparisons.log"); File.WriteAllText(log, transcript.ToString()); artifacts.Add(log);
        string report = Path.Combine(output, "threeway-data.json"); FeatureLedgerEvidence.WriteJson(report, data); artifacts.Add(report);
        if (failed != 0) return 1;
        // Only the numeric formatter/field escaper and the actual window control/event are covered.
        // Successful fixtures do not establish the partition's other refusal/status branches.
        string[] formatIds = ["FQ15-DF75D56E6E5E", "FQ15-B3686CCA5BFE", "FQ15-399C3233EFA4", "FQ15-3F845CD11BCD", "FQ15-7418B217FD10"];
        var supported = cohort.Where(row => formatIds.Contains(row.Id, StringComparer.Ordinal)
            || row.Kind is "handler" or "code control").Select(row => new DeNovoDeclarationResult(row.Id, row.Kind, true, row.Detail,
                "Native numeric/CSV and actual window export contract", row.Source.EndsWith("ThreeWayPartition.cs", StringComparison.Ordinal)
                    ? "FamilyQuant.Core/DeNovo/ThreeWayPartition.cs" : "FamilyQuant/DeNovo/ThreeWayWindow.cs",
                "FamilyQuant.DeNovo.ThreeWayPartition.BuildCsv / ThreeWayWindow.Create and SaveCsv",
                row.Detail + " -> complete frozen/current numeric rows, CSV bytes, actual native Save/cancel and empty/positive window states in two cultures", "")).ToList();
        var assertions = supported.Select(row => row.Kind).Distinct().ToDictionary(kind => kind,
            _ => "Whole three-way numeric, CSV and native window Save/cancel contract passed in both cultures", StringComparer.Ordinal);
        string[] sources = ["FamilyQuant.Core/DeNovo/ThreeWayPartition.cs", "FamilyQuant/DeNovo/ThreeWayChart.cs", "FamilyQuant/DeNovo/ThreeWayWindow.cs",
            "FamilyQuant/ChartDrawingBase.cs", "FamilyQuant/ShellDialogs.cs", "FamilyQuant.Tests/Program.ThreeWayLedger.cs", "FamilyQuant.Tests/NativeProofDesktop.cs", "FamilyQuant.Tests/NativeOwnedDialogDriver.cs"];
        foreach (var file in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
        {
            string copy = Path.Combine(output, "frozen_" + Path.GetFileName(file.Path)); File.Copy(Path.Combine(frozen, file.Path), copy); artifacts.Add(copy);
        }
        return CompleteDeNovoLedgerProof(root, frozen, output, args[4], supported, assertions, transcript.ToString(), sources,
            "EV-threeway", nameof(ThreeWayLedgerProof), "Native component/API/Save contract; original partition navigation retired by Paulo. Wording remains for the shared list.", artifacts.Where(path => path != log).Distinct(StringComparer.Ordinal), sources);
    }
}
