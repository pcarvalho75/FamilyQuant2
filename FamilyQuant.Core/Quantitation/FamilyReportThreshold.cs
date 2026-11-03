using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using FamilyQuant.Panels;

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        /// <summary>Optional report threshold as a fraction. Null uses the default and preserves old files.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ReportThreshold { get; set; }
    }

    /// <summary>One canonical partition of family summaries on the mean per-run assigned-signal share
    /// and explicit panel decisions. No panel key is inferred from a family name.</summary>
    public static class FamilyReportThreshold
    {
        public const double DefaultThreshold = 0.001;

        public static FamilyReportThresholdResult Build(FamilyRunSharesResult source, double threshold) =>
            BuildCore(source, threshold, null, new Dictionary<int, IReadOnlyList<string>>());

        public static FamilyReportThresholdResult Build(FamilyRunSharesResult source, FamilyClusterFile project)
        {
            ArgumentNullException.ThrowIfNull(project);
            var mapping = new Dictionary<int, IReadOnlyList<string>>();
            if (project.FamilyPanel != null && project.PanelProteinAssignments != null)
            {
                if (PanelDerivedDataCurrentness.Check(project).FamilyAssignments == PanelDerivedDataStatus.OutOfDate)
                    throw new InvalidOperationException("Panel family assignments are out of date. Rebuild them before reporting.");
                foreach (var item in project.PanelProteinAssignments.Where(x => x.IsApplied && x.FamilyUserId.HasValue && x.PanelFamilyKey != null))
                {
                    if (item.ProteinIndex < 0 || item.ProteinIndex >= project.MyExtendedFastaItems.Count
                        || project.MyExtendedFastaItems[item.ProteinIndex].SequenceIdentifier != item.Identifier
                        || project.MyExtendedFastaItems[item.ProteinIndex].UserID != item.FamilyUserId)
                        throw new InvalidOperationException("Panel family assignments do not match the current proteins. Rebuild them before reporting.");
                }
                mapping = project.PanelProteinAssignments.Where(x => x.IsApplied && x.FamilyUserId.HasValue && x.PanelFamilyKey != null)
                    .GroupBy(x => x.FamilyUserId!.Value).ToDictionary(g => g.Key,
                        g => (IReadOnlyList<string>)g.Select(x => x.PanelFamilyKey!).Distinct(StringComparer.Ordinal).ToArray());
            }
            return BuildCore(source, project.ReportThreshold ?? DefaultThreshold, project.FamilyPanel, mapping);
        }

        /// <summary>For an authoritative mapping supplied by the assignment producer. Multiple distinct
        /// keys for one quant family are surfaced as a conflict, never guessed or grouped.</summary>
        public static FamilyReportThresholdResult Build(FamilyRunSharesResult source, FamilyClusterFile project,
            IReadOnlyDictionary<int, IReadOnlyList<string>> familyPanelKeys)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(familyPanelKeys);
            return BuildCore(source, project.ReportThreshold ?? DefaultThreshold, project.FamilyPanel, familyPanelKeys);
        }

        private static FamilyReportThresholdResult BuildCore(FamilyRunSharesResult source, double threshold, FamilyPanel? panel,
            IReadOnlyDictionary<int, IReadOnlyList<string>> familyPanelKeys)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!double.IsFinite(threshold) || threshold < 0 || threshold > 1)
                throw new ArgumentOutOfRangeException(nameof(threshold));
            if (panel != null) FamilyPanelValidation.Validate(panel);
            var result = new FamilyReportThresholdResult { Threshold = threshold };
            foreach (var family in source.Families)
            {
                if (family.MeanShare.HasValue && (!double.IsFinite(family.MeanShare.Value) || family.MeanShare.Value < 0))
                    throw new ArgumentException("Family mean shares must be finite and nonnegative.", nameof(source));
                var keys = familyPanelKeys.TryGetValue(family.FamilyUserId, out var assignedKeys)
                    ? assignedKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() : Array.Empty<string>();
                if (keys.Length > 0 && panel == null) throw new ArgumentException("Panel keys require a panel.", nameof(familyPanelKeys));
                foreach (string key in keys)
                    if (panel!.Families.All(x => x.Key != key)) throw new ArgumentException("A family mapping refers to a missing panel key.", nameof(familyPanelKeys));
                if (keys.Length > 1)
                {
                    result.ConflictFamilies.Add(new FamilyReportMappingConflict { Family = family, PanelFamilyKeys = keys.ToList() });
                    continue;
                }
                FamilyPanelFamily? definition = keys.Length == 0 ? null : panel!.Families.Single(x => x.Key == keys[0]);
                bool mergedToExcluded = false;
                if (definition?.Decision == FamilyPanelDecision.MergeIntoAnotherFamily)
                {
                    string? target = FamilyPanelValidation.ResolveFamilyKey(panel!, definition.Key);
                    mergedToExcluded = target == null;
                    definition = target == null ? null : panel!.Families.Single(x => x.Key == target);
                }
                if (mergedToExcluded || definition?.Decision == FamilyPanelDecision.NotAFamily)
                {
                    result.ExcludedFamilies.Add(family);
                    continue;
                }
                FamilyReportOtherReason reason = FamilyReportOtherReason.None;
                if (family.MeanShare.HasValue && family.MeanShare.Value < threshold) reason |= FamilyReportOtherReason.Threshold;
                if (definition?.Decision == FamilyPanelDecision.ReportUnderOthers) reason |= FamilyReportOtherReason.PanelDecision;
                if (reason != FamilyReportOtherReason.None)
                {
                    result.OtherFamilies.Add(family);
                    result.OtherMembership.Add(new FamilyReportOtherMembership { FamilyUserId = family.FamilyUserId, Reason = reason });
                }
                else if (!family.MeanShare.HasValue) result.UnavailableFamilies.Add(family);
                else result.ListedFamilies.Add(family);
            }

            foreach (var run in source.Runs)
            {
                var rows = result.OtherFamilies.Select(f => f.Runs.SingleOrDefault(r =>
                    string.Equals(r.Run, run.Run, StringComparison.OrdinalIgnoreCase))).ToList();
                if (rows.Any(r => r == null))
                    throw new ArgumentException("Each family must have a row for every run.", nameof(source));
                bool complete = run.AssignedSignal > 0 && rows.All(r => r!.ShareOfAssignedSignal != null);
                result.OthersRuns.Add(new FamilyRunShare
                {
                    Run = run.Run,
                    Signal = rows.Sum(r => r!.Signal),
                    ShareOfAssignedSignal = complete ? rows.Sum(r => r!.ShareOfAssignedSignal!.Value) : null,
                    ShareOfMeasuredSignal = run.MeasuredSignal > 0 && rows.All(r => r!.ShareOfMeasuredSignal != null)
                        ? rows.Sum(r => r!.ShareOfMeasuredSignal!.Value) : null
                });
            }
            result.OthersAssignedStatistics = Summarize(result.OthersRuns.Select(x => x.ShareOfAssignedSignal).ToArray(), requireAll: false);
            result.OthersMeasuredStatistics = Summarize(result.OthersRuns.Select(x => x.ShareOfMeasuredSignal).ToArray(), requireAll: true);
            result.OthersMeanShare = result.OthersAssignedStatistics.Mean;
            return result;
        }

        private static FamilyReportShareStatistics Summarize(double?[] runs, bool requireAll)
        {
            double[] values = runs.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
            var result = new FamilyReportShareStatistics { AvailableRunCount = values.Length };
            if (values.Length == 0 || (requireAll && values.Length != runs.Length)) return result;
            double mean = values.Average();
            result.Mean = mean;
            if (values.Length < 2) return result;
            double sd = Math.Sqrt(values.Sum(x => (x - mean) * (x - mean)) / (values.Length - 1));
            result.StandardDeviation = sd;
            if (mean != 0) result.CoefficientOfVariation = sd / mean;
            return result;
        }
    }
}
