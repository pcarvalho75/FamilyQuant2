using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Report;

/// <summary>Long detail stays in the native workbook. The Word citation retains the original block
/// id, so the same section/block exclusions govern both outputs. No quantity is recalculated.</summary>
public static class ReportSupplementLayout
{
    private static readonly IReadOnlyDictionary<string, string> TableFiles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["composition-assigned"] = "family_composition_credited.csv", ["composition-measured"] = "family_composition_measured.csv",
        ["family-run-shares"] = "family_run_shares.csv", ["signal-summary"] = "signal_by_run.csv",
        ["protein-count-support"] = "family_protein_counts.csv", ["quality-overlap"] = "de_novo_overlap.csv",
        ["quality-concordance"] = "database_search_concordance.csv", ["quality-family-world"] = "family_world_rates.csv",
        ["subclass-parts"] = "family_subclass_parts.csv", ["domain-parts"] = "family_domain_parts.csv",
        ["product-parts"] = "family_product_parts.csv", ["database-gap-table"] = "database_gap.csv",
        ["others-members"] = "family_others_members.csv",
        ["sensitivity-table"] = "family_sensitivity.csv",
    };

    public static ReportSupplementLayoutResult Apply(ReportDocument source, IReadOnlyDictionary<string, string> csvExports)
    {
        var sheets = new List<ReportSupplementSheet>();
        var usedFiles = new HashSet<string>(StringComparer.Ordinal);
        var sections = source.Sections.Select(section => section.Id.StartsWith(ReportSectionIds.FamilyPrefix, StringComparison.Ordinal)
            ? FamilyBrief(section, sheets) : section with
        {
            Blocks = section.Blocks.Select(block =>
            {
                if (block is not ReportTable table || !MoveToWorkbook(table)) return block;
                ReportTable full = table;
                if (TableFiles.TryGetValue(table.Id, out string? file) && csvExports.TryGetValue(file, out string? csv))
                {
                    full = ReportCsvTable.Build(table.Id, table.Title, csv, table.Caption);
                    usedFiles.Add(file);
                }
                string name = SheetName(table.Id, table.Title);
                sheets.Add(new(table.Id, section.Id, name, full));
                return (ReportBlock)new ReportParagraph(table.Id, table.Title + ". See the companion supplement workbook, sheet '" + name + "'. " + table.Caption);
            }).ToArray(),
        }).ToList();
        foreach (var export in csvExports.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (usedFiles.Contains(export.Key)) continue;
            string id = "export:" + export.Key;
            string owner = ExportSection(export.Key);
            var table = ReportCsvTable.Build(id, export.Key, export.Value,
                "Native program export. The values and empty availability cells are retained as written by the source exporter.");
            sheets.Add(new(id, owner, SheetName(id, export.Key), table));
        }
        var supplement = sections.FindIndex(s => s.Id == ReportSectionIds.Supplements);
        if (supplement >= 0)
        {
            var blocks = sections[supplement].Blocks.ToList();
            blocks.Insert(0, new ReportParagraph("supplement-workbook-guide",
                "The companion workbook uses the same report name followed by _supplement.xlsx. Its Contents sheet lists every selected detail table and its source section. Full current-grouping, per-run, per-protein and per-peptide detail is retained there. Worksheets follow the owning section choices."));
            sections[supplement] = sections[supplement] with { Blocks = blocks };
        }
        return new(source with { Sections = sections }, sheets);
    }

    private static ReportSection FamilyBrief(ReportSection section, List<ReportSupplementSheet> sheets)
    {
        var detail = new List<IReadOnlyList<string>>();
        var observations = new List<string>();
        var panelText = new StringBuilder();
        foreach (var block in section.Blocks)
        {
            if (block is ReportTable table)
            {
                for (int row = 0; row < table.Rows.Count; row++)
                    for (int column = 0; column < table.Columns.Count; column++)
                    {
                        string raw = table.Rows[row][column];
                        string display = BriefValue(table, column, raw);
                        detail.Add(new[] { table.Title, (row + 1).ToString(CultureInfo.InvariantCulture), table.Columns[column], raw, display });
                    }
                detail.Add(new[] { table.Title, "", "Caption / units", table.Caption, table.Caption });
                if (table.Id.EndsWith(":text", StringComparison.Ordinal))
                    foreach (var row in table.Rows.Where(r => r.Count >= 2 && r[1].Length > 0))
                        panelText.Append(row[0]).Append(": ").Append(row[1]).Append("; ");
                bool effectiveParts = table.Id.EndsWith(":effective:parts", StringComparison.Ordinal);
                if (!table.SupplementOnly && effectiveParts)
                {
                    observations.Add("Pooled subclass, domain and product parts are separate partitions and must not be added across kinds.");
                    foreach (var row in table.Rows.Where(r => r.Count >= 6))
                    {
                        string values = double.TryParse(row[3], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                            ? " Pooled signal = " + BriefValue(table, 3, row[3]) + "; within-family fraction = " + BriefValue(table, 4, row[4]) + "."
                            : " " + row[5];
                        observations.Add(row[0] + " - " + row[1] + ": " + row[2] + "." + values);
                    }
                }
                if (!table.SupplementOnly && !effectiveParts && table.Id.Contains(":observed:", StringComparison.Ordinal) && table.Rows.Count == 1
                    && !table.Id.EndsWith(":run-shares", StringComparison.Ordinal))
                    observations.Add(table.Title + ": " + string.Join("; ", table.Columns.Select((label, column) =>
                    {
                        string display = BriefValue(table, column, table.Rows[0][column]);
                        return label + " = " + display;
                    })) + ".");
            }
            else if (block is ReportParagraph paragraph) detail.Add(new[] { "Status", "", "Text", paragraph.Text, paragraph.Text });
        }
        string id = section.Id + ":full-detail";
        string name = SheetName(id, section.Title);
        sheets.Add(new(id, section.Id, name, new ReportTable(id, section.Title + " - complete panel and observed detail",
            new[] { "Source table", "Row", "Field", "Native value", "Word display value" }, detail,
            "Complete user panel text and native observations. Source captions retain units and availability. The Word brief uses six significant digits for observed numeric values.")));
        string text = panelText.ToString();
        bool excerpt = text.Length > 300;
        if (excerpt) { text = text[..300]; if (char.IsHighSurrogate(text[^1])) text = text[..^1]; }
        var blocks = new List<ReportBlock>();
        if (text.Length > 0) blocks.Add(new ReportParagraph(section.Id + ":panel-brief", (excerpt ? "Panel text excerpt: " : "Panel text: ") + text + (excerpt ? " ..." : "")));
        foreach (string observation in observations) blocks.Add(new ReportParagraph(section.Id + ":observation-brief:" + blocks.Count.ToString(CultureInfo.InvariantCulture), observation));
        blocks.AddRange(section.Blocks.OfType<ReportParagraph>());
        blocks.AddRange(section.Blocks.OfType<ReportFigure>());
        blocks.Add(new ReportParagraph(section.Id + ":detail-reference",
            "Full panel text, literature, notes, sources, observed tables and units: supplement sheet '" + name + "'. Share entries in the brief are fractions unless their label states otherwise."));
        return section with { Blocks = blocks };
    }

    private static string BriefValue(ReportTable table, int column, string raw)
    {
        if (table.Id.Contains(":effective:", StringComparison.Ordinal)
            && table.Columns[column] is "Panel family key" or "Source User IDs" or "Run" or "Part kind" or "Named part" or "Status" or "Reason") return raw;
        return table.Id.Contains(":observed:", StringComparison.Ordinal)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
                ? value.ToString("G6", CultureInfo.InvariantCulture) : raw;
    }

    private static bool MoveToWorkbook(ReportTable table)
    {
        if (table.SupplementOnly) return true;
        if (table.Id is "composition-assigned" or "composition-measured" or "family-run-shares" or "signal-summary" or "protein-count-support" or "others-members") return true;
        if (table.Id.StartsWith("panel-block:", StringComparison.Ordinal)
            && (table.Id.EndsWith(":run-shares", StringComparison.Ordinal) || table.Id.EndsWith(":domains", StringComparison.Ordinal)
                || table.Id.EndsWith(":subclass-definitions", StringComparison.Ordinal))) return true;
        if (table.Id is "family-run-means" or "protein-counts" or "quality-family-world" or "source-world-support" or "methods-project" or "assignment-conflicts-table") return false;
        return table.Rows.Count > 20;
    }

    private static string ExportSection(string file) => file switch
    {
        "peptide_representatives.csv" or "protein_audit.csv" => ReportSectionIds.Supplements,
        "family_pooled_minus_mean.csv" or "family_sensitivity.csv" => ReportSectionIds.Sensitivity,
        "database_gap.csv" => ReportSectionIds.DatabaseGap,
        "assignment_conflicts.csv" => ReportSectionIds.AssignmentConflicts,
        "de_novo_overlap.csv" or "database_search_concordance.csv" or "family_world_rates.csv" => ReportSectionIds.Quality,
        _ => ReportSectionIds.Results,
    };

    internal static string SheetName(string id, string title)
    {
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..6];
        string clean = new(title.Select(c => "[]:*?/\\'".Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        clean = clean.Trim();
        if (clean.Length == 0) clean = "Table";
        if (clean.Length > 24) clean = clean[..24];
        if (char.IsHighSurrogate(clean[^1])) clean = clean[..^1];
        return clean + "_" + suffix;
    }
}
