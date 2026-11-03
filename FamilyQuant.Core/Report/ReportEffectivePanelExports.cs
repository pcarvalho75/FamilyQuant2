using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Additive effective-key exports. Existing UserID export names and contents are never reused.</summary>
public static class ReportEffectivePanelExports
{
    public static IReadOnlyDictionary<string, string> Build(EffectivePanelFamilyResult result)
    {
        string N(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        string I(int value) => value.ToString(CultureInfo.InvariantCulture);
        string Csv(string[] columns, IEnumerable<IReadOnlyList<string>> rows) => Row(columns) + string.Concat(rows.Select(Row));
        string Row(IReadOnlyList<string> cells) => string.Join(",", cells.Select(CsvFormatting.AlwaysQuoted)) + "\n";
        var families = result.Families.Where(f => f.Kind == EffectivePanelFamilyKind.PanelFamily).OrderBy(f => f.PanelFamilyKey, StringComparer.Ordinal).ToArray();
        var shares = result.RunShares.Families.ToDictionary(f => f.FamilyUserId);
        var counts = result.ProteinCounts.Families.ToDictionary(f => f.FamilyUserId);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["effective_panel_families.csv"] = Csv(new[] { "effective_panel_key", "stored_family_label", "source_user_ids", "policy", "available_runs", "mean_fraction", "sample_sd_fraction", "cv_ratio", "pooled_primary_signal", "pooled_share_fraction", "status" },
                families.Select(f => { var s = shares[f.StudyFamilyId]; return (IReadOnlyList<string>)new[] { f.PanelFamilyKey!, f.DisplayName, string.Join(";", f.SourceUserIds.Select(I)), result.Policy.ToString(),
                    I(s.AvailableRunCount), N(s.MeanShare), N(s.StandardDeviation), N(s.CoefficientOfVariation), N(s.PooledSignal), N(s.PooledShare), s.NoPeptideObserved ? FamilyUnobservedStudies.Status : "Stored computed result" }; })),
            ["effective_panel_family_runs.csv"] = Csv(new[] { "effective_panel_key", "run", "primary_signal", "assigned_fraction", "measured_fraction", "tic_fraction" },
                families.SelectMany(f => shares[f.StudyFamilyId].Runs.Select(r => (IReadOnlyList<string>)new[] { f.PanelFamilyKey!, r.Run, N(r.Signal), N(r.ShareOfAssignedSignal), N(r.ShareOfMeasuredSignal), N(r.ShareOfTic) }))),
            ["effective_panel_protein_counts.csv"] = Csv(new[] { "effective_panel_key", "greedy_protein_estimate", "unique_peptide_proteins", "observed_peptides", "unexplained_peptides", "selected_protein_identifiers" },
                families.Select(f => { var c = counts[f.StudyFamilyId]; return (IReadOnlyList<string>)new[] { f.PanelFamilyKey!, I(c.GreedyProteinCount), I(c.UniquePeptideProteinCount), I(c.ObservedPeptideCount), string.Join(";", c.UnexplainedPeptides), string.Join(";", c.SelectedProteinIdentifiers) }; })),
            ["effective_panel_parts.csv"] = Csv(new[] { "effective_panel_key", "part_kind", "part_key", "part", "status", "pooled_primary_signal", "within_family_fraction", "reason" },
                result.Parts.OrderBy(p => p.PanelFamilyKey, StringComparer.Ordinal).ThenBy(p => p.Kind).ThenBy(p => p.PartKey, StringComparer.Ordinal).Select(p => (IReadOnlyList<string>)new[]
                { p.PanelFamilyKey, p.Kind.ToString(), p.PartKey ?? "", p.Label, ReportEffectivePanelFamilies.State(p.State), N(p.Signal), N(p.ShareOfFamily), p.Reason })),
            ["effective_panel_run_totals.csv"] = Csv(new[] { "run", "assigned_primary_signal", "measured_signal", "raw_tic" }, result.RunShares.Runs.Select(r => (IReadOnlyList<string>)new[] { r.Run, N(r.AssignedSignal), N(r.MeasuredSignal), N(r.TotalIonCurrent) })),
            ["effective_panel_family_map.csv"] = Csv(new[] { "study_family_id", "effective_panel_key", "kind", "source_user_ids" }, result.Families.Select(f => (IReadOnlyList<string>)new[]
                { I(f.StudyFamilyId), f.PanelFamilyKey ?? "", f.Kind.ToString(), string.Join(";", f.SourceUserIds.Select(I)) })),
        };
    }
}
