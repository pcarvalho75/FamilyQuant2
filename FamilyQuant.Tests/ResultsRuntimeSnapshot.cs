using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

// The same source is compiled against the frozen application and the current application.
// These deliberately artificial presentation inputs are never scientific result files.
internal static class ResultsRuntimeSnapshot
{
    internal const string Argument = "--results-runtime-snapshot";
    internal sealed record Value(string Key, string Text);

    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply a fresh snapshot path.");
        if (File.Exists(args[1])) throw new IOException("Snapshot must be fresh.");
        var values = new List<Value>();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                for (int tic = 0; tic < 3; tic++)
                for (int family = 0; family < 3; family++)
                for (int under = 0; under < 3; under++)
                for (int provenance = 0; provenance < 2; provenance++)
                {
                    var data = new AnalyticsData();
                    data.Peptides.Add(new AnalyticsPeptide { Side = "identified" });
                    data.Peptides.Add(new AnalyticsPeptide { Side = "denovo" });
                    data.Peptides.Add(new AnalyticsPeptide { Side = "denovo" });
                    data.Fractions.Add(new NativeRunFraction { Run = "fixture 1" });
                    data.Fractions.Add(new NativeRunFraction { Run = "fixture 2" });
                    data.Grouped = tic == 0 ? null : new NativeRunFraction
                    {
                        Tic = tic == 1 ? 0 : 10000, IdentifiedEnvelope = 2468.5, DeNovoEnvelope = 1234.25,
                        IdentifiedPeptides = 1234, DeNovoPeptides = 2345,
                        IdentifiedUnderSampled = under == 0 ? 0 : 75, DeNovoUnderSampled = under == 0 ? 0 : 50,
                        IdentifiedUnderSampledPeptides = under == 0 ? 0 : 2,
                        DeNovoUnderSampledPeptides = under == 0 ? 0 : 3, UnderSampledIsEstimate = under == 2
                    };
                    var families = family == 0 ? null : new FamilyEnvelopeQuantResult
                    {
                        MeasuredIdentifiedEnvelope = 800, MeasuredDeNovoEnvelope = 200,
                        SharedIdentifiedEnvelope = 70, SharedDeNovoEnvelope = 30,
                        UnattributedIdentifiedEnvelope = 40, UnattributedDeNovoEnvelope = 10
                    };
                    if (family == 2)
                        for (int i = 0; i < 1234; i++) families!.Families.Add(new FamilyEnvelopeQuantRow());
                    QuantManifest? manifest = provenance == 0 ? null : new QuantManifest
                    {
                        DeNovoInputs = new List<DeNovoInputManifest>
                        {
                            new() { SourceId = "fixture-b", SourceDisplayName = "Fixture B", SourcePriority = 2,
                                ConfidenceField = "fixture confidence", MinimumConfidence = .875 },
                            new() { SourceId = "fixture-a", SourceDisplayName = "Fixture A", SourcePriority = 1,
                                ConfidenceField = "fixture score", MinimumConfidence = 12.5 }
                        },
                        DeNovoUnionResolutionMethod = "fixture union",
                        DeNovoIdentification = new DeNovoIdentificationSummary
                        { FilteredSpectrumCalls = 4567, CarriedForwardDistinctSequencesIgnoringPtm = 1234 }
                    };
                    Type owner = typeof(AnalyticsControl).Assembly.GetType("FamilyQuant.AnalyticsViewModel")
                        ?? typeof(AnalyticsControl);
                    var summary = owner.GetMethod("SummaryLine", BindingFlags.NonPublic | BindingFlags.Static)!;
                    string text = (string)summary.Invoke(null, new object?[]
                        { data, families, family == 0 ? null : new FamilyClusterFile(), manifest })!;
                    values.Add(new($"summary/{culture}/tic={tic}/family={family}/under={under}/sources={provenance}", text));
                }
                foreach (NormalizationMode mode in Enum.GetValues<NormalizationMode>())
                {
                    values.Add(new($"column/{culture}/{(int)mode}", Header("ColumnHeader", mode)));
                    values.Add(new($"csv-header/{culture}/{(int)mode}", Header("CsvHeader", mode)));
                }
                values.Add(new($"csv-header/{culture}/unknown", Header("CsvHeader", null)));
                var number = typeof(AnalyticsCsv).GetMethod("Num", BindingFlags.NonPublic | BindingFlags.Static)!;
                double[] numbers = { 0, -0.0, .1, -1234.56789, double.Epsilon, double.MaxValue, double.NaN,
                    double.NegativeInfinity, double.PositiveInfinity, 1.2345678901234567e-100 };
                for (int i = 0; i < numbers.Length; i++)
                    values.Add(new($"csv-number/{culture}/{i}", (string)number.Invoke(null, new object[] { numbers[i] })!));
                var field = typeof(AnalyticsCsv).GetMethod("Field", BindingFlags.NonPublic | BindingFlags.Static)!;
                string?[] fields = { null, "", "plain", "a,b", "a\"b", "a\nb", "a\rb", "a,\"b\r\nc" };
                for (int i = 0; i < fields.Length; i++)
                    values.Add(new($"csv-field/{culture}/{i}", (string)field.Invoke(null, new object?[] { fields[i] })!));
                var key = typeof(AnalyticsBuilder).GetMethod("KeyOf", BindingFlags.NonPublic | BindingFlags.Static)!;
                foreach (string run in new[] { "fixture", "fixture2" })
                foreach (string side in new[] { "identified", "denovo" })
                foreach (int charge in new[] { 2, 3 })
                    values.Add(new($"key/{culture}/{run}/{side}/{charge}", (string)key.Invoke(null,
                        new object[] { run, side, "PEPTIDE", charge })!));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        File.WriteAllText(args[1], JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
        Console.WriteLine("Recorded " + values.Count.ToString(CultureInfo.InvariantCulture) + " native presentation values.");
        return 0;
    }

    private static string Header(string method, NormalizationMode? mode)
    {
        var member = typeof(NormalizationModeExtensions).GetMethod(method)!;
        // Only the retained User-family branch is compared. No USearch branch is inferred.
        object?[] arguments = member.GetParameters().Length == 1 ? new object?[] { mode }
            : new[] { (object?)mode, Enum.Parse(member.GetParameters()[1].ParameterType, "User") };
        return (string)member.Invoke(null, arguments)!;
    }
}
