using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FamilyQuant.DeNovo;

namespace FamilyQuant;

public static class ProteinCoverageFigures
{
    /// <summary>Consumes FL's persisted pooled localization and current sequence/evidence; no search is performed.</summary>
    public static IReadOnlyList<AnalyticsFigure> FromStored(FamilyPeptideLocalizationResult localization,
        FamilyClusterFile project, int topPerFamily = 3)
    {
        ArgumentNullException.ThrowIfNull(localization); ArgumentNullException.ThrowIfNull(project);
        if (topPerFamily < 1) throw new ArgumentOutOfRangeException(nameof(topPerFamily));
        var proteins = (project.MyExtendedFastaItems ?? new List<FastaItemExtended>()).ToLookup(p => (p.UserID, p.SequenceIdentifier));
        var ranked = localization.Families.SelectMany(f => f.Proteins.Select(p => (Family: f.FamilyUserId, Protein: p))).ToArray();
        if (ranked.GroupBy(p => (p.Family, p.Protein.ProteinIdentifier)).Any(g => g.Count() != 1))
            throw new ArgumentException("Stored protein ranking has duplicate family/protein identities.", nameof(localization));
        if (ranked.Any(p => p.Protein.Rank < 1)
            || ranked.GroupBy(p => p.Family).Any(g => g.Select(p => p.Protein.Rank).Distinct().Count() != g.Count()))
            throw new ArgumentException("Stored protein ranks must be positive and distinct within each family.", nameof(localization));
        var rankedKeys = ranked.Select(p => (p.Family, p.Protein.ProteinIdentifier)).ToHashSet();
        if (localization.Segments.Any(s => !rankedKeys.Contains((s.FamilyUserId, s.ProteinIdentifier))))
            throw new ArgumentException("A localized segment has no matching ranked protein.", nameof(localization));
        var maps = new List<ProteinCoverageMapData>();
        foreach (var entry in ranked.GroupBy(p => p.Family).OrderBy(g => g.Key)
            .SelectMany(g => g.OrderBy(p => p.Protein.Rank).Take(topPerFamily)))
        {
            var candidates = proteins[(entry.Family, entry.Protein.ProteinIdentifier)].ToArray();
            if (candidates.Length != 1 || string.IsNullOrEmpty(candidates[0].Sequence))
                throw new InvalidOperationException("Coverage protein is missing or ambiguous in the current family: " + entry.Protein.ProteinIdentifier);
            var item = candidates[0];
            if (entry.Protein.ProteinLength != item.Sequence.Length || !string.Equals(entry.Protein.ProteinSequenceSha256,
                    ProteinCoverageMapData.SequenceHash(item.Sequence), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Stored localization sequence differs for " + entry.Protein.ProteinIdentifier + "; recompute localization before drawing coverage.");
            var segments = localization.Segments.Where(s => s.FamilyUserId == entry.Family && s.ProteinIdentifier == item.SequenceIdentifier)
                .OrderBy(s => s.Side, StringComparer.Ordinal).ThenBy(s => s.NormalizedSequence, StringComparer.Ordinal)
                .ThenBy(s => s.MatchKind).ThenBy(s => string.Join(";", s.Intervals.Select(i =>
                    i.Start.ToString(CultureInfo.InvariantCulture) + ":" + i.End.ToString(CultureInfo.InvariantCulture))), StringComparer.Ordinal).ToArray();
            var placements = segments.Select((s, i) =>
            {
                if (string.IsNullOrWhiteSpace(s.NormalizedSequence) || !double.IsFinite(s.Signal) || s.Signal < 0)
                    throw new ArgumentException("Invalid localized peptide metadata.", nameof(localization));
                var source = s.Side switch
                {
                    NativeQuantFractions.SideIdentified => CoveragePeptideSource.Identified,
                    NativeQuantFractions.SideDeNovo => CoveragePeptideSource.DeNovo,
                    _ => throw new ArgumentException("Unsupported localization evidence side.", nameof(localization))
                };
                var kind = s.MatchKind switch
                {
                    PeptideLocalizationMatchKind.ExactContainment => CoveragePlacementKind.Exact,
                    PeptideLocalizationMatchKind.Aligned => CoveragePlacementKind.Aligned,
                    _ => throw new ArgumentException("Unsupported localization match kind.", nameof(localization))
                };
                return new CoveragePeptidePlacement(i.ToString(CultureInfo.InvariantCulture) + ":" + s.Side + ":" + s.NormalizedSequence,
                    s.NormalizedSequence, source, kind, s.Intervals.Select(r => new CoverageInterval(r.Start, r.End)).ToArray());
            }).ToArray();
            maps.Add(new ProteinCoverageMapData(entry.Family, item.SequenceIdentifier, item.SequenceIdentifier, item.Sequence,
                item.Evidence?.Positions, entry.Protein.ProteinSequenceSha256, placements, entry.Protein.CreditedSignal,
                string.IsNullOrWhiteSpace(localization.Method) ? "All runs pooled; display allocation across candidate proteins and placements" : localization.Method));
        }
        return maps.Select(Build).ToArray();
    }

    public static AnalyticsFigure Build(ProteinCoverageMapData data) => new(
        "protein_coverage_" + data.FamilyUserId.ToString(CultureInfo.InvariantCulture) + "_"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ProteinId))).ToLowerInvariant() + ".png",
        new ProteinCoverageMapChart(data), ProteinCoverageMapChart.RecommendedHeight(data));

    /// <summary>Ranking values are supplied by localization, never inferred from annotation count or sequence length.</summary>
    public static IReadOnlyList<AnalyticsFigure> BuildTopPerFamily(IEnumerable<ProteinCoverageMapData> maps, int topPerFamily = 3)
    {
        ArgumentNullException.ThrowIfNull(maps);
        if (topPerFamily < 1) throw new ArgumentOutOfRangeException(nameof(topPerFamily));
        var input = maps.ToArray();
        if (input.GroupBy(m => (m.FamilyUserId, m.ProteinId)).Any(g => g.Count() > 1))
            throw new ArgumentException("Coverage maps must have distinct family/protein identities.", nameof(maps));
        return input.GroupBy(m => m.FamilyUserId).OrderBy(g => g.Key)
            .SelectMany(g => g.OrderByDescending(m => m.RankingSignal).ThenBy(m => m.ProteinId, StringComparer.Ordinal).Take(topPerFamily))
            .Select(Build).ToArray();
    }
}
