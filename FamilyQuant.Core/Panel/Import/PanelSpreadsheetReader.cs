using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FamilyQuant.Panels.Import;

/// <summary>Reads cached XLSX cell text without Office, formula evaluation or workbook edits.</summary>
public sealed class PanelSpreadsheetReader : IPanelSpreadsheetReader
{
    public IReadOnlyList<string> ReadSheetNames(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path), writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        return Sheets(document).Select(s => s.Name?.Value ?? string.Empty).ToArray();
    }

    public PanelSpreadsheetTable ReadTable(string path, string sheetName, int headerRowNumber = 1)
    {
        if (headerRowNumber < 1) throw new ArgumentOutOfRangeException(nameof(headerRowNumber));
        byte[] bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        WorkbookPart main = document.WorkbookPart ?? throw new InvalidDataException("The workbook has no workbook part.");
        var sheet = Sheets(document).SingleOrDefault(s => string.Equals(s.Name?.Value, sheetName, StringComparison.Ordinal))
            ?? throw new InvalidDataException("The selected sheet does not exist in this workbook.");
        if (sheet.Id?.Value is not { } id || main.GetPartById(id) is not WorksheetPart part || part.Worksheet == null)
            throw new InvalidDataException("The selected tab is not a worksheet.");
        var strings = main.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>()
            .Select(VisibleText).ToArray() ?? Array.Empty<string>();
        var cells = new Dictionary<(int Row, int Column), string>();
        var rows = new SortedSet<int>();
        int previousRow = 0;
        foreach (var row in part.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>() ?? Enumerable.Empty<Row>())
        {
            int rowNumber = checked((int)(row.RowIndex?.Value ?? (uint)(previousRow + 1)));
            if (rowNumber < 1) throw new InvalidDataException("The worksheet has an invalid row number.");
            previousRow = rowNumber; rows.Add(rowNumber);
            int previousColumn = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                int column;
                if (cell.CellReference?.Value is { Length: > 0 } reference)
                {
                    var address = Address(reference);
                    if (address.Row != rowNumber) throw new InvalidDataException("A cell reference disagrees with its row.");
                    column = address.Column;
                }
                else column = previousColumn + 1;
                previousColumn = column;
                if (!cells.TryAdd((rowNumber, column), CellText(cell, strings)))
                    throw new InvalidDataException("The worksheet has duplicate cell references.");
            }
        }
        var merges = (part.Worksheet.GetFirstChild<MergeCells>()?.Elements<MergeCell>() ?? Enumerable.Empty<MergeCell>())
            .Select(m => Range(m.Reference?.Value ?? string.Empty)).ToArray();
        string At(int row, int column)
        {
            if (cells.TryGetValue((row, column), out string? text) && text.Length > 0) return text;
            foreach (var merge in merges)
                if (row >= merge.Start.Row && row <= merge.End.Row && column >= merge.Start.Column && column <= merge.End.Column)
                    return cells.TryGetValue(merge.Start, out var first) ? first : string.Empty;
            return text ?? string.Empty;
        }
        int width = cells.Keys.Where(k => k.Row == headerRowNumber).Select(k => k.Column)
            .Concat(merges.Where(m => headerRowNumber >= m.Start.Row && headerRowNumber <= m.End.Row).Select(m => m.End.Column))
            .DefaultIfEmpty(0).Max();
        if (width == 0) throw new InvalidDataException("The chosen header row is empty.");
        var columns = Enumerable.Range(1, width).Select(c => (Index: c, Header: At(headerRowNumber, c)))
            .Where(c => !string.IsNullOrWhiteSpace(c.Header)).ToArray();
        if (columns.Length == 0 || columns.Select(c => c.Header).Distinct(StringComparer.Ordinal).Count() != columns.Length)
            throw new InvalidDataException("The chosen row must have distinct, nonempty column headers.");
        var named = columns.Select(c => c.Index).ToHashSet();
        if (cells.Any(c => c.Key.Row > headerRowNumber && c.Value.Length > 0 && !named.Contains(c.Key.Column)))
            throw new InvalidDataException("A populated column has no header. Choose another header row or label that column.");
        var table = new PanelSpreadsheetTable
        {
            SourceName = Path.GetFileName(path), SourceSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            SheetName = sheetName, HeaderRowNumber = headerRowNumber, Headers = columns.Select(c => c.Header).ToList(),
        };
        foreach (int row in rows.Where(r => r > headerRowNumber))
        {
            var values = columns.ToDictionary(c => c.Header, c => At(row, c.Index), StringComparer.Ordinal);
            if (values.Values.All(string.IsNullOrWhiteSpace)) continue;
            table.Rows.Add(new PanelSpreadsheetRow { RowNumber = row, Cells = values });
        }
        return table;
    }

    private static IEnumerable<Sheet> Sheets(SpreadsheetDocument document) => document.WorkbookPart?.Workbook?.Sheets?.Elements<Sheet>()
        ?? throw new InvalidDataException("The workbook has no sheets.");

    private static string VisibleText(DocumentFormat.OpenXml.OpenXmlElement item) => string.Concat(item.Descendants<Text>()
        .Where(t => !t.Ancestors<PhoneticRun>().Any()).Select(t => t.Text));

    private static string CellText(Cell cell, string[] strings)
    {
        if (cell.CellFormula != null && cell.CellValue == null && cell.InlineString == null)
            throw new InvalidDataException("A formula has no cached result. Recalculate and save the workbook before importing.");
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (!int.TryParse(cell.CellValue?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= strings.Length)
                throw new InvalidDataException("The workbook contains an invalid shared-string reference.");
            return strings[index];
        }
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString == null ? string.Empty : VisibleText(cell.InlineString);
        if (cell.DataType?.Value == CellValues.Error) throw new InvalidDataException("The workbook contains a cell error.");
        return cell.CellValue?.Text ?? string.Empty;
    }

    private static (int Row, int Column) Address(string value)
    {
        value = value.Replace("$", string.Empty, StringComparison.Ordinal);
        int index = 0, column = 0;
        while (index < value.Length && char.IsAsciiLetter(value[index]))
            column = checked(column * 26 + char.ToUpperInvariant(value[index++]) - 'A' + 1);
        if (column < 1 || !int.TryParse(value.AsSpan(index), NumberStyles.None, CultureInfo.InvariantCulture, out int row) || row < 1)
            throw new InvalidDataException("The worksheet contains an invalid cell address.");
        return (row, column);
    }

    private static ((int Row, int Column) Start, (int Row, int Column) End) Range(string value)
    {
        string[] endpoints = value.Split(':');
        if (endpoints.Length is < 1 or > 2) throw new InvalidDataException("The worksheet contains an invalid merged-cell range.");
        var start = Address(endpoints[0]); var end = Address(endpoints[^1]);
        if (end.Row < start.Row || end.Column < start.Column) throw new InvalidDataException("The worksheet contains a reversed merged-cell range.");
        return (start, end);
    }
}
