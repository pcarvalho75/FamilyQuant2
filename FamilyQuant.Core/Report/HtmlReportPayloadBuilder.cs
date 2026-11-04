using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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
        private readonly HtmlReportLinks links;
        private readonly HashSet<string> excluded;
        private readonly Dictionary<string, Frame> frames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Frame> workbookFrames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ReportSection> sections = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, HtmlReportField> fields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ReportTable> nativeTables = new(StringComparer.Ordinal);

        internal Builder(ReportDocument document, IReadOnlyList<ReportSupplementSheet> sheets,
            ReportSelection? selection, HtmlReportContext context, string wordFileName)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));
            this.context = context;
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

        internal HtmlReportPayload Build() => new()
        {
            Title = sections.ContainsKey(ReportSectionIds.Title) ? Add(document.Title, new() { Kind = "word-property", Property = "Title" }) : null,
            Authors = sections.ContainsKey(ReportSectionIds.Title) && document.Authors.Length > 0
                ? Add(document.Authors, new() { Kind = "word-property", Property = "Creator" }) : null,
            Links = links,
            Composition = new() { IsAdditive = context.IsAdditive && !HasBlock("composition-overlap-warning"), Families = Families() },
            Fields = fields,
        };

        private bool HasBlock(string id) => !excluded.Contains(id) && sections.Values.Any(s => s.Blocks.Any(b => b.Id == id));

        private string Add(string text, HtmlReportReference source, string unit = "", double? value = null)
        {
            if (value.HasValue && !double.IsFinite(value.Value)) throw new InvalidDataException("A stored HTML quantity is not finite.");
            string id = "f" + (fields.Count + 1).ToString("D4", CultureInfo.InvariantCulture);
            fields.Add(id, new(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), value, unit, source));
            return id;
        }

        private string Cell(Frame frame, int row, int column, string unit = "", double? stored = null)
        {
            string text = frame.Table.Rows[row][column];
            double? number = stored;
            // Only structured cells are parsed, never prose. Typed stored geometry takes precedence.
            if (!number.HasValue && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                && double.IsFinite(parsed)) number = parsed;
            return Add(text, frame.Reference(row, column), unit, number);
        }

        private static int Column(Frame frame, params string[] names)
        {
            for (int i = 0; i < frame.Table.Columns.Count; i++)
                if (names.Any(name => frame.Table.Columns[i].Equals(name, StringComparison.OrdinalIgnoreCase)
                    || frame.Table.Columns[i].StartsWith(name + " (Word:", StringComparison.OrdinalIgnoreCase))) return i;
            return -1;
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
                result.Add(new()
                {
                    Id = key, IsOthers = others, Name = Cell(headline, row, nameColumn),
                    Mean = OptionalCell(headline, row, "%", summary?.MeanShare * 100, "Mean share (%)", "mean_pct"),
                    Sd = OptionalCell(headline, row, "pp", summary?.StandardDeviation * 100, "Sample SD (pp)", "sample_sd_pct"),
                    WholeMeasured = OptionalCell(headline, row, "%", summary?.MeanShareOfMeasuredSignal * 100, "Whole measured (%)", "mean_measured_pct"),
                    Proteins = OptionalCell(headline, row, "", null, "Proteins needed (greedy)", "greedy_protein_estimate"),
                    UniqueProteins = OptionalCell(headline, row, "", null, "Proteins with own peptide", "unique_peptide_proteins"),
                });
            }
            return result;
        }
    }
}
