using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Projects the native coverage snapshots independently of Word's picture limit.</summary>
public static class HtmlCoverageCatalogBuilder
{
    public static HtmlCoverageCatalog? Build(FamilyClusterFile project, QuantManifest manifest,
        FamilyReportThresholdResult? grouping = null,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? reportMembers = null,
        IReadOnlyDictionary<int, string>? reportLabels = null,
        IReadOnlyDictionary<int, string>? ownerSections = null, ReportSelection? selection = null)
    {
        if (manifest.FamilyPeptideLocalization is not { } localization) return null;
        FamilyStudyMembership.RequireCurrent(project, manifest);
        var excluded = new HashSet<string>(selection?.ExcludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (excluded.Contains(ReportSectionIds.Results)) return new();
        var maps = ProteinCoverageFigures.DataFromStored(localization, project, int.MaxValue);
        var proteins = new List<HtmlCoverageCatalogProtein>();
        foreach (var map in maps.Where(map => map.Peptides.Count > 0)
            .OrderBy(map => map.FamilyUserId).ThenByDescending(map => map.RankingSignal).ThenBy(map => map.ProteinId, StringComparer.Ordinal))
        {
            if (excluded.Contains(HtmlReportCoverageBuilder.FigureId(map.FamilyUserId, map.ProteinId))
                || ownerSections != null && ownerSections.TryGetValue(map.FamilyUserId, out string? owner) && excluded.Contains(owner)) continue;
            int count = map.Peptides.Select(peptide => peptide.Label).Distinct(StringComparer.Ordinal).Count();
            var peptides = map.Peptides.SelectMany(peptide => peptide.Segments.Select(span => new HtmlCoverageCatalogSpan(span.Start, span.End,
                peptide.Label, peptide.Kind == CoveragePlacementKind.Exact ? "ExactContainment" : "Aligned",
                peptide.Source == CoveragePeptideSource.Identified ? "identified" : "de novo"))).ToArray();
            var domains = map.Annotations.SelectMany(annotation => annotation.Segments.Select(span => new HtmlCoverageCatalogSpan(span.Start, span.End,
                annotation.Label, annotation.Kind == CoverageAnnotationKind.Domain ? "domain" : "feature"))).ToArray();
            proteins.Add(new(map.FamilyUserId, map.ProteinId,
                map.ProteinId + " (" + count.ToString(CultureInfo.InvariantCulture) + " distinct placed sequences)",
                map.SequenceLength, count, map.RankingSignal, peptides, domains));
        }
        var names = ReportFamilyNames.SourceLabels(project);
        var families = new List<HtmlCoverageCatalogFamily>();
        foreach (var family in proteins.Select((protein, index) => (Protein: protein, Index: index)).GroupBy(row => row.Protein.FamilyUserId))
        {
            string id = family.Key.ToString(CultureInfo.InvariantCulture);
            string name = names.GetValueOrDefault(family.Key, "");
            name = "Family " + id + (name.Length > 0 ? " - " + name : "");
            string? reportId = reportMembers == null || reportMembers.Count == 0 ? id
                : reportMembers.Where(pair => pair.Value.Count == 1 && pair.Value[0] == family.Key)
                    .Select(pair => pair.Key.ToString(CultureInfo.InvariantCulture)).FirstOrDefault();
            families.Add(Family("family:" + id, name, family.Select(row => row.Index).ToArray(), reportId));
        }
        if (reportMembers != null)
            foreach (var report in reportMembers.Where(pair => pair.Value.Count > 1).OrderBy(pair => pair.Key))
            {
                var members = report.Value.ToHashSet();
                int[] indices = Indices(members);
                if (indices.Length == 0) continue;
                string name = reportLabels?.GetValueOrDefault(report.Key) ?? "Family " + report.Key.ToString(CultureInfo.InvariantCulture);
                families.Add(Family("report:" + report.Key.ToString(CultureInfo.InvariantCulture), name, indices,
                    report.Key.ToString(CultureInfo.InvariantCulture)));
            }
        if (grouping != null)
        {
            int[] others = Indices(grouping.OtherFamilies.Select(family => family.FamilyUserId).ToHashSet());
            if (others.Length > 0) families.Add(Family("others", "Others", others, "others"));
        }
        return new() { Families = families, Proteins = proteins };

        int[] Indices(HashSet<int> ids) => proteins.Select((protein, index) => (Protein: protein, Index: index))
            .Where(row => ids.Contains(row.Protein.FamilyUserId)).OrderByDescending(row => row.Protein.CreditedSignal)
            .ThenBy(row => row.Protein.Protein, StringComparer.Ordinal).ThenBy(row => row.Protein.FamilyUserId).Select(row => row.Index).ToArray();
        static HtmlCoverageCatalogFamily Family(string id, string name, int[] indices, string? reportId) => new(id, name,
            name + " (" + indices.Length.ToString(CultureInfo.InvariantCulture) + " proteins)", indices, reportId);
    }
}
