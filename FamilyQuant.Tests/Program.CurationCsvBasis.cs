using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string CurationPriorSignalBasis = "signal = identified observed envelope plus the de novo identified-reference, mass-calibrated estimate";
    private const string CurationMeasuredSignalBasis = "signal = identified observed envelope plus the de novo measured envelope";

    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationCsvBasisCommands() =>
    [new("--test-curation-csv-basis", 84, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-curation-csv-basis", (_, _) =>
        {
            CurationCsvBasisTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    // D95 changes this one basis-field phrase. Preserve every other byte, including numeric cells,
    // names, CSV quoting and line endings, against the actual frozen file.
    private static bool CurationCompositionBytesMatch(byte[] frozen, byte[] current)
    {
        var encoding = new UTF8Encoding(false, true);
        string expected = encoding.GetString(frozen);
        var table = ReportCsvTable.Build("curation-basis", "Curation basis", expected, "");
        int basis = table.Columns.ToList().IndexOf("basis");
        if (basis != table.Columns.Count - 1 || table.Rows.Count == 0) return false;
        string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        foreach (string value in table.Rows.Select(row => row[basis]).Distinct(StringComparer.Ordinal))
        {
            if (!value.Contains(CurationPriorSignalBasis, StringComparison.Ordinal)) return false;
            string oldCell = "," + Quote(value);
            string newCell = "," + Quote(value.Replace(CurationPriorSignalBasis, CurationMeasuredSignalBasis, StringComparison.Ordinal));
            expected = expected.Replace(oldCell + "\r\n", newCell + "\r\n", StringComparison.Ordinal)
                .Replace(oldCell + "\n", newCell + "\n", StringComparison.Ordinal);
        }
        return encoding.GetBytes(expected).SequenceEqual(current);
    }

    private static void CurationCsvBasisTests()
    {
        var result = new FamilyEnvelopeQuantResult();
        result.Families.Add(new() { FamilyUserId = 1, Description = "Synthetic, \"quoted\" member", UniqueIdentifiedEnvelope = 12.5 });
        string oldBasis = "fraction of the quantified signal; " + CurationPriorSignalBasis;
        string newBasis = "fraction of the quantified signal; " + CurationMeasuredSignalBasis;
        byte[] Write(string basis) => Encoding.UTF8.GetBytes(CsvReport.BuildFamilyComposition(result, SharedPeptidePolicy.FamilyUnique, 25, basis));
        byte[] frozen = Write(oldBasis), current = Write(newBasis);
        Check("L1 curation permits only the approved D95 basis-field wording", CurationCompositionBytesMatch(frozen, current));
        Check("L1 curation rejects a changed signal number", !CurationCompositionBytesMatch(frozen,
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(current).Replace("12.5", "12.6", StringComparison.Ordinal))));
        Check("L1 curation rejects a changed family name", !CurationCompositionBytesMatch(frozen,
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(current).Replace("Synthetic", "Changed", StringComparison.Ordinal))));
        Check("L1 curation rejects the obsolete model basis", !CurationCompositionBytesMatch(frozen, frozen));
        Check("L1 curation rejects an extra output line", !CurationCompositionBytesMatch(frozen, current.Concat(Encoding.UTF8.GetBytes("extra\n")).ToArray()));
        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("L1 curation D95 comparison stays invariant in pt-BR", current.SequenceEqual(Write(newBasis))
                && CurationCompositionBytesMatch(frozen, Write(newBasis)));
        }
        finally { CultureInfo.CurrentCulture = priorCulture; }
    }
}
