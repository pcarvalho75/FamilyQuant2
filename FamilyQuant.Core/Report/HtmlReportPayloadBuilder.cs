using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FamilyQuant.Report;

/// <summary>Projects selected, already prepared Word/native cells. No search, quantitation,
/// aggregation, evidence fetch or missing-study calculation belongs in this producer.</summary>
public static partial class HtmlReportPayloadBuilder
{
    public static HtmlReportPayload Build(ReportDocument document, IReadOnlyList<ReportSupplementSheet> sheets,
        ReportSelection? selection = null, HtmlReportContext? context = null, string wordFileName = "report.docx")
        => new Builder(document, sheets, selection, context ?? new(), wordFileName).Build();

    private sealed partial class Builder
    {
        private sealed record Frame(ReportTable Table, string Owner, int? WordTable, string? Sheet)
        {
            internal HtmlReportReference Reference(int row, int column) => new()
            {
                Kind = WordTable.HasValue ? "word-cell" : "workbook-cell", Table = WordTable,
                Sheet = Sheet, Row = row + 2, Column = column + 1,
            };
        }
        private readonly ReportDocument document;
        private readonly HtmlReportContext context;
        private readonly ReportSelection? selection;
        private readonly HtmlReportLinks links;
        private readonly HashSet<string> excluded;
        private readonly Dictionary<string, Frame> frames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Frame> workbookFrames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ReportSection> sections = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, HtmlReportField> fields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ReportTable> nativeTables = new(StringComparer.Ordinal);
        private string[]? wordParagraphs;

        internal Builder(ReportDocument document, IReadOnlyList<ReportSupplementSheet> sheets,
            ReportSelection? selection, HtmlReportContext context, string wordFileName)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));
            this.context = context;
            this.selection = selection;
            excluded = new(selection?.ExcludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            string name = Path.GetFileName(wordFileName);
            links = new() { Word = Uri.EscapeDataString(name), Workbook = Uri.EscapeDataString(Path.GetFileNameWithoutExtension(name) + "_supplement.xlsx") };
            foreach (var section in document.Sections.Where(s => !excluded.Contains(s.Id))) sections.Add(section.Id, section);
            foreach (var sheet in sheets.Where(s => sections.ContainsKey(s.OwnerSectionId) && !excluded.Contains(s.Id)
                && (s.SourceTableId == null || !excluded.Contains(s.SourceTableId))))
            {
                var frame = new Frame(sheet.Table, sheet.OwnerSectionId, null, sheet.Name);
                workbookFrames[sheet.Id] = frame; frames[sheet.Id] = frame;
            }
            int tableIndex = 0;
            foreach (var section in document.Sections.Where(s => sections.ContainsKey(s.Id)))
                foreach (var table in section.Blocks.OfType<ReportTable>().Where(t => !t.SupplementOnly && !excluded.Contains(t.Id)))
                    frames[table.Id] = new(ReportListSheets.WordView(table), section.Id, ++tableIndex, null);
        }

        internal HtmlReportPayload Build()
        {
            string? title = sections.ContainsKey(ReportSectionIds.Title) ? Add(document.Title, new() { Kind = "word-property", Property = "Title" }) : null;
            string? authors = sections.ContainsKey(ReportSectionIds.Title) && document.Authors.Length > 0
                ? Add(document.Authors, new() { Kind = "word-property", Property = "Creator" }) : null;
            var families = Families();
            var payload = new HtmlReportPayload
            {
                Title = title, Authors = authors, Links = links,
                Composition = new() { IsAdditive = context.IsAdditive && !HasBlock("composition-overlap-warning"), Families = families,
                    ContributionRows = ContributionRows(families) },
                Highlights = Highlights(), CreditedRuns = CreditedRuns(families), DatabaseGap = DatabaseGap(),
                MassQualityCharts = MassQualityCharts(), // U3b native projection, no browser analysis.
                // D47: this is a plot-first page. No methods/quality prose or interpretation block.
                Fields = fields,
            };
            // Preserve all old bindings and axis IDs before appending the new stored-source view.
            var measures = DotMeasures(families);
            var withEntries = DatabaseEntryFamilies(families);
            return payload with
            {
                Composition = payload.Composition with { Families = withEntries },
                DotMeasures = measures.Concat(DotMeasures(withEntries, databaseOnly: true)).ToArray(),
                CoverageCatalog = context.CoverageCatalogProvider?.Invoke(selection),
            };
        }

        private bool HasBlock(string id) => !excluded.Contains(id) && sections.Values.Any(s => s.Blocks.Any(b => b.Id == id));

        private string Add(string text, HtmlReportReference source, string unit = "", double? value = null)
        {
            if (value.HasValue && !double.IsFinite(value.Value)) throw new InvalidDataException("A stored HTML quantity is not finite.");
            if (source.Kind == "word-text") source = LocateParagraph(text, source);
            string id = "f" + (fields.Count + 1).ToString("D4", CultureInfo.InvariantCulture);
            fields.Add(id, new(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), value, unit, source));
            return id;
        }

        private HtmlReportReference LocateParagraph(string text, HtmlReportReference source)
        {
            if (wordParagraphs == null)
            {
                using var memory = new MemoryStream(WordReportWriter.BuildBytes(document, selection));
                using var word = WordprocessingDocument.Open(memory, false);
                wordParagraphs = word.MainDocumentPart!.Document!.Body!.Descendants<W.Paragraph>().Select(p => p.InnerText).ToArray();
            }
            string? paragraph = source.ParagraphText;
            if (paragraph == null)
            {
                if (wordParagraphs.Contains(text, StringComparer.Ordinal)) paragraph = text;
                else
                {
                    var candidates = wordParagraphs.Where(p => p.Contains(text, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
                    if (candidates.Length == 1) paragraph = candidates[0];
                }
            }
            if (paragraph == null || source.Occurrence < 0 || !paragraph.Contains(text, StringComparison.Ordinal)
                || wordParagraphs.Count(p => p == paragraph) <= source.Occurrence)
                throw new InvalidDataException("A word-text field needs an exact, unambiguous paragraph and valid occurrence.");
            return source with { ParagraphText = paragraph };
        }

        private string Cell(Frame frame, int row, int column, string unit = "", double? stored = null,
            HtmlReportReference? storedSource = null)
        {
            string text = frame.Table.Rows[row][column];
            double? number = null;
            HtmlReportReference precision = storedSource ?? frame.Reference(row, column);
            // Only structured cells are parsed, never prose; missing display values stay missing.
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed))
            {
                number = stored ?? parsed;
                if (frame.WordTable.HasValue && workbookFrames.TryGetValue(frame.Table.Id, out var workbook)
                    && row < workbook.Table.Rows.Count && column < workbook.Table.Columns.Count
                    && workbook.Table.Columns[column].Contains("(unrounded; Word:", StringComparison.Ordinal)
                    && double.TryParse(workbook.Table.Rows[row][column], NumberStyles.Float, CultureInfo.InvariantCulture, out double native)
                    && double.IsFinite(native))
                {
                    if (stored.HasValue && native != stored.Value) throw new InvalidDataException("HTML geometry and its native unrounded cell differ.");
                    number = native; precision = workbook.Reference(row, column);
                }
            }
            string id = Add(text, frame.Reference(row, column), unit, number);
            if (number.HasValue && unit is "%" or "pp" or "fraction") AddShort(id, number.Value, precision);
            return id;
        }

        private void AddShort(string exact, double nativeValue, HtmlReportReference nativeSource, string? unit = null)
        {
            string shortId = Add(nativeValue.ToString("G3", CultureInfo.InvariantCulture), nativeSource with { Format = "G3" }, unit ?? fields[exact].Unit, nativeValue);
            fields[exact] = fields[exact] with { Short = shortId };
        }

        private static int Column(Frame frame, params string[] names)
        {
            for (int i = 0; i < frame.Table.Columns.Count; i++)
                if (names.Any(name => frame.Table.Columns[i].Equals(name, StringComparison.OrdinalIgnoreCase)
                    || frame.Table.Columns[i].StartsWith(name + " (", StringComparison.OrdinalIgnoreCase))) return i;
            return -1;
        }

        private string? ShareCell(Frame frame, int row, string unit, double? stored, string family, string property, params string[] names)
        {
            int column = Column(frame, names);
            if (column < 0 || frame.Table.Rows[row][column].Length == 0) return null;
            if (stored.HasValue && frame.Table.Rows[row][column] != stored.Value.ToString("G6", CultureInfo.InvariantCulture))
                throw new InvalidDataException("The exact Word share does not match its stored quantity and display rule.");
            HtmlReportReference? source = null;
            if (stored.HasValue && context.StoredStudies?.FamilyRunShares is { } shares)
            {
                int index = shares.Families.FindIndex(f => f.FamilyUserId.ToString(CultureInfo.InvariantCulture) == family);
                string member = property switch { "mean" => "MeanShare", "sd" => "StandardDeviation", "whole" => "MeanShareOfMeasuredSignal", "cv" => "CoefficientOfVariation", _ => throw new ArgumentException("Unknown stored share property.") };
                source = new() { Kind = "stored-value", Property = "/manifest/" + (context.UsesPanel && !context.IsIdentity
                    ? "EffectivePanelFamilies/RunShares" : "FamilyRunShares") + "/Families/" + index.ToString(CultureInfo.InvariantCulture) + "/" + member, Scale = 100 };
            }
            return Cell(frame, row, column, unit, stored, source);
        }

        private string? OptionalCell(Frame frame, int row, string unit, double? stored, params string[] names)
        {
            int column = Column(frame, names);
            return column < 0 || frame.Table.Rows[row][column].Length == 0 ? null : Cell(frame, row, column, unit, stored);
        }

        private ReportTable Native(Frame frame)
        {
            if (frame.Table.SupplementSource is not { } source) return frame.Table;
            if (!nativeTables.TryGetValue(frame.Table.Id, out var table))
                nativeTables[frame.Table.Id] = table = ReportCsvTable.Build(frame.Table.Id, frame.Table.Title, source.Csv, "");
            return table;
        }

        private IReadOnlyList<HtmlReportFamily> Families()
        {
            bool panel = context.UsesPanel || document.Sections.Any(s => s.Blocks.Any(b => b.Id == "panel-family-run-means"));
            string id = panel ? "panel-family-run-means" : "family-run-means";
            if (!HasBlock(id) || !frames.TryGetValue(id, out var headline)) return Array.Empty<HtmlReportFamily>();
            int nameColumn = Column(headline, "Family", "family");
            if (nameColumn < 0) return Array.Empty<HtmlReportFamily>();
            var native = Native(headline);
            int nativeId = native.Columns.ToList().IndexOf("family_user_id");
            int nativeName = native.Columns.ToList().IndexOf("family");
            var stored = context.StoredStudies?.FamilyRunShares?.Families.ToDictionary(f => f.FamilyUserId.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
            var result = new List<HtmlReportFamily>();
            for (int row = 0; row < headline.Table.Rows.Count; row++)
            {
                string key = "row:" + row.ToString(CultureInfo.InvariantCulture);
                bool others = false;
                if (nativeId >= 0 && row < native.Rows.Count)
                {
                    if (nativeName >= 0 && native.Rows[row][nativeName] != headline.Table.Rows[row][nameColumn])
                        throw new InvalidDataException("The prepared HTML headline and its native row identity differ.");
                    key = native.Rows[row][nativeId];
                    others = key.Length == 0;
                    if (others) key = "others";
                }
                FamilyRunShareSummary? summary = null;
                if (stored != null) stored.TryGetValue(key, out summary);
                // Preserve native row order and canonical Others. Never introduce a second fold or a top-N rule.
                var family = new HtmlReportFamily
                {
                    Id = key, IsOthers = others, Name = Cell(headline, row, nameColumn),
                    Mean = ShareCell(headline, row, "%", summary?.MeanShare * 100, key, "mean", "Mean share (%)", "mean_pct"),
                    Sd = ShareCell(headline, row, "pp", summary?.StandardDeviation * 100, key, "sd", "Sample SD (pp)", "sample_sd_pct"),
                    WholeMeasured = ShareCell(headline, row, "%", summary?.MeanShareOfMeasuredSignal * 100, key, "whole", "Whole measured (%)", "mean_measured_pct"),
                    Proteins = OptionalCell(headline, row, "", null, "Proteins needed (greedy)", "greedy_protein_estimate"),
                    UniqueProteins = OptionalCell(headline, row, "", null, "Proteins with own peptide", "unique_peptide_proteins"),
                };
                result.Add(FamilyDetails(family, summary));
            }
            return result;
        }
    }
}
