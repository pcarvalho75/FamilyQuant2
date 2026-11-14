using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.Report;

/// <summary>Native report exports of stored studies. No fitting, attribution or set cover runs here.
/// Display percentages use six significant digits; fraction columns retain round-trip values.</summary>
public static class ReportStudyExports
{
    private static string Number(double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("A stored report quantity is not finite.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
    private static string Optional(double? value) => value.HasValue ? Number(value.Value) : string.Empty;
    private static string Percent(double? value) => value.HasValue
        ? Finite(100 * value.Value).ToString("G6", CultureInfo.InvariantCulture) : string.Empty;
    private static double Finite(double value) { _ = Number(value); return value; }
    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Label(int id, IReadOnlyDictionary<int, string> labels) => labels.TryGetValue(id, out string? value) ? value : Integer(id);
    private static string Csv(IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string>> rows)
    {
        var text = new StringBuilder();
        void Append(IReadOnlyList<string> row) => text.Append(string.Join(",", row.Select(CsvFormatting.AlwaysQuoted))).Append('\n');
        Append(columns);
        foreach (var row in rows)
        {
            if (row.Count != columns.Count) throw new InvalidDataException("Stored report export row width is inconsistent.");
            Append(row);
        }
        return text.ToString();
    }

    public static string MeanShares(FamilyRunSharesResult result, IReadOnlyDictionary<int, string> labels, FamilyReportThresholdResult? grouping = null)
    {
        var bucket = grouping?.Contaminants;
        var ids = bucket?.Families.Select(f => f.FamilyUserId).ToHashSet() ?? new();
        var rows = result.Families.Where(f => !ids.Contains(f.FamilyUserId)).OrderByDescending(f => f.MeanShare).ThenBy(f => f.FamilyUserId).Select(f => (IReadOnlyList<string>)new[]
        {
            Integer(f.FamilyUserId), Label(f.FamilyUserId, labels), Integer(f.AvailableRunCount), Percent(f.MeanShare),
            Percent(f.StandardDeviation), Percent(f.CoefficientOfVariation), Optional(f.MeanShare), Optional(f.StandardDeviation), Optional(f.CoefficientOfVariation),
            Percent(f.MeanShareOfMeasuredSignal), Percent(f.StandardDeviationOfMeasuredSignal), Percent(f.MeanShareOfTic), Percent(f.StandardDeviationOfTic),
            Optional(f.MeanShareOfMeasuredSignal), Optional(f.StandardDeviationOfMeasuredSignal), Optional(f.MeanShareOfTic), Optional(f.StandardDeviationOfTic),
        }).ToList();
        if (bucket?.Families.Count > 0)
            rows.Add(new[] { "", "contaminants", Integer(bucket.AssignedStatistics.AvailableRunCount), Percent(bucket.AssignedStatistics.Mean),
                Percent(bucket.AssignedStatistics.StandardDeviation), Percent(bucket.AssignedStatistics.CoefficientOfVariation), Optional(bucket.AssignedStatistics.Mean),
                Optional(bucket.AssignedStatistics.StandardDeviation), Optional(bucket.AssignedStatistics.CoefficientOfVariation), Percent(bucket.MeasuredStatistics.Mean),
                Percent(bucket.MeasuredStatistics.StandardDeviation), Percent(bucket.TicStatistics.Mean), Percent(bucket.TicStatistics.StandardDeviation),
                Optional(bucket.MeasuredStatistics.Mean), Optional(bucket.MeasuredStatistics.StandardDeviation), Optional(bucket.TicStatistics.Mean), Optional(bucket.TicStatistics.StandardDeviation) });
        return Csv(new[] { "family_user_id", "family", "available_runs", "mean_pct", "sample_sd_pct", "cv_pct", "mean_fraction", "sample_sd_fraction", "cv_ratio",
            "mean_measured_pct", "sample_sd_measured_pct", "mean_tic_pct", "sample_sd_tic_pct", "mean_measured_fraction", "sample_sd_measured_fraction", "mean_tic_fraction", "sample_sd_tic_fraction" }, rows);
    }

    public static string Headline(FamilyReportThresholdResult grouping, FamilyProteinCountsResult? counts, IReadOnlyDictionary<int, string> labels)
    {
        var proteins = counts?.Families.ToDictionary(f => f.FamilyUserId) ?? new Dictionary<int, FamilyProteinCount>();
        var unavailable = grouping.UnavailableFamilies.Select(f => f.FamilyUserId).ToHashSet();
        var rows = new List<IReadOnlyList<string>>();
        foreach (var family in grouping.ListedFamilies.Concat(grouping.UnavailableFamilies)
            .OrderByDescending(f => f.MeanShare).ThenBy(f => f.FamilyUserId))
        {
            proteins.TryGetValue(family.FamilyUserId, out var protein);
            rows.Add(new[] { Integer(family.FamilyUserId), Label(family.FamilyUserId, labels),
                unavailable.Contains(family.FamilyUserId) ? "Unavailable" : "Listed",
                Integer(family.AvailableRunCount), Percent(family.MeanShare), Percent(family.StandardDeviation), Percent(family.CoefficientOfVariation),
                Percent(family.MeanShareOfMeasuredSignal), Percent(family.StandardDeviationOfMeasuredSignal),
                protein == null ? "" : Integer(protein.GreedyProteinCount), protein == null ? "" : Integer(protein.UniquePeptideProteinCount),
                protein == null ? "Not computed" : protein.ExplainsAllObservedPeptides ? "Complete" : "Partial; unexplained peptides retained",
                Optional(family.MeanShare), Optional(family.StandardDeviation), Optional(family.MeanShareOfMeasuredSignal), Optional(family.StandardDeviationOfMeasuredSignal) });
        }
        if (grouping.OtherFamilies.Count > 0)
            rows.Add(new[] { "", "Others", "Canonical grouped members", Integer(grouping.OthersAssignedStatistics.AvailableRunCount),
                Percent(grouping.OthersAssignedStatistics.Mean), Percent(grouping.OthersAssignedStatistics.StandardDeviation), Percent(grouping.OthersAssignedStatistics.CoefficientOfVariation),
                Percent(grouping.OthersMeasuredStatistics.Mean), Percent(grouping.OthersMeasuredStatistics.StandardDeviation), "", "", "Member protein estimates are in the supplement",
                Optional(grouping.OthersAssignedStatistics.Mean), Optional(grouping.OthersAssignedStatistics.StandardDeviation), Optional(grouping.OthersMeasuredStatistics.Mean), Optional(grouping.OthersMeasuredStatistics.StandardDeviation) });
        var contaminants = grouping.Contaminants;
        if (contaminants.Families.Count > 0)
            rows.Add(new[] { "", "contaminants", "Contaminants", Integer(contaminants.AssignedStatistics.AvailableRunCount),
                Percent(contaminants.AssignedStatistics.Mean), Percent(contaminants.AssignedStatistics.StandardDeviation), Percent(contaminants.AssignedStatistics.CoefficientOfVariation),
                Percent(contaminants.MeasuredStatistics.Mean), Percent(contaminants.MeasuredStatistics.StandardDeviation), "", "", "Contains "
                    + string.Join("; ", contaminants.Families.OrderBy(f => f.FamilyUserId).Select(f => "F" + Integer(f.FamilyUserId) + " " + Label(f.FamilyUserId, labels))) + ".",
                Optional(contaminants.AssignedStatistics.Mean), Optional(contaminants.AssignedStatistics.StandardDeviation), Optional(contaminants.MeasuredStatistics.Mean), Optional(contaminants.MeasuredStatistics.StandardDeviation) });
        return Csv(new[] { "family_user_id", "family", "status", "available_runs", "mean_pct", "sample_sd_pct", "cv_pct", "mean_measured_pct", "sample_sd_measured_pct",
            "greedy_protein_estimate", "unique_peptide_proteins", "protein_explanation", "mean_fraction", "sample_sd_fraction", "mean_measured_fraction", "sample_sd_measured_fraction" }, rows);
    }

    public static string Others(FamilyReportThresholdResult grouping, IReadOnlyDictionary<int, string> labels)
    {
        var reasons = grouping.OtherMembership.ToDictionary(m => m.FamilyUserId, m => m.Reason);
        return Csv(new[] { "family_user_id", "family", "reason", "threshold_fraction", "mean_fraction", "mean_pct", "sample_sd_pct", "cv_pct" },
            grouping.OtherFamilies.OrderByDescending(f => f.MeanShare).ThenBy(f => f.FamilyUserId).Select(f => (IReadOnlyList<string>)new[]
            {
                Integer(f.FamilyUserId), Label(f.FamilyUserId, labels), reasons[f.FamilyUserId].ToString(), Number(grouping.Threshold),
                Optional(f.MeanShare), Percent(f.MeanShare), Percent(f.StandardDeviation), Percent(f.CoefficientOfVariation),
            }));
    }

    public static string Conflicts(FamilyReportThresholdResult grouping, IReadOnlyDictionary<int, string> labels) => Csv(
        new[] { "status", "family_user_id", "current_label", "pooled_primary_signal", "pooled_share_pct", "panel_keys", "outside_members", "reason" },
        grouping.ConflictFamilies.OrderBy(c => c.Family.FamilyUserId).Select(c => (IReadOnlyList<string>)new[]
        {
            "Assignment conflict", Integer(c.Family.FamilyUserId), Label(c.Family.FamilyUserId, labels), Number(c.Family.PooledSignal),
            Percent(c.Family.PooledShare), string.Join("; ", c.PanelFamilyKeys), c.HasOutsideMembers ? "Yes" : "No", c.Reason,
        }));

    public static string RunShares(FamilyRunSharesResult result, IReadOnlyDictionary<int, string> labels, FamilyReportThresholdResult? grouping = null)
    {
        var bucket = grouping?.Contaminants;
        var ids = bucket?.Families.Select(f => f.FamilyUserId).ToHashSet() ?? new();
        var rows = result.Families.Where(f => !ids.Contains(f.FamilyUserId)).OrderBy(f => f.FamilyUserId).SelectMany(f => f.Runs.OrderBy(r => r.Run, StringComparer.Ordinal)
            .Select(r => (IReadOnlyList<string>)new[] { Integer(f.FamilyUserId), Label(f.FamilyUserId, labels), r.Run, Number(r.Signal),
                Percent(r.ShareOfAssignedSignal), Percent(r.ShareOfMeasuredSignal), Percent(r.ShareOfTic),
                Optional(r.ShareOfAssignedSignal), Optional(r.ShareOfMeasuredSignal), Optional(r.ShareOfTic) })).ToList();
        if (bucket?.Families.Count > 0)
            rows.AddRange(bucket.Runs.Select(r => (IReadOnlyList<string>)new[] { "", "contaminants", r.Run, Number(r.Signal),
                Percent(r.ShareOfAssignedSignal), Percent(r.ShareOfMeasuredSignal), Percent(r.ShareOfTic), Optional(r.ShareOfAssignedSignal),
                Optional(r.ShareOfMeasuredSignal), Optional(r.ShareOfTic) }));
        return Csv(new[] { "family_user_id", "family", "run", "primary_signal", "assigned_signal_pct", "measured_peptide_signal_pct", "raw_tic_pct",
            "assigned_signal_fraction", "measured_peptide_signal_fraction", "raw_tic_fraction" }, rows);
    }

    public static string RunTotals(FamilyRunSharesResult result) => Csv(
        new[] { "run", "assigned_family_signal", "measured_peptide_signal", "raw_tic" },
        result.Runs.OrderBy(r => r.Run, StringComparer.Ordinal).Select(r => (IReadOnlyList<string>)new[]
            { r.Run, Number(r.AssignedSignal), Number(r.MeasuredSignal), Optional(r.TotalIonCurrent) }));

    /// <summary>The report threshold is also printed outside its tables. Retain the stored
    /// fraction, unrounded percentage and exact G6 Word lexeme without changing existing exports.</summary>
    public static string ReportSettings(FamilyReportThresholdResult grouping) => Csv(
        new[] { "setting", "stored_fraction", "unrounded_percent", "word_percent_6_significant_digits" },
        new[] { (IReadOnlyList<string>)new[] { "Report family threshold", Number(grouping.Threshold),
            Number(100 * grouping.Threshold), Percent(grouping.Threshold) } });

    public static string BasisComparison(FamilyRunSharesResult result, IReadOnlyDictionary<int, string> labels, FamilyReportThresholdResult? grouping = null)
    {
        var bucket = grouping?.Contaminants;
        var ids = bucket?.Families.Select(f => f.FamilyUserId).ToHashSet() ?? new();
        var rows = result.Families.Where(f => !ids.Contains(f.FamilyUserId)).OrderBy(f => f.FamilyUserId).Select(f => (IReadOnlyList<string>)new[]
            { Integer(f.FamilyUserId), Label(f.FamilyUserId, labels), Percent(f.PooledShare), Percent(f.MeanShare), Percent(f.PooledMinusMeanShare),
                Optional(f.PooledShare), Optional(f.MeanShare), Optional(f.PooledMinusMeanShare) }).ToList();
        if (bucket?.Families.Count > 0)
        {
            double? pooled = bucket.Families.All(f => f.PooledShare.HasValue) ? bucket.Families.Sum(f => f.PooledShare!.Value) : null;
            double? mean = bucket.AssignedStatistics.Mean;
            rows.Add(new[] { "", "contaminants", Percent(pooled), Percent(mean), Percent(pooled - mean), Optional(pooled), Optional(mean), Optional(pooled - mean) });
        }
        return Csv(new[] { "family_user_id", "family", "pooled_pct", "run_mean_pct", "pooled_minus_mean_percentage_points", "pooled_fraction", "run_mean_fraction", "pooled_minus_mean_fraction" }, rows);
    }

    public static string ProteinCounts(FamilyProteinCountsResult result, IReadOnlyDictionary<int, string> labels) => Csv(
        new[] { "family_user_id", "family", "greedy_protein_estimate", "unique_peptide_proteins", "observed_peptides", "explained_peptides", "unexplained_peptides", "explanation", "selected_protein_identifiers", "unexplained_sequences" },
        result.Families.OrderBy(f => f.FamilyUserId).Select(f => (IReadOnlyList<string>)new[]
        {
            Integer(f.FamilyUserId), Label(f.FamilyUserId, labels), Integer(f.GreedyProteinCount), Integer(f.UniquePeptideProteinCount),
            Integer(f.ObservedPeptideCount), Integer(f.ExplainedPeptideCount), Integer(f.UnexplainedPeptides.Count),
            f.ExplainsAllObservedPeptides ? "Complete" : "Partial", string.Join("; ", f.SelectedProteinIdentifiers), string.Join("; ", f.UnexplainedPeptides),
        }));

    public static string Parts(IEnumerable<FamilySignalBreakdown> families, IReadOnlyDictionary<int, string> labels) => Csv(
        new[] { "family_user_id", "family", "part_key", "part", "unresolved", "pooled_family_signal", "pooled_part_signal", "within_family_pct", "within_family_fraction" },
        families.OrderBy(f => f.FamilyUserId).SelectMany(f => f.Parts.Select(p => (IReadOnlyList<string>)new[]
        {
            Integer(f.FamilyUserId), Label(f.FamilyUserId, labels), p.PartKey ?? string.Empty, p.Label, p.IsUnresolved ? "1" : "0",
            Number(f.FamilySignal), Number(p.Signal), Percent(p.ShareOfFamily), Number(p.ShareOfFamily),
        })));

    public static string SourceSupport(FamilyWorldSummary result) => Csv(
        new[] { "family", "kind", "decoy_ratio_pct", "right_rate_pct", "known_credits", "correct_known_credits", "evidence", "minimum_known_credits", "decoy_ratio", "right_rate" },
        result.Composition.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Family, r.Kind, Percent(r.WorldDecoyRate), Percent(r.WorldRightRate), Integer(r.WorldKnownCredited), Integer(r.WorldKnownCreditedTrue),
            string.IsNullOrWhiteSpace(r.WorldEvidence) ? "Unavailable" : r.WorldEvidence, Integer(result.MinimumKnownCredits), Optional(r.WorldDecoyRate), Optional(r.WorldRightRate),
        }));
}
