using System.Collections.Generic;
using System;
using System.Linq;
using FamilyQuant.DeNovo;

namespace FamilyQuant;

public static partial class FamilyCompositionStudy
{
    public const string ProteinCountLabel = "Proteins needed to explain the peptides (greedy estimate)";
    public const string UniquePeptideProteinCountLabel = "Proteins with a peptide of their own";
    public const string ProteinCountsMethod = "Proteins needed to explain the peptides (greedy estimate): repeatedly select the database target " +
        "covering the most unexplained distinct peptide sequences; ties use ordinal protein identifiers. " +
        "Exact containment with I/L folded and modifications removed, across all assigned quantified peptides " +
        "(shared peptides included). This is an approximation, not an exact minimum or a gene/proteoform count. " +
        "Greedy explanation candidates exclude contaminants. Proteins with a peptide of their own require " +
        "uniqueness across all database targets, including contaminants; decoys are excluded. " +
        "Homology-only support does not establish exact containment and remains unexplained.";

    public static FamilyProteinCountsResult BuildProteinCounts(FamilyPeptideAttribution attribution,
        IEnumerable<FastaItemExtended> database) => BuildProteinCountsCore(attribution, database);

    /// <summary>Store only after the entire deterministic explanation has been computed.</summary>
    public static void StoreProteinCounts(QuantManifest manifest, FamilyPeptideAttribution attribution,
        IEnumerable<FastaItemExtended> database)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.FamilyProteinCounts = BuildProteinCounts(attribution, database);
    }

    private static FamilyProteinCountsResult BuildProteinCountsCore(FamilyPeptideAttribution attribution,
        IEnumerable<FastaItemExtended> database)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(database);
        List<FastaItemExtended> targets = database.Where(p => p != null
            && !SequenceLabels.IsDecoy(p.SequenceIdentifier ?? string.Empty))
            .OrderBy(p => p.SequenceIdentifier, StringComparer.Ordinal).ToList();
        if (targets.Any(p => string.IsNullOrWhiteSpace(p.SequenceIdentifier))
            || targets.GroupBy(p => p.SequenceIdentifier, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("Database targets require distinct nonempty protein identifiers.", nameof(database));
        var byFamily = new SortedDictionary<int, HashSet<string>>();
        foreach (FamilyAttributedPeptide assigned in attribution.Peptides)
        {
            string peptide = DeNovoReconciler.NormalizeSequence(assigned.Peptide.Sequence);
            if (assigned.FamilyUserIds.Count > 0 && peptide.Length == 0)
                throw new ArgumentException("An assigned peptide has no interpretable residue sequence.", nameof(attribution));
            foreach (int family in assigned.FamilyUserIds)
            {
                if (!byFamily.TryGetValue(family, out var set)) byFamily[family] = set = new(StringComparer.Ordinal);
                set.Add(peptide);
            }
        }
        string[] sequences = byFamily.Values.SelectMany(s => s).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var index = new CompositionPeptideIndex(sequences);
        var support = sequences.ToDictionary(s => s, _ => new List<FastaItemExtended>(), StringComparer.Ordinal);
        foreach (FastaItemExtended protein in targets)
            foreach (int match in index.Match(protein.Sequence ?? string.Empty)) support[sequences[match]].Add(protein);
        var result = new FamilyProteinCountsResult
        {
            MembershipSha256 = attribution.MembershipSha256,
            Method = ProteinCountsMethod,
        };
        foreach (var entry in byFamily)
        {
            var row = new FamilyProteinCount { FamilyUserId = entry.Key, ObservedPeptideCount = entry.Value.Count };
            var coverage = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var own = new HashSet<string>(StringComparer.Ordinal);
            foreach (string peptide in entry.Value.OrderBy(s => s, StringComparer.Ordinal))
            {
                var familyTargets = support[peptide].Where(p => p.UserID == entry.Key
                    && !SequenceLabels.IsContaminant(p.SequenceIdentifier)).ToList();
                if (familyTargets.Count == 0) row.UnexplainedPeptides.Add(peptide);
                foreach (var protein in familyTargets)
                {
                    if (!coverage.TryGetValue(protein.SequenceIdentifier, out var set))
                        coverage[protein.SequenceIdentifier] = set = new(StringComparer.Ordinal);
                    set.Add(peptide);
                    if (support[peptide].Count == 1) own.Add(protein.SequenceIdentifier);
                }
            }
            var uncovered = new HashSet<string>(entry.Value.Except(row.UnexplainedPeptides), StringComparer.Ordinal);
            while (uncovered.Count > 0)
            {
                var best = coverage.Select(p => (p.Key, Count: p.Value.Count(uncovered.Contains)))
                    .OrderByDescending(p => p.Count).ThenBy(p => p.Key, StringComparer.Ordinal).First();
                if (best.Count == 0) throw new InvalidOperationException("A supported peptide could not be covered.");
                row.SelectedProteinIdentifiers.Add(best.Key);
                uncovered.ExceptWith(coverage[best.Key]);
            }
            row.GreedyProteinCount = row.SelectedProteinIdentifiers.Count;
            row.UniquePeptideProteinCount = own.Count;
            result.Families.Add(row);
        }
        return result;
    }
}
