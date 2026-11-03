using FamilyQuant.DeNovo;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static readonly string[] AssaySpectrumFactIds = ["FQ15-AB0D120CA773", "FQ15-ABD9B82A59A3", "FQ15-CE9FF8A983EE"];
    private static readonly string[] AssaySpectrumProofSources =
    ["FamilyQuant.Core/DeNovo/SpectrumComparisonTable.cs", "FamilyQuant.Core/Storage/QuantRow.cs",
        "FamilyQuant.Core/DeNovo/DeNovoReconciler.cs", "FamilyQuant.Core/DeNovo/NativeQuantFractions.cs",
        "FamilyQuant.Core/DeNovo/PeptidoformIdentity.cs", "FamilyQuant.Core/DeNovo/DeNovoSource.cs",
        "FamilyQuant.Core/DeNovo/SeproPsmAdapter.cs", "FamilyQuant.Core/DeNovo/Readers/DeNovoSourceRows.cs",
        "FamilyQuant.Core/Operations/CoreOperation.cs",
        "FamilyQuant.Tests/Program.LedgerAssayComparison.cs", "FamilyQuant.Tests/Program.LedgerAssaySpectrumProof.cs",
        "FamilyQuant.Tests/Program.SecondaryInjectedMethod.cs", "FamilyQuant.Tests/Program.DeNovoLedgerWorkflow.cs",
        "FamilyQuant.Tests/LedgerMemberFactComparison.cs"];
    private sealed record AssaySpectrumProof(Dictionary<string, string> Assertions, List<string> Artifacts);
    private sealed record AssaySpectrumClaim(int Scan, double? Score, string ScoreField, string SourceId, string SourceDisplayName, bool External);
    private static List<AssaySpectrumClaim>? assaySpectrumClaims;

    // Observe the actual Claim objects immediately before native clustering, then call the original
    // clustering method unchanged. The existing injector preserves the rest of Build's compiled IL.
    private static void ObserveAssaySpectrumClaims(object value)
    {
        foreach (object claim in (IEnumerable)value)
        {
            object? Read(string name) => claim.GetType().GetProperty(name)!.GetValue(claim);
            if (Read("Kind")?.ToString() != "PatternLab") continue;
            assaySpectrumClaims!.Add(new((int)Read("Scan")!, (double?)Read("Score"), (string)Read("ScoreField")!,
                (string)Read("SourceId")!, (string)Read("SourceDisplayName")!, (bool)Read("IsExternalSourceObservation")!));
        }
    }

    private static DynamicMethod AssaySpectrumObserver(MethodInfo clustering)
    {
        Type[] parameters = clustering.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        if (parameters.Length != 1) throw new InvalidDataException("Spectrum clustering observation boundary changed.");
        var observer = new DynamicMethod("observe_spectrum_claims", clustering.ReturnType, parameters, typeof(Program).Module, true);
        var il = observer.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(ObserveAssaySpectrumClaims), BindingFlags.NonPublic | BindingFlags.Static)!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, clustering); il.Emit(OpCodes.Ret);
        return observer;
    }

    private static AssaySpectrumProof ProveAssaySpectrumFacts(string frozenSource, string referenceApp, string output,
        IEnumerable<FeatureLedger.Observation> cohort)
    {
        var proof = new AssaySpectrumProof(new(StringComparer.Ordinal), new());
        const string source = "FamilyQuant/DeNovo/SpectrumComparisonTable.cs";
        var original = new Dictionary<string, string> { [source] = frozenSource };
        var selected = cohort.Where(row => AssaySpectrumFactIds.Contains(row.Id, StringComparer.Ordinal))
            .Select(row => (Row: row, Anchor: LedgerMemberFactComparison.Compare(row, frozenSource, original))).ToArray();
        bool anchored = selected.Length == 3 && selected.All(item => item.Row.Source == source && item.Anchor.Matched
            && item.Anchor.FrozenOccurrences == 1 && item.Anchor.FrozenMember.Contains("SpectrumComparisonTable::public static List < SpectrumComparisonRow > Build (", StringComparison.Ordinal))
            && selected.Select(item => item.Anchor.FrozenMember).Distinct(StringComparer.Ordinal).Count() == 1;
        Check("K9b scopes exactly three frozen Build literal observations", anchored);
        if (!anchored) return proof;
        string referencePath = Path.Combine(Path.GetFullPath(referenceApp), "FamilyQuant.dll");
        var legacy = Assembly.LoadFrom(referencePath);
        bool pinned = legacy.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal) == true;
        Check("K9b executes the pinned frozen source build", pinned);
        if (!pinned) return proof;
        var oldType = legacy.GetType("FamilyQuant.DeNovo.SpectrumComparisonTable", true)!;
        var newType = typeof(SpectrumComparisonTable);
        MethodInfo Build(Type type, int count) => type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Build" && method.GetParameters().Length == count);
        MethodInfo Csv(Type type) => type.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(method => method.Name == "ToCsv");
        var before = Build(oldType, 9); var after = Build(newType, 10);
        var injections = new List<SecondaryInjection>();
        DynamicMethod Observed(MethodInfo build)
        {
            var clustering = build.DeclaringType!.GetMethod("BuildClusters", BindingFlags.NonPublic | BindingFlags.Static)!;
            return SecondaryInject(build, new Dictionary<MethodBase, MethodInfo> { [clustering] = AssaySpectrumObserver(clustering) },
                injections, new Dictionary<MethodBase, int> { [clustering] = 1 });
        }
        var oldObserved = Observed(before); var newObserved = Observed(after);
        QuantRow Row(int scan, double score)
        {
            var row = Qr("identified", "fixture.raw", scan, 2, "ACDEFGHIK", 10, 20);
            row.PrecursorMz = 500.25; row.PatternLabXScore = score; return row;
        }
        var cases = new (string Name, QuantRow[] Rows)[]
        { ("single", new[] { Row(101, 12.375) }), ("multiple", new[] { Row(102, -1.25), Row(103, 0) }), ("empty", Array.Empty<QuantRow>()) };
        string folder = Path.Combine(output, "spectrum-native"); Directory.CreateDirectory(folder);
        var outcomes = new List<object>(); var witnessed = new List<AssaySpectrumClaim>();
        bool allEqual = true;
        object?[] Arguments(MethodInfo method, QuantRow[] rows, bool progress)
        {
            var parameters = method.GetParameters(); var values = new object?[parameters.Length];
            for (int index = 0; index < 3; index++) values[index] = Array.CreateInstance(parameters[index].ParameterType.GetGenericArguments()[0], 0);
            values[3] = LedgerLegacyValue(rows, parameters[3].ParameterType);
            for (int index = 4; index < values.Length; index++) values[index] = parameters[index].DefaultValue;
            if (values.Length == 10 && progress) values[9] = new FamilyQuant.Operations.OperationContext();
            return values;
        }
        string Write(MethodInfo method, object rows) => (string)Csv(method.DeclaringType!).Invoke(null, new[] { rows })!;
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (string language in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                foreach (var item in cases)
                {
                    var oldArgs = Arguments(before, item.Rows, false);
                    string frozen = Write(before, before.Invoke(null, oldArgs)!);
                    assaySpectrumClaims = new();
                    string frozenObserved = Write(before, oldObserved.Invoke(null, oldArgs)!);
                    var oldClaims = assaySpectrumClaims.ToArray();
                    for (int round = 0; round < 2; round++)
                    {
                        var newArgs = Arguments(after, item.Rows, round == 1);
                        string current = Write(after, after.Invoke(null, newArgs)!);
                        assaySpectrumClaims = new();
                        string currentObserved = Write(after, newObserved.Invoke(null, newArgs)!);
                        var claims = assaySpectrumClaims.ToArray();
                        bool same = frozen == current && frozenObserved == frozen && currentObserved == current
                            && claims.SequenceEqual(oldClaims) && claims.Length == item.Rows.Length
                            && claims.Zip(item.Rows).All(pair => !pair.First.External && pair.First.Scan == pair.Second.Scan
                                && pair.First.Score == pair.Second.PatternLabXScore && pair.First.ScoreField == "XCorr"
                                && pair.First.SourceId == "patternlab" && pair.First.SourceDisplayName == "PatternLab");
                        string name = language + "/" + item.Name + "/" + round.ToString(CultureInfo.InvariantCulture);
                        Check("K9b native Build claims and full CSV equal frozen/current with transparent observation " + name, same);
                        allEqual &= same; witnessed.AddRange(claims);
                        string prefix = Path.Combine(folder, language + "-" + item.Name + "-" + round.ToString(CultureInfo.InvariantCulture));
                        string oldFile = prefix + "-frozen.csv", newFile = prefix + "-current.csv";
                        File.WriteAllText(oldFile, frozen, new UTF8Encoding(false)); File.WriteAllText(newFile, current, new UTF8Encoding(false));
                        proof.Artifacts.Add(oldFile); proof.Artifacts.Add(newFile);
                        outcomes.Add(new { language, item.Name, progress = round == 1, Same = same, FrozenClaims = oldClaims, CurrentClaims = claims,
                            FrozenSha256 = FeatureLedgerEvidence.Hash(oldFile), CurrentSha256 = FeatureLedgerEvidence.Hash(newFile) });
                    }
                }
            }
        }
        finally { CultureInfo.CurrentCulture = saved; assaySpectrumClaims = null; }
        foreach (var item in selected)
        {
            string literal = JsonSerializer.Deserialize<string>(item.Row.Detail)!;
            string assertion = "K9b native fallback fact " + item.Row.Id + ": exact frozen/current claim field observed and full CSV bytes equal in both cultures with progress absent/present";
            bool present = allEqual && witnessed.Count > 0 && witnessed.All(claim => literal switch
            { "XCorr" => claim.ScoreField == literal, "patternlab" => claim.SourceId == literal, "PatternLab" => claim.SourceDisplayName == literal, _ => false });
            Check(assertion, present); if (present) proof.Assertions.Add(item.Row.Id, assertion);
        }
        foreach (var (name, value) in new (string, object)[] { ("outcomes.json", outcomes), ("injections.json", injections), ("inputs.json", cases.Select(item => new { item.Name, item.Rows }).ToArray()) })
        { string file = Path.Combine(folder, name); FeatureLedgerEvidence.WriteJson(file, value); proof.Artifacts.Add(file); }
        string frozenCopy = Path.Combine(folder, "frozen-SpectrumComparisonTable.cs"); File.WriteAllText(frozenCopy, frozenSource); proof.Artifacts.Add(frozenCopy);
        string assemblyCopy = Path.Combine(folder, "frozen-FamilyQuant.dll"); File.Copy(referencePath, assemblyCopy); proof.Artifacts.Add(assemblyCopy);
        return proof;
    }
}
