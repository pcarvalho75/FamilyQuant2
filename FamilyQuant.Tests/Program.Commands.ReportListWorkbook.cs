using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportListWorkbookCommands() =>
    [
        new("--write-report-list-workbook", 444, TestCommandPhase.Raw,
            args => args.Length == 4 && args[0] == "--write-report-list-workbook", (args, _) =>
            {
                string project = Path.GetFullPath(args[1]), native = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
                Directory.CreateDirectory(output);
                string before = FileFingerprint.Sha256(project);
                var files = Directory.EnumerateFiles(native, "*.csv").Order(StringComparer.Ordinal).ToArray();
                if (files.Length == 0) throw new InvalidDataException("No native CSV exports were supplied.");
                var hashes = files.ToDictionary(p => p, FileFingerprint.Sha256, StringComparer.Ordinal);
                Console.WriteLine("Reading stored project only; no attribution, figures or RAW access.");
                var (file, _, manifest) = ProjectStore.ReadFqproj(project);
                FamilyStudyMembership.RequireCurrent(file, manifest);
                var document = ReportDocumentBuilder.Build(file, manifest);
                var exports = files.ToDictionary(p => Path.GetFileName(p), File.ReadAllText, StringComparer.Ordinal);
                var layout = ReportSupplementLayout.Apply(document, exports);
                var complete = ReportListSheets.Complete(ReportTableSupplements.Complete(layout.Document, layout.Sheets));
                string target = Path.Combine(output, "stored-native-tables_supplement.xlsx");
                Console.WriteLine("Writing native CSV tables and methods to " + target);
                ReportSupplementWorkbook.Write(target, layout.Document, layout.Sheets);
                int lists = 0, itemRows = 0;
                using (var package = SpreadsheetDocument.Open(target, false))
                {
                    var workbook = package.WorkbookPart!;
                    var root = workbook.Workbook ?? throw new InvalidDataException("The generated workbook root is missing.");
                    var sheets = root.Sheets!.Elements<S.Sheet>().ToDictionary(s => s.Name!.Value!, StringComparer.Ordinal);
                    foreach (var expected in complete.Where(s => s.SourceTableId != null))
                    {
                        var part = (WorksheetPart)workbook.GetPartById(sheets[expected.Name].Id!.Value!);
                        using var reader = OpenXmlReader.Create(part);
                        int row = -1;
                        while (reader.Read())
                            if (reader.ElementType == typeof(S.Row) && reader.IsStartElement)
                            {
                                var element = reader.LoadCurrentElement() as S.Row ?? throw new InvalidDataException("A generated item row is missing.");
                                if (row >= 0)
                                {
                                    var cells = element.Elements<S.Cell>().Select(c => SpreadsheetText(c.InlineString?.InnerText ?? c.CellValue?.Text ?? "")).ToArray();
                                    if (row >= expected.Table.Rows.Count || !cells.SequenceEqual(expected.Table.Rows[row]))
                                        throw new InvalidDataException("A list-sheet item or owner key differs from its native source.");
                                }
                                row++;
                            }
                        if (row != expected.Table.Rows.Count) throw new InvalidDataException("A list sheet lost source items.");
                        lists++; itemRows += row;
                    }
                }
                bool unchanged = before == FileFingerprint.Sha256(project) && hashes.All(p => FileFingerprint.Sha256(p.Key) == p.Value);
                if (!unchanged) throw new InvalidDataException("The workbook proof changed its project or native CSV source.");
                File.WriteAllText(Path.Combine(output, "proof.json"), JsonSerializer.Serialize(new
                { ProjectSha256 = before, ProjectAndCsvUnchanged = unchanged, NativeCsvFiles = files.Length, ListSheets = lists, ItemRows = itemRows,
                    Workbook = target, WorkbookSha256 = FileFingerprint.Sha256(target), Mode = "Stored native CSV tables and project methods; no attribution or figure rendering" },
                    new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("PASS: " + lists.ToString(CultureInfo.InvariantCulture) + " list sheets; " + itemRows.ToString(CultureInfo.InvariantCulture)
                    + " item/owner rows match exactly; source project and every native CSV unchanged.");
                return 0;
            }),
    ];
}
