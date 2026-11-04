using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Report;
using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using W = DocumentFormat.OpenXml.Wordprocessing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace FamilyQuant.Tests;

internal static partial class ReportNumberTrace
{
    internal static readonly JsonSerializerOptions HtmlJson = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private sealed record HtmlBound(string Text, double? Number, string Source, string Cell);

    internal static int RunHtml(string htmlPath, string sourceFolder, string outputPath, string? projectPath = null)
    {
        void Protect(string path)
        {
            if (Path.GetFullPath(path).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Trace cannot overwrite a source input.");
        }
        Protect(htmlPath);
        string content = File.ReadAllText(htmlPath);
        var matches = Regex.Matches(content, "<script\\b(?=[^>]*\\bid\\s*=\\s*[\"']fq-data[\"'])[^>]*>(?<json>.*?)</script\\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (matches.Count != 1) throw new InvalidDataException("HTML needs exactly one fq-data payload.");
        var payload = JsonSerializer.Deserialize<HtmlReportPayload>(matches[0].Groups["json"].Value, HtmlJson) ?? throw new InvalidDataException("HTML payload is unavailable.");
        string Sibling(string escaped)
        {
            string name = Uri.UnescapeDataString(escaped);
            if (name != Path.GetFileName(name) || Path.IsPathRooted(name)) throw new InvalidDataException("Report links must be sibling filenames.");
            string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(htmlPath))!, name);
            return File.Exists(path) ? path : Path.Combine(sourceFolder, name);
        }
        projectPath ??= new[] { Path.Combine(sourceFolder, "project.fqproj"), Path.Combine(Path.GetDirectoryName(Path.GetFullPath(htmlPath))!, "project.fqproj") }.FirstOrDefault(File.Exists);
        FamilyClusterFile? project = null; QuantManifest? manifest = null;
        if (projectPath != null) { Protect(projectPath); var saved = ProjectStore.ReadFqproj(projectPath); project = saved.fcf; manifest = saved.manifest; }
        return RunHtmlBindings(payload, Sibling(payload.Links.Word), Sibling(payload.Links.Workbook), project, manifest, outputPath);
    }

    internal static int RunHtmlBindings(HtmlReportPayload payload, string wordPath, string workbookPath,
        FamilyClusterFile? project, QuantManifest? manifest, string outputPath)
    {
        string output = Path.GetFullPath(outputPath);
        if (new[] { wordPath, workbookPath }.Any(p => Path.GetFullPath(p).Equals(output, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Trace cannot overwrite source documents.");
        using var word = WordprocessingDocument.Open(wordPath, false);
        var main = word.MainDocumentPart ?? throw new InvalidDataException("Word main part unavailable.");
        var body = main.Document?.Body ?? throw new InvalidDataException("Word body unavailable.");
        var tables = body.Descendants<W.Table>().ToArray();
        var paragraphs = body.Descendants<W.Paragraph>().Select(p => N(p.InnerText)).ToArray();
        var pictures = body.Descendants<DW.DocProperties>().Select(p =>
        {
            string? relation = p.Parent?.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value;
            string? hash = null;
            if (relation != null && main.GetPartById(relation) is ImagePart image) { using var stream = image.GetStream(); hash = Convert.ToHexString(SHA256.HashData(stream)); }
            return (Title: N(p.Name?.Value ?? ""), Caption: N(p.Description?.Value ?? ""), Hash: hash);
        }).ToArray();
        var workbook = new Dictionary<(string Sheet, int Row, int Column), string>();
        if (payload.Fields.Values.Any(f => f.Source.Kind == "workbook-cell"))
        {
            using var book = SpreadsheetDocument.Open(workbookPath, false);
            var part = book.WorkbookPart ?? throw new InvalidDataException("Workbook unavailable.");
            var shared = part.SharedStringTablePart?.SharedStringTable?.Elements<S.SharedStringItem>().Select(s => s.InnerText).ToArray() ?? Array.Empty<string>();
            var sheets = part.Workbook?.Sheets ?? throw new InvalidDataException("Workbook sheets unavailable.");
            foreach (var sheet in sheets.Elements<S.Sheet>())
            {
                string name = sheet.Name?.Value ?? "";
                var needed = payload.Fields.Values.Where(f => f.Source.Kind == "workbook-cell" && f.Source.Sheet == name)
                    .Select(f => (Row: f.Source.Row ?? -1, Column: f.Source.Column ?? -1)).ToHashSet();
                if (needed.Count == 0) continue;
                if (part.GetPartById(sheet.Id!.Value!) is not WorksheetPart worksheet) throw new InvalidDataException("Sheet unavailable.");
                using var reader = DocumentFormat.OpenXml.OpenXmlReader.Create(worksheet);
                while (reader.Read())
                {
                    if (!reader.IsStartElement || reader.ElementType != typeof(S.Row)) continue;
                    if (reader.LoadCurrentElement() is not S.Row row) continue;
                    foreach (var cell in row.Elements<S.Cell>())
                    {
                        var address = CellReference.Match(cell.CellReference?.Value ?? "");
                        if (!address.Success) continue;
                        int r = int.Parse(address.Groups["row"].Value, CultureInfo.InvariantCulture), c = 0;
                        foreach (char letter in address.Groups["column"].Value) c = c * 26 + letter - 'A' + 1;
                        if (!needed.Contains((r, c))) continue;
                        if (cell.CellFormula != null) throw new InvalidDataException("Trace does not evaluate workbook formulas.");
                        string text = cell.DataType?.Value == S.CellValues.InlineString ? cell.InlineString?.InnerText ?? "" : cell.CellValue?.Text ?? "";
                        if (cell.DataType?.Value == S.CellValues.SharedString) text = shared[int.Parse(text, CultureInfo.InvariantCulture)];
                        workbook.Add((name, r, c), N(text));
                    }
                }
            }
        }
        var bindings = new Dictionary<string, HtmlBound>(StringComparer.Ordinal);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in payload.Fields)
        {
            try
            {
                var source = pair.Value.Source ?? throw new InvalidDataException("Missing binding.");
                var bound = Resolve(source, pair.Value);
                if (N(pair.Value.Text) != bound.Text) throw new InvalidDataException("Display differs from exact source/program format.");
                bindings.Add(pair.Key, bound);
            }
            catch (Exception e) when (e is ArgumentException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or IndexOutOfRangeException)
            { errors[pair.Key] = e.Message; }
        }
        foreach (var pair in payload.Fields)
        {
            if (errors.ContainsKey(pair.Key)) continue;
            var field = pair.Value; var bound = bindings[pair.Key];
            if (field.Short != null)
            {
                if (!bindings.TryGetValue(field.Short, out var shortBound) || payload.Fields[field.Short].Short != null)
                    errors[pair.Key] = "Short field missing, invalid or cyclic.";
                else if (payload.Fields[field.Short].Source.Format == "run-suffix")
                {
                    var label = payload.Fields[field.Short];
                    if (field.Value.HasValue || label.Value.HasValue || (field.Source with { Format = null }) != (label.Source with { Format = null })
                        || !N(field.Text).EndsWith(N(label.Text), StringComparison.Ordinal))
                        errors[pair.Key] = "Short run label must be a text suffix bound to the same full-name source.";
                }
                else if (payload.Fields[field.Short].Source.Format != "G3" || !shortBound.Number.HasValue)
                    errors[pair.Key] = "Short label must be the program's G3 formatting of a numeric source.";
                else if (field.Value.HasValue && field.Value != shortBound.Number && field.Value != bound.Number)
                    errors[pair.Key] = "Geometry differs from the stored precision used for the short label.";
            }
            else if (field.Value.HasValue && field.Value != bound.Number)
                errors[pair.Key] = "Geometry differs from its direct stored source.";
        }
        foreach (var (location, id) in FieldReferences(payload))
            if (!payload.Fields.ContainsKey(id)) errors[location] = "Missing field ID: " + id;
        foreach (var measure in payload.DotMeasures)
        {
            if (payload.Fields.TryGetValue(measure.Label, out var label)
                && (label.Source.Kind != "axis-label" || label.Source.Property != measure.Id))
                errors[measure.Label] = "Axis label is not bound to this measure.";
            foreach (string id in measure.Ticks)
                if (payload.Fields.TryGetValue(id, out var tick)
                    && (tick.Source.Kind != "axis-tick" || tick.Source.Property?.StartsWith(measure.Id + "/", StringComparison.Ordinal) != true))
                    errors[id] = "Axis tick is not bound to this measure.";
        }
        foreach (var gap in payload.DatabaseGap)
            if (payload.Fields.TryGetValue(gap.Fraction, out var fraction)
                && (fraction.Unit != "fraction" || fraction.Source.Scale != 1 || fraction.Short == null
                    || !payload.Fields.TryGetValue(fraction.Short, out var compact) || compact.Unit != "fraction"
                    || compact.Source.Scale != 1 || compact.Source.Format != "G3"))
                errors[gap.Fraction] = "Database gap short label must retain the native fraction unit and scale.";
        foreach (var family in payload.Composition.Families)
            foreach (var coverage in family.Coverage)
            {
                try { CheckCoverage(coverage); }
                catch (Exception e) when (e is ArgumentException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException)
                { errors[coverage.SourceFigureId] = e.Message; }
            }
        foreach (var figure in payload.Quality.Concat(payload.Sensitivity)) CheckPng(figure.SourceFigureId, figure.Png, Text(figure.Title), Text(figure.Caption));
        using (var writer = new StreamWriter(output, false, new UTF8Encoding(false)))
        {
            writer.WriteLine("location,number,status,csv,cell,match_kind,context,subject,candidate_count");
            foreach (var pair in payload.Fields)
            {
                bool found = !errors.TryGetValue(pair.Key, out string? reason);
                var bound = bindings.GetValueOrDefault(pair.Key);
                var tokens = pair.Value.Source.Format == "run-suffix" ? Array.Empty<Match>() : Tokens.Matches(pair.Value.Text).Cast<Match>().ToArray();
                var values = tokens.Length == 0 ? new[] { pair.Value.Text } : tokens.Select(m => m.Value);
                foreach (string value in values) Write(new[] { "html field " + pair.Key, value, found ? "MATCH" : "NOT FOUND", bound?.Source ?? "", bound?.Cell ?? "",
                    found ? "explicit " + pair.Value.Source.Kind + (pair.Value.Source.Format == null ? "" : " format " + pair.Value.Source.Format) : reason!, pair.Value.Text, pair.Key, found ? "1" : "0" });
            }
            foreach (var pair in errors.Where(e => !payload.Fields.ContainsKey(e.Key))) Write(new[] { pair.Key, "", "NOT FOUND", "", "", pair.Value, "", "", "0" });
            void Write(IEnumerable<string> row) => writer.WriteLine(string.Join(",", row.Select(value => SpreadsheetSafeCsv.Encode(value))));
        }
        Console.WriteLine($"HTML number trace: {payload.Fields.Count} bound fields, {errors.Count} NOT FOUND. Output: {output}");
        return errors.Count == 0 ? 0 : 1;

        string Text(string id) => payload.Fields.TryGetValue(id, out var field) ? N(field.Text) : "";
        void CheckCoverage(HtmlReportCoverage coverage)
        {
            if (!pictures.Any(p => p.Title == Text(coverage.Title) && p.Caption == Text(coverage.Caption)))
                errors[coverage.SourceFigureId] = "Coverage title/caption do not identify a native Word figure.";
            if (coverage.Png != null) CheckPng(coverage.SourceFigureId, coverage.Png, Text(coverage.Title), Text(coverage.Caption));
            else if (project != null && manifest != null)
            {
                if (coverage.LengthLabel == null || !payload.Fields.TryGetValue(coverage.LengthLabel, out var length)
                    || HtmlReportStoredValues.Read(length.Source, project, manifest) is not int n || n != coverage.Length)
                    errors[coverage.SourceFigureId] = "Live length differs from the stored sequence length.";
                foreach (var span in coverage.Peptides.Concat(coverage.Domains))
                {
                    if (span.Range == null || !payload.Fields.TryGetValue(span.Range, out var range)) { errors[coverage.SourceFigureId] = "Live interval lacks stored endpoints."; continue; }
                    if (errors.ContainsKey(span.Range)) continue;
                    object value = HtmlReportStoredValues.Read(range.Source, project, manifest);
                    if (!Equals(value.GetType().GetProperty("Start")?.GetValue(value), span.Start) || !Equals(value.GetType().GetProperty("End")?.GetValue(value), span.End))
                        errors[span.Range] = "Live geometry differs from stored endpoints.";
                }
            }
            else errors[coverage.SourceFigureId] = "Live coverage requires its stored source project.";
        }
        void CheckPng(string id, string png, string title, string caption)
        {
            const string prefix = "data:image/png;base64,";
            string hash = "";
            try { if (png.StartsWith(prefix, StringComparison.Ordinal)) hash = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(png[prefix.Length..]))); }
            catch (FormatException) { }
            if (!pictures.Any(p => p.Title == title && p.Caption == caption && p.Hash == hash)) errors[id] = "Image differs from its native Word figure.";
        }
        HtmlBound Resolve(HtmlReportReference source, HtmlReportField field)
        {
            if (!double.IsFinite(source.Scale)) throw new InvalidDataException("Non-finite display scale.");
            if (source.Kind is "axis-label" or "axis-tick")
            {
                if (!HtmlReportAxisTicks.TryResolve(source, out var resolved) || resolved == null)
                    throw new InvalidDataException("Unknown or invalid axis source recipe.");
                if (field.Value != resolved.Value || field.Unit != resolved.Unit)
                    throw new InvalidDataException("Axis value or unit differs from the shared resolver.");
                return new(N(resolved.Text), resolved.Value, "axis", source.Property ?? "");
            }
            if (source.Kind is "stored-value" or "stored-family")
            {
                if (project == null || manifest == null) throw new InvalidDataException("Stored field requires its source project.");
                var pointer = source;
                if (source.Kind == "stored-family")
                {
                    var study = source.Study == "effective" ? manifest.EffectivePanelFamilies?.RunShares : manifest.FamilyRunShares;
                    if (study == null) throw new InvalidDataException("Stored family study unavailable.");
                    int i = study.Families.FindIndex(f => f.FamilyUserId.ToString(CultureInfo.InvariantCulture) == source.Family);
                    if (i < 0) throw new InvalidDataException("Stored family unavailable.");
                    string path = (source.Study == "effective" ? "/manifest/EffectivePanelFamilies/RunShares" : "/manifest/FamilyRunShares") + "/Families/" + i;
                    if (source.Run != null)
                    {
                        int run = study.Families[i].Runs.FindIndex(r => r.Run == source.Run);
                        if (run < 0) throw new InvalidDataException("Stored run unavailable."); path += "/Runs/" + run;
                    }
                    pointer = source with { Kind = "stored-value", Property = path + "/" + source.Property };
                }
                object raw = HtmlReportStoredValues.Read(pointer, project, manifest);
                double? number = raw is double or int or long or float or decimal ? Convert.ToDouble(raw, CultureInfo.InvariantCulture) * source.Scale : null;
                return new(N(HtmlReportStoredValues.Display(pointer, project, manifest)), number, "project", pointer.Property!);
            }
            string text, address;
            switch (source.Kind)
            {
                case "word-cell":
                    if (source.Table is not > 0 || source.Row is not > 0 || source.Column is not > 0) throw new InvalidDataException("Word cell coordinates unavailable.");
                    var cell = tables[source.Table.Value - 1].Elements<W.TableRow>().ElementAt(source.Row.Value - 1).Elements<W.TableCell>().ElementAt(source.Column.Value - 1);
                    text = N(string.Join("\n", cell.Elements<W.Paragraph>().Select(p => p.InnerText))); address = "table " + source.Table + " " + ColumnName(source.Column.Value) + source.Row; break;
                case "workbook-cell":
                    text = workbook[(source.Sheet ?? "", source.Row ?? -1, source.Column ?? -1)]; address = source.Sheet + "!" + ColumnName(source.Column!.Value) + source.Row; break;
                case "word-property": text = source.Property switch { "Title" => word.PackageProperties.Title ?? "", "Creator" => word.PackageProperties.Creator ?? "", _ => throw new InvalidDataException("Unknown Word property.") }; address = source.Property!; break;
                case "figure-title": if (!pictures.Any(p => p.Title == field.Text)) throw new InvalidDataException("Word figure title unavailable."); text = field.Text; address = source.Figure ?? ""; break;
                case "figure-caption": if (!pictures.Any(p => p.Caption == field.Text)) throw new InvalidDataException("Word figure caption unavailable."); text = field.Text; address = source.Figure ?? ""; break;
                case "word-text":
                    string paragraph = source.ParagraphText == null ? throw new InvalidDataException("Exact paragraph text missing.") : N(source.ParagraphText);
                    int ordinal = source.Occurrence;
                    var equal = paragraphs.Where(p => p == paragraph).ToArray();
                    if (ordinal < 0 || ordinal >= equal.Length || !equal[ordinal].Contains(field.Text, StringComparison.Ordinal)) throw new InvalidDataException("Exact Word paragraph/ordinal field not found.");
                    text = field.Text; address = "paragraph occurrence " + ordinal; break;
                default: throw new InvalidDataException("Unknown HTML source kind: " + source.Kind);
            }
            if (source.Format == "run-suffix")
            {
                if (source.Kind is not ("word-cell" or "workbook-cell") || source.Scale != 1 || field.Value.HasValue
                    || string.IsNullOrEmpty(field.Text) || !text.EndsWith(N(field.Text), StringComparison.Ordinal))
                    throw new InvalidDataException("Run suffix must be text from the bound full run name.");
                return new(N(field.Text), null, source.Kind == "workbook-cell" ? Path.GetFileName(workbookPath) : Path.GetFileName(wordPath), address);
            }
            double? numeric = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed) ? parsed * source.Scale : null;
            string display = source.Format == null && source.Scale == 1 ? text : numeric?.ToString(source.Format ?? "G6", CultureInfo.InvariantCulture) ?? throw new InvalidDataException("Numeric source is unavailable.");
            return new(N(display), numeric, source.Kind == "workbook-cell" ? Path.GetFileName(workbookPath) : Path.GetFileName(wordPath), address);
        }
    }

    private static IEnumerable<(string Location, string Id)> FieldReferences(HtmlReportPayload p)
    {
        var ids = new List<string?> { p.Title, p.Authors, p.Interpretation };
        foreach (var f in p.Composition.Families)
        {
            ids.AddRange(new[] { f.Name, f.Mean, f.Sd, f.Cv, f.WholeMeasured, f.Proteins, f.UniqueProteins, f.Peptides });
            foreach (var r in f.Runs) ids.AddRange(new[] { r.Name, r.Share, r.Measured });
            foreach (var part in f.Parts) ids.AddRange(new[] { part.Name, part.Share });
            foreach (var c in f.Coverage) { ids.AddRange(new[] { c.Title, c.Caption, c.Protein, c.LengthLabel }); foreach (var s in c.Peptides.Concat(c.Domains)) ids.AddRange(new[] { s.Label, s.Range }); }
        }
        foreach (var h in p.Highlights) ids.AddRange(new[] { h.Name, h.Value, h.Description });
        foreach (var measure in p.DotMeasures)
        {
            ids.Add(measure.Label);
            ids.AddRange(measure.Ticks);
            ids.AddRange(measure.Values.Values);
        }
        foreach (var r in p.CreditedRuns) ids.AddRange(new[] { r.Name, r.Share });
        foreach (var g in p.DatabaseGap) ids.AddRange(new[] { g.Name, g.Fraction });
        foreach (var m in p.Methods) { ids.Add(m.Title); foreach (var f in m.Facts) ids.AddRange(new[] { f.Name, f.Value }); }
        foreach (var f in p.Quality.Concat(p.Sensitivity)) ids.AddRange(new[] { f.Title, f.Caption });
        return ids.Where(id => id != null).Select(id => ("HTML reference " + id, id!));
    }
    private static string N(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
