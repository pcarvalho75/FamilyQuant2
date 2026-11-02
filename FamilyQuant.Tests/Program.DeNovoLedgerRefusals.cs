using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int DeNovoLedgerRefusalsCommand(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Supply frozen source root, frozen app folder, fresh output and a timestamp from date.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row) && row.Source == "FamilyQuant/DeNovoControl.xaml.cs").ToArray();
        string sourcePath = Path.Combine(frozenRoot, "FamilyQuant/DeNovoControl.xaml.cs");
        if (FeatureLedgerEvidence.Hash(sourcePath) != inventory.Files.Single(file => file.Path == "FamilyQuant/DeNovoControl.xaml.cs").Sha256)
            throw new InvalidDataException("Frozen source hash differs.");
        Directory.CreateDirectory(output);
        string legacyPath = Path.Combine(Path.GetFullPath(args[2]), "FamilyQuant.dll");
        var legacyAssembly = Assembly.LoadFrom(legacyPath); var legacyType = legacyAssembly.GetType("FamilyQuant.DeNovoControl", true)!;
        string oldSource = File.ReadAllText(sourcePath);
        string[] supported = ["TryReadInt", "TryReadDouble", "Refuse", "ReadHomologySettingsFromBoxes", "WriteHomologySettingsToBoxes", "N", "Count", "CacheFolderName", "CachePathFor", "MatchRunsToRaw"];
        var selected = cohort.Select(row => new { Row = row, Member = LedgerMemberFactComparison.Compare(row, oldSource, new Dictionary<string, string>()).FrozenMember })
            .Select(item => new { item.Row, Member = supported.SingleOrDefault(name => item.Member.Contains(" " + name + " (", StringComparison.Ordinal)
                || name == "CacheFolderName" && item.Member.EndsWith(" " + name, StringComparison.Ordinal)) })
            .Where(item => item.Member != null && item.Row.Kind is "code fact" or "format/export").ToArray();
        var before = supported.ToDictionary(name => name, _ => new SortedDictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var after = supported.ToDictionary(name => name, _ => new SortedDictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var original = Console.Out; var priorCulture = CultureInfo.CurrentCulture;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var results = new List<DeNovoDeclarationResult>(); var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript)); EnsureApplication();
            Check("De novo refusal baseline identifies the exact frozen commit", (legacyAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
                .EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal));
            foreach (string culture in new[] { "en-US", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var legacy = (UserControl)Activator.CreateInstance(legacyType)!; var current = new DeNovoControl();
                foreach (string method in new[] { "TryReadInt", "TryReadDouble" })
                foreach (string typed in new[] { "-1", "0", "1", "42", "100", "101", "85.5", "1,5", "NaN", "Infinity", "bad", " 8 " })
                {
                    int oldRestores = 0, newRestores = 0;
                    legacyType.GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(legacy, ["fixture status"]); current.ViewModel.ShowStatus("fixture status");
                    object initial = method == "TryReadInt" ? (object)0 : 0d;
                    object?[] oldArgs = [new TextBox { Text = typed }, "Fixture input", method == "TryReadInt" ? (object)0 : 0d,
                        method == "TryReadInt" ? (object)100 : 100d, (Action)(() => oldRestores++), initial];
                    object?[] newArgs = [typed, "Fixture input", method == "TryReadInt" ? (object)0 : 0d,
                        method == "TryReadInt" ? (object)100 : 100d, (Action)(() => newRestores++), initial];
                    bool oldAccepted = (bool)legacyType.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(legacy, oldArgs)!;
                    bool newAccepted = (bool)typeof(DeNovoSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(current.ViewModel, newArgs)!;
                    PumpDispatcher();
                    string oldValue = JsonSerializer.Serialize(new { accepted = oldAccepted, value = oldArgs[^1], restores = oldRestores,
                        status = ((TextBlock)legacy.FindName("TextBlockDeNovoStatus")).Text }, DeNovoBridgeJson);
                    string newValue = JsonSerializer.Serialize(new { accepted = newAccepted, value = newArgs[^1], restores = newRestores, status = current.ViewModel.Status }, DeNovoBridgeJson);
                    string key = culture + "/" + method + "/" + typed;
                    before[method][key] = oldValue; after[method][key] = newValue;
                    if (!oldAccepted && !newAccepted) { before["Refuse"][key] = oldValue; after["Refuse"][key] = newValue; }
                    Check("De novo parser value, refusal callback and exact message " + key, oldValue == newValue);
                }
                foreach (var input in new[] { ("0.72", "3", "Reverse_"), ("1.1", "3", "Reverse_"),
                    ("0.72", "0", "Reverse_"), ("0.72", "1001", "Reverse_"), ("0.72", "3", " "), ("0,72", "3", "Reverse_") })
                {
                    foreach (var control in new[] { legacy, current })
                    {
                        ((TextBox)control.FindName("TextBoxMinIdentity")).Text = input.Item1;
                        ((TextBox)control.FindName("TextBoxTopHits")).Text = input.Item2;
                        ((TextBox)control.FindName("TextBoxDecoyLabel")).Text = input.Item3;
                    }
                    PumpDispatcher();
                    bool oldAccepted = (bool)legacyType.GetMethod("ReadHomologySettingsFromBoxes")!.Invoke(legacy, null)!;
                    bool newAccepted = current.ReadHomologySettingsFromBoxes(); PumpDispatcher();
                    string oldValue = Settings(legacy, oldAccepted), newValue = Settings(current, newAccepted);
                    string key = culture + "/" + input.Item1 + "/" + input.Item2 + "/" + input.Item3;
                    before["ReadHomologySettingsFromBoxes"][key] = oldValue; after["ReadHomologySettingsFromBoxes"][key] = newValue;
                    before["WriteHomologySettingsToBoxes"][key] = oldValue; after["WriteHomologySettingsToBoxes"][key] = newValue;
                    Check("De novo homology settings preserve acceptance, restoration and exact feedback " + key, oldValue == newValue);
                }
                foreach (int number in new[] { 0, 1, 12345, -1, int.MaxValue })
                {
                    string oldValue = (string)legacyType.GetMethod("N", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [number])!;
                    string newValue = (string)typeof(DeNovoSession).GetMethod("N", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [number])!;
                    string key = culture + "/" + number.ToString(CultureInfo.InvariantCulture); before["N"][key] = oldValue; after["N"][key] = newValue;
                    Check("De novo native display integer formatter " + key, oldValue == newValue);
                }
                var countInput = new DeNovoReconciliation { Counts = new() { [FamilyQuant.DeNovo.ReconciliationVerdict.DeNovoOnly] = 12345 } };
                var oldCount = legacyType.GetMethod("Count", BindingFlags.NonPublic | BindingFlags.Static)!;
                object oldCountInput = LedgerLegacyValue(countInput, oldCount.GetParameters()[0].ParameterType)!;
                foreach (var verdict in Enum.GetValues<FamilyQuant.DeNovo.ReconciliationVerdict>())
                {
                    string oldValue = (string)oldCount.Invoke(null, [oldCountInput, Enum.ToObject(oldCount.GetParameters()[1].ParameterType, (int)verdict)])!;
                    string newValue = (string)typeof(DeNovoSession).GetMethod("Count", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [countInput, verdict])!;
                    string key = culture + "/" + verdict; before["Count"][key] = oldValue; after["Count"][key] = newValue;
                    Check("De novo native verdict formatter handles present and absent keys " + key, oldValue == newValue);
                }
                string rawRoot = Path.Combine(output, "run-name-fixture"); Directory.CreateDirectory(rawRoot);
                foreach (string file in new[] { "RunA.raw", "runb.RAW" }) if (!File.Exists(Path.Combine(rawRoot, file))) File.WriteAllBytes(Path.Combine(rawRoot, file), []);
                foreach (string run in new[] { "RunA", "runb.raw", "missing.raw" })
                {
                    string oldValue = (string)legacyType.GetMethod("CachePathFor", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [run, rawRoot])!;
                    string newValue = DeNovoControl.CachePathFor(run, rawRoot);
                    string key = culture + "/" + run;
                    before["CachePathFor"][key] = Path.GetRelativePath(rawRoot, oldValue); after["CachePathFor"][key] = Path.GetRelativePath(rawRoot, newValue);
                    Check("De novo native cache path " + key, oldValue == newValue);
                }
                string[] names = ["RunA", "RUNB.sqt", "missing.raw"];
                object oldMatches = legacyType.GetMethod("MatchRunsToRaw", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [names, rawRoot])!;
                object newMatches = DeNovoControl.MatchRunsToRaw(names, rawRoot);
                Check("De novo native run-name lookup preserves missing and case/extension matches " + culture,
                    JsonSerializer.Serialize(oldMatches, oldMatches.GetType()) == JsonSerializer.Serialize(newMatches, newMatches.GetType()));
                string RunMatches(object value) => JsonSerializer.Serialize(((System.Collections.IEnumerable)value).Cast<object>().Select(item => new
                {
                    run = item.GetType().GetProperty("Run")!.GetValue(item), expected = item.GetType().GetProperty("ExpectedFileName")!.GetValue(item),
                    path = item.GetType().GetProperty("RawPath")!.GetValue(item) is string path ? Path.GetRelativePath(rawRoot, path) : null,
                    found = item.GetType().GetProperty("Found")!.GetValue(item),
                }));
                before["MatchRunsToRaw"][culture] = RunMatches(oldMatches); after["MatchRunsToRaw"][culture] = RunMatches(newMatches);
                before["CacheFolderName"][culture] = (string)legacyType.GetField("CacheFolderName", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;
                after["CacheFolderName"][culture] = DeNovoControl.CacheFolderName;
                Check("De novo cache folder name remains the frozen literal " + culture, before["CacheFolderName"][culture] == after["CacheFolderName"][culture]);
            }
            foreach (var item in selected)
            {
                string member = item.Member!; string oldValue = JsonSerializer.Serialize(before[member]), newValue = JsonSerializer.Serialize(after[member]);
                bool matched = before[member].Count > 0 && oldValue == newValue;
                string path = member is "CacheFolderName" or "CachePathFor" or "MatchRunsToRaw" ? "FamilyQuant.Core/DeNovoScreenOperations.cs" : "FamilyQuant/DeNovoSession.cs";
                results.Add(new(item.Row.Id, item.Row.Kind, matched, oldValue, newValue, path, "De novo " + member,
                    item.Row.Source + ":" + item.Row.Line.ToString(CultureInfo.InvariantCulture) + " " + item.Row.Detail + " -> executed " + member
                    + "; frozen=" + oldValue + "; current=" + newValue,
                    matched ? "Frozen/current parser, callback, exact feedback or path result agrees for every recorded case." : "Unexecuted or differing behavior remains OPEN."));
                if (matched) Check("De novo refusal/path row " + item.Row.Id + ": " + member, true);
                else { skipped++; Console.WriteLine("  SKIP  " + item.Row.Id + ": no matching executed behavior"); }
            }
            foreach (var group in results.Where(result => result.Matched).GroupBy(result => result.Kind))
            {
                string assertion = "De novo refusal/path kind " + group.Key + ": every listed old/current case agrees";
                Check(assertion, group.All(result => result.Before == result.Actual)); assertions[group.Key] = assertion;
            }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); CultureInfo.CurrentCulture = priorCulture; }
        var sources = Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "DeNovo*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*DeNovo*.xaml"))
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core"), "DeNovo*.cs"))
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core/DeNovo"), "*.cs", SearchOption.AllDirectories))
            .Concat(new[] { Path.Combine(root, "FamilyQuant.Core/Homology/HomologyOptions.cs") })
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        return CompleteDeNovoLedgerProof(root, frozenRoot, output, args[4], results, assertions, transcript.ToString(), sources,
            "EV-denovo-refusals", nameof(DeNovoLedgerRefusalsCommand), "Synthetic scalar-input, callbacks and empty RAW-name fixtures; no acquired-data extraction or search.", [legacyPath], sources);

        string Settings(UserControl control, bool accepted)
        {
            object settings = control.GetType().GetProperty("HomologySettings")!.GetValue(control)!;
            return JsonSerializer.Serialize(new { accepted, settings = JsonSerializer.SerializeToElement(settings, settings.GetType()),
                identity = ((TextBox)control.FindName("TextBoxMinIdentity")).Text, hits = ((TextBox)control.FindName("TextBoxTopHits")).Text,
                prefix = ((TextBox)control.FindName("TextBoxDecoyLabel")).Text, status = ((TextBlock)control.FindName("TextBlockDeNovoStatus")).Text });
        }
    }
}
