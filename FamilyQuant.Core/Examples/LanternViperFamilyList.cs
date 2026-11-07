using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Examples;

/// <summary>
/// Writes the invented example family list of the import tutorial (Z12p2c): six sheets shaped like a real
/// consensus workbook, with a title row above the header, accented headers, free-text decisions, coloured
/// abbreviation cells, a row naming several families in one cell, a repeated abbreviation, an empty row,
/// line breaks inside cells and merged title cells. Every value is made up; the text lives in the embedded
/// LanternViperFamilyList.json, so the file is rebuilt the same way each time and nothing binary is shipped.
/// </summary>
public static class LanternViperFamilyList
{
    public const string ResourceName = "FamilyQuant.Examples.LanternViperFamilyList";
    public const string DefaultFileName = "Lanternis-example-family-list.xlsx";

    /// <summary>The first sheet, where the families are.</summary>
    public static string FamilySheetName => Load().Sheets[0].Name;

    /// <summary>The header row of the first sheet: 3 below the title and the subtitle, 1 without them.</summary>
    public static int FamilyHeaderRow(bool titleRow = true) => titleRow ? 3 : 1;

    // Light fills for the abbreviation cells, by the colour names the data uses; dark teal for headers.
    private static readonly string[] FillNames = { "teal", "blue", "lilac", "grey", "rose", "mint" };
    private static readonly string[] FillArgb = { "FFB9E3E6", "FFCFE0F7", "FFE6D6F2", "FFD9D9D9", "FFF6D3DF", "FFD3F0E0" };
    private const string HeaderArgb = "FF284E5B";
    private const uint BodyStyle = 1, TitleStyle = 2, SubtitleStyle = 3, HeaderStyle = 4, FirstFillStyle = 5;
    private static readonly uint PlainKeyStyle = FirstFillStyle + (uint)FillNames.Length;

    private sealed class Book
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public Dictionary<string, string> Fills { get; set; } = new();
        public List<Page> Sheets { get; set; } = new();
    }

    private sealed class Page
    {
        public string Name { get; set; } = "";
        public bool Titled { get; set; }
        public int? FillColumn { get; set; }
        public int? FillKeyColumn { get; set; }
        public List<double> Widths { get; set; } = new();
        public List<string> Headers { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
    }

    private static Book Load()
    {
        using Stream stream = typeof(LanternViperFamilyList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The example family list is missing from the program.");
        return JsonSerializer.Deserialize<Book>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The example family list could not be read.");
    }

    /// <summary>Writes the workbook to <paramref name="path"/>, replacing a file there. With
    /// <paramref name="titleRow"/> false the first sheet starts with its header row (a plain variant for tests).</summary>
    public static void Write(string path, bool titleRow = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Book book = Load();
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        WorkbookPart workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var strings = workbook.AddNewPart<SharedStringTablePart>();
        strings.SharedStringTable = new SharedStringTable();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        int Text(string value)
        {
            if (index.TryGetValue(value, out int at)) return at;
            var item = new SharedStringItem(new Text(value) { Space = SpaceProcessingModeValues.Preserve });
            strings.SharedStringTable.AppendChild(item);
            index[value] = at = index.Count;
            return at;
        }
        workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = Styles();
        var sheets = workbook.Workbook.AppendChild(new Sheets());
        uint sheetId = 1;
        foreach (Page page in book.Sheets)
        {
            var part = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            var columns = new Columns(page.Widths.Select((w, i) => new Column
            {
                Min = (uint)(i + 1), Max = (uint)(i + 1), Width = w, CustomWidth = true,
            }));
            int width = page.Headers.Count;
            uint rowNumber = 1;
            var merges = new List<string>();
            void Add(IReadOnlyList<string>? cells, Func<int, string, uint> style)
            {
                var row = new Row { RowIndex = rowNumber };
                if (cells != null)
                    for (int c = 0; c < cells.Count; c++)
                    {
                        if (cells[c].Length == 0) continue;
                        row.AppendChild(new Cell
                        {
                            CellReference = Reference(c, rowNumber), DataType = CellValues.SharedString,
                            CellValue = new CellValue(Text(cells[c]).ToString(CultureInfo.InvariantCulture)), StyleIndex = style(c, cells[c]),
                        });
                    }
                data.AppendChild(row);
                rowNumber++;
            }
            if (page.Titled && titleRow)
            {
                merges.Add(Reference(0, rowNumber) + ":" + Reference(width - 1, rowNumber));
                Add(new[] { book.Title }, (_, _) => TitleStyle);
                merges.Add(Reference(0, rowNumber) + ":" + Reference(width - 1, rowNumber));
                Add(new[] { book.Subtitle }, (_, _) => SubtitleStyle);
            }
            Add(page.Headers, (_, _) => HeaderStyle);
            foreach (List<string> cells in page.Rows)
            {
                if (cells.Count == 0) { Add(null, (_, _) => BodyStyle); continue; }
                string key = page.FillKeyColumn is int k && k < cells.Count ? cells[k] : "";
                Add(cells, (c, _) => c == page.FillColumn ? KeyStyle(book, key) : BodyStyle);
            }
            var sheet = new Worksheet(columns, data);
            if (merges.Count > 0) sheet.AppendChild(new MergeCells(merges.Select(m => new MergeCell { Reference = m })));
            part.Worksheet = sheet;
            sheets.AppendChild(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = sheetId++, Name = page.Name });
        }
        strings.SharedStringTable.Count = (uint)index.Count;
        strings.SharedStringTable.UniqueCount = (uint)index.Count;
        workbook.Workbook.Save();
    }

    private static uint KeyStyle(Book book, string key)
    {
        if (!book.Fills.TryGetValue(key, out string? fill)) return PlainKeyStyle;
        int at = Array.IndexOf(FillNames, fill);
        return at < 0 ? PlainKeyStyle : FirstFillStyle + (uint)at;
    }

    private static string Reference(int column, uint row)
    {
        string letters = "";
        for (int n = column + 1; n > 0; n = (n - 1) / 26) letters = (char)('A' + (n - 1) % 26) + letters;
        return letters + row.ToString(CultureInfo.InvariantCulture);
    }

    private static Stylesheet Styles()
    {
        Font Face(bool bold = false, bool italic = false, double size = 11, string? argb = null)
        {
            var font = new Font();
            if (bold) font.AppendChild(new Bold());
            if (italic) font.AppendChild(new Italic());
            font.AppendChild(new FontSize { Val = size });
            if (argb != null) font.AppendChild(new Color { Rgb = argb });
            font.AppendChild(new FontName { Val = "Calibri" });
            return font;
        }
        Fill Solid(string argb) => new(new PatternFill(new ForegroundColor { Rgb = argb }, new BackgroundColor { Indexed = 64 })
            { PatternType = PatternValues.Solid });
        var fonts = new Fonts(Face(), Face(bold: true), Face(bold: true, size: 14), Face(bold: true, argb: "FFFFFFFF"),
            Face(italic: true, size: 10, argb: "FF595959"));
        var fills = new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }), Solid(HeaderArgb));
        foreach (string argb in FillArgb) fills.AppendChild(Solid(argb));
        CellFormat Format(uint font, uint fill, bool wrap) => new(new Alignment { WrapText = wrap, Vertical = VerticalAlignmentValues.Top })
            { FontId = font, FillId = fill, BorderId = 0, FormatId = 0, ApplyFont = true, ApplyFill = fill != 0, ApplyAlignment = true };
        var formats = new CellFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
            Format(0, 0, true), Format(2, 0, false), Format(4, 0, false), Format(3, 2, true));
        for (int i = 0; i < FillArgb.Length; i++) formats.AppendChild(Format(1, (uint)(3 + i), false));
        formats.AppendChild(Format(1, 0, false));
        return new Stylesheet(fonts, fills, new Borders(new Border()),
            new CellStyleFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0 }), formats);
    }
}
