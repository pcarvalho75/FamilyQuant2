using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Evidence;

public static partial class PanelEvidenceClassifier
{
    /// <summary>Compare row membership before and after named-family separation and row-specific
    /// scope. Both sides keep the same scope words, reviewed search and current user choices.</summary>
    public static PanelTieCounts CountNamedFamilyRules(FamilyClusterFile file, EvidenceClassificationOptions options,
        NeighbourSearch? search = null, IReadOnlyList<NeighbourReference>? references = null)
    {
        var panel = file.FamilyPanel ?? throw new InvalidOperationException("Import a family panel before counting its families.");
        references ??= References(file, options);
        search ??= file.EvidenceSource?.NearestReviewed;
        var after = Propose(file, options, search, references);
        var beforeLinks = BuildLinks(panel, references, options, true, useNamedFamilyRules: false);
        var context = new ClassificationContext(panel, beforeLinks, useNamedFamilyRules: false);
        var neighbours = search?.ByQuery() ?? new Dictionary<string, NearestNeighbour>(StringComparer.Ordinal);
        var before = file.MyExtendedFastaItems.Select((protein, index) => (protein, index))
            .Where(row => !NearestReviewed.IsDecoy(row.protein))
            .Select(row => Classify(row.protein, row.index, options, context,
                neighbours.GetValueOrDefault(row.protein.SequenceIdentifier), references, search != null)).ToArray();
        var beforeCounts = before.GroupBy(p => p.Family, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var afterCounts = after.Proteins.GroupBy(p => p.Family, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return new(beforeLinks.Count(l => l.NeedsConfirmation), after.PanelContext!.Links.Count(l => l.NeedsConfirmation),
            panel.Families.Select(f => new PanelFamilyTieCount(f.DisplayName.Length > 0 ? f.DisplayName : f.Key,
                beforeCounts.GetValueOrDefault(f.Key), afterCounts.GetValueOrDefault(f.Key))).ToArray());
    }
}
