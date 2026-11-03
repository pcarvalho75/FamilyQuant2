using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class ReportStudyFigures
{
    private static void AddCoverage(QuantManifest manifest, FamilyClusterFile project, PanelFamilyMap? map, int top,
        List<ReportStudyFigure> figures, List<ReportFigureAvailability> unavailable)
    {
        var localization = manifest.FamilyPeptideLocalization;
        if (localization != null)
        {
            foreach (var family in localization.Families)
                if (family.Proteins.Any(p => !double.IsFinite(p.CreditedSignal) || p.CreditedSignal < 0 || p.Rank < 1)
                    || family.Proteins.Select(p => p.ProteinIdentifier).Distinct(StringComparer.Ordinal).Count() != family.Proteins.Count
                    || family.Proteins.Select(p => p.Rank).Distinct().Count() != family.Proteins.Count)
                    throw new InvalidOperationException("Stored coverage ranking is invalid.");
            var known = localization.Families.SelectMany(f => f.Proteins.Select(p => (f.FamilyUserId, p.ProteinIdentifier))).ToHashSet();
            if (localization.Segments.Any(s => !known.Contains((s.FamilyUserId, s.ProteinIdentifier))))
                throw new InvalidOperationException("A stored peptide placement has no ranked protein.");
        }
        var groups = new List<(string Section, string Label, HashSet<int> Ids)>();
        if (project.FamilyPanel != null && map != null)
            foreach (var panel in project.FamilyPanel.Families.Where(f => f.Decision is FamilyPanelDecision.Quantify or FamilyPanelDecision.ReportUnderOthers))
                groups.Add((ReportSectionIds.FamilyPrefix + panel.Key, panel.DisplayName,
                    map.UserFamilies.Values.Where(f => !f.HasConflict && f.PanelFamilyKey == panel.Key).Select(f => f.FamilyUserId).ToHashSet()));
        else if (localization != null)
            groups.AddRange(localization.Families.OrderBy(f => f.FamilyUserId).Select(f =>
                (ReportSectionIds.Results, "Family " + f.FamilyUserId.ToString(CultureInfo.InvariantCulture), new HashSet<int> { f.FamilyUserId })));
        if (groups.Count == 0) unavailable.Add(new(ReportSectionIds.Results, "Protein coverage maps", "No panel-linked protein localization is stored."));
        foreach (var group in groups)
        {
            var candidates = localization?.Families.Where(f => group.Ids.Contains(f.FamilyUserId))
                .SelectMany(f => f.Proteins.Select(p => (Family: f.FamilyUserId, Protein: p)))
                .OrderByDescending(p => p.Protein.CreditedSignal).ThenBy(p => p.Protein.ProteinIdentifier, StringComparer.Ordinal)
                .ThenBy(p => p.Family).Take(top).ToArray();
            if (candidates == null || candidates.Length == 0)
            { unavailable.Add(new(group.Section, "Protein coverage: " + group.Label, "No ranked protein localization is stored for this panel family.")); continue; }
            var keys = candidates.Select(p => (p.Family, p.Protein.ProteinIdentifier)).ToHashSet();
            var selected = new FamilyPeptideLocalizationResult { Method = localization!.Method, Policy = localization.Policy,
                Segments = localization.Segments.Where(s => keys.Contains((s.FamilyUserId, s.ProteinIdentifier))).ToList(),
                Families = localization.Families.Where(f => group.Ids.Contains(f.FamilyUserId)).Select(f => new FamilyProteinRanking
                { FamilyUserId = f.FamilyUserId, FamilySignal = f.FamilySignal, UnlocalizedSignal = f.UnlocalizedSignal,
                    Proteins = f.Proteins.Where(p => keys.Contains((f.FamilyUserId, p.ProteinIdentifier))).ToList() }).Where(f => f.Proteins.Count > 0).ToList() };
            foreach (var plot in ProteinCoverageFigures.FromStored(selected, project, int.MaxValue))
                figures.Add(new(group.Section, "Protein coverage: " + group.Label + " / " + ((ProteinCoverageMapChart)plot.Chart).Data.ProteinId,
                    plot, CompositionChartCaptions.Describe(plot)));
        }
        if (localization != null && project.FamilyPanel != null && map != null)
            foreach (var family in localization.Families.Where(f => !map.UserFamilies.TryGetValue(f.FamilyUserId, out var link)
                || link.HasConflict || link.PanelFamilyKey == null))
                unavailable.Add(new(ReportSectionIds.Results, "Protein coverage: group " + family.FamilyUserId.ToString(CultureInfo.InvariantCulture),
                    "Panel membership is outside the panel or conflicting; no panel-family placement was guessed."));
    }
}
