using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Panels;

/// <summary>Versioned, editable spreadsheet template. A hidden snapshot preserves panel fields not
/// represented by visible columns; visible rows are applied over that snapshot when read.</summary>
public static class FamilyPanelTemplate
{
    private const string Marker = "FamilyQuant panel template 1";
    private const int ChunkLength = 30000;
    private static readonly string[] FamilyHeaders = { "Record ID", "Key", "Name", "Decision", "Merge target key", "Pfam", "UniProt family" };
    private static readonly string[] SubclassHeaders = { "Record ID", "Family key", "Subclass key", "Name", "Required Pfam", "Absent Pfam", "UniProt subfamily", "Report as subclass key" };

    public static void Write(string path, FamilyPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        FamilyPanelValidation.Validate(panel);
        var fields = TextColumns(panel);
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var sheets = workbook.Workbook.AppendChild(new Sheets());
        var families = NewSheet(workbook, sheets, "Families", 1, false);
        AddRow(families, "", Marker, panel.Name);
        AddRow(families, FamilyHeaders.Concat(fields.Select(FieldHeader)).ToArray());
        for (int i = 0; i < panel.Families.Count; i++)
        {
            var f = panel.Families[i];
            var sourceFamilies = f.UniProtFamilies.Concat(panel.EvidenceLinks.Where(link => link.PanelFamilyKey == f.Key)
                .Select(link => link.SourceFamily)).Select(name => name.Trim()).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
            AddRow(families, new[] { "f" + i.ToString(CultureInfo.InvariantCulture), f.Key, f.DisplayName, DecisionText(f.Decision), f.MergeTargetFamilyKey, Join(f.Pfam.Select(d => d.Accession)), Join(sourceFamilies) }
                .Concat(fields.Select(field => FieldValue(f, field))).ToArray());
        }
        ((Worksheet)families.Parent!).Append(new AutoFilter { Reference = "A2:" + ColumnName(FamilyHeaders.Length + fields.Count - 1)
            + (panel.Families.Count + 2).ToString(CultureInfo.InvariantCulture) });
        var subclasses = NewSheet(workbook, sheets, "Subclasses", 2, false);
        AddRow(subclasses, SubclassHeaders);
        for (int i = 0; i < panel.Families.Count; i++)
            for (int j = 0; j < panel.Families[i].Subclasses.Count; j++)
            {
                var f = panel.Families[i]; var s = f.Subclasses[j];
                AddRow(subclasses, "s" + i.ToString(CultureInfo.InvariantCulture) + ":" + j.ToString(CultureInfo.InvariantCulture), f.Key, s.Key, s.DisplayName, Join(s.RequiredPfam), Join(s.AbsentPfam), Join(s.UniProtSubfamilies), s.ReportAsSubclassKey);
            }
        var metadata = NewSheet(workbook, sheets, "Panel metadata", 3, true);
        AddRow(metadata, Marker);
        string encoded = Encode(JsonSerializer.Serialize(new TemplateMetadata { Panel = panel, TextFields = fields }));
        for (int i = 0; i < encoded.Length; i += ChunkLength) AddRow(metadata, encoded.Substring(i, Math.Min(ChunkLength, encoded.Length - i)));
        workbook.Workbook.Save();
    }

    public static bool IsTemplate(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart ?? throw new InvalidDataException("The spreadsheet has no workbook.");
        var sheet = FindSheet(workbook, "Families");
        if (sheet == null) return false;
        try { return Rows(workbook, sheet).FirstOrDefault() is { } first && At(first, 1) == Marker; }
        catch (InvalidDataException) { return false; }
    }

    public static FamilyPanel Read(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart ?? throw new InvalidDataException("The spreadsheet has no workbook.");
        var familySheet = FindSheet(workbook, "Families") ?? throw new InvalidDataException("The Families sheet is missing.");
        var subclassSheet = FindSheet(workbook, "Subclasses") ?? throw new InvalidDataException("The Subclasses sheet is missing.");
        var metadataSheet = FindSheet(workbook, "Panel metadata") ?? throw new InvalidDataException("The panel metadata sheet is missing.");
        var familyRows = Rows(workbook, familySheet).ToList();
        var partRows = Rows(workbook, subclassSheet).ToList();
        var metadataRows = Rows(workbook, metadataSheet).ToList();
        if (familyRows.Count < 2 || At(familyRows[0], 1) != Marker || metadataRows.FirstOrDefault()?.FirstOrDefault() != Marker)
            throw new InvalidDataException("This is not a supported panel template.");
        var metadata = JsonSerializer.Deserialize<TemplateMetadata>(Decode(string.Concat(metadataRows.Skip(1).Select(x => At(x, 0)))))
            ?? throw new InvalidDataException("The panel metadata is missing.");
        var original = metadata.Panel ?? throw new InvalidDataException("The panel metadata is missing.");
        var fields = metadata.TextFields ?? throw new InvalidDataException("The panel text columns are missing.");
        var familyHeaders = At(familyRows[1], 6) == "UniProt family" ? FamilyHeaders : FamilyHeaders.Take(6).ToArray();
        RequireHeaders(familyRows[1], familyHeaders.Concat(fields.Select(FieldHeader)).ToArray());
        int fixedWidth = familyHeaders.Length + fields.Count;
        foreach (string header in familyRows[1].Skip(fixedWidth))
        {
            if (string.IsNullOrWhiteSpace(header)) throw new InvalidDataException("Every additional text column needs a header.");
            string name = header.StartsWith("Text: ", StringComparison.Ordinal) ? header.Substring(6) : header;
            int occurrence = fields.Where(x => x.Name == name).Select(x => x.Occurrence).DefaultIfEmpty(0).Max() + 1;
            fields.Add(new TextColumn { Name = name, Occurrence = occurrence });
        }
        if (partRows.Count == 0) throw new InvalidDataException("The subclass headers are missing.");
        RequireHeaders(partRows[0], SubclassHeaders);
        var result = FamilyPanelValidation.Clone(original);
        result.Name = At(familyRows[0], 2);
        var familyIds = new HashSet<string>(StringComparer.Ordinal);
        result.Families = new List<FamilyPanelFamily>();
        var retainedMatchKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in familyRows.Skip(2).Where(HasContent))
        {
            if (row.Skip(familyHeaders.Length + fields.Count).Any(x => x.Length > 0)) throw new InvalidDataException("A family value has no column header.");
            string id = At(row, 0);
            if (id.Length > 0 && !familyIds.Add(id)) throw new InvalidDataException("A family record ID is repeated.");
            int index = ParseFamilyId(id, original.Families.Count);
            var f = index >= 0 ? FamilyPanelValidation.Clone(new FamilyPanel { Families = new() { original.Families[index] } }).Families[0] : new FamilyPanelFamily();
            f.Key = At(row, 1); f.DisplayName = At(row, 2); f.Decision = ParseDecision(At(row, 3)); f.MergeTargetFamilyKey = At(row, 4);
            if (index >= 0) retainedMatchKeys[original.Families[index].Key] = f.Key;
            string domainText = At(row, 5);
            if (domainText != Join(f.Pfam.Select(d => d.Accession)))
            {
                var existing = f.Pfam.GroupBy(x => x.Accession, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
                f.Pfam = Split(domainText).Select(x => existing.TryGetValue(x, out var d) ? d : new FamilyPanelDomain { Accession = x }).ToList();
            }
            if (familyHeaders.Length > 6) f.UniProtFamilies = Split(At(row, 6)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < fields.Count; i++) SetField(f, fields[i], At(row, familyHeaders.Length + i));
            f.Subclasses = new List<FamilyPanelSubclass>();
            result.Families.Add(f);
        }
        var familyByKey = result.Families.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var partIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in partRows.Skip(1).Where(HasContent))
        {
            if (row.Skip(SubclassHeaders.Length).Any(x => x.Length > 0)) throw new InvalidDataException("An unsupported subclass column contains data.");
            string id = At(row, 0);
            if (id.Length > 0 && !partIds.Add(id)) throw new InvalidDataException("A subclass record ID is repeated.");
            var (familyIndex, partIndex) = ParseSubclassId(id, original);
            var s = familyIndex >= 0 ? FamilyPanelValidation.Clone(new FamilyPanel { Families = new() { original.Families[familyIndex] } }).Families[0].Subclasses[partIndex] : new FamilyPanelSubclass();
            string key = At(row, 1);
            if (!familyByKey.TryGetValue(key, out var family)) throw new InvalidDataException("A subclass refers to a missing family key.");
            s.Key = At(row, 2); s.DisplayName = At(row, 3);
            string required = At(row, 4), absent = At(row, 5), subfamilies = At(row, 6);
            if (required != Join(s.RequiredPfam)) s.RequiredPfam = Split(required).ToList();
            if (absent != Join(s.AbsentPfam)) s.AbsentPfam = Split(absent).ToList();
            if (subfamilies != Join(s.UniProtSubfamilies)) s.UniProtSubfamilies = Split(subfamilies).ToList();
            s.ReportAsSubclassKey = At(row, 7);
            family.Subclasses.Add(s);
        }
        result = Evidence.PanelEvidenceMatching.RekeyConfirmedMatches(original, result, retainedMatchKeys);
        FamilyPanelValidation.Validate(result);
        return result;
    }

    private sealed class TemplateMetadata
    {
        public FamilyPanel? Panel { get; set; }
        public List<TextColumn>? TextFields { get; set; }
    }
    private sealed class TextColumn { public string Name { get; set; } = string.Empty; public int Occurrence { get; set; } }
    private static List<TextColumn> TextColumns(FamilyPanel panel)
    {
        var result = new List<TextColumn>();
        foreach (var family in panel.Families)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var field in family.TextFields)
            {
                counts.TryGetValue(field.Name, out int count); counts[field.Name] = ++count;
                if (!result.Any(x => x.Name == field.Name && x.Occurrence == count)) result.Add(new TextColumn { Name = field.Name, Occurrence = count });
            }
        }
        return result;
    }
    private static string FieldHeader(TextColumn field) => "Text: " + field.Name + (field.Occurrence == 1 ? "" : " [" + field.Occurrence.ToString(CultureInfo.InvariantCulture) + "]");
    private static string FieldValue(FamilyPanelFamily family, TextColumn field) => family.TextFields.Where(x => x.Name == field.Name).Skip(field.Occurrence - 1).FirstOrDefault()?.Text ?? string.Empty;
    private static void SetField(FamilyPanelFamily family, TextColumn field, string value)
    {
        var matches = family.TextFields.Where(x => x.Name == field.Name).ToList();
        if (matches.Count >= field.Occurrence) matches[field.Occurrence - 1].Text = value;
        else if (value.Length > 0)
        {
            while (matches.Count < field.Occurrence) { var item = new FamilyPanelText { Name = field.Name }; family.TextFields.Add(item); matches.Add(item); }
            matches[^1].Text = value;
        }
    }
    private static string DecisionText(FamilyPanelDecision value) => value switch
    {
        FamilyPanelDecision.Quantify => "Quantify", FamilyPanelDecision.ReportUnderOthers => "Report under others",
        FamilyPanelDecision.NotAFamily => "Not a family", FamilyPanelDecision.MergeIntoAnotherFamily => "Merge into family",
        _ => throw new InvalidDataException("Unknown panel decision.")
    };
    private static FamilyPanelDecision ParseDecision(string text) => text switch
    {
        "Quantify" => FamilyPanelDecision.Quantify, "Report under others" => FamilyPanelDecision.ReportUnderOthers,
        "Not a family" => FamilyPanelDecision.NotAFamily, "Merge into family" => FamilyPanelDecision.MergeIntoAnotherFamily,
        _ => throw new InvalidDataException("Choose a valid decision for every family.")
    };
    private static int ParseFamilyId(string id, int count)
    {
        if (id.Length == 0) return -1;
        if (id[0] != 'f' || !int.TryParse(id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= count) throw new InvalidDataException("A family record ID is invalid.");
        return index;
    }
    private static (int, int) ParseSubclassId(string id, FamilyPanel panel)
    {
        if (id.Length == 0) return (-1, -1);
        var parts = id.Split(':');
        if (parts.Length != 2 || parts[0].Length < 2 || parts[0][0] != 's' || !int.TryParse(parts[0].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int family)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int part) || family < 0 || family >= panel.Families.Count || part < 0 || part >= panel.Families[family].Subclasses.Count)
            throw new InvalidDataException("A subclass record ID is invalid.");
        return (family, part);
    }
    private static string Join(IEnumerable<string> values) => string.Join("; ", values);
    private static IEnumerable<string> Split(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0);
    private static bool HasContent(string[] row) => row.Any(x => x.Length > 0);
    private static string At(string[] row, int index) => index < row.Length ? row[index] : string.Empty;
    private static void RequireHeaders(string[] actual, string[] expected)
    { if (!actual.Take(expected.Length).SequenceEqual(expected)) throw new InvalidDataException("The template headers were changed."); }
    private static SheetData NewSheet(WorkbookPart workbook, Sheets sheets, string name, uint id, bool hidden)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        part.Worksheet = new Worksheet(new Columns(new Column { Min = 1, Max = 1, Hidden = true, Width = 12, CustomWidth = true }), data);
        sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id, Name = name, State = hidden ? SheetStateValues.Hidden : SheetStateValues.Visible });
        return data;
    }
    private static void AddRow(SheetData sheet, params string[] values)
    {
        uint number = ((sheet.LastChild as Row)?.RowIndex?.Value ?? 0) + 1;
        var row = new Row { RowIndex = number };
        for (int i = 0; i < values.Length; i++)
        {
            string value = values[i] ?? string.Empty;
            if (value.Length > 32767) throw new InvalidDataException("A panel value exceeds the spreadsheet cell limit.");
            row.Append(new Cell(new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }))
            { DataType = CellValues.InlineString, CellReference = ColumnName(i) + number.ToString(CultureInfo.InvariantCulture) });
        }
        sheet.Append(row);
    }
    private static string ColumnName(int index)
    {
        string result = string.Empty;
        for (int value = index + 1; value > 0; value = (value - 1) / 26)
            result = (char)('A' + (value - 1) % 26) + result;
        return result;
    }    private static Sheet? FindSheet(WorkbookPart workbook, string name) => workbook.Workbook?.Sheets?.Elements<Sheet>().SingleOrDefault(x => x.Name?.Value == name);
    private static IEnumerable<string[]> Rows(WorkbookPart workbook, Sheet sheet)
    {
        var part = (WorksheetPart)workbook.GetPartById(sheet.Id!);
        var worksheet = part.Worksheet ?? throw new InvalidDataException("The worksheet is missing.");
        foreach (var row in worksheet.Descendants<Row>())
        {
            var values = new List<string>(); int previous = -1;
            foreach (var cell in row.Elements<Cell>())
            {
                if (cell.CellFormula != null) throw new InvalidDataException("Formula cells are not supported in panel templates.");
                int column = cell.CellReference == null ? previous + 1 : ColumnIndex(cell.CellReference.Value!);
                while (values.Count < column) values.Add(string.Empty);
                string value = cell.DataType?.Value == CellValues.SharedString
                    ? workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ElementAt(int.Parse(cell.InnerText, CultureInfo.InvariantCulture)).InnerText ?? string.Empty
                    : cell.InnerText;
                if (column != values.Count) throw new InvalidDataException("Cells are repeated or out of order.");
                values.Add(value); previous = column;
            }
            yield return values.ToArray();
        }
    }
    private static int ColumnIndex(string reference)
    {
        int index = 0;
        foreach (char c in reference) { if (c < 'A' || c > 'Z') break; index = checked(index * 26 + c - 'A' + 1); }
        if (index == 0) throw new InvalidDataException("Invalid cell reference.");
        return index - 1;
    }
    private static string Encode(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true)) { byte[] bytes = Encoding.UTF8.GetBytes(text); gzip.Write(bytes, 0, bytes.Length); }
        return Convert.ToBase64String(output.ToArray());
    }
    private static string Decode(string text)
    {
        using var input = new MemoryStream(Convert.FromBase64String(text));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
