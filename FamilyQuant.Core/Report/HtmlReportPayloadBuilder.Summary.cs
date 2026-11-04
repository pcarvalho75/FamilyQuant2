using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class HtmlReportPayloadBuilder
{
    private sealed partial class Builder
    {
        private Frame? Findings()
        {
            if (!sections.ContainsKey(ReportSectionIds.Findings) || excluded.Contains("findings-numbers")) return null;
            var frame = context.UsesPanel ? Workbook("export:panel_family_report_findings.csv") : null;
            return frame ?? (context.IsIdentity ? Workbook("findings-numbers", "export:report_findings.csv") : null);
        }

        private IReadOnlyList<HtmlReportHighlight> Highlights()
        {
            var frame = Findings();
            if (frame == null) return Array.Empty<HtmlReportHighlight>();
            int statement = Column(frame, "statement", "Statement"), quantity = Column(frame, "quantity", "Quantity");
            if (statement < 0 || quantity < 0) return Array.Empty<HtmlReportHighlight>();
            var result = new List<HtmlReportHighlight>();
            foreach (var wanted in new[] { (Statement: "listed", Quantity: "families above the threshold", Unit: ""),
                (Statement: "replicates", Quantity: "runs", Unit: ""), (Statement: "listed", Quantity: "report threshold (%)", Unit: "%") })
                for (int row = 0; row < frame.Table.Rows.Count; row++)
                {
                    if (frame.Table.Rows[row][statement] != wanted.Statement || frame.Table.Rows[row][quantity] != wanted.Quantity) continue;
                    string? value = NativeCell(frame, row, "value", wanted.Unit, shortLabel: wanted.Unit != "%");
                    if (value == null) continue;
                    if (wanted.Unit == "%")
                    {
                        var settings = Workbook("export:report_settings.csv");
                        int native = settings == null ? -1 : Column(settings, "unrounded_percent");
                        if (settings != null && settings.Table.Rows.Count == 1 && native >= 0
                            && double.TryParse(settings.Table.Rows[0][native], NumberStyles.Float, CultureInfo.InvariantCulture, out double raw))
                        {
                            if (raw.ToString("G6", CultureInfo.InvariantCulture) != fields[value].Text)
                                throw new System.IO.InvalidDataException("The prepared threshold and its stored setting differ.");
                            fields[value] = fields[value] with { Value = raw };
                            AddShort(value, raw, settings.Reference(0, native));
                        }
                        else if (fields[value].Value is double known) AddShort(value, known, fields[value].Source);
                    }
                    result.Add(new(Cell(frame, row, quantity), value, null));
                    break;
                }
            return result;
        }

        private IReadOnlyList<HtmlRunCredit> CreditedRuns(IReadOnlyList<HtmlReportFamily> families)
        {
            var frame = Findings();
            var runs = context.StoredStudies?.FamilyRunShares?.Runs;
            if (frame == null || runs == null) return Array.Empty<HtmlRunCredit>();
            int statement = Column(frame, "statement"), quantity = Column(frame, "quantity");
            if (statement < 0 || quantity < 0) return Array.Empty<HtmlRunCredit>();
            var result = new List<HtmlRunCredit>();
            foreach (var run in runs)
                for (int row = 0; row < frame.Table.Rows.Count; row++)
                {
                    if (frame.Table.Rows[row][statement] != "credited" || frame.Table.Rows[row][quantity] != run.Run + " credited share of measured (%)") continue;
                    string? share = NativeCell(frame, row, "value", "%");
                    if (share == null) continue;
                    string? name = families.SelectMany(f => f.Runs).Select(r => r.Name).FirstOrDefault(id => fields[id].Text == run.Run);
                    if (name == null)
                    {
                        var totals = Workbook("family-run-totals", "export:family_run_totals.csv", "export:panel_family_run_totals.csv");
                        int col = totals == null ? -1 : Column(totals, "run", "Run");
                        if (totals != null && col >= 0)
                            for (int r = 0; r < totals.Table.Rows.Count; r++)
                                if (totals.Table.Rows[r][col] == run.Run) { name = Cell(totals, r, col); break; }
                    }
                    if (name != null) result.Add(new(name, share));
                    break;
                }
            return result;
        }

        private IReadOnlyList<HtmlGapRow> DatabaseGap()
        {
            if (!HasBlock("database-gap-table") || !frames.TryGetValue("database-gap-table", out var frame)) return Array.Empty<HtmlGapRow>();
            int name = Column(frame, "Family or global row", "family"), fraction = Column(frame, "Fraction of measured signal", "fraction_of_measured_total");
            if (name < 0 || fraction < 0) return Array.Empty<HtmlGapRow>();
            // The compact Word table is already projected. A full native worksheet requires the
            // very same canonical projection, never a new HTML membership rule.
            var hidden = context.World != null ? ReportWordMembership.HiddenWorldFamilies(context.World, context.SourceGrouping) : null;
            if (!frame.WordTable.HasValue && (context.World == null || context.SourceGrouping == null)) return Array.Empty<HtmlGapRow>();
            var result = new List<HtmlGapRow>();
            for (int row = 0; row < frame.Table.Rows.Count; row++)
            {
                if (hidden?.Contains(frame.Table.Rows[row][name]) == true) continue;
                string text = frame.Table.Rows[row][fraction];
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) continue;
                string field = Add(text, frame.Reference(row, fraction), "fraction", value);
                AddShort(field, 100 * value, frame.Reference(row, fraction) with { Scale = 100 }, "%");
                result.Add(new(Cell(frame, row, name), field));
            }
            return result;
        }
    }
}
