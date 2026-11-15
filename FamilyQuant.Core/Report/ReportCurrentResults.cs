using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

public sealed record ReportResultPacket(IReadOnlyList<ReportSection> Sections, IReadOnlyDictionary<string, string> CsvExports);

/// <summary>Draft results from existing program exports. Missing new studies remain explicit.</summary>
public static class ReportCurrentResults
{
    public static ReportResultPacket Build(AnalyticsData data, QuantManifest manifest, FamilyEnvelopeQuantResult? families,
        SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique, FamilyReportThresholdResult? grouping = null,
        IReadOnlyDictionary<int, string>? familyLabels = null, IReadOnlySet<int>? contaminantFamilyIds = null)
    {
        var exports = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var results = new List<ReportBlock>(); var quality = new List<ReportBlock>();
        var sensitivity = new List<ReportBlock>(); var gap = new List<ReportBlock>();
        var supplement = new List<ReportBlock>(); var summary = new List<ReportBlock>();
        ReportTable Export(string id, string title, string file, string csv, string caption, params string[] columns)
        {
            exports.Add(file, csv);
            return ReportCsvTable.Build(id, title, csv, caption, columns);
        }
        if (families != null)
        {
            if (families.SourceSequenceSummary.Families.Count > 0)
                results.Add(ReportFamilySourceSequences.Build(families.SourceSequenceSummary, familyLabels));
            string basis = "identified observed envelope plus database-mapped de novo measured envelope";
            double assigned = families.Families.Sum(f => f.Signal(policy));
            var measured = Export("composition-measured", "Family shares of the whole measured signal, all runs pooled", "family_composition_measured.csv",
                CsvReport.BuildFamilyComposition(families, policy, families.MeasuredTotalEnvelopeReferenceCalibrated,
                    "fraction of whole measured peptide signal; " + basis, ProteinNameFormatting.ForNewOutput, familyLabels, contaminantFamilyIds),
                "Fractions use the whole measured peptide signal, not RAW TIC. Shared and unattributed rows retain the program's selected allocation policy.",
                "family_user_id", "description", "signal_primary_estimate", "fraction");
            results.Add(measured with { Columns = new[] { "Family ID", "Family", "Primary signal", "Fraction of measured signal" } });
            if (policy != SharedPeptidePolicy.CountInEveryFamily)
            {
                var composition = Export("composition-assigned", "Database-family composition diagnostic, all runs pooled", "family_composition_credited.csv",
                    CsvReport.BuildFamilyComposition(families, policy, assigned, "fraction of signal credited to database families; " + basis, ProteinNameFormatting.ForNewOutput, familyLabels, contaminantFamilyIds),
                    "All runs pooled. Family rows sum to 1 when credited signal is available. Signal found only in UniProt families is excluded. The mean-of-per-run composition is a separate result. These are signal shares, not a direct measurement of sample mass.",
                    "family_user_id", "description", "signal_primary_estimate", "fraction");
                results.Insert(0, composition with
                {
                    Columns = new[] { "Family ID", "Family", "Primary signal", "Fraction of credited signal" },
                    Rows = composition.Rows.Where(r => r[0].Length > 0 || r[1] == "contaminants").ToArray(),
                });
            }
            else results.Insert(0, new ReportParagraph("composition-overlap-warning", "Count in every family produces overlapping contributions. They cannot be summed or normalized as sample composition."));
            summary.Add(new ReportParagraph("summary-composition", "The current pooled composition diagnostic is reported beside each family's share of the whole measured peptide signal. All runs are pooled in these tables. The primary signal basis is " + basis + ". Allocation: " + policy.ShortName() + ". The headline mean of per-run shares requires the separately stored per-run study."));
            if (families.MeasuredTotalEnvelopeReferenceCalibrated > 0)
            {
                double shared = families.SharedTotalEnvelopeReferenceCalibrated / families.MeasuredTotalEnvelopeReferenceCalibrated;
                string sharedPercent = (100 * shared).ToString("F2", CultureInfo.InvariantCulture);
                exports.Add("current_grouping_summary.csv", "\"quantity\",\"share_pct\",\"share_fraction\",\"basis\"\n"
                    + "\"shared_between_families\",\"" + sharedPercent + "\",\"" + shared.ToString("R", CultureInfo.InvariantCulture)
                    + "\",\"all runs pooled; primary measured peptide signal\"\n");
                summary.Add(new ReportParagraph("summary-shared-signal", "On the saved current grouping, " + sharedPercent
                    + "% of the primary measured peptide signal is shared between families, with all runs pooled. This shared signal remains separate from family-specific composition."));
            }
        }
        else results.Add(new ReportParagraph("composition-unavailable", "Family composition: not computed in this project."));
        string runCsv = AnalyticsCsv.BuildSummaryCsv(data.Fractions, data.Grouped);
        results.Add(Export("signal-summary", "Signal recovery by run", "signal_by_run.csv", runCsv,
            "Fractions use each run's recorded RAW TIC. Primary recovery and M+0 sensitivity have distinct signal bases. The grouped row pools signal and TIC rather than averaging fractions.",
            "run", "grouped", "recovered_fraction_reference_calibrated", "recovered_fraction_m0", "resolved_isotope_series_fraction_diagnostic")
            with { Columns = new[] { "Run", "Grouped", "Primary recovery fraction", "M+0 recovery fraction", "Detected diagnostic fraction" } });
        exports.Add("peptide_representatives.csv", AnalyticsCsv.BuildPeptideCsv(data.Peptides));
        if (manifest.DeNovoOverlap is { } overlap)
        {
            overlap.Validate();
            var table = Export("quality-overlap", "De novo overlap audit", "de_novo_overlap.csv", DeNovoOverlapStudy.BuildSummaryCsv(overlap),
                "The named scan, charge, precursor and sequence stages have separate counting denominators.");
            quality.Add(table);
        }
        else quality.Add(new ReportParagraph("quality-overlap-unavailable", "De novo overlap audit: not computed in this project."));
        if (manifest.IdentificationConcordance is { } concordance)
        {
            concordance.Validate();
            quality.Add(Export("quality-concordance", "Database-search concordance", "database_search_concordance.csv",
                ConcordanceAuditCsv(concordance), "Agreement is measured among precursor-qualified one-to-one PSM pairs, not all shared scans."));
        }
        else quality.Add(new ReportParagraph("quality-concordance-unavailable", "Database-search concordance: not computed in this project."));
        var world = manifest.FamilyWorldResult?.ForLength(manifest.DatabaseGapMinimumTagLength ?? FamilyWorldOptions.DefaultMinimumTagLength);
        if (world != null)
        {
            string csv = world.BuildCsv();
            quality.Add(ReportWordMembership.FilterWorld(Export("quality-family-world", "UniProt families support and rates", "family_world_rates.csv", csv,
                "Decoy ratio is decoy signal divided by credited signal. Right rate is correct known credits divided by known credits and remains unavailable below the stored evidence floor.",
                "family", "decoy_rate", "known_credited", "known_credited_true", "right_rate", "evidence")
                with { Columns = new[] { "Family", "Decoy ratio", "Known credits", "Correct credits", "Right rate", "Evidence" } }, world, grouping));
            quality.Add(new ReportParagraph("quality-world-rules", world.Rules + "\n" + world.KnownAnswerMethod));
            if (world.CompositionRules.Length > 0)
                gap.Add(ReportWordMembership.FilterWorld(Export("database-gap-table", "Stored Database gap components", "database_gap.csv", world.BuildCompositionCsv(),
                    "Identified, database de novo and signal found only in UniProt families are distinct. Global shared and unexplained rows are retained; signal found only in UniProt families is not added to database family composition.",
                    "family", "identified_envelope", "database_de_novo_envelope", "family_world_de_novo_envelope", "fraction_of_measured_total")
                    with { Columns = new[] { "Family or global row", "Identified", "Database de novo", "UniProt families only", "Fraction of measured signal" } }, world, grouping));
            else gap.Add(new ReportParagraph("gap-table-unavailable", "Database gap components: not computed in this project."));
        }
        else
        {
            quality.Add(new ReportParagraph("quality-world-unavailable", "UniProt families support and error rates: not computed in this project."));
            gap.Add(new ReportParagraph("gap-unavailable", "Database gap: not computed in this project."));
        }
        foreach (var (id, title, stored) in new[] { ("subclasses", "Subclass parts", manifest.FamilySubclassParts != null),
            ("family-runs", "Per-run family shares", manifest.FamilyRunShares != null),
            ("protein-counts", "Proteins needed to explain the peptides (greedy estimate)", manifest.FamilyProteinCounts != null),
            ("domains", "Domain and mature-product parts", manifest.FamilyDomainParts != null) })
            results.Add(new ReportParagraph(id + "-unavailable", title + (stored ? ": stored study awaits report integration." : ": not computed in this project.")));
        sensitivity.Add(new ReportParagraph("sensitivity-unavailable", "Shared-peptide and de novo cutoff sensitivity: not computed in this project."));
        supplement.Add(new ReportParagraph("supplements-exports", "Companion CSV files are native program exports. The report copies their numeric cells without reformatting. Empty source cells mean unavailable, not zero."));
        supplement.Add(new ReportParagraph("others-unavailable", "Report threshold and the others membership list: not computed in this project."));
        return new(new[]
        {
            new ReportSection(ReportSectionIds.Summary, "Summary", summary),
            new ReportSection(ReportSectionIds.Results, "Results", results),
            new ReportSection(ReportSectionIds.Quality, "Quality", quality),
            new ReportSection(ReportSectionIds.Sensitivity, "Sensitivity", sensitivity),
            new ReportSection(ReportSectionIds.DatabaseGap, "Database gap", gap),
            new ReportSection(ReportSectionIds.Supplements, "Supplements", supplement),
        }, exports);
    }

    // Preserve the original audit verbatim, then expose the two stored source counts as integer cells.
    // Empty denominators keep raw PSM counts distinct from precursor-qualified agreement rates.
    private static string ConcordanceAuditCsv(IdentificationConcordanceSummary summary) =>
        IdentificationConcordanceStudy.BuildCsv(summary)
        + "PatternLab_vs_PEAKS_database,patternlab_psm_rows," + summary.PatternLabPsmRows.ToString(CultureInfo.InvariantCulture)
        + ",,\"Stored PatternLab database-search PSM rows; source audit only, not a comparable-pair denominator\"\n"
        + "PatternLab_vs_PEAKS_database,peaks_database_psm_rows," + summary.PeaksDatabasePsmRows.ToString(CultureInfo.InvariantCulture)
        + ",,\"Stored PEAKS database-search PSM rows; source audit only, not a comparable-pair denominator\"\n";
}
