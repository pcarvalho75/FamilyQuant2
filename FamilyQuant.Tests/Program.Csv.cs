using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        // ================================================================= CSV

        private static void CsvTests()
        {
            var items = new List<FastaItemExtended>
            {
                new FastaItemExtended
                {
                    SequenceIdentifier = "sp|P00001|TEST",
                    Description = "Metalloproteinase, jararhagin-like \"partial\"",
                    UserAnnotation = "checked, twice",
                    USearchID = 3,
                    UserID = 7,
                    XIC = 0.5,
                    XICUser = 0.25
                }
            };

            // A machine whose decimal separator is a comma - Spanish, Portuguese, most of Europe.
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");
            string csv;
            try { csv = CsvReport.Build(items); }
            finally { CultureInfo.CurrentCulture = previous; }

            var lines = csv.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            Check("header plus one row", lines.Length == 2, $"got {lines.Length}");

            var row = ParseCsvLine(lines[1]);

            // Eight columns: the .plp share and, since 2026-10-25, the full-XIC share beside it.
            Check("header has 8 columns", ParseCsvLine(lines[0]).Count == 8);
            Check("row still has 8 columns under a comma locale", row.Count == 8, $"got {row.Count}");
            Check("the share uses a dot decimal separator", row[5] == "0.25", row[5]);
            Check("the full-XIC share is empty and its heading says so when nothing was computed",
                row[6] == string.Empty && ParseCsvLine(lines[0])[6] == CsvReport.NoFullXicHeader, row[6]);
            Check("a comma inside the description stays in one field", row[7].StartsWith("Metalloproteinase, jararhagin"), row[7]);
            Check("quotes inside the description are readable", row[7].Contains("\"partial\""), row[7]);
            Check("a comma inside the annotation stays in one field", row[2] == "checked, twice", row[2]);
            Check("integer columns are unaffected", row[0] == "3" && row[1] == "7");

            string empty = CsvReport.Build(new List<FastaItemExtended>());
            Check("an empty grid still writes a usable header",
                ParseCsvLine(empty.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)[0]).Count == 8);

            items[0].FullXicShare = 0.125;
            string withFullXic;
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");
            try { withFullXic = CsvReport.Build(items, null, "Full XIC family share, fraction 0-1 of the quantified signal"); }
            finally { CultureInfo.CurrentCulture = previous; }
            List<string> fxRow = ParseCsvLine(withFullXic.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)[1]);
            Check("a computed full-XIC share is written invariantly under a comma locale, with its heading",
                fxRow[6] == "0.125" && withFullXic.Contains("of the quantified signal", StringComparison.Ordinal), fxRow[6]);
        }

        /// <summary>Minimal RFC 4180 reader, so the assertions see the file as a spreadsheet would.</summary>
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c != '"') current.Append(c);
                    else if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }

            fields.Add(current.ToString());
            return fields;
        }
    }
}
