using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class ReportStudyFigures
{
    private const string OthersKey = "others";
    private static string FamilyKey(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static FamilyCompositionData WordOthersNames(FamilyCompositionData data, FamilyReportThresholdResult threshold)
    {
        var others = threshold.OtherFamilies.Select(f => FamilyKey(f.FamilyUserId)).ToHashSet(StringComparer.Ordinal);
        int member = 0;
        return new(data.Families.Select(f => !others.Contains(f.Key) ? f : f with
        {
            Label = "Other member " + (++member).ToString(CultureInfo.InvariantCulture),
            Subclasses = f.Subclasses.Select((p, index) => p with
            {
                Label = p.Key.Length == 0 ? "Subclass not resolved" : "Part " + (index + 1).ToString(CultureInfo.InvariantCulture),
            }).ToArray(),
        }), data.Basis, data.IsMeanOfRuns, data.UnavailableFamilies);
    }

    private static FamilyCompositionData CanonicalRows(FamilyCompositionData data, FamilyReportThresholdResult threshold,
        FamilyProteinCountsResult? counts)
    {
        var listed = threshold.ListedFamilies.Select(f => FamilyKey(f.FamilyUserId)).ToHashSet(StringComparer.Ordinal);
        var others = threshold.OtherFamilies.Select(f => FamilyKey(f.FamilyUserId)).ToHashSet(StringComparer.Ordinal);
        var rows = data.Families.Where(r => listed.Contains(r.Key)).ToList();
        string basis = data.Basis + "; canonical report groups";
        var unavailable = new List<string>();
        if (threshold.UnavailableFamilies.Count > 0)
            unavailable.AddRange(threshold.UnavailableFamilies.Select(f => "Family " + FamilyKey(f.FamilyUserId)));
        if (others.Count > 0)
        {
            var members = data.Families.Where(r => others.Contains(r.Key)).ToArray();
            if (data.IsMeanOfRuns)
            {
                var statistics = threshold.OthersAssignedStatistics;
                if (statistics.Mean is double mean)
                {
                    int? greedy = null, unique = null, unexplained = null;
                    var countRows = counts?.Families.Where(f => others.Contains(FamilyKey(f.FamilyUserId))).ToArray();
                    if (countRows?.Length == others.Count && countRows.All(f => f.SelectedProteinIdentifiers.Count == f.GreedyProteinCount
                        && f.SelectedProteinIdentifiers.All(id => !string.IsNullOrWhiteSpace(id))))
                    {
                        greedy = countRows.SelectMany(f => f.SelectedProteinIdentifiers).Distinct(StringComparer.Ordinal).Count();
                        unexplained = countRows.SelectMany(f => f.UnexplainedPeptides).Distinct(StringComparer.Ordinal).Count();
                    }
                    // One member retains its own stored count, including older count-only results.
                    if (others.Count == 1 && members.Length == 1)
                    { greedy = members[0].GreedyProteinCount; unique = members[0].UniqueProteinCount; unexplained = members[0].UnexplainedPeptideCount; }
                    rows.Add(new(OthersKey, "Others", mean * 100, statistics.StandardDeviation * 100,
                        statistics.AvailableRunCount, Array.Empty<FamilySubclassValue>(), greedy, unique, unexplained));
                }
                else unavailable.Add("Others");
            }
            else
            {
                if (members.Length != others.Count) throw new InvalidOperationException("Canonical Others members are missing from pooled subclass parts.");
                // Keys are local to a family. Retain the member key and label to avoid conflating subclasses.
                var parts = members.SelectMany(f => f.Subclasses.Select(p => new FamilySubclassValue(
                    p.Key.Length == 0 ? "" : f.Key + ":" + p.Key, f.Label + ": " + p.Label, p.Percent)))
                    .GroupBy(p => p.Key, StringComparer.Ordinal).Select(g => new FamilySubclassValue(g.Key,
                        g.Key.Length == 0 ? "Subclass not resolved" : g.First().Label, g.Sum(p => p.Percent))).ToArray();
                rows.Add(new(OthersKey, "Others", members.Sum(f => f.MeanPercent), null, 0, parts, null, null));
            }
        }
        if (threshold.Contaminants.Families.Count > 0)
        {
            var contaminants = data.Families.SingleOrDefault(row => row.Key == "contaminants")
                ?? StoredCompositionChartData.WithContaminants(data, threshold.Contaminants).Families.SingleOrDefault(row => row.Key == "contaminants");
            if (contaminants != null) rows.Add(contaminants);
        }
        return new(rows.OrderByDescending(r => r.MeanPercent).ThenBy(r => r.Key, StringComparer.Ordinal), basis,
            data.IsMeanOfRuns, unavailable);
    }

    private static IEnumerable<FamilyWorldCompositionRow> CanonicalSources(IEnumerable<FamilyWorldCompositionRow> source,
        FamilyReportThresholdResult? threshold, List<ReportFigureAvailability> unavailable)
    {
        var rows = source.Select(Snapshot).ToArray();
        if (threshold == null) return rows;
        var others = threshold.OtherFamilies.Select(f => f.FamilyUserId).ToHashSet();
        var blocked = threshold.ExcludedFamilies.Select(f => f.FamilyUserId)
            .Concat(threshold.ConflictFamilies.Select(f => f.Family.FamilyUserId)).ToHashSet();
        var retained = new List<FamilyWorldCompositionRow>(); var small = new List<FamilyWorldCompositionRow>();
        foreach (var row in rows)
        {
            if (row.CuratedUserIds.Any(blocked.Contains)
                || row.CuratedUserIds.Any(others.Contains) && row.CuratedUserIds.Any(id => !others.Contains(id)))
            {
                unavailable.Add(new(ReportSectionIds.Results, row.CuratedUserIds.Any(others.Contains) ? "Family signal source" : "Family signal source: " + row.Family,
                    "The stored source row mixes report memberships or includes an excluded/conflicting group; its signal cannot be split without a new source study."));
            }
            else if (row.CuratedUserIds.Count > 0 && row.CuratedUserIds.All(others.Contains)) small.Add(row);
            else retained.Add(row);
        }
        if (small.Count > 0) retained.Add(new() { Family = "Others", Kind = FamilyWorldCompositionRow.CuratedKind,
            CuratedUserIds = small.SelectMany(r => r.CuratedUserIds).Distinct().Order().ToList(),
            IdentifiedEnvelope = small.Sum(r => r.IdentifiedEnvelope), DatabaseDeNovoEnvelope = small.Sum(r => r.DatabaseDeNovoEnvelope),
            WorldDeNovoEnvelope = small.Sum(r => r.WorldDeNovoEnvelope), WorldEvidence = "Canonical Others membership; stored source amounts combined" });
        return retained;
    }
}
