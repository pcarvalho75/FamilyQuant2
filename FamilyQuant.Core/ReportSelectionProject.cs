using FamilyQuant.Report;
using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ReportSelection? ReportSelection { get; set; }
    }
}

namespace FamilyQuant.Report
{
    /// <summary>Persists unchecked stable ids, including unavailable parts. No selection means include all.</summary>
    public static class ReportSelectionStore
    {
        public static bool Apply(FamilyClusterFile file, ReportSelection selection)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(selection);
            var ids = (selection.ExcludedIds ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            if (ids.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Report choice ids must not be empty.", nameof(selection));
            if (ids.SetEquals(file.ReportSelection?.ExcludedIds ?? Array.Empty<string>())) return false;
            file.ReportSelection = ids.Count == 0 ? null : new() { ExcludedIds = ids.OrderBy(id => id, StringComparer.Ordinal).ToArray() };
            return true;
        }
    }
}
