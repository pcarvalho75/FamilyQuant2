using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

/// <summary>The same optional stored-study figures for Results, PNG export and report consumers.</summary>
public static class StoredCompositionFigures
{
    public static IReadOnlyList<AnalyticsFigure> Build(QuantManifest? manifest, IReadOnlyDictionary<int, string>? labels = null,
        FamilyRingGrouping? grouping = null, FamilyClusterFile? project = null)
    {
        if (manifest?.FamilyRunShares == null && manifest?.FamilySubclassParts == null
            && manifest?.FamilyPeptideLocalization == null) return Array.Empty<AnalyticsFigure>();
        if (project != null) FamilyStudyMembership.RequireCurrent(project, manifest);
        FamilyStudyMembership.RequireCompatible(manifest.FamilyRunShares, manifest.FamilyProteinCounts,
            manifest.FamilySubclassParts, manifest.FamilyPeptideLocalization);
        var means = manifest.FamilyRunShares == null
            ? new FamilyCompositionData(Array.Empty<FamilyCompositionValue>(), "Per-run shares unavailable")
            : StoredCompositionChartData.FromRunShares(manifest.FamilyRunShares, manifest.FamilyProteinCounts, labels);
        var pooled = manifest.FamilySubclassParts == null ? null : StoredCompositionChartData.FromSubclassParts(manifest.FamilySubclassParts, labels);
        if (pooled != null && manifest.FamilyRunShares != null && manifest.FamilyRunShares.Policy != manifest.FamilySubclassParts!.Policy)
            throw new ArgumentException("Stored run shares and subclass parts use different shared-peptide policies.", nameof(manifest));
        if (grouping == null && pooled != null && manifest.FamilyRunShares != null && project != null)
            grouping = FamilyRingGrouping.FromReportThreshold(FamilyReportThreshold.Build(manifest.FamilyRunShares, project));
        var partition = project != null && manifest.FamilyRunShares != null ? FamilyReportThreshold.Build(manifest.FamilyRunShares, project) : null;
        if (partition != null) means = StoredCompositionChartData.WithContaminants(means, partition.Contaminants);
        var displayedPooled = pooled != null && partition != null ? StoredCompositionChartData.WithContaminants(pooled, partition.Contaminants) : pooled;
        var figures = FamilyCompositionFigures.Build(means, displayedPooled);
        if (manifest.FamilyRunShares != null)
            figures.Add(FamilyCompositionFigures.BuildAbundanceDiversity(new(means.Families.Where(row => row.Key != "contaminants"),
                means.Basis, means.IsMeanOfRuns, means.UnavailableFamilies)));
        if (pooled != null && grouping != null) figures.Add(FamilyCompositionFigures.BuildRing(pooled, grouping));
        if (manifest.FamilyPeptideLocalization != null && project != null)
            figures.AddRange(ProteinCoverageFigures.FromStored(manifest.FamilyPeptideLocalization, project));
        return figures;
    }
}
