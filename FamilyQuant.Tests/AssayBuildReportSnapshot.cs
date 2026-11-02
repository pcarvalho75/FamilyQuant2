using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static class AssayBuildReportSnapshot
{
    internal const string Argument = "--assay-build-report-snapshot";
    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply a fresh output folder.");
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output); CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var assembly = typeof(USearchLib).Assembly;
        var method = assembly.GetType("FamilyQuant.BuildReportFormatter")?.GetMethod("Format", BindingFlags.Public | BindingFlags.Static)
            ?? typeof(USearchLib).GetMethod("FormatScanReport", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Product identity report formatter is absent.");
        IdentityScanRow Row(int id, int groups, int targets, int mixed) => new()
        { Identity = id, Groups = groups, TargetGroups = targets, MixedGroups = mixed, Score = groups > 0 ? (double)mixed / groups : 0,
            TargetRelativeScore = targets > 0 ? (double)mixed / targets : 0 };
        var detailed = Row(85, 1234, 1230, 2);
        detailed.MixedFamilies.Add(new MixedFamily { FamilyId = 17, Targets = new() { "synthetic target (9 aa)" }, Decoys = new() { "synthetic decoy" } });
        var fixtures = new SortedDictionary<string, List<IdentityScanRow>>(StringComparer.Ordinal)
        {
            ["empty"] = new(), ["no-decoys"] = new() { Row(85, 1234, 1234, 0) },
            ["clean-and-mixed"] = new() { Row(90, 1500, 1490, 0), detailed },
            ["all-mixed"] = new() { detailed, Row(80, 1200, 1195, 3) }
        };
        var reports = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var fixture in fixtures) reports[fixture.Key] = (string)method.Invoke(null, new object[] { fixture.Value })!;
        File.WriteAllText(Path.Combine(output, "reports.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("PASS native identity reports: " + reports.Count.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}
