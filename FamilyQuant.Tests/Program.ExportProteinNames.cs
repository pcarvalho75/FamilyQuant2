using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ExportProteinNameCommands() =>
    [new("--verify-export-name-cleanup", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--verify-export-name-cleanup", (args, _) => VerifyExportNameCleanup(args)),
     new("--test-export-protein-names", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-export-protein-names", (_, _) =>
    {
        ExportProteinNameTests(); CsvTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static int VerifyExportNameCleanup(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply baseline CSV, current CSV and new verification JSON.");
        if (File.Exists(args[3])) throw new IOException("Use a new verification output.");
        int changedNames = 0, cleanNames = 0;
        string expected = Regex.Replace(File.ReadAllText(args[1]), @"(?m)^(-?\d+),""((?:[^""]|"""")*)""", match =>
        {
            string text = match.Groups[2].Value;
            int marker = text.IndexOf(" OS=", StringComparison.Ordinal);
            if (marker < 0) { cleanNames++; return match.Value; }
            changedNames++;
            return match.Groups[1].Value + ",\"" + text.Substring(0, marker) + "\"";
        }, RegexOptions.CultureInvariant);
        bool exact = Encoding.UTF8.GetBytes(expected).SequenceEqual(File.ReadAllBytes(args[2]));
        Check("J12 real golden: every byte outside the intended organism-name tails is identical", exact);
        Check("J12 real golden: the baseline actually contains source-name tails", changedNames > 0);
        File.WriteAllText(args[3], JsonSerializer.Serialize(new { BaselineSha256 = TestFileHash.Upper(args[1]),
            CurrentSha256 = TestFileHash.Upper(args[2]), NamesTrimmed = changedNames, AlreadyCleanNames = cleanNames,
            OnlyExpectedNameCleanup = exact }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static void ExportProteinNameTests()
    {
        const string source = "Protein, example (Fragment) OS=Source organism OX=42 GN=gene PE=1 SV=1";
        const string clean = " Custom, \"annotation\" (Fragment) ";
        var result = new FamilyEnvelopeQuantResult();
        result.Families.Add(new() { FamilyUserId = 1, Description = source, IdentifiedEnvelope = 1, UniqueIdentifiedEnvelope = 1, SplitIdentifiedEnvelope = 1 });
        result.Families.Add(new() { FamilyUserId = 2, Description = clean, IdentifiedEnvelope = 1, UniqueIdentifiedEnvelope = 1, SplitIdentifiedEnvelope = 1 });
        foreach (var policy in Enum.GetValues<SharedPeptidePolicy>())
        {
            string csv = CsvReport.BuildFamilyComposition(result, policy, 2, "measured signal");
            var rows = csv.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(ParseCsvLine).Where(r => r[0].Length > 0).ToArray();
            Check("J12: " + policy + " historical family export retains its complete description", rows.Single(r => r[0] == "1")[1] == source);
            Check("J12: " + policy + " already-clean names stay exact, including spaces and qualifiers", rows.Single(r => r[0] == "2")[1] == clean);
            string newCsv = CsvReport.BuildFamilyComposition(result, policy, 2, "measured signal", ProteinNameFormatting.ForNewOutput);
            var newRows = newCsv.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(ParseCsvLine).Where(r => r[0].Length > 0).ToArray();
            Check("J12: " + policy + " new-output name helper removes only the organism tail", newRows.Single(r => r[0] == "1")[1] == "Protein, example (Fragment)"
                && newRows.Single(r => r[0] == "2")[1] == clean);
        }
        Check("J12: presentation cleanup does not mutate stored evidence or labels", result.Families[0].Description == source && result.Families[1].Description == clean);
        var data = AnalyticsBuilder.Build(Array.Empty<QuantRow>(), new Dictionary<string, double>());
        var packet = Report.ReportCurrentResults.Build(data, new QuantManifest(), result);
        var reportTable = packet.Sections.SelectMany(s => s.Blocks).OfType<Report.ReportTable>().Single(t => t.Id == "composition-measured");
        Check("J12: the Word report table uses the shared cleaned protein name", reportTable.Rows.Single(r => r[0] == "1")[1] == "Protein, example (Fragment)");
        Check("J12: report companion data for the supplement contains the same cleaned names", !packet.CsvExports["family_composition_measured.csv"].Contains(" OS=", StringComparison.Ordinal)
            && Report.ReportCsvTable.Build("supplement", "Supplement", packet.CsvExports["family_composition_measured.csv"], "").Rows.Single(r => r[0] == "1")[1] == reportTable.Rows.Single(r => r[0] == "1")[1]);
        var auditFile = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "sp|P00001|ONE", Description = source } } };
        var auditRow = ParseCsvLine(PanelAuditCsv.BuildCsv(auditFile).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)[1]);
        Check("J12: panel audit uses the same helper and preserves protein qualifiers", auditRow[1] == "Protein, example (Fragment)");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            string first = CsvReport.BuildFamilyComposition(result, SharedPeptidePolicy.FamilyUnique, 2, "measured signal");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("J12: cleaned export bytes are independent of display culture", first == CsvReport.BuildFamilyComposition(result, SharedPeptidePolicy.FamilyUnique, 2, "measured signal"));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
