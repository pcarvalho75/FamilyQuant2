using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FamilyQuant.Panels;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelTemplateCommandTable() =>
    [
        new("--test-panel-template", 76, TestCommandPhase.Raw,
            args => args.Contains("--test-panel-template", StringComparer.Ordinal), (_, _) =>
            {
                PanelTemplateTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void PanelTemplateTests()
    {
        var type = typeof(FamilyPanel).Assembly.GetType("FamilyQuant.Panels.FamilyPanelTemplate");
        var write = type?.GetMethod("Write", new[] { typeof(string), typeof(FamilyPanel) });
        var read = type?.GetMethod("Read", new[] { typeof(string) });
        Check("J1e versioned panel template reader and writer exist", write != null && read != null);
        if (write == null || read == null) return;
        var panel = new FamilyPanel { Name = "Panel, α", Levels = new() { new FamilyPanelText { Name = "Level", Text = "Value" } },
            TextFields = new() { new FamilyPanelText { Name = "Panel note", Text = "Text\nline" } },
            Import = new FamilyPanelImport { SourceName = "original.xlsx", SourceSha256 = "hash", FamilySheet = "Original" },
            DecisionMappings = new() { new FamilyPanelDecisionMapping { SourceValue = "keep", Decision = FamilyPanelDecision.Quantify } },
            EvidenceLinks = new() { new FamilyPanelEvidenceLink { SourceFamily = "source", ArchitectureKey = "architecture", PanelFamilyKey = "a", PanelDefinitionFingerprint = "proof", SupportingAccessions = new() { "P12345" } } },
            Families = new() { new FamilyPanelFamily { Key = "a", DisplayName = "Family, α", SourceDecision = "keep",
                Names = new() { new FamilyPanelText { Name = "alias", Text = "name" } },
                Pfam = new() { new FamilyPanelDomain { Accession = "PF00001", Name = "domain" } },
                TextFields = new() { new FamilyPanelText { Name = "note", Text = "a\nvalue" }, new FamilyPanelText { Name = "note", Text = "second" } },
                Subclasses = new() { new FamilyPanelSubclass { Key = "sub", DisplayName = "Subclass", RequiredPfam = new() { "PF00001" },
                    AbsentPfam = new() { "PF00002" }, UniProtSubfamilies = new() { "example" }, TextFields = new() { new FamilyPanelText { Name = "note", Text = "detail" } } } } } } };
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        try
        {
            write.Invoke(null, new object[] { path, panel });
            Check("J1e writer marks its own template", FamilyPanelTemplate.IsTemplate(path));
            var tooLong = FamilyPanelValidation.Clone(panel);
            tooLong.Families[0].TextFields[0].Text = new string('x', 32768);
            string longPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            bool longRefused = false;
            try { FamilyPanelTemplate.Write(longPath, tooLong); } catch (InvalidDataException) { longRefused = true; }
            finally { if (File.Exists(longPath)) File.Delete(longPath); }
            Check("J1e refuses values exceeding Excel's cell limit", longRefused);
            var roundtrip = (FamilyPanel)read.Invoke(null, new object[] { path })!;
            Check("J1e template round-trips the complete panel", JsonSerializer.Serialize(roundtrip) == JsonSerializer.Serialize(panel));
            using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, true))
            {
                var wb = document.WorkbookPart!;
                var sheet = wb.Workbook!.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Single(x => x.Name == "Families");
                var part = (DocumentFormat.OpenXml.Packaging.WorksheetPart)wb.GetPartById(sheet.Id!);
                var row = part.Worksheet!.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().ElementAt(2);
                row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().ElementAt(2).InlineString!.Text!.Text = "Edited family";
                var header = part.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().ElementAt(1);
                header.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell(new DocumentFormat.OpenXml.Spreadsheet.InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("Extra note")))
                    { CellReference = "I2", DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString });
                row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell(new DocumentFormat.OpenXml.Spreadsheet.InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("new text")))
                    { CellReference = "I3", DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString });
                part.Worksheet.Save();
            }
            var edited = (FamilyPanel)read.Invoke(null, new object[] { path })!;
            Check("J1e template imports edited visible cells while preserving hidden details",
                edited.Families[0].DisplayName == "Edited family" && edited.Families[0].Names[0].Text == "name"
                && edited.Families[0].Subclasses[0].RequiredPfam[0] == "PF00001" && edited.Import!.SourceName == "original.xlsx");
            Check("J1e retains a user-added text column", edited.Families[0].TextFields.Any(x => x.Name == "Extra note" && x.Text == "new text"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
