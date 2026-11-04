using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

/// <summary>Audits numeric equality, preferring the locally named subject and exposing unresolved alternatives.</summary>
internal static partial class ReportNumberTrace
{
    private sealed record SourceCell(string Csv, string Cell, string Column, SourceRow Row);
    private sealed record Candidate(SourceCell Source, string Kind);
    private static readonly Regex Tokens = new(
        @"(?<![\p{L}\p{N}_])(?<date>\d{4}-\d{2}-\d{2})(?![\p{L}\p{N}_])|(?<![\p{L}\p{N}_])(?<algorithm>SHA-?256)(?![\p{L}\p{N}_])|(?<![\p{L}\p{N}_])(?<hash>(?=[0-9a-fA-F]{32,}(?![\p{L}\p{N}_]))(?=[0-9a-fA-F]*[a-fA-F])(?=[0-9a-fA-F]*[0-9])[0-9a-fA-F]{32,})|(?<![\p{L}\p{N}_])(?<number>[+-]?(?:(?:\d{1,3}(?:,\d{3})+)|\d+)(?:\.\d+)?(?:[eE][+-]?\d+)?%?)(?![\p{L}\p{N}_])|(?<![\p{L}\p{N}_])(?<identifier>(?=[\p{L}\p{N}_]*[\p{L}_])(?=[\p{L}\p{N}_]*\d)[\p{L}\p{N}_]+)(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RunPrefix = new(@"\b(?:run|sample)\s*#?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PagePrefix = new(@"\bpages?\s*#?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex YearPrefix = new(@"\b(?:year|years|published|publication)\s*[:#]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HashPrefix = new(@"\b(?:sha-?256|sha-?1|hash|digest)\s*[:=]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CaptionDimensions = new(@"The table shows (?<rows>\d+) data rows? across (?<columns>\d+) columns?\.", RegexOptions.CultureInvariant);
    private static readonly Regex CellReference = new(@"^(?<column>[A-Z]+)(?<row>[1-9]\d*)$", RegexOptions.CultureInvariant);

    internal static int Run(string documentPath, string folder, string outputPath)
    {
        if (Path.GetExtension(documentPath).Equals(".html", StringComparison.OrdinalIgnoreCase)) return RunHtml(documentPath, folder, outputPath);
        string doc = Path.GetFullPath(documentPath), source = Path.GetFullPath(folder), output = Path.GetFullPath(outputPath);
        if (!File.Exists(doc)) throw new FileNotFoundException("Report document not found.", doc);
        string workbook = ReportSupplementWorkbook.PathForReport(doc);
        string[] files = Directory.Exists(source)
            ? Directory.GetFiles(source, "*.csv", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        if (files.Length == 0 && !File.Exists(workbook))
            throw new InvalidDataException("No sidecar CSVs or companion workbook are available.");
        if (string.Equals(doc, output, StringComparison.OrdinalIgnoreCase) || string.Equals(workbook, output, StringComparison.OrdinalIgnoreCase)
            || files.Any(f => string.Equals(Path.GetFullPath(f), output, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Trace output must not overwrite the report, workbook or a source CSV.");
        var exact = new Dictionary<string, List<SourceCell>>(StringComparer.Ordinal);
        var subjects = new SubjectCatalog();
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(source, file).Replace(Path.DirectorySeparatorChar, (char)47);
            using var reader = new StreamReader(file, Encoding.UTF8, true);
            string[]? header = null;
            int row = 0;
            foreach (string[] cells in CsvRows(reader))
            {
                row++;
                if (header == null) { header = cells; continue; }
                RegisterRow(cells.Select((text, column) => (text, ColumnName(column + 1) + row.ToString(CultureInfo.InvariantCulture),
                    column < header.Length ? header[column] : string.Empty)), relative);
            }
        }
        if (File.Exists(workbook)) IndexWorkbook();
        var outputRows = new List<string[]> { new[] { "location", "number", "status", "csv", "cell", "match_kind", "context", "subject", "candidate_count" } };
        // Match only the report's complete, standalone label for this running version.
        var tokens = new Regex(@"(?<=^FamilyQuant )(?<version>" + Regex.Escape(AppVersionInfo.Current())
            + @")$|" + Tokens.ToString(), Tokens.Options);
        using (var package = WordprocessingDocument.Open(doc, false))
        {
            var main = package.MainDocumentPart ?? throw new InvalidDataException("The document has no main part.");
            AddPart(main.Document?.Body, "body");
            int headerNumber = 0, footerNumber = 0;
            foreach (var part in main.HeaderParts) AddPart(part.Header, "header " + (++headerNumber).ToString(CultureInfo.InvariantCulture));
            foreach (var part in main.FooterParts) AddPart(part.Footer, "footer " + (++footerNumber).ToString(CultureInfo.InvariantCulture));
        }
        string? directory = Path.GetDirectoryName(output);
        if (directory == null || !Directory.Exists(directory)) throw new DirectoryNotFoundException("Trace output directory not found: " + directory);
        using (var writer = new StreamWriter(output, false, new UTF8Encoding(false)))
            foreach (string[] row in outputRows) writer.WriteLine(string.Join(",", row.Select(value => SpreadsheetSafeCsv.Encode(value))));
        int missing = outputRows.Skip(1).Count(row => row[2] == "NOT FOUND");
        int ambiguous = outputRows.Skip(1).Count(row => row[2] == "AMBIGUOUS");
        Console.WriteLine($"Report number trace: {outputRows.Count - 1} tokens, {missing} not found, {ambiguous} ambiguous. Output: {output}");
        return missing == 0 ? 0 : 1;

        void RegisterRow(IEnumerable<(string Text, string Address, string Heading)> sourceCells, string sourceName)
        {
            var cells = sourceCells.ToArray();
            var row = new SourceRow(cells.Select(c => c.Text).ToArray());
            foreach (var cell in cells)
            {
                if (!TryKey(cell.Text.Trim(), 0, out string key)) continue;
                if (!exact.TryGetValue(key, out var values)) exact.Add(key, values = new());
                values.Add(new(sourceName, cell.Address, cell.Heading, row));
                if (!IsRowReference(cell.Heading)) subjects.Add(row);
            }
        }

        void IndexWorkbook()
        {
            using var book = SpreadsheetDocument.Open(workbook, false);
            var part = book.WorkbookPart ?? throw new InvalidDataException("The supplement has no workbook part.");
            var root = part.Workbook ?? throw new InvalidDataException("The supplement has no workbook document.");
            var shared = part.SharedStringTablePart?.SharedStringTable?.Elements<S.SharedStringItem>().Select(item => item.InnerText).ToArray() ?? Array.Empty<string>();
            foreach (var sheet in root.Descendants<S.Sheet>())
            {
                string name = sheet.Name?.Value ?? throw new InvalidDataException("A supplement sheet has no name.");
                if (name == ReportSupplementWorkbook.ContentsName) continue;
                if (sheet.Id?.Value == null || part.GetPartById(sheet.Id.Value) is not WorksheetPart worksheet)
                    throw new InvalidDataException("A supplement sheet is unavailable: " + name);
                var headings = new Dictionary<string, string>(StringComparer.Ordinal);
                using var reader = DocumentFormat.OpenXml.OpenXmlReader.Create(worksheet);
                while (reader.Read())
                {
                    if (!reader.IsStartElement || reader.ElementType != typeof(S.Row)) continue;
                    if (reader.LoadCurrentElement() is not S.Row row) throw new InvalidDataException("A supplement row could not be read.");
                    var values = new List<(string Text, string Address, string Heading)>();
                    foreach (var cell in row.Elements<S.Cell>())
                    {
                        var address = CellReference.Match(cell.CellReference?.Value ?? string.Empty);
                        if (!address.Success || cell.CellFormula != null) continue;
                        string value = cell.DataType?.Value == S.CellValues.InlineString ? cell.InlineString?.InnerText ?? "" : cell.CellValue?.Text ?? "";
                        if (cell.DataType?.Value == S.CellValues.SharedString)
                        {
                            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= shared.Length)
                                throw new InvalidDataException("A supplement cell has an invalid shared string index.");
                            value = shared[index];
                        }
                        if (cell.DataType?.Value == S.CellValues.Boolean || cell.DataType?.Value == S.CellValues.Error || cell.DataType?.Value == S.CellValues.Date) continue;
                        string column = address.Groups["column"].Value;
                        if (address.Groups["row"].Value == "1") headings[column] = value;
                        else values.Add((value, cell.CellReference!.Value!, headings.GetValueOrDefault(column, "")));
                    }
                    if (values.Count > 0) RegisterRow(values, Path.GetFileName(workbook) + "!" + name);
                }
            }
        }

        void AddPart(DocumentFormat.OpenXml.OpenXmlElement? root, string part)
        {
            if (root == null) return;
            int paragraphIndex = 0;
            foreach (Paragraph paragraph in root.Descendants<Paragraph>())
            {
                paragraphIndex++;
                var contents = new StringBuilder();
                foreach (var item in paragraph.Descendants())
                {
                    if (!ReferenceEquals(item.Ancestors<Paragraph>().FirstOrDefault(), paragraph)) continue;
                    if (item is Text textItem) contents.Append(textItem.Text);
                    else if (item is TabChar or Break or CarriageReturn) contents.Append(' ');
                }
                string text = contents.ToString();
                if (text.Length == 0) continue;
                string location = part + ", paragraph " + paragraphIndex.ToString(CultureInfo.InvariantCulture);
                var cell = paragraph.Ancestors<TableCell>().FirstOrDefault();
                if (cell != null)
                {
                    var row = cell.Parent as TableRow;
                    var table = row?.Parent as Table;
                    int tableIndex = table == null ? 0 : table.ElementsBefore().OfType<Table>().Count() + 1;
                    int rowIndex = row == null ? 0 : row.ElementsBefore().OfType<TableRow>().Count() + 1;
                    int colIndex = cell.ElementsBefore().OfType<TableCell>().Count() + 1;
                    location += ", table " + tableIndex.ToString(CultureInfo.InvariantCulture) + " cell " + ColumnName(colIndex) + rowIndex.ToString(CultureInfo.InvariantCulture);
                }
                bool isCaption = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Caption";
                if (isCaption) location += ", caption";
                Match dimensions = CaptionDimensions.Match(text);
                Table? captionTable = null;
                if (isCaption && dimensions.Success)
                {
                    var previous = paragraph.PreviousSibling();
                    while (previous is Paragraph prior && prior.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Caption")
                        previous = previous.PreviousSibling();
                    var next = paragraph.NextSibling();
                    while (next is Paragraph following && following.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Caption")
                        next = next.NextSibling();
                    var precedingTable = previous as Table;
                    var followingTable = next as Table;
                    // A caption may precede its table or follow an older export's table.
                    // Require exactly one adjacent owner; never infer ownership from matching numbers.
                    if (precedingTable == null || followingTable == null)
                        captionTable = precedingTable ?? followingTable;
                }
                foreach (Match token in tokens.Matches(text))
                {
                    string value = token.Value;
                    string prefix = text.Substring(0, token.Index);
                    string suffix = text.Substring(token.Index + token.Length);
                    string category = Classify(token, prefix, suffix, part);
                    string sourceCsv = "", sourceCell = "", matchKind = "", subject = "", candidateCount = "";
                    bool rowCount = isCaption && dimensions.Success && token.Index == dimensions.Groups["rows"].Index;
                    bool columnCount = isCaption && dimensions.Success && token.Index == dimensions.Groups["columns"].Index;
                    if (rowCount || columnCount)
                    {
                        var tableRows = captionTable?.Elements<TableRow>().ToArray();
                        int actual = rowCount ? (tableRows?.Length ?? 0) - 1 : tableRows?.FirstOrDefault()?.Elements<TableCell>().Count() ?? -1;
                        bool agrees = actual >= 0 && value == actual.ToString(CultureInfo.InvariantCulture);
                        category = agrees ? (rowCount ? "TABLE ROWS" : "TABLE COLUMNS") : "NOT FOUND";
                        matchKind = captionTable == null ? "no unique adjacent Word table" : "Word table " + (rowCount ? "rows: " : "columns: ") + actual.ToString(CultureInfo.InvariantCulture);
                    }
                    if (category.Length == 0)
                    {
                        bool percent = value.EndsWith('%');
                        var candidates = new List<Candidate>();
                        if (TryKey(value, 0, out string key) && exact.TryGetValue(key, out var equal))
                            candidates.AddRange(equal.Where(c => !percent || !c.Column.Contains("fraction", StringComparison.OrdinalIgnoreCase))
                                .Select(c => new Candidate(c, "numeric equality")));
                        if (percent && TryKey(value, -2, out key) && exact.TryGetValue(key, out var scaled))
                            candidates.AddRange(scaled.Where(c => c.Column.Contains("fraction", StringComparison.OrdinalIgnoreCase))
                                .Select(c => new Candidate(c, "percentage from fraction column")));
                        candidateCount = candidates.Count.ToString(CultureInfo.InvariantCulture);
                        var resolved = ResolveSubject(candidates, text, token.Index, token.Length, cell, subjects, paragraph);
                        category = resolved.Status; matchKind = resolved.Reason; subject = resolved.Subject;
                        if (resolved.Found is { } found) { sourceCsv = found.Source.Csv; sourceCell = found.Source.Cell; }
                    }
                    outputRows.Add(new[] { location, value, category, sourceCsv, sourceCell, matchKind, text, subject, candidateCount });
                }
            }
        }
    }

    private static string Classify(Match token, string prefix, string suffix, string part)
    {
        if (token.Groups["version"].Success) return "VERSION";
        if (token.Groups["date"].Success) return "DATE";
        if (token.Groups["hash"].Success || token.Groups["algorithm"].Success || HashPrefix.IsMatch(prefix)) return "SHA-256";
        if (token.Groups["identifier"].Success) return "IDENTIFIER";
        if (PagePrefix.IsMatch(prefix) || (part.StartsWith("footer", StringComparison.Ordinal) && prefix.Trim().Equals("Page", StringComparison.OrdinalIgnoreCase))) return "PAGE";
        if (RunPrefix.IsMatch(prefix)) return "RUN INDEX";
        if (token.Value.Length == 4 && int.TryParse(token.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            && year is >= 1900 and <= 2100 && (YearPrefix.IsMatch(prefix) || suffix.StartsWith('-') || (prefix.EndsWith('(') && suffix.StartsWith(')')))) return "YEAR";
        return string.Empty;
    }

    // Canonical decimal as signed significant digits and decimal exponent, avoiding floating-point rounding.
    private static bool TryKey(string text, int exponentAdjustment, out string key) =>
        InterpretationNumberTrace.TryKey(text, exponentAdjustment, out key);

    private static string ColumnName(int number)
    {
        var result = new StringBuilder();
        while (number > 0) { number--; result.Insert(0, (char)('A' + number % 26)); number /= 26; }
        return result.ToString();
    }

    private static IEnumerable<string[]> CsvRows(TextReader reader)
    {
        var row = new List<string>(); var field = new StringBuilder();
        bool quoted = false, closed = false, started = false;
        int next;
        while ((next = reader.Read()) >= 0)
        {
            char c = (char)next;
            if (quoted)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else { quoted = false; closed = true; }
                }
                else field.Append(c);
            }
            else if (c is ',' or '\r' or '\n')
            {
                row.Add(field.ToString()); field.Clear(); started = false; closed = false;
                if (c != ',')
                {
                    if (c == '\r' && reader.Peek() == '\n') reader.Read();
                    yield return row.ToArray(); row.Clear();
                }
            }
            else if (c == '"' && !started && !closed) { quoted = true; started = true; }
            else
            {
                if (closed || c == '"') throw new InvalidDataException("Malformed quoted field in sidecar CSV.");
                field.Append(c); started = true;
            }
        }
        if (quoted) throw new InvalidDataException("Unclosed quoted field in sidecar CSV.");
        if (row.Count > 0 || field.Length > 0 || started || closed) { row.Add(field.ToString()); yield return row.ToArray(); }
    }
}
