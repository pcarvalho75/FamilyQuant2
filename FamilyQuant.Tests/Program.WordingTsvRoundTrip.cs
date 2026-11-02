using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int WordingTsvRoundTripCommand(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply fresh output.");
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        string actual = FileContractStrings(typeof(FamilyClusterFile).Assembly.GetType("FamilyQuant.FamilyClusterScoring", true)!
            .GetMethod("DivideAndConquerDetailed")!).Single(value => value.Contains("Use a target-decoy database (.T-R)", StringComparison.Ordinal));
        int index = 0;
        foreach (string value in new[] { actual, "User \"quoted\" text", "first line\nsecond line\tvalue", "\"quoted field\"" })
        {
            string path = Path.Combine(output, "case-" + (++index).ToString(CultureInfo.InvariantCulture) + ".tsv");
            string[] fields = { "FQFloater", "FQ15-0DB3E899880B", "Prior \"text\"", value, "Roundtrip \"reason\"" };
            File.WriteAllLines(path, new[] { SecondaryWordingHeader, string.Join('\t', fields.Select(SecondaryWordingCell)) }, new UTF8Encoding(false));
            try
            {
                using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(path, Encoding.UTF8);
                parser.SetDelimiters("\t"); parser.HasFieldsEnclosedInQuotes = true; parser.TrimWhiteSpace = false;
                _ = parser.ReadFields(); var row = parser.ReadFields()!;
                Check("wording TSV native parser roundtrip " + index, row.Length == 5 && row[3] == value.Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal));
                var canonical = LedgerPauloRulings.ReadInputs(path).Single();
                Check("wording TSV ruling parser roundtrip " + index, canonical.Lane == fields[0] && canonical.Id == fields[1]
                    && canonical.OldText == fields[2] && canonical.NewText == value && canonical.Reason == fields[4]);
            }
            catch (Exception error) { Check("wording TSV both readers accept case " + index, false, error.Message); }
        }
        Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        return failed == 0 ? 0 : 1;
    }
}
