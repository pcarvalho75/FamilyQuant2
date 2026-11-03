using System;
using System.Collections.Generic;

namespace FamilyQuant;

/// <summary>The same optional stored-study figures for Results, PNG export and report consumers.</summary>
public static class StoredCompositionFigures
{
    public static IReadOnlyList<AnalyticsFigure> Build(QuantManifest? manifest, IReadOnlyDictionary<int, string>? labels = null,
        FamilyClusterFile? currentFamilies = null)
    {
        if (manifest?.FamilyRunShares == null && manifest?.FamilySubclassParts == null) return Array.Empty<AnalyticsFigure>();
        if (currentFamilies != null) FamilyStudyMembership.RequireCurrent(currentFamilies, manifest);
        FamilyStudyMembership.RequireCompatible(manifest.FamilyRunShares, manifest.FamilyProteinCounts, manifest.FamilySubclassParts);
        var means = manifest.FamilyRunShares == null
            ? new FamilyCompositionData(Array.Empty<FamilyCompositionValue>(), "Per-run shares unavailable")
            : StoredCompositionChartData.FromRunShares(manifest.FamilyRunShares, manifest.FamilyProteinCounts, labels);
        var pooled = manifest.FamilySubclassParts == null ? null : StoredCompositionChartData.FromSubclassParts(manifest.FamilySubclassParts, labels);
        var figures = FamilyCompositionFigures.Build(means, pooled);
        if (manifest.FamilyRunShares != null) figures.Add(FamilyCompositionFigures.BuildAbundanceDiversity(means));
        return figures;
    }
}
