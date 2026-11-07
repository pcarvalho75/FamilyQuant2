using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;

namespace FamilyQuant.Panels.Import;

/// <summary>Reads cached XLSX cell text and direct fill colours without Office, formula evaluation or workbook
/// edits. The file is read once into memory with sharing, so a workbook open in Excel can still be read.</summary>
public sealed class PanelSpreadsheetReader : IPanelSheetPreviewReader
{
    private const int PreviewRowLimit = 2000;

    public IReadOnlyList<string> ReadSheetNames(string path)
    {
        using var stream = new MemoryStream(ReadBytes(path), writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        return Sheets(document).Select(s => s.Name?.Value ?? string.Empty).ToArray();
    }

    public PanelSpreadsheetTable ReadTable(string path, string sheetName, int headerRowNumber = 1)
    {
        if (headerRowNumber < 1) throw new ArgumentOutOfRangeException(nameof(headerRowNumber));
        byte[] bytes = ReadBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        var grid = Grid.Load(document, sheetName);
        var table = new PanelSpreadsheetTable
        {
            SourceName = Path.GetFileName(path), SourceSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            SheetName = sheetName, HeaderRowNumber = headerRowNumber,
        };
        int width = grid.Text.Keys.Where(k => k.Row >= headerRowNumber).Select(k => k.Column)
            .Concat(grid.Merges.Where(m => headerRowNumber >= m.Start.Row && headerRowNumber <= m.End.Row).Select(m => m.End.Column))
            .DefaultIfEmpty(0).Max();
        var headerTexts = Enumerable.Range(1, width).Select(c => grid.At(headerRowNumber, c)).ToArray();
        if (headerTexts.All(string.IsNullOrWhiteSpace))
        {
            table.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.HeaderRowEmpty, Severity = PanelImportSeverity.Blocking,
                RowNumber = headerRowNumber, Message = "Row " + Number(headerRowNumber) + " is empty, so it cannot hold the column names. Choose the row that does." });
            return table;
        }
        var populated = grid.Text.Where(c => c.Key.Row > headerRowNumber && c.Value.Length > 0).Select(c => c.Key.Column).ToHashSet();
        var columns = new List<(int Index, string Header)>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int c = 1; c <= width; c++)
        {
            string header = headerTexts[c - 1], letter = ColumnLetter(c);
            if (string.IsNullOrWhiteSpace(header))
            {
                if (!populated.Contains(c)) continue;
                header = Unique("Column " + letter, used);
                table.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.UnnamedColumn, RowNumber = headerRowNumber,
                    Column = header, ColumnLetter = letter, Message = "Column " + letter + " has values but no name in row "
                        + Number(headerRowNumber) + ", so it is called \"" + header + "\"." });
            }
            else if (!used.Add(header))
            {
                string original = header;
                header = Unique(original + " (column " + letter + ")", used);
                table.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.DuplicateHeader, RowNumber = headerRowNumber,
                    Column = header, ColumnLetter = letter, Value = original, Message = "Column " + letter + " has the same name as an earlier column, \""
                        + OneLine(original) + "\", so it is called \"" + OneLine(header) + "\"." });
            }
            columns.Add((c, header));
        }
        table.Headers = columns.Select(c => c.Header).ToList();
        table.ColumnLetters = columns.Select(c => ColumnLetter(c.Index)).ToList();
        var empty = new List<int>();
        foreach (int row in grid.Rows.Where(r => r > headerRowNumber))
        {
            var values = columns.ToDictionary(c => c.Header, c => grid.At(row, c.Index), StringComparer.Ordinal);
            if (values.Values.All(string.IsNullOrWhiteSpace)) { empty.Add(row); continue; }
            table.EmptyRows.AddRange(empty); empty.Clear();
            var result = new PanelSpreadsheetRow { RowNumber = row, Cells = values };
            foreach (var (index, header) in columns)
            {
                string fill = grid.FillAt(row, index), merge = grid.MergeAt(row, index);
                if (fill.Length > 0) result.Fills[header] = fill;
                if (merge.Length > 0) result.Merges[header] = merge;
            }
            table.Rows.Add(result);
        }
        var byIndex = columns.ToDictionary(c => c.Index, c => c.Header);
        foreach (var issue in grid.Issues.Where(i => i.Row > headerRowNumber && byIndex.ContainsKey(i.Column)))
            table.Problems.Add(CellIssue(issue, byIndex[issue.Column]));
        return table;
    }

    public PanelSheetPreview ReadPreview(string path, string sheetName = "")
    {
        ArgumentNullException.ThrowIfNull(path);
        var preview = new PanelSheetPreview { SourceName = Path.GetFileName(path) };
        try
        {
            byte[] bytes = ReadBytes(path);
            preview.SourceSha256 = Convert.ToHexString(SHA256.HashData(bytes));
            using var stream = new MemoryStream(bytes, writable: false);
            using var document = SpreadsheetDocument.Open(stream, false);
            var sheets = Sheets(document).ToArray();
            preview.SheetNames = sheets.Select(s => s.Name?.Value ?? string.Empty).ToList();
            preview.HiddenSheetNames = sheets.Where(s => s.State?.Value is { } state && state != SheetStateValues.Visible)
                .Select(s => s.Name?.Value ?? string.Empty).ToList();
            if (sheetName.Length == 0)
                sheetName = preview.SheetNames.FirstOrDefault(n => !preview.HiddenSheetNames.Contains(n)) ?? preview.SheetNames.FirstOrDefault() ?? string.Empty;
            preview.SheetName = sheetName;
            if (!preview.SheetNames.Contains(sheetName, StringComparer.Ordinal))
            {
                preview.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.SheetMissing, Severity = PanelImportSeverity.Blocking,
                    Value = sheetName, Message = preview.SheetNames.Count == 0 ? "The workbook has no sheets." : "The workbook has no sheet named \"" + sheetName + "\"." });
                return preview;
            }
            var grid = Grid.Load(document, sheetName);
            int lastRow = grid.Rows.Where(r => Enumerable.Range(1, grid.Width).Any(c => grid.At(r, c).Length > 0 || grid.FillAt(r, c).Length > 0))
                .DefaultIfEmpty(0).Max();
            preview.ColumnCount = grid.Width;
            if (lastRow > PreviewRowLimit)
            {
                preview.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.PreviewTruncated,
                    Message = "The sheet has " + Number(lastRow) + " rows; the preview shows the first " + Number(PreviewRowLimit) + ". The import reads them all." });
                lastRow = PreviewRowLimit;
            }
            for (int r = 1; r <= lastRow; r++)
                preview.Rows.Add(new PanelPreviewRow { RowNumber = r, Cells = Enumerable.Range(1, grid.Width).Select(c =>
                {
                    string fill = grid.FillAt(r, c);
                    return new PanelPreviewCell { Column = c, ColumnLetter = ColumnLetter(c), Text = grid.At(r, c), Fill = fill,
                        FillName = PanelColorNames.Describe(fill), MergeRange = grid.MergeAt(r, c) };
                }).ToList() });
            (preview.ProposedHeaderRow, preview.HeaderReason) = ProposeHeader(grid);
            foreach (var issue in grid.Issues.Where(i => i.Row <= lastRow)) preview.Problems.Add(CellIssue(issue, string.Empty));
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            preview.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.FileUnreadable, Severity = PanelImportSeverity.Blocking,
                Message = Describe(ex) });
        }
        return preview;
    }

    /// <summary>A plain sentence for a failure to read the workbook file.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => "The file was not found. It may have been moved or renamed.",
        UnauthorizedAccessException => "Windows did not allow the program to read the file. Check that you can open it, or copy it to Documents.",
        IOException => "The file could not be read: " + ex.Message,
        _ => "The file could not be read as an Excel workbook (.xlsx). If it is an older .xls file or protected by a password, "
            + "open it in Excel and save a copy as an .xlsx workbook without a password.",
    };

    internal static bool IsReadFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidDataException
        or OpenXmlPackageException or FormatException or NotSupportedException or ArgumentException or XmlException or OverflowException or InvalidOperationException;

    private static (int Row, string Reason) ProposeHeader(Grid grid)
    {
        if (grid.FilterRow > 0)
            return (grid.FilterRow, "Row " + Number(grid.FilterRow) + " has Excel's filter buttons, so it is proposed as the header row.");
        if (grid.TableHeaderRow > 0)
            return (grid.TableHeaderRow, "Row " + Number(grid.TableHeaderRow) + " is the header of an Excel table, so it is proposed as the header row.");
        if (grid.FrozenRows > 0 && Labels(grid, grid.FrozenRows) >= 2)
            return (grid.FrozenRows, "Rows 1 to " + Number(grid.FrozenRows) + " stay in view when scrolling, and row " + Number(grid.FrozenRows)
                + " is the last of them, so it is proposed as the header row.");
        var rows = grid.Rows.Where(r => Labels(grid, r) > 0).ToArray();
        for (int i = 0; i + 1 < rows.Length && i < 50; i++)
            if (Labels(grid, rows[i]) >= 2 && Labels(grid, rows[i + 1]) >= 2)
                return (rows[i], "Row " + Number(rows[i]) + " is the first row with several different labels above rows of data, so it is proposed as the header row.");
        return rows.Length == 0 ? (0, "The sheet has no text.")
            : (rows[0], "Row " + Number(rows[0]) + " is the first row with text. Check that it holds the column names.");
    }

    /// <summary>Distinct nonempty texts in a row, counting a merged range once.</summary>
    private static int Labels(Grid grid, int row) => Enumerable.Range(1, grid.Width)
        .Select(c => (Text: grid.At(row, c), Merge: grid.MergeAt(row, c)))
        .Where(x => !string.IsNullOrWhiteSpace(x.Text)).Select(x => x.Merge.Length > 0 ? "merge:" + x.Merge : "text:" + x.Text)
        .Distinct(StringComparer.Ordinal).Count();

    private static PanelImportProblem CellIssue((int Row, int Column, PanelImportProblemKind Kind, string Value) issue, string header)
    {
        string where = "Row " + Number(issue.Row) + ", " + (header.Length > 0 ? "column \"" + OneLine(header) + "\"" : "column " + ColumnLetter(issue.Column));
        return new PanelImportProblem
        {
            Kind = issue.Kind, RowNumber = issue.Row, Column = header, ColumnLetter = ColumnLetter(issue.Column), Value = issue.Value,
            Message = issue.Kind == PanelImportProblemKind.CellError
                ? where + ": the cell shows the Excel error " + issue.Value + "; it is read as that text."
                : where + ": the formula has no saved result, so the cell is read as empty. Open the file in Excel, let it calculate, and save it to import the result.",
        };
    }

    private static string Unique(string header, HashSet<string> used)
    {
        string candidate = header;
        for (int n = 2; !used.Add(candidate); n++) candidate = header + " " + n.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    internal static string OneLine(string text) => string.Join(" ", text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)).Trim();

    private static byte[] ReadBytes(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        file.CopyTo(copy);
        return copy.ToArray();
    }

    private static IEnumerable<Sheet> Sheets(SpreadsheetDocument document) => document.WorkbookPart?.Workbook?.Sheets?.Elements<Sheet>()
        ?? throw new InvalidDataException("The workbook has no sheets.");

    private static string VisibleText(OpenXmlElement item) => string.Concat(item.Descendants<Text>()
        .Where(t => !t.Ancestors<PhoneticRun>().Any()).Select(t => t.Text));

    internal static string ColumnLetter(int column)
    {
        string result = string.Empty;
        for (int value = column; value > 0; value = (value - 1) / 26) result = (char)('A' + (value - 1) % 26) + result;
        return result;
    }

    internal static (int Row, int Column) Address(string value)
    {
        value = value.Replace("$", string.Empty, StringComparison.Ordinal);
        int index = 0, column = 0;
        while (index < value.Length && char.IsAsciiLetter(value[index]))
            column = checked(column * 26 + char.ToUpperInvariant(value[index++]) - 'A' + 1);
        if (column < 1 || !int.TryParse(value.AsSpan(index), NumberStyles.None, CultureInfo.InvariantCulture, out int row) || row < 1)
            throw new InvalidDataException("The worksheet contains an invalid cell address.");
        return (row, column);
    }

    internal static ((int Row, int Column) Start, (int Row, int Column) End) Range(string value)
    {
        string[] endpoints = value.Split(':');
        if (endpoints.Length is < 1 or > 2) throw new InvalidDataException("The worksheet contains an invalid merged-cell range.");
        var start = Address(endpoints[0]); var end = Address(endpoints[^1]);
        if (end.Row < start.Row || end.Column < start.Column) throw new InvalidDataException("The worksheet contains a reversed merged-cell range.");
        return (start, end);
    }

    /// <summary>One worksheet's cell text, fills, merges and header signals.</summary>
    private sealed class Grid
    {
        public readonly Dictionary<(int Row, int Column), string> Text = new();
        public readonly Dictionary<(int Row, int Column), string> Fill = new();
        public readonly List<(int Row, int Column, PanelImportProblemKind Kind, string Value)> Issues = new();
        public readonly SortedSet<int> Rows = new();
        public ((int Row, int Column) Start, (int Row, int Column) End)[] Merges = Array.Empty<((int, int), (int, int))>();
        public int Width, FilterRow, TableHeaderRow, FrozenRows;

        public static Grid Load(SpreadsheetDocument document, string sheetName)
        {
            WorkbookPart main = document.WorkbookPart ?? throw new InvalidDataException("The workbook has no workbook part.");
            var sheet = Sheets(document).SingleOrDefault(s => string.Equals(s.Name?.Value, sheetName, StringComparison.Ordinal))
                ?? throw new InvalidDataException("The workbook has no sheet named \"" + sheetName + "\".");
            if (sheet.Id?.Value is not { } id || main.GetPartById(id) is not WorksheetPart part || part.Worksheet == null)
                throw new InvalidDataException("The sheet \"" + sheetName + "\" is a chart or another kind of tab, not a worksheet.");
            var strings = main.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>()
                .Select(VisibleText).ToArray() ?? Array.Empty<string>();
            var colors = new PanelCellColors(main);
            var grid = new Grid();
            int previousRow = 0;
            foreach (var row in part.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>() ?? Enumerable.Empty<Row>())
            {
                int rowNumber = checked((int)(row.RowIndex?.Value ?? (uint)(previousRow + 1)));
                if (rowNumber < 1) throw new InvalidDataException("The worksheet has an invalid row number.");
                previousRow = rowNumber; grid.Rows.Add(rowNumber);
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
                    if (!grid.Text.TryAdd((rowNumber, column), grid.CellText(cell, strings, rowNumber, column)))
                        throw new InvalidDataException("The worksheet has duplicate cell references.");
                    string fill = colors.Fill(cell.StyleIndex?.Value);
                    if (fill.Length > 0) grid.Fill[(rowNumber, column)] = fill;
                }
            }
            grid.Merges = (part.Worksheet.GetFirstChild<MergeCells>()?.Elements<MergeCell>() ?? Enumerable.Empty<MergeCell>())
                .Select(m => Range(m.Reference?.Value ?? string.Empty)).ToArray();
            grid.Width = grid.Text.Where(c => c.Value.Length > 0).Select(c => c.Key.Column).Concat(grid.Fill.Keys.Select(k => k.Column))
                .Concat(grid.Merges.Select(m => m.End.Column)).DefaultIfEmpty(0).Max();
            if (part.Worksheet.GetFirstChild<AutoFilter>()?.Reference?.Value is { Length: > 0 } filter) grid.FilterRow = Range(filter).Start.Row;
            var table = part.TableDefinitionParts.Select(t => t.Table).FirstOrDefault(t => t?.Reference?.Value is { Length: > 0 } && (t.HeaderRowCount?.Value ?? 1) > 0);
            if (table?.Reference?.Value is { } tableRange) grid.TableHeaderRow = Range(tableRange).Start.Row;
            var pane = part.Worksheet.GetFirstChild<SheetViews>()?.Elements<SheetView>().FirstOrDefault()?.Pane;
            if (pane?.State?.Value is { } paneState && paneState != PaneStateValues.Split && pane.VerticalSplit?.Value is { } frozen && frozen >= 1)
                grid.FrozenRows = (int)frozen;
            return grid;
        }

        public string At(int row, int column)
        {
            if (Text.TryGetValue((row, column), out string? text) && text.Length > 0) return text;
            foreach (var merge in Merges)
                if (Inside(merge, row, column))
                    return Text.TryGetValue(merge.Start, out var first) ? first : string.Empty;
            return text ?? string.Empty;
        }

        public string FillAt(int row, int column)
        {
            if (Fill.TryGetValue((row, column), out string? fill)) return fill;
            foreach (var merge in Merges)
                if (Inside(merge, row, column)) return Fill.TryGetValue(merge.Start, out var first) ? first : string.Empty;
            return string.Empty;
        }

        public string MergeAt(int row, int column)
        {
            foreach (var merge in Merges)
                if (Inside(merge, row, column))
                    return ColumnLetter(merge.Start.Column) + Number(merge.Start.Row) + ":" + ColumnLetter(merge.End.Column) + Number(merge.End.Row);
            return string.Empty;
        }

        private static bool Inside(((int Row, int Column) Start, (int Row, int Column) End) merge, int row, int column) =>
            row >= merge.Start.Row && row <= merge.End.Row && column >= merge.Start.Column && column <= merge.End.Column;

        private string CellText(Cell cell, string[] strings, int row, int column)
        {
            if (cell.CellFormula != null && cell.CellValue == null && cell.InlineString == null)
            {
                Issues.Add((row, column, PanelImportProblemKind.FormulaWithoutResult, string.Empty));
                return string.Empty;
            }
            if (cell.DataType?.Value == CellValues.SharedString)
            {
                if (!int.TryParse(cell.CellValue?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= strings.Length)
                    throw new InvalidDataException("The workbook contains an invalid shared-string reference.");
                return strings[index];
            }
            if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString == null ? string.Empty : VisibleText(cell.InlineString);
            if (cell.DataType?.Value == CellValues.Error)
            {
                string error = cell.CellValue?.Text ?? "#VALUE!";
                Issues.Add((row, column, PanelImportProblemKind.CellError, error));
                return error;
            }
            return cell.CellValue?.Text ?? string.Empty;
        }
    }
}
