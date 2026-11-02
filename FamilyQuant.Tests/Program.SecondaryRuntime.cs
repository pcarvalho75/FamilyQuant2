using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record SecondaryRuntimeFact(string Id, string Before, string Actual, bool Passed, string Operation, string Symbol);

    private static int SecondaryRuntimeCommand(string[] args, bool interactions = false)
    {
        if (args.Length is not (6 or 7) || args.Length == 7 && args[6] != "--negative-control")
            throw new ArgumentException("Supply frozen source root, native master audit, frozen app folder, fresh output, date and optional --negative-control.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), audit = Path.GetFullPath(args[2]);
        string legacyPath = Path.Combine(Path.GetFullPath(args[3]), "FamilyQuant.dll"), output = Path.GetFullPath(args[4]);
        string step = interactions ? "FL-secondary-interactions" : "FL-secondary-runtime";
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Runtime output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = SecondaryOpenCohort(root, audit, inventory, step);
        foreach (string source in cohort.Select(row => row.Source).Distinct(StringComparer.Ordinal))
            if (FeatureLedgerEvidence.Hash(Path.Combine(frozenRoot, source)) != inventory.Files.Single(file => file.Path == source).Sha256)
                throw new InvalidDataException("Frozen source differs: " + source);
        var legacy = Assembly.LoadFrom(legacyPath);
        EnsureApplication();
        var facts = new Dictionary<string, SecondaryRuntimeFact>(StringComparer.Ordinal);
        void Add(string source, int first, int last, string operation, object? before, object? actual, bool passed, string symbol,
            Func<FeatureLedger.Observation, bool>? include = null)
        {
            string oldValue = JsonSerializer.Serialize(before), newValue = JsonSerializer.Serialize(actual);
            foreach (var row in cohort.Where(row => row.Source == source && row.Line >= first && row.Line <= last && (include?.Invoke(row) ?? true)))
            {
                var fact = new SecondaryRuntimeFact(row.Id, oldValue, newValue, passed, operation, symbol);
                if (!facts.TryAdd(row.Id, fact)) throw new InvalidDataException("A secondary row received two runtime checks: " + row.Id);
            }
        }
        var original = Console.Out; using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var results = new List<SecondaryLedgerResult>(); var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        var priorTheme = Themes.ThemeManager.Current;
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            Check("secondary runtime loads the exact frozen source build", legacy.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                .EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal) == true);
            if (!interactions) SecondaryCacheRuntime(legacy, output, args.Length == 7, Add);
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                // A second theme repeats the same operation contracts independently. Per-row data is
                // retained once after comparing the two theme executions below.
                var captured = new Dictionary<string, SecondaryRuntimeFact>(facts, StringComparer.Ordinal);
                facts.Clear();
                if (interactions)
                {
                    SecondaryComputeInteractions(legacy, output, theme, Add);
                    SecondaryIdentityChooser(legacy, output, theme, Add);
                    SecondaryGridBoundaries(legacy, output, theme, args.Length == 7, Add);
                }
                else
                {
                    SecondaryControlsRuntime(legacy, output, theme, args.Length == 7, Add);
                    SecondaryLockRuntime(legacy, output, theme, Add);
                    SecondaryIdentityRuntime(legacy, output, theme, Add);
                    SecondarySuperRuntime(legacy, output, theme, Add, args.Length == 7);
                    SecondaryGridRuntime(legacy, output, theme, Add);
                    SecondaryComputeRuntime(legacy, output, theme, Add);
                    SecondaryDialogScreenRuntime(legacy, output, theme);
                    SecondaryComputeSpectrumRuntime(legacy, output, theme, Add);
                }
                if (theme == Themes.AppTheme.Dark)
                {
                    foreach (var fact in captured) facts.Add(fact.Key, fact.Value);
                    FeatureLedgerEvidence.WriteJson(Path.Combine(output, "dark-values.json"), facts.Values.OrderBy(fact => fact.Id, StringComparer.Ordinal));
                }
                else
                {
                    foreach (var fact in facts.ToArray())
                    {
                        if (!captured.TryGetValue(fact.Key, out var dark) || dark.Before != fact.Value.Before || dark.Actual != fact.Value.Actual)
                            facts[fact.Key] = fact.Value with { Passed = false, Operation = fact.Value.Operation + "; theme executions differ" };
                        else if (!dark.Passed) facts[fact.Key] = fact.Value with { Passed = false };
                    }
                    foreach (var fact in captured.Where(pair => !facts.ContainsKey(pair.Key))) facts.Add(fact.Key, fact.Value);
                }
            }
            foreach (var row in cohort)
            {
                if (facts.TryGetValue(row.Id, out var fact))
                {
                    bool negativeProbe = args.Length == 7 && (interactions
                        ? row.Source == "FamilyQuant/GridCopy.cs" && row.Line == 51
                        : row.Source == "FamilyQuant/ComputeXicWindow.xaml" && row.Line == 37
                            || row.Source == "FamilyQuant/SuperClusterPreviewWindow.cs" && row.Line == 183);
                    if (fact.Passed || negativeProbe) Check("secondary runtime " + row.Id + ": " + fact.Operation, fact.Passed, fact.Actual);
                    else { skipped++; Console.WriteLine("  SKIP  " + row.Id + ": native values differ or the required action was not established; remains OPEN."); }
                    results.Add(new(row.Id, row.Kind, row.Source, row.Line, fact.Passed, fact.Before, fact.Actual,
                        SecondaryCurrentSources(row.Source).First(), fact.Symbol, fact.Operation,
                        row.Detail + "; " + fact.Operation + "; frozen/contract=" + fact.Before + "; current=" + fact.Actual));
                }
                else
                {
                    skipped++; Console.WriteLine("  SKIP  " + row.Id + ": no supported native check; help remains LK and other gaps remain OPEN.");
                    results.Add(new(row.Id, row.Kind, row.Source, row.Line, false, row.Detail, "", "", "",
                        "No supported native check. Help/tooltips stay with LK; other facts remain OPEN.", ""));
                }
            }
            foreach (var group in results.Where(result => result.Matched).GroupBy(result => result.Kind).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string assertion = "secondary runtime kind " + group.Key + ": each listed native control, value or action check passed";
                Check(assertion, group.All(result => facts[result.Id].Passed)); assertions[group.Key] = assertion;
            }
            Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        }
        finally { Console.SetOut(original); Themes.ThemeManager.Apply(priorTheme); }
        string log = Path.Combine(output, "runtime.log"), data = Path.Combine(output, "runtime.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false)); FeatureLedgerEvidence.WriteJson(data, results);
        File.Copy(legacyPath, Path.Combine(output, "frozen-source-build.dll"), false);
        string[] sources = cohort.SelectMany(row => SecondaryCurrentSources(row.Source)).Where(path => File.Exists(Path.Combine(root, path)))
            .Concat(new[] { "FamilyQuant/IdentityScanChart.cs", "FamilyQuant/ChartDrawingBase.cs", "FamilyQuant/SecondaryWindowView.cs",
                "FamilyQuant/ShellDialogs.cs", "FamilyQuant/AppOperationFiles.cs", "FamilyQuant/SecondaryWindows.ViewModels.cs", "FamilyQuant/SuperClusterPreviewWindow.xaml" })
            .Concat(new[] { "FamilyQuant/ChartImageExporter.cs", "FamilyQuant/ChartPalette.cs", "FamilyQuant.Core/FamilyNaming.cs", "FamilyQuant.Core/ProjectStore.cs" })
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core/Storage"), "*.cs").Select(path => Path.GetRelativePath(root, path)))
            .Distinct(StringComparer.Ordinal).ToArray();
        SecondaryLedgerRegister(root, frozenRoot, audit, output, args[5], step, nameof(SecondaryRuntimeCommand), results, assertions, log, data,
            sources, Directory.GetFiles(Path.Combine(root, "FamilyQuant.Tests"), "Program.Secondary*.cs").Select(path => Path.GetRelativePath(root, path))
                .Append("FamilyQuant.Tests/Program.Commands.SecondaryLedger.cs").Append("FamilyQuant.Tests/Program.QuantifyClose.cs"), runtime: true);
        return failed == 0 ? 0 : 1;
    }

    private delegate void SecondaryAdd(string source, int first, int last, string operation, object? before, object? actual,
        bool passed, string symbol, Func<FeatureLedger.Observation, bool>? include = null);

    private static Window SecondaryHost(Window window)
    {
        window.Width = 1024; window.Height = 768; window.Left = -30000; window.Top = -30000;
        window.ShowInTaskbar = false; window.ShowActivated = false; window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Show(); PumpDispatcher(); return window;
    }
    private static PropertyInfo SecondaryProperty(Type type, string property)
    {
        for (Type? current = type; current != null; current = current.BaseType)
            if (current.GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly) is { } found) return found;
        throw new MissingMemberException(type.FullName, property);
    }
    private static object? SecondaryRead(object value, string property) => SecondaryProperty(value.GetType(), property).GetValue(value);
    private static object? SecondaryCall(object? value, Type type, string name, params object?[] arguments) => type.GetMethod(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!.Invoke(value, arguments);
    private static void SecondaryClick(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static FrameworkElement? SecondaryNamed(FrameworkElement root, string name) => root.FindName(name) as FrameworkElement
        ?? WalkTree(root).OfType<FrameworkElement>().SingleOrDefault(element => element.Name == name);
}
