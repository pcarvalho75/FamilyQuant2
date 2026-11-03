using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string HistoricalCompositionMember = "FamilyQuant.CsvReport::public static string BuildFamilyComposition ( FamilyEnvelopeQuantResult result , SharedPeptidePolicy policy , double denominator , string basis )";
    private static readonly string[] ShellCompositionSources =
    ["FamilyQuant.Core/CsvReport.cs", "FamilyQuant.Core/CsvFormatting.cs", "FamilyQuant.Core/SharedPeptidePolicy.cs",
        "FamilyQuant.Core/Quantitation/FamilyEnvelopeModels.cs", "FamilyQuant.Tests/Program.ShellCompositionProof.cs"];

    private sealed record ShellCompositionEvidence(Dictionary<string, string> Assertions, List<string> Artifacts);
    private sealed record ShellCompositionFile(string Culture, string Case, string Policy, double Denominator,
        string FrozenPath, string CurrentPath, string FrozenSha256, string CurrentSha256, bool Same);

    // These are native output facts, not a relaxed declaration match. The four-argument public
    // overload is exercised even when it delegates to a new overload. No sample or RAW is read.
    private static ShellCompositionEvidence ProveShellComposition(string frozenPath, string referenceApp,
        string output, IEnumerable<FeatureLedger.Observation> cohort, bool negative)
    {
        string frozenSource = File.ReadAllText(frozenPath);
        var evidence = new ShellCompositionEvidence(new(StringComparer.Ordinal), new());
        var source = new Dictionary<string, string> { ["FamilyQuant/CsvReport.cs"] = frozenSource };
        var selected = cohort.Where(row => row.Source == "FamilyQuant/CsvReport.cs" && row.Kind == "export/storage fact")
            .Select(row => (Row: row, Anchor: LedgerMemberFactComparison.Compare(row, frozenSource, source)))
            .Where(value => value.Anchor.Matched && value.Anchor.FrozenMember == HistoricalCompositionMember)
            .OrderBy(value => value.Row.Id, StringComparer.Ordinal).ToArray();
        Check("shell composition scope binds all thirteen frozen physical literal observations to the historical member", selected.Length == 13);
        string folder = Path.Combine(output, "composition-native"); Directory.CreateDirectory(folder);
        string referencePath = Path.Combine(Path.GetFullPath(referenceApp), "FamilyQuant.dll");
        var legacy = Assembly.LoadFrom(referencePath);
        bool pinned = legacy.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal) == true;
        Check("shell composition executes the exact frozen source build", pinned);
        if (!pinned || selected.Length != 13) return evidence;
        MethodInfo oldWrite = legacy.GetType("FamilyQuant.CsvReport", true)!.GetMethods()
            .Single(method => method.Name == nameof(CsvReport.BuildFamilyComposition) && method.GetParameters().Length == 4);
        Type oldResultType = oldWrite.GetParameters()[0].ParameterType, oldPolicyType = oldWrite.GetParameters()[1].ParameterType;
        Check("shell composition exercises every native shared-peptide policy", Enum.GetNames(oldPolicyType)
            .SequenceEqual(Enum.GetNames<SharedPeptidePolicy>(), StringComparer.Ordinal));
        var cases = new[] { ("populated", ShellCompositionFixture(), 125.25), ("zero-denominator", ShellCompositionFixture(), 0d),
            ("negative-denominator", ShellCompositionFixture(), -2d), ("nonfinite-denominator", ShellCompositionFixture(), double.NaN),
            ("empty", new FamilyEnvelopeQuantResult(), 17d) };
        var files = new List<ShellCompositionFile>();
        var outputs = new List<(string Frozen, string Current)>();
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        bool corrupted = false;
        try
        {
            foreach (string language in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                string cultureFolder = Path.Combine(folder, language); Directory.CreateDirectory(cultureFolder);
                foreach (var (name, result, denominator) in cases)
                {
                    object oldResult = CopyCompositionFixture(result, oldResultType);
                    foreach (var policy in Enum.GetValues<SharedPeptidePolicy>())
                    {
                        const string basis = "Synthetic, \"quoted\" basis\r\nwith a retained line break";
                        string before = (string)oldWrite.Invoke(null, new[] { oldResult, Enum.Parse(oldPolicyType, policy.ToString()), denominator, basis })!;
                        string after = CsvReport.BuildFamilyComposition(result, policy, denominator, basis);
                        if (negative && !corrupted)
                        { after = after.Replace("family_user_id", "changed_family_user_id", StringComparison.Ordinal); corrupted = true; }
                        string stem = name + "-" + policy;
                        string oldPath = Path.Combine(cultureFolder, "frozen-" + stem + ".csv"), currentPath = Path.Combine(cultureFolder, "current-" + stem + ".csv");
                        File.WriteAllText(oldPath, before, new UTF8Encoding(false)); File.WriteAllText(currentPath, after, new UTF8Encoding(false));
                        bool same = File.ReadAllBytes(oldPath).SequenceEqual(File.ReadAllBytes(currentPath));
                        Check("native shell composition complete CSV " + language + "/" + stem, same);
                        outputs.Add((before, after)); evidence.Artifacts.Add(oldPath); evidence.Artifacts.Add(currentPath);
                        files.Add(new(language, name, policy.ToString(), denominator, oldPath, currentPath,
                            FeatureLedgerEvidence.Hash(oldPath), FeatureLedgerEvidence.Hash(currentPath), same));
                    }
                }
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        bool invariant = files.Where(file => file.Culture == "en-US").All(file => files.Any(other => other.Culture == "pt-BR"
            && other.Case == file.Case && other.Policy == file.Policy && other.FrozenSha256 == file.FrozenSha256 && other.CurrentSha256 == file.CurrentSha256));
        Check("native shell composition frozen and current bytes repeat across both cultures", invariant);
        bool allEqual = files.Count == cases.Length * Enum.GetValues<SharedPeptidePolicy>().Length * 2 && files.All(file => file.Same) && invariant;
        foreach (var (row, _) in selected)
        {
            string literal = JsonSerializer.Deserialize<string>(row.Detail)!;
            string assertion = "native shell composition fact " + row.Id + ": anchored frozen literal observed and complete historical CSV bytes agree in every case and culture";
            bool witnessed = allEqual && outputs.Any(pair => pair.Frozen.Contains(literal, StringComparison.Ordinal) && pair.Current.Contains(literal, StringComparison.Ordinal));
            Check(assertion, witnessed);
            if (witnessed) evidence.Assertions.Add(row.Id, assertion);
        }
        string facts = Path.Combine(folder, "files.json");
        // NaN is an explicit denominator fixture, so retain its name rather than emitting invalid JSON.
        FeatureLedgerEvidence.WriteJson(facts, files.Select(file => new { file.Culture, file.Case, file.Policy,
            denominator = file.Denominator.ToString("R", CultureInfo.InvariantCulture), file.FrozenPath, file.CurrentPath,
            file.FrozenSha256, file.CurrentSha256, file.Same }));
        evidence.Artifacts.Add(facts);
        string fixture = Path.Combine(folder, "inputs.json");
        FeatureLedgerEvidence.WriteJson(fixture, cases.Select(value => new { name = value.Item1,
            denominator = value.Item3.ToString("R", CultureInfo.InvariantCulture), result = value.Item2 }));
        evidence.Artifacts.Add(fixture);
        foreach (var (path, name) in new[] { (referencePath, "frozen-FamilyQuant.dll"),
            (Path.Combine(FindLedgerRoot()!, "FamilyQuant.Core/CsvReport.cs"), "current-CsvReport.cs") })
        {
            string retained = Path.Combine(folder, name); File.Copy(path, retained); evidence.Artifacts.Add(retained);
        }
        string frozen = Path.Combine(folder, "frozen-CsvReport.cs"); File.Copy(frozenPath, frozen); evidence.Artifacts.Add(frozen);
        return evidence;
    }

    private static FamilyEnvelopeQuantResult ShellCompositionFixture()
    {
        var result = new FamilyEnvelopeQuantResult { SharedIdentifiedEnvelope = 7.125, UnattributedIdentifiedEnvelope = 13.75 };
        result.Families.Add(new() { FamilyUserId = 11, Description = "=1+2, \"quoted\"\r\nsecond line OS=Fixture source",
            UniqueIdentifiedEnvelope = 1.25, SplitIdentifiedEnvelope = 3.125, IdentifiedEnvelope = 7.25 });
        result.Families.Add(new() { FamilyUserId = 2, Description = "  retained Unicode α and whitespace  ",
            UniqueIdentifiedEnvelope = 1.25, SplitIdentifiedEnvelope = 3.125, IdentifiedEnvelope = 7.25 });
        result.Families.Add(new() { FamilyUserId = 7, Description = null!,
            UniqueIdentifiedEnvelope = 0, SplitIdentifiedEnvelope = .125, IdentifiedEnvelope = .25 });
        return result;
    }

    private static object CopyCompositionFixture(FamilyEnvelopeQuantResult source, Type resultType)
    {
        object result = Activator.CreateInstance(resultType)!;
        void Scalars(object from, object to)
        {
            foreach (var property in from.GetType().GetProperties().Where(property => property.CanRead && property.GetIndexParameters().Length == 0
                && (property.PropertyType.IsPrimitive || property.PropertyType == typeof(string))))
            {
                var destination = to.GetType().GetProperty(property.Name);
                if (destination?.CanWrite == true && destination.PropertyType == property.PropertyType)
                    destination.SetValue(to, property.GetValue(from));
            }
        }
        Scalars(source, result);
        var families = (IList)resultType.GetProperty(nameof(FamilyEnvelopeQuantResult.Families))!.GetValue(result)!;
        Type rowType = families.GetType().GetGenericArguments().Single();
        foreach (var family in source.Families)
        { object row = Activator.CreateInstance(rowType)!; Scalars(family, row); families.Add(row); }
        return result;
    }
}
