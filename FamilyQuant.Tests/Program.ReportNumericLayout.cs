using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportNumericLayoutCommands() =>
    [new("--test-report-numeric-layout", 441, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-report-numeric-layout", (args, _) =>
        {
            ReportNumericLayoutTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "report-numeric-layout"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReportNumericLayoutTests(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        var dense = new ReportTable("exact-values", "Exact values", new[] { "Label", "Mean", "Spread", "Signal", "Ratio" },
            new IReadOnlyList<string>[]
            {
                new[] { "First observation", "0.12345678901234568", "-0.23456789012345678", "123456789012345680", "1.23456789012345E-120" },
                new[] { "Second observation", "unavailable", "0.0000000000000001", "987654321098765430", "9.87654321098765E+120" },
            }, "Exact stored values.");
        var mixed = new ReportTable("mixed-values", "Mixed availability", new[] { "Label", "Value", "Count", "Evidence" },
            new IReadOnlyList<string>[]
            {
                new[] { "Full range", "-1.7976931348623157E+308", "12345", "Stored value with a narrative explanation." },
                new[] { "Missing observation", "unavailable", "0", "No stored value." },
            }, "Availability is not a numeric zero.");
        var document = new ReportDocument { Sections = new[] { new ReportSection("numeric-layout", "Numeric layout", new ReportBlock[] { dense, mixed }) } };
        string[][] original = new[] { dense, mixed }.SelectMany(table => table.Rows).Select(row => row.ToArray()).ToArray();
        byte[]? previous = null;
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                byte[] bytes = WordReportWriter.BuildBytes(document);
                File.WriteAllBytes(Path.Combine(output, culture + ".docx"), bytes);
                using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
                Check("J34e exact-number document satisfies the Word schema " + culture, !new OpenXmlValidator().Validate(package).Any());
                XDocument xml = XDocument.Parse(package.MainDocumentPart!.Document!.OuterXml);
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var tables = xml.Descendants(w + "tbl").ToArray();
                var printed = tables.SelectMany(table => table.Elements(w + "tr").Skip(1))
                    .Select(row => row.Elements(w + "tc").Select(cell => string.Join("\n", cell.Elements(w + "p")
                        .Select(paragraph => string.Concat(paragraph.Descendants(w + "t").Select(text => text.Value))))).ToArray()).ToArray();
                Check("J34e all exact values and exponents remain unchanged " + culture,
                    printed.Length == original.Length && printed.Zip(original).All(pair => pair.First.SequenceEqual(pair.Second)));
                var fits = new List<bool>();
                var sizes = new List<double>();
                foreach (var table in tables)
                {
                    int[] widths = table.Element(w + "tblGrid")!.Elements(w + "gridCol").Select(column => (int)column.Attribute(w + "w")!).ToArray();
                    Check("J34e numeric table fits the printable page " + culture, widths.Sum() == 9360 && widths.All(width => width > 200));
                    foreach (var row in table.Elements(w + "tr").Skip(1))
                        foreach (var (cell, column) in row.Elements(w + "tc").Select((cell, column) => (cell, column)))
                            foreach (var paragraph in cell.Elements(w + "p"))
                            {
                                string text = string.Concat(paragraph.Descendants(w + "t").Select(value => value.Value));
                                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) continue;
                                var run = paragraph.Element(w + "r")!;
                                double points = ((double?)run.Element(w + "rPr")?.Element(w + "sz")?.Attribute(w + "val") ?? 22) / 2;
                                var face = new Typeface(new FontFamily("Calibri"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                                double measured = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                    face, points * 96 / 72, Brushes.Black, 1).WidthIncludingTrailingWhitespace * 72 / 96;
                                fits.Add(measured + 10 <= widths[column] / 20.0);
                                sizes.Add(points);
                            }
                }
                Check("J34e every complete exact number including signed exponents fits its cell " + culture, fits.Count == 10 && fits.All(value => value));
                Check("J34e dense numeric text remains readable " + culture, sizes.All(size => size >= 8));
                Check("J34e layout does not mutate source cells " + culture,
                    new[] { dense, mixed }.SelectMany(table => table.Rows).Zip(original).All(pair => pair.First.SequenceEqual(pair.Second)));
                if (previous != null) Check("J34e exact-number document repeats across cultures", bytes.SequenceEqual(previous));
                previous = bytes;
            }
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
}
