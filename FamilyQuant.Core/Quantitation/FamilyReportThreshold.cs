using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        /// <summary>Optional report threshold as a fraction. Null uses the default and preserves old files.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ReportThreshold { get; set; }
    }

    /// <summary>Partitions family summaries by their mean per-run assigned-signal share.</summary>
    public static class FamilyReportThreshold
    {
        public const double DefaultThreshold = 0.001;

        public static FamilyReportThresholdResult Build(FamilyRunSharesResult source, double threshold)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!double.IsFinite(threshold) || threshold < 0 || threshold > 1)
                throw new ArgumentOutOfRangeException(nameof(threshold));
            var result = new FamilyReportThresholdResult { Threshold = threshold };
            foreach (var family in source.Families)
            {
                if (!family.MeanShare.HasValue) result.UnavailableFamilies.Add(family);
                else if (!double.IsFinite(family.MeanShare.Value) || family.MeanShare.Value < 0)
                    throw new ArgumentException("Family mean shares must be finite and nonnegative.", nameof(source));
                else if (family.MeanShare.Value < threshold) result.OtherFamilies.Add(family);
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
                    Signal = rows.Where(r => r != null).Sum(r => r!.Signal),
                    ShareOfAssignedSignal = complete ? rows.Sum(r => r!.ShareOfAssignedSignal!.Value) : null,
                    ShareOfMeasuredSignal = run.MeasuredSignal > 0 && rows.All(r => r?.ShareOfMeasuredSignal != null)
                        ? rows.Sum(r => r!.ShareOfMeasuredSignal!.Value) : null
                });
            }
            var available = result.OthersRuns.Where(r => r.ShareOfAssignedSignal.HasValue).ToList();
            result.OthersMeanShare = available.Count == 0 ? null : available.Average(r => r.ShareOfAssignedSignal!.Value);
            return result;
        }

        public static FamilyReportThresholdResult Build(FamilyRunSharesResult source, FamilyClusterFile project) =>
            Build(source, project.ReportThreshold ?? DefaultThreshold);
    }
}

