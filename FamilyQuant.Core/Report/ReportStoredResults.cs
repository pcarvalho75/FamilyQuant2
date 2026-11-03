using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Places stored study values and their native report CSV cells without recalculating them.</summary>
public static class ReportStoredResults
{
    public static ReportResultPacket Apply(ReportResultPacket current, QuantManifest manifest,
        IReadOnlyDictionary<int, string> familyLabels, FamilyReportThresholdResult? grouping = null)
    {
        var exports = new Dictionary<string, string>(current.CsvExports, StringComparer.Ordinal);
        var sections = current.Sections.ToDictionary(s => s.Id, s => s.Blocks.ToList(), StringComparer.Ordinal);
        var results = sections[ReportSectionIds.Results];
        var sensitivity = sections[ReportSectionIds.Sensitivity];
        var supplement = sections[ReportSectionIds.Supplements];
        ReportSection? conflicts = null;
        ReportTable Table(string id, string title, string file, string csv, string caption, string[] columns, string[] headings)
        {
            exports.Add(file, csv);
            var table = ReportCsvTable.Build(id, title, csv, caption, columns) with { Columns = headings };
            return ReportWordMembership.Filter(ReportTableSupplements.WithStoredPercentages(table, csv, columns), csv, grouping);
        }
        void Remove(string id) => results.RemoveAll(b => b.Id == id);
        const string displayed = " Percentages use six significant digits; companion CSV fraction columns retain round-trip values. Empty values are unavailable, not zero.";
        if (manifest.FamilyRunShares is { } runs)
        {
            if (grouping == null) throw new InvalidOperationException("Stored report studies require the canonical J5c report partition.");
            sections[ReportSectionIds.Summary].RemoveAll(b => b.Id == "summary-composition");
            sections[ReportSectionIds.Summary].Add(new ReportParagraph("summary-composition",
                "Headline family composition uses the arithmetic mean of available per-run shares, with sample SD and CV. Pooled composition and within-family parts are labelled separately. Per-run whole measured peptide signal and RAW TIC shares retain their own denominators. Family-world-only signal remains separate."));
            Remove("family-runs-unavailable");
            exports.Add("family_run_means.csv", ReportStudyExports.MeanShares(runs, familyLabels));
            string headlineCsv = ReportStudyExports.Headline(grouping, manifest.FamilyProteinCounts, familyLabels);
            exports.Add("report_settings.csv", ReportStudyExports.ReportSettings(grouping));
            results.Insert(0, Table("family-run-means", "Headline family composition: mean of per-run shares", "family_composition_headline.csv",
                headlineCsv,
                "Each run's composition uses signal credited to database families. Mean and sample SD use available runs. Whole measured-signal mean/SD are separate stored statistics; family-world-only signal is excluded. Protein estimates count database entries, not genes or proteoforms. Others membership and summed-run uncertainty come only from the canonical stored-threshold accessor." + displayed,
                new[] { "family", "mean_pct", "sample_sd_pct", "mean_measured_pct", "sample_sd_measured_pct", "greedy_protein_estimate", "unique_peptide_proteins" },
                new[] { "Family", "Mean share (%)", "Sample SD (pp)", "Whole measured (%)", "Whole-signal SD (pp)", "Proteins needed (greedy)", "Proteins with own peptide" }));
            var paired = ReportCsvTable.Build("paired-source", "Paired source", headlineCsv, "",
                "family_user_id", "family", "status", "mean_pct", "greedy_protein_estimate");
            results.Insert(1, new ReportParagraph("family-paired-intro",
                "Each percentage is the mean of per-run shares of signal credited to database families; abundance is separate from protein diversity. Each count is a greedy estimate of database proteins that explain the family's observed peptides; partial estimates may leave peptides unexplained. These estimates are not counts of genes."));
            int pairedIndex = 2;
            foreach (var row in paired.Rows.Where(r => r[2] == "Listed"))
            {
                string count = row[4].Length == 0 ? "protein count unavailable" : row[4] + (row[4] == "1" ? " protein" : " proteins");
                results.Insert(pairedIndex++, new ReportParagraph("family-paired:" + row[0], row[1] + " " + row[3] + "% (" + count + ")"));
            }
            supplement.RemoveAll(b => b.Id == "others-unavailable");
            supplement.Add(Table("others-members", "Canonical others membership", "family_others_members.csv", ReportStudyExports.Others(grouping, familyLabels),
                "Membership is decided once from the stored mean-share threshold and explicit panel decisions. Reasons are retained; unavailable/conflicting/excluded groups are not silently folded into others.",
                new[] { "family_user_id", "family", "reason", "mean_pct" }, new[] { "Family ID", "Family", "Canonical reason", "Mean share (%)" }));
            results.Add(new ReportParagraph("report-threshold", "Stored report threshold: " + (100 * grouping.Threshold).ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
                + "%. The complete others membership and reasons are in the companion workbook."));
            if (grouping.ConflictFamilies.Count > 0)
            {
                var table = Table("assignment-conflicts-table", "Conflicting assignments, all runs pooled", "assignment_conflicts.csv",
                    ReportStudyExports.Conflicts(grouping, familyLabels),
                    "Flagged User IDs span more than one panel family or mix panel and outside members. Their pooled signal remains outside every reported family and outside Others. The stored denominator is retained; no conflict signal is redistributed.",
                    new[] { "family_user_id", "current_label", "pooled_primary_signal", "panel_keys", "outside_members", "reason" },
                    new[] { "Conflicting User ID", "Current label", "Pooled primary signal", "Panel keys", "Outside members", "Reason" });
                conflicts = new ReportSection(ReportSectionIds.AssignmentConflicts, "Assignment conflicts", new ReportBlock[] { table });
                sections[ReportSectionIds.Summary].Insert(0, new ReportParagraph("summary-conflicts", "Assignment conflicts are flagged in their own section. Their signal is excluded from reported families and Others, with its original amount and reason retained."));
            }
            results.Add(Table("family-run-shares", "Per-run family shares", "family_run_shares.csv", ReportStudyExports.RunShares(runs, familyLabels),
                "Shares of credited family signal, whole measured peptide signal and RAW TIC have distinct, explicit denominators." + displayed,
                new[] { "family", "run", "assigned_signal_pct", "measured_peptide_signal_pct", "raw_tic_pct" },
                new[] { "Family", "Run", "Credited signal (%)", "Measured peptide signal (%)", "RAW TIC (%)" }));
            results.Add(Table("family-run-totals", "Recorded run denominators", "family_run_totals.csv", ReportStudyExports.RunTotals(runs),
                "Credited family signal, measured peptide signal and recorded RAW TIC are separate quantities. Missing TIC is unavailable.",
                new[] { "run", "assigned_family_signal", "measured_peptide_signal", "raw_tic" },
                new[] { "Run", "Credited family signal", "Measured peptide signal", "RAW TIC" }));
            sensitivity.Insert(0, Table("pooled-minus-mean", "Pooled-minus-mean diagnostic", "family_pooled_minus_mean.csv", ReportStudyExports.BasisComparison(runs, familyLabels),
                "Signed difference: all-runs-pooled family share minus the arithmetic mean of per-run shares, in percentage points. This diagnostic does not rescale a pooled subclass or domain part by the run mean." + displayed,
                new[] { "family", "pooled_minus_mean_percentage_points" }, new[] { "Family", "Pooled minus mean (percentage points)" }));
        }
        if (manifest.FamilyProteinCounts is { } counts)
        {
            Remove("protein-counts-unavailable");
            string csv = ReportStudyExports.ProteinCounts(counts, familyLabels);
            results.Add(Table("protein-counts", "Proteins needed to explain the peptides (greedy estimate)", "family_protein_counts.csv", csv,
                "Database-protein diversity across all assigned peptides. Greedy set cover is an approximation; proteins with a peptide of their own use database-wide uniqueness. A partial explanation retains its unexplained peptides. These are not gene or proteoform counts.",
                new[] { "family", "greedy_protein_estimate", "unique_peptide_proteins", "unexplained_peptides", "explanation" },
                new[] { "Family", "Proteins needed (greedy estimate)", "Proteins with a peptide of their own", "Unexplained peptides", "Explanation" }));
            supplement.Add(ReportCsvTable.Build("protein-count-support", "Database-protein explanation support", csv,
                "Selected database identifiers and unexplained sequences are retained for audit.", "family", "selected_protein_identifiers", "unexplained_sequences")
                with { Columns = new[] { "Family", "Selected database proteins", "Unexplained sequences" } });
        }
        void Parts(string unavailableId, string id, string title, string file, IReadOnlyList<FamilySignalBreakdown> rows, string method)
        {
            Remove(unavailableId);
            results.Add(Table(id, title + ", all runs pooled", file, ReportStudyExports.Parts(rows, familyLabels),
                "All runs pooled. Within-family fractions use the pooled family signal. Unresolved or outside parts remain explicit. " + method + displayed,
                new[] { "family", "part", "pooled_family_signal", "pooled_part_signal", "within_family_pct" },
                new[] { "Family", "Part", "Pooled family signal", "Pooled part signal", "Within-family share (%)" }));
        }
        if (manifest.FamilySubclassParts is { } subclasses)
            Parts("subclasses-unavailable", "subclass-parts", "Subclass parts", "family_subclass_parts.csv", subclasses.Families, subclasses.Method);
        if (manifest.FamilyDomainParts is { } domains)
        {
            Parts("domains-unavailable", "domain-parts", "Domain parts", "family_domain_parts.csv", domains.Domains, domains.Method);
            Parts("domains-unavailable", "product-parts", "Mature-product parts", "family_product_parts.csv", domains.Products, domains.Method);
        }
        if (manifest.FamilySensitivity is { } variants)
        {
            FamilySensitivityTable.Validate(variants);
            sensitivity.RemoveAll(b => b.Id == "sensitivity-unavailable");
            sensitivity.Add(Table("sensitivity-table", "Stored shared-peptide and cutoff sensitivity", "family_sensitivity.csv",
                FamilySensitivityTable.BuildCsv(variants), FamilySensitivityTable.Explanation + " " + variants.Method,
                new[] { "family", "policy", "variant", "share_of_measured_signal", "status" },
                new[] { "Family", "Shared-peptide policy", "Computed variant", "Measured-signal fraction", "Status" }));
            sensitivity.Insert(0, new ReportTable("sensitivity-variants", "Computed sensitivity variants",
                new[] { "Variant", "Source SHA-256", "Recorded source cutoffs" },
                variants.Variants.Select(v => (IReadOnlyList<string>)new[] { v.Label, v.SourceSha256,
                    string.Join("; ", v.DeNovoInputs.Select(i => i.SourceDisplayName + " " + i.ConfidenceField + " >= " + i.MinimumConfidence.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Distinct(StringComparer.Ordinal)) }).ToArray(),
                "Only stored, computed variants appear. Full-count policies overlap and cannot be summed as a normalized composition. Missing comparison variants remain unavailable."));
        }
        var final = current.Sections.Select(s => s with { Blocks = sections[s.Id] }).ToList();
        if (conflicts != null) final.Add(conflicts);
        return new(final, exports);
    }
}
