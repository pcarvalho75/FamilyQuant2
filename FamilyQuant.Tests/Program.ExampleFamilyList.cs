using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FamilyQuant.Examples;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ExampleFamilyListCommands() =>
    [
        new("--write-example-family-list", 10100, TestCommandPhase.Raw, args => args.Contains("--write-example-family-list", StringComparer.Ordinal), (args, _) =>
        {
            int at = Array.IndexOf(args, "--write-example-family-list");
            ExampleFamilyListTests(at + 1 < args.Length && args[at + 1] != "." ? args[at + 1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    /// <summary>Z12p2c: the invented Lantern viper family list keeps the shape and the quirks of a real consensus
    /// workbook, reads back with the panel spreadsheet reader, and is rebuilt the same way each time. With
    /// <paramref name="output"/> a copy is written there for people to open.</summary>
    private static void ExampleFamilyListTests(string? output = null)
    {
        Section("Example family list for the import tutorial (Z12p2c)");
        string folder = Path.Combine(Path.GetTempPath(), "example-family-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string titled = Path.Combine(folder, LanternViperFamilyList.DefaultFileName), plain = Path.Combine(folder, "plain.xlsx"),
                again = Path.Combine(folder, "again.xlsx");
            LanternViperFamilyList.Write(titled);
            LanternViperFamilyList.Write(plain, titleRow: false);
            LanternViperFamilyList.Write(again);
            var reader = new PanelSpreadsheetReader();
            var sheets = reader.ReadSheetNames(titled);
            Check("six sheets, the families first", sheets.Count == 6 && sheets[0] == LanternViperFamilyList.FamilySheetName
                && sheets.Select(s => s[..2]).SequenceEqual(new[] { "1.", "2.", "3.", "4.", "5.", "6." }), string.Join(" | ", sheets));
            var table = reader.ReadTable(titled, sheets[0], LanternViperFamilyList.FamilyHeaderRow());
            Check("the header row sits below a title and a subtitle (row 3) with 15 accented headers",
                LanternViperFamilyList.FamilyHeaderRow() == 3 && table.Headers.Count == 15 && table.Headers[0] == "Sigla"
                && table.Headers.Contains("Decisão FamilyQuant") && table.Headers.Contains("Domínio(s) Pfam")
                && table.Headers.Contains("Nome (português)") && table.Headers.Contains("Observações"), string.Join(" | ", table.Headers));
            string Cell(PanelSpreadsheetRow row, string header) => row.Cells[header];
            var keys = table.Rows.Select(r => Cell(r, "Sigla")).ToList();
            Check("about forty families: 38 rows, one of them naming four", table.Rows.Count == 38
                && table.Rows.Any(r => Cell(r, "Nome (português)").Count(c => c == ';') >= 3));
            Check("one abbreviation is repeated", keys.GroupBy(k => k).Count(g => g.Count() > 1) == 1
                && keys.Count(k => k == "CTL") == 2);
            Check("one empty row interrupts the list", table.Rows.Zip(table.Rows.Skip(1), (a, b) => b.RowNumber - a.RowNumber).Count(d => d == 2) == 1
                && table.Rows.Zip(table.Rows.Skip(1), (a, b) => b.RowNumber - a.RowNumber).All(d => d is 1 or 2));
            var decisions = table.Rows.Select(r => Cell(r, "Decisão FamilyQuant")).Distinct().ToList();
            Check("the decision column holds free-text labels, some with words in parentheses",
                decisions.Count >= 8 && decisions.Count(d => d.Contains('(')) >= 2, string.Join(" | ", decisions));
            var pfam = new Regex(@"^(sem domínio Pfam|[A-Za-z0-9_\-]+ \(PF\d{5}\)(; [A-Za-z0-9_\-]+ \(PF\d{5}\))*)$");
            Check("Pfam cells are written as Name (PFnnnnn) joined by semicolons, or say there is none",
                table.Rows.All(r => pfam.IsMatch(Cell(r, "Domínio(s) Pfam"))),
                string.Join(" | ", table.Rows.Select(r => Cell(r, "Domínio(s) Pfam")).Where(v => !pfam.IsMatch(v))));
            Check("some cells keep line breaks", table.Rows.Any(r => r.Cells.Values.Any(v => v.Contains('\n'))));
            Check("the plain variant starts with the same header row", reader.ReadTable(plain, sheets[0], 1).Headers.SequenceEqual(table.Headers)
                && reader.ReadTable(plain, sheets[0], 1).Rows.Count == table.Rows.Count);
            var second = reader.ReadTable(again, sheets[0], 3);
            Check("written twice, the content is the same", second.Rows.Count == table.Rows.Count
                && second.Rows.Zip(table.Rows).All(p => p.First.RowNumber == p.Second.RowNumber
                    && p.First.Cells.OrderBy(c => c.Key).SequenceEqual(p.Second.Cells.OrderBy(c => c.Key))));

            using (var document = SpreadsheetDocument.Open(titled, false))
            {
                var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document).ToList();
                Check("the workbook passes the Open XML validator, so spreadsheet programs open it cleanly", errors.Count == 0,
                    string.Join(" | ", errors.Take(3).Select(e => e.Description)));
                var workbook = document.WorkbookPart!;
                var first = (WorksheetPart)workbook.GetPartById(workbook.Workbook!.Sheets!.Elements<Sheet>().First().Id!.Value!);
                var merges = first.Worksheet!.GetFirstChild<MergeCells>()?.Elements<MergeCell>().Select(m => m.Reference!.Value).ToList() ?? new();
                Check("the title and the subtitle are merged across the 15 columns", merges.SequenceEqual(new[] { "A1:O1", "A2:O2" }), string.Join(",", merges));
                var formats = workbook.WorkbookStylesPart!.Stylesheet!.CellFormats!.Elements<CellFormat>().ToList();
                var fills = workbook.WorkbookStylesPart.Stylesheet!.Fills!.Elements<Fill>().ToList();
                string? FillOf(Cell cell)
                {
                    uint id = formats[(int)(cell.StyleIndex?.Value ?? 0)].FillId?.Value ?? 0;
                    return fills[(int)id].PatternFill?.ForegroundColor?.Rgb?.Value;
                }
                var keyCells = first.Worksheet!.Descendants<Row>().Where(r => r.RowIndex!.Value > 3)
                    .Select(r => r.Elements<Cell>().FirstOrDefault(c => c.CellReference!.Value!.StartsWith("A", StringComparison.Ordinal)))
                    .Where(c => c != null).Select(c => FillOf(c!)).ToList();
                Check("the abbreviation cells are coloured by decision, in several colours",
                    keyCells.Count == 38 && keyCells.All(f => f != null) && keyCells.Distinct().Count() >= 5, string.Join(",", keyCells.Distinct()));
                Check("no fill is yellow or gold", fills.Select(f => f.PatternFill?.ForegroundColor?.Rgb?.Value).OfType<string>()
                    .All(argb => !(Convert.ToInt32(argb.Substring(2, 2), 16) > 200 && Convert.ToInt32(argb.Substring(4, 2), 16) > 180
                        && Convert.ToInt32(argb.Substring(6, 2), 16) < 120)));
            }
            var legend = reader.ReadTable(titled, sheets[5], 1);
            Check("the legend explains every decision label and says the sheet is invented",
                decisions.All(d => legend.Rows.Any(r => r.Cells.Values.First() == d))
                && legend.Rows.Any(r => r.Cells.Values.Any(v => v.Contains("inventado", StringComparison.Ordinal))));
            var references = reader.ReadTable(titled, sheets[4], 1);
            Check("every reference is marked fictitious", references.Headers.Any(h => h.Contains("fictícia", StringComparison.Ordinal))
                && references.Rows.All(r => r.Cells.Values.Any(v => v.Contains("Fictíci", StringComparison.Ordinal) || v.Contains("Exemplo", StringComparison.Ordinal))));
            string allText = string.Join("\n", sheets.Select((s, i) => reader.ReadTable(titled, s, i == 0 ? 3 : 1))
                .SelectMany(t => t.Headers.Concat(t.Rows.SelectMany(r => r.Cells.Values))));
            Check("no em or en dash anywhere in the workbook", !allText.Contains('–') && !allText.Contains('—'));
            if (output != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                LanternViperFamilyList.Write(output);
                Check("a copy was written for people to open", File.Exists(output) && reader.ReadTable(output, sheets[0], 3).Rows.Count == 38, output);
            }
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
