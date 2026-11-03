using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Report;

/// <summary>Literal native-export cells, one selected table per sheet. Names are the citation
/// contract supplied by the document builder and are never silently changed. Contents is metadata.
/// No formula, numerical conversion, quantity or classification is produced by this writer.</summary>
public static class ReportSupplementWorkbook
{
    public const string ContentsName = "Contents";
    private const int MaxColumns = 16384, MaxDataRows = 1048575, MaxCellCharacters = 32767;
    private static readonly DateTimeOffset PackageDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Regex LiteralEscape = new("_(?=x[0-9a-fA-F]{4}_)", RegexOptions.CultureInvariant);

    public static string PathForReport(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        string full = Path.GetFullPath(reportPath);
        return Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + "_supplement.xlsx");
    }

    public static byte[] BuildBytes(ReportDocument document, IReadOnlyList<ReportSupplementSheet> sheets,
        ReportSelection? selection = null) => Build(document, sheets, selection, null);

    public static byte[] BuildBytes(ReportDocument document, IReadOnlyList<ReportSupplementSheet> sheets,
        ReportSelection? selection, OperationContext operation) => Build(document, sheets, selection, operation);

    public static void Write(string path, ReportDocument document, IReadOnlyList<ReportSupplementSheet> sheets,
        ReportSelection? selection = null, OperationContext? operation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use an .xlsx file name for the supplement workbook.", nameof(path));
        byte[] bytes = Build(document, sheets, selection, operation);
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            operation?.Report("supplement-publish", "Publishing the completed supplement workbook", 1, 1);
            void Publish() => File.Move(temporary, full, true);
            if (operation == null) Publish(); else operation.Commit(Publish);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static byte[] Build(ReportDocument document, IReadOnlyList<ReportSupplementSheet> sheets,
        ReportSelection? selection, OperationContext? operation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sheets);
        operation?.ThrowIfCancellationRequested();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in document.Sections)
            if (string.IsNullOrWhiteSpace(section.Id) || !owners.TryAdd(section.Id, section.Title))
                throw new ArgumentException("Report section ids must be nonempty and unique.", nameof(document));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ContentsName };
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sheet in sheets)
        {
            if (sheet == null || string.IsNullOrWhiteSpace(sheet.Id) || !ids.Add(sheet.Id))
                throw new ArgumentException("Supplement ids must be nonempty and unique.", nameof(sheets));
            if (!owners.ContainsKey(sheet.OwnerSectionId)) throw new ArgumentException("A supplement sheet refers to an unknown report section.", nameof(sheets));
            ValidateName(sheet.Name);
            if (!names.Add(sheet.Name)) throw new ArgumentException("Supplement worksheet names must be unique, including Contents.", nameof(sheets));
            if (sheet.Table == null) throw new ArgumentException("A supplement sheet requires its source table.", nameof(sheets));
        }
        var excluded = new HashSet<string>(selection?.ExcludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var included = sheets.Where(sheet => !excluded.Contains(sheet.Id) && !excluded.Contains(sheet.OwnerSectionId)).ToArray();
        var index = new ReportTable("supplement-index", ContentsName,
            new[] { "Worksheet", "Report section", "Table", "Table id", "Data rows" },
            included.Select(sheet => (IReadOnlyList<string>)new[] { sheet.Name, owners[sheet.OwnerSectionId], sheet.Table.Title,
                sheet.Id, sheet.Table.Rows.Count.ToString(CultureInfo.InvariantCulture) }).ToArray(), "");
        var all = new[] { (Name: ContentsName, Table: index) }.Concat(included.Select(sheet => (sheet.Name, sheet.Table))).ToArray();
        using var stream = new MemoryStream();
        using (var package = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbook = package.AddWorkbookPart();
            workbook.Workbook = new S.Workbook();
            var styles = workbook.AddNewPart<WorkbookStylesPart>("rIdStyles");
            styles.Stylesheet = Styles();
            var list = new S.Sheets();
            workbook.Workbook.Append(new S.BookViews(new S.WorkbookView { ActiveTab = 0U }));
            workbook.Workbook.Append(list);
            for (int i = 0; i < all.Length; i++)
            {
                operation?.Report("supplement-sheet", "Writing supplement worksheet " + all[i].Name, i, all.Length);
                string relation = "rIdSheet" + (i + 1).ToString(CultureInfo.InvariantCulture);
                var part = workbook.AddNewPart<WorksheetPart>(relation);
                WriteSheet(part, all[i].Table, i == 0 ? included : null, operation);
                list.Append(new S.Sheet { Id = relation, SheetId = (uint)i + 1, Name = all[i].Name });
            }
            workbook.Workbook.Save();
        }
        operation?.ThrowIfCancellationRequested();
        return CanonicalPackage(stream.ToArray());
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 31 || name[0] == '\'' || name[^1] == '\''
            || name.Any(c => "[]:*?/\\".Contains(c) || char.IsControl(c))
            || name.Equals("History", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A supplement worksheet name is not supported by Excel; the citation name was not changed.");
        try { XmlConvert.VerifyXmlChars(name); }
        catch (XmlException exception) { throw new ArgumentException("A supplement worksheet name contains invalid text.", exception); }
    }

    private static string Column(int value)
    {
        string name = "";
        for (int index = value; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    private static string Text(string value)
    {
        if (value == null) throw new InvalidDataException("A supplement cell is null; use the source export's empty string for an unavailable cell.");
        if (value.Length > MaxCellCharacters || value.Count(c => c == '\n') > 253)
            throw new InvalidDataException("A source cell exceeds Excel's text limit; the supplement was not truncated.");
        try { XmlConvert.VerifyXmlChars(value); }
        catch (XmlException exception) { throw new InvalidDataException("A source cell contains text that Excel cannot retain.", exception); }
        // Escape literal SpreadsheetML escape tokens first, then preserve carriage returns explicitly.
        return LiteralEscape.Replace(value, "_x005F_").Replace("\r", "_x000D_", StringComparison.Ordinal);
    }

    private static void WriteSheet(WorksheetPart part, ReportTable table, ReportSupplementSheet[]? index,
        OperationContext? operation)
    {
        int columns = table.Columns.Count;
        if (columns < 1 || columns > MaxColumns || table.Rows.Count > MaxDataRows)
            throw new InvalidDataException("A source table exceeds Excel's row or column limit; no rows or columns were dropped.");
        if (table.Rows.Any(row => row == null || row.Count != columns)) throw new InvalidDataException("A supplement source table has inconsistent row widths.");
        string range = "A1:" + Column(columns) + ((long)table.Rows.Count + 1).ToString(CultureInfo.InvariantCulture);
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartElement(new S.Worksheet());
        writer.WriteElement(new S.SheetDimension { Reference = range });
        writer.WriteElement(new S.SheetViews(new S.SheetView(new S.Pane
            { VerticalSplit = 1D, TopLeftCell = "A2", ActivePane = S.PaneValues.BottomLeft, State = S.PaneStateValues.Frozen }) { WorkbookViewId = 0U }));
        writer.WriteElement(new S.SheetFormatProperties { DefaultRowHeight = 15D });
        var widths = new S.Columns();
        for (int col = 0; col < columns; col++)
        {
            int length = table.Columns[col]?.Length ?? 0;
            foreach (var row in table.Rows.Take(256)) length = Math.Max(length, row[col]?.Split('\n').Max(line => line.Length) ?? 0);
            widths.Append(new S.Column { Min = (uint)col + 1, Max = (uint)col + 1, Width = Math.Clamp(length + 2D, 12D, 60D), CustomWidth = true });
        }
        writer.WriteElement(widths);
        writer.WriteStartElement(new S.SheetData());
        void Row(IReadOnlyList<string> cells, uint number, bool header)
        {
            operation?.ThrowIfCancellationRequested();
            writer.WriteStartElement(new S.Row { RowIndex = number });
            for (int col = 0; col < cells.Count; col++)
                writer.WriteElement(new S.Cell
                {
                    CellReference = Column(col + 1) + number.ToString(CultureInfo.InvariantCulture), DataType = S.CellValues.InlineString,
                    StyleIndex = header ? 1U : 0U, InlineString = new S.InlineString(new S.Text(Text(cells[col])) { Space = SpaceProcessingModeValues.Preserve })
                });
            writer.WriteEndElement();
        }
        Row(table.Columns, 1, true);
        for (int row = 0; row < table.Rows.Count; row++) Row(table.Rows[row], (uint)row + 2, false);
        writer.WriteEndElement();
        writer.WriteElement(new S.AutoFilter { Reference = range });
        if (index != null && index.Length > 0)
            writer.WriteElement(new S.Hyperlinks(index.Select((sheet, row) => new S.Hyperlink
            {
                Reference = "A" + (row + 2).ToString(CultureInfo.InvariantCulture),
                Location = "'" + sheet.Name.Replace("'", "''", StringComparison.Ordinal) + "'!A1", Display = sheet.Name
            })));
        writer.WriteEndElement();
    }

    private static S.Stylesheet Styles() => new(
        new S.Fonts(new S.Font(new S.FontSize { Val = 11D }, new S.FontName { Val = "Arial" }),
            new S.Font(new S.Bold(), new S.FontSize { Val = 11D }, new S.FontName { Val = "Arial" })) { Count = 2U },
        new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }), new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })) { Count = 2U },
        new S.Borders(new S.Border()) { Count = 1U },
        new S.CellStyleFormats(new S.CellFormat { NumberFormatId = 0U, FontId = 0U, FillId = 0U, BorderId = 0U }) { Count = 1U },
        new S.CellFormats(
            new S.CellFormat(new S.Alignment { WrapText = true, Vertical = S.VerticalAlignmentValues.Top }) { NumberFormatId = 49U, FontId = 0U, FillId = 0U, BorderId = 0U, FormatId = 0U, ApplyNumberFormat = true, ApplyAlignment = true },
            new S.CellFormat(new S.Alignment { WrapText = true, Vertical = S.VerticalAlignmentValues.Top }) { NumberFormatId = 49U, FontId = 1U, FillId = 0U, BorderId = 0U, FormatId = 0U, ApplyNumberFormat = true, ApplyFont = true, ApplyAlignment = true }) { Count = 2U },
        new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U }) { Count = 1U });

    private static byte[] CanonicalPackage(byte[] original)
    {
        using var source = new ZipArchive(new MemoryStream(original));
        using var result = new MemoryStream();
        using (var zip = new ZipArchive(result, ZipArchiveMode.Create, true))
        {
            foreach (var item in source.Entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry(item.FullName, CompressionLevel.Optimal); entry.LastWriteTime = PackageDate;
                using var output = entry.Open(); using var input = item.Open();
                if (item.FullName != "_rels/.rels") { input.CopyTo(output); continue; }
                var xml = XDocument.Load(input);
                var links = xml.Root!.Elements().OrderBy(link => (string?)link.Attribute("Target"), StringComparer.Ordinal)
                    .ThenBy(link => (string?)link.Attribute("Type"), StringComparer.Ordinal).ToArray();
                xml.Root.RemoveNodes();
                for (int i = 0; i < links.Length; i++)
                {
                    links[i].SetAttributeValue("Id", "rId" + (i + 1).ToString(CultureInfo.InvariantCulture)); xml.Root.Add(links[i]);
                }
                output.Write(Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting)));
            }
        }
        return result.ToArray();
    }
}
