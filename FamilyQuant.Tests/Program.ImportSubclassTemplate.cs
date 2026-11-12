using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportSubclassTemplateCommands() =>
    [new("--test-import-subclass-template", 74, TestCommandPhase.Raw, args => args.Contains("--test-import-subclass-template", StringComparer.Ordinal), (args, _) =>
    {
        ImportSubclassTemplateTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "import-subclass-template"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];
    private static void ImportSubclassTemplateTests(string output)
    {
        Directory.CreateDirectory(output); string path = Path.Combine(output, "subclass-template.xlsx");
        PanelImportTemplate.Write(path); var reader = new PanelSpreadsheetReader();
        var blank = reader.ReadPreview(path, PanelImportTemplate.SubclassesSheet);
        string instructions = string.Join(" ", reader.ReadPreview(path, PanelImportTemplate.InstructionsSheet).Rows.SelectMany(row => row.Cells).Select(cell => cell.Text));
        Check("IM9 template exposes empty five-column Subclasses sheet and one note per header", blank.Rows.Count == 1
            && blank.Rows[0].Cells.Select(cell => cell.Text).SequenceEqual(new[] { "Family", "Subclass", "UniProt family or subfamily", "Pfam required", "Pfam absent" })
            && PanelImportTemplate.SubclassColumns.All(column => instructions.Contains(column.Header + ": " + column.Note, StringComparison.Ordinal)));
        FillTemplate(path, new[] { new[] { "A", "Family a", "", "", "" }, new[] { "B", "Family b", "", "", "" } });
        var source = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "sp|P10000|ENTRY", Description = "Database entry", Sequence = "PEPTIDE", Evidence = new()
            { Accession = "P10000", Pfam = new() { new() { Accession = "PF01234", Name = "Example left domain" }, new() { Accession = "PF05678", Name = "Example right domain" } } } },
        } };
        string before = source.ToJsonText(); var importer = new FamilyPanelSpreadsheetImporter();
        var names = new[] { new[] { "A", "First class", "Source one family", "", "" }, new[] { "A", "Second class", "Source two subfamily", "", "" } };
        FillSubclassTemplate(path, names);
        FamilyPanel? ReadPanel(string label, int expectedCount)
        {
            var proposal = importer.ProposeWorkbook(path);
            Check("IM9 " + label + " uses the existing subclass-table path without family-sheet ambiguity", proposal.SubclassTables.Count == 1
                && proposal.FamilySheet == PanelImportTemplate.FamilySheet && !proposal.NeedsFamilySheetChoice);
            var summary = WorkbookImportSummaries.Build(proposal, new PanelEvidenceMatching(source));
            var accepted = WorkbookImportSummaries.AcceptAll(summary, importer, summary.ReviewedSubclassSelections, summary.ProposedEvidenceSelections);
            Check("IM9 " + label + " imports the displayed subclass definitions", accepted.CanImport && accepted.Panel != null && accepted.Panel.Families.Sum(family => family.Subclasses.Count) == expectedCount,
                string.Join("; ", accepted.Items.SelectMany(item => item.Problems).Select(problem => problem.Message)));
            var injected = WorkbookImportSummaries.AcceptAll(summary, importer, summary.ReviewedSubclassSelections, summary.ProposedEvidenceSelections,
                buildDefinitions: (proposals, choices, keys) =>
                {
                    var built = PanelSubclassImport.BuildDefinitions(proposals, choices, keys);
                    if (built.Definitions.Count > 0) built.Definitions[0].Definition.UniProtSubfamilies.Add("Unsupported source family");
                    return built;
                });
            Check("IM9 " + label + " rejects a builder-injected UniProt rule", !injected.CanImport);
            return accepted.Panel;
        }
        var named = ReadPanel("name-only sheet", 2)?.Families.Single(family => family.Key == "A");
        Check("IM9 full family name and subfamily name split hierarchy evidence without stripping family", named?.Subclasses.Count == 2
            && named.Subclasses[0].UniProtSubfamilies.SequenceEqual(new[] { "Source one family" })
            && SubclassAssignment.ClassifyDirect(named, new ProteinEvidence { FamilyAnnotation = "Source one family" }).SubclassKey == "First class"
            && SubclassAssignment.ClassifyDirect(named, new ProteinEvidence { Subfamily = "Source two subfamily" }).SubclassKey == "Second class"
            && SubclassAssignment.ClassifyDirect(named, new ProteinEvidence { FamilyAnnotation = "Source one" }).Status == ProteinSubclassStatus.Unknown);
        FillSubclassTemplate(path, names.Concat(new[] { new[] { "B", "Left class", "", "PF01234", "PF05678" }, new[] { "B", "Right class", "", "PF05678", "" } }).ToArray());
        var domains = ReadPanel("name and Pfam sheet", 4)?.Families.Single(family => family.Key == "B");
        Check("IM9 required and absent Pfam columns split stored domain evidence", domains?.Subclasses.Count == 2
            && domains.Subclasses[0].RequiredPfam.SequenceEqual(new[] { "PF01234" }) && domains.Subclasses[0].AbsentPfam.SequenceEqual(new[] { "PF05678" })
            && SubclassAssignment.ClassifyDirect(domains, new ProteinEvidence { Fragment = false, Pfam = new() { new() { Accession = "PF01234" } } }).SubclassKey == "Left class"
            && SubclassAssignment.ClassifyDirect(domains, new ProteinEvidence { Pfam = new() { new() { Accession = "PF05678" } } }).SubclassKey == "Right class"
            && SubclassAssignment.ClassifyDirect(domains, new ProteinEvidence()).Status == ProteinSubclassStatus.Unknown);
        Check("IM9 importing subclass rules changes no source protein data", source.ToJsonText() == before);
    }
    private static void FillSubclassTemplate(string path, string[][] rows)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var workbook = document.WorkbookPart!;
        var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().Single(sheet => sheet.Name == PanelImportTemplate.SubclassesSheet);
        var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
        var worksheet = part.Worksheet!;
        var data = worksheet.GetFirstChild<SheetData>()!;
        foreach (var row in data.Elements<Row>().Where(row => (row.RowIndex?.Value ?? 0) > 1).ToArray()) row.Remove();
        for (int index = 0; index < rows.Length; index++)
        {
            uint number = (uint)index + 2;
            data.Append(new Row(rows[index].Select((text, column) => new Cell(new InlineString(new Text(text)))
            { CellReference = ((char)('A' + column)).ToString() + number, DataType = CellValues.InlineString })) { RowIndex = number });
        }
        worksheet.Save();
    }
}
