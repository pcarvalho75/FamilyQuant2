using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.IO;
using System.Linq;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void OpenXmlDependencyTests(string workDir)
        {
            string workbookPath = Path.Combine(workDir, "openxml-dependency.xlsx");
            const string text = "Decisão e anotação\nαβ";
            using (var workbook = SpreadsheetDocument.Create(workbookPath, SpreadsheetDocumentType.Workbook))
            {
                var main = workbook.AddWorkbookPart();
                main.Workbook = new Workbook();
                var sheet = main.AddNewPart<WorksheetPart>();
                sheet.Worksheet = new Worksheet(new SheetData(new Row(new Cell(
                    new InlineString(new Text(text))) { CellReference = "A1", DataType = CellValues.InlineString })));
                main.Workbook.Append(new Sheets(new Sheet { Id = main.GetIdOfPart(sheet), SheetId = 1, Name = "Panel" }));
            }
            using (var workbook = SpreadsheetDocument.Open(workbookPath, false))
                Check("J1b0: Open XML reads XLSX Unicode and line breaks", workbook.WorkbookPart!
                    .WorksheetParts.Single().Worksheet!.Descendants<Cell>().Single().InnerText == text);

            string documentPath = Path.Combine(workDir, "openxml-dependency.docx");
            using (var document = WordprocessingDocument.Create(documentPath, WordprocessingDocumentType.Document))
                document.AddMainDocumentPart().Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text(text)))));
            using (var document = WordprocessingDocument.Open(documentPath, false))
                Check("J1b0: Open XML writes readable DOCX text", document.MainDocumentPart!.Document!.InnerText == text);
            Check("J1b0: Open XML runtime resolves transitively through Core", File.Exists(Path.Combine(
                Path.GetDirectoryName(typeof(FamilyClusterFile).Assembly.Location)!, "DocumentFormat.OpenXml.dll")));
        }
    }
}
