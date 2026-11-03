using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string HistoricalTicMember = "FamilyQuant.ClusterGenerationControl::private static FullXicPrepared PrepareFullXic ( IReadOnlyList < QuantRow > rows , IReadOnlyDictionary < string , double > ? perRunTic , FamilyClusterFile proteins )";
    private static readonly string[] ShellTicSources =
    ["FamilyQuant/CurationQuantitationViewModel.cs", "FamilyQuant.Core/DeNovo/DeNovoReconciler.cs",
        "FamilyQuant.Core/FamilyEnvelopeQuant.cs", "FamilyQuant.Core/Quantitation/CurationHomologyCache.cs",
        "FamilyQuant.Core/FamilyQuantFile.cs", "FamilyQuant.Core/Storage/QuantRow.cs",
        "FamilyQuant.Tests/Program.DeNovoLedgerWorkflow.cs", "FamilyQuant.Tests/Program.NativeFractions.cs",
        "FamilyQuant.Tests/Program.ShellLedgerReceipts.cs", "FamilyQuant.Tests/Program.ShellTicProof.cs"];

    private sealed record ShellTicEvidence(Dictionary<string, string> Assertions, List<string> Artifacts);
    private sealed record ShellTicOutcome(string? Refusal, int Runs, double Tic);

    // A native comparison of the original refusal output, not a declaration relocation or search-parity claim.
    private static ShellTicEvidence ProveShellTic(string frozenPath, string referenceApp, string output,
        IEnumerable<FeatureLedger.Observation> cohort)
    {
        var proof = new ShellTicEvidence(new(StringComparer.Ordinal), new());
        string frozenSource = File.ReadAllText(frozenPath);
        var original = new Dictionary<string, string> { ["FamilyQuant/ClusterGenerationControl.xaml.cs"] = frozenSource };
        var selected = cohort.Where(row => row.Source == "FamilyQuant/ClusterGenerationControl.xaml.cs" && row.Kind == "code fact")
            .Select(row => (Row: row, Anchor: LedgerMemberFactComparison.Compare(row, frozenSource, original)))
            .Where(item => item.Anchor.Matched && item.Anchor.FrozenMember == HistoricalTicMember)
            .OrderBy(item => item.Row.Id, StringComparer.Ordinal).ToArray();
        Check("shell TIC scope binds all eight frozen literal observations to the historical member", selected.Length == 8);
        string referencePath = Path.Combine(Path.GetFullPath(referenceApp), "FamilyQuant.dll");
        var legacy = Assembly.LoadFrom(referencePath);
        bool pinned = legacy.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal) == true;
        Check("shell TIC executes the exact frozen source build", pinned);
        if (!pinned || selected.Length != 8) return proof;

        var before = legacy.GetType("FamilyQuant.ClusterGenerationControl", true)!.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "PrepareFullXic" && method.GetParameters().Length == 3);
        var after = typeof(CurationQuantitationViewModel).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "PrepareFullXic" && method.GetParameters().Length == 5);
        var proteins = new FamilyClusterFile { MyExtendedFastaItems = new() {
            new() { SequenceIdentifier = "fixture-protein", Sequence = "ACDEFGHIK", UserID = 1, USearchID = 1 } } };
        object oldProteins = legacy.GetType("FamilyQuant.FamilyClusterFile", true)!.GetMethod("FromJsonText")!
            .Invoke(null, new object[] { proteins.ToJsonText() })!;
        var one = new[] { Qr("identified", "run-1", 1, 2, "ACDEFGHIK", 10, 20) };
        var five = Enumerable.Range(1, 5).Select(i => Qr("identified", "run-" + i.ToString(CultureInfo.InvariantCulture), i, 2, "ACDEFGHIK", 10, 20)).ToArray();
        var cases = new (string Name, QuantRow[] Rows, Dictionary<string, double>? Tic)[] {
            ("no-tic", one, null),
            ("missing-runs", five, new() { ["other"] = 25 }),
            ("duplicate-run", one, new() { ["run-1.raw"] = 10, ["run-1"] = 11 }),
            ("invalid-extra", one, new() { ["run-1"] = 10, ["extra"] = 0 }),
            ("valid", one, new() { ["run-1"] = 10, ["extra"] = 20 }) };
        string folder = Path.Combine(output, "tic-native"); Directory.CreateDirectory(folder);
        var observations = new List<object>();
        var witnessed = new List<(string? Frozen, string? Current)>();
        bool allEqual = true;
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (string language in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                foreach (var item in cases)
                {
                    object oldRows = LedgerLegacyValue(item.Rows, before.GetParameters()[0].ParameterType)!;
                    object oldResult = before.Invoke(null, new object?[] { oldRows, item.Tic, oldProteins })!;
                    ShellTicOutcome frozen = ReadTicOutcome(oldResult);
                    string cache = Path.Combine(folder, language, item.Name, "cache");
                    for (int round = 0; round < 2; round++)
                    {
                        object currentResult = after.Invoke(null, new object?[] { item.Rows, item.Tic, proteins, null, cache })!;
                        ShellTicOutcome current = ReadTicOutcome(currentResult);
                        bool same = frozen == current;
                        Check("native shell TIC refusal, run count and denominator " + language + "/" + item.Name + "/" + round.ToString(CultureInfo.InvariantCulture), same);
                        allEqual &= same;
                        observations.Add(new { language, item.Name, round, Frozen = frozen, Current = current, Same = same });
                        witnessed.Add((frozen.Refusal, current.Refusal));
                    }
                }
            }
        }
        finally { CultureInfo.CurrentCulture = saved; }
        foreach (var item in selected)
        {
            string literal = JsonSerializer.Deserialize<string>(item.Row.Detail)!;
            string assertion = "native shell TIC fact " + item.Row.Id + ": anchored frozen message fragment occurs in equal frozen/current refusals";
            bool present = allEqual && witnessed.Any(pair => pair.Frozen != null && pair.Current != null
                && pair.Frozen.Contains(literal, StringComparison.Ordinal) && pair.Current.Contains(literal, StringComparison.Ordinal));
            Check(assertion, present);
            if (present) proof.Assertions.Add(item.Row.Id, assertion);
        }
        string facts = Path.Combine(folder, "outcomes.json"); FeatureLedgerEvidence.WriteJson(facts, observations); proof.Artifacts.Add(facts);
        string inputs = Path.Combine(folder, "inputs.json");
        FeatureLedgerEvidence.WriteJson(inputs, cases.Select(item => new { item.Name, item.Rows, item.Tic })); proof.Artifacts.Add(inputs);
        foreach (var (path, name) in new[] { (referencePath, "frozen-FamilyQuant.dll"), (frozenPath, "frozen-curation.cs"),
            (Path.Combine(FindLedgerRoot()!, "FamilyQuant/CurationQuantitationViewModel.cs"), "current-curation.cs") })
        { string target = Path.Combine(folder, name); File.Copy(path, target); proof.Artifacts.Add(target); }
        return proof;
    }

    private static ShellTicOutcome ReadTicOutcome(object value)
    {
        var type = value.GetType();
        return new((string?)type.GetField("TicRefusal")!.GetValue(value), (int)type.GetField("Runs")!.GetValue(value)!,
            (double)type.GetField("Tic")!.GetValue(value)!);
    }
}
