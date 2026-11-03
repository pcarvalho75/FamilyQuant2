using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>One selected composition unit for headline, uncertainty, Others and primary plots.
/// A one-to-one panel reuses the original stored rows and denominators without new arithmetic.</summary>
public sealed record ReportCompositionBasis(bool UsesPanel, bool IsIdentity, QuantManifest Studies,
    PanelFamilyMap? Map, IReadOnlyDictionary<int, string> Labels, FamilyReportThresholdResult? Grouping,
    IReadOnlyDictionary<int, IReadOnlyList<int>> SourceUserIds)
{
    public const string PanelNotice = "The headline and composition figures use effective panel-family totals; the original source UserID studies remain in the supplement under their original export names.";

    public FamilyReportThresholdResult? SourceMembership()
    {
        if (Grouping == null || IsIdentity) return Grouping;
        IEnumerable<FamilyRunShareSummary> Expand(IEnumerable<FamilyRunShareSummary> rows) => rows.SelectMany(r => SourceUserIds[r.FamilyUserId]
            .Select(id => new FamilyRunShareSummary { FamilyUserId = id }));
        return new()
        {
            Threshold = Grouping.Threshold, OtherFamilies = Expand(Grouping.OtherFamilies).ToList(),
            ExcludedFamilies = Expand(Grouping.ExcludedFamilies).ToList(),
            ConflictFamilies = Grouping.ConflictFamilies.SelectMany(c => SourceUserIds[c.Family.FamilyUserId].Select(id => new FamilyReportMappingConflict
                { Family = new() { FamilyUserId = id }, PanelFamilyKeys = c.PanelFamilyKeys, HasOutsideMembers = c.HasOutsideMembers, Reason = c.Reason })).ToList(),
        };
    }

    public static ReportCompositionBasis Select(FamilyClusterFile project, QuantManifest source,
        IReadOnlyDictionary<int, string>? sourceLabels = null)
    {
        FamilyStudyMembership.RequireCurrent(project, source);
        var labels = new Dictionary<int, string>(sourceLabels ?? FamilyNaming.Names(project.MyExtendedFastaItems
            .Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier))));
        var identity = (source.FamilyRunShares?.Families.Select(f => f.FamilyUserId) ?? Enumerable.Empty<int>())
            .ToDictionary(id => id, id => (IReadOnlyList<int>)new[] { id });
        if (project.FamilyPanel == null)
            return new(false, true, source, null, labels, source.FamilyRunShares == null ? null : FamilyReportThreshold.Build(source.FamilyRunShares, project), identity);
        var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(project);
        if (!map.IsCurrent) throw new InvalidOperationException(map.Message);
        bool multiple = map.UserFamilies.Values.Where(f => !f.HasConflict && f.PanelFamilyKey != null
            && f.Decision is FamilyPanelDecision.Quantify or FamilyPanelDecision.ReportUnderOthers)
            .GroupBy(f => f.PanelFamilyKey, StringComparer.Ordinal).Any(g => g.Count() > 1);
        if (!multiple)
        {
            foreach (var member in map.UserFamilies.Values.Where(f => !f.HasConflict && f.PanelFamilyKey != null))
                labels[member.FamilyUserId] = project.FamilyPanel.Families.Single(f => f.Key == member.PanelFamilyKey).DisplayName;
            return new(true, true, source, map, labels,
                source.FamilyRunShares == null ? null : FamilyReportThreshold.Build(source.FamilyRunShares, project, map), identity);
        }
        var effective = source.EffectivePanelFamilies ?? throw new InvalidOperationException(
            "The panel links several source families to one key. Compute the native effective panel-family study before reporting its headline.");
        if (EffectivePanelFamilyStudy.Problem(project, effective) is string problem) throw new InvalidOperationException(problem);
        var studies = source.CopyForFamilyStudies();
        studies.FamilyRunShares = effective.RunShares; studies.FamilyProteinCounts = effective.ProteinCounts;
        studies.FamilySubclassParts = effective.SubclassParts; studies.FamilyDomainParts = effective.DomainParts;
        studies.FamilyPeptideLocalization = effective.Localization;
        var users = new Dictionary<int, PanelFamilyUserMap>();
        foreach (var slot in effective.Families)
        {
            var originals = slot.SourceUserIds.Where(map.UserFamilies.ContainsKey).Select(id => map.UserFamilies[id]).ToArray();
            string? key = slot.PanelFamilyKey ?? originals.Select(f => f.PanelFamilyKey).Distinct(StringComparer.Ordinal).SingleOrDefault();
            users[slot.StudyFamilyId] = new()
            {
                FamilyUserId = slot.StudyFamilyId, PanelFamilyKey = key,
                Decision = key == null ? null : project.FamilyPanel.Families.Single(f => f.Key == key).Decision,
                PanelKeys = originals.SelectMany(f => f.PanelKeys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                HasConflict = slot.Kind == EffectivePanelFamilyKind.AssignmentConflict,
                HasOutsideMembers = originals.Any(f => f.HasOutsideMembers),
            };
        }
        var collapsedMap = new PanelFamilyMap { IsCurrent = true, UserFamilies = users,
            ConflictingUserIds = users.Values.Where(f => f.HasConflict).Select(f => f.FamilyUserId).Order().ToArray(),
            PanelRows = map.PanelRows.Select(r => new PanelFamilyRowMap
            {
                PanelRowKey = r.PanelRowKey, EffectivePanelKey = r.EffectivePanelKey, Decision = r.Decision, MergeTargetFamilyKey = r.MergeTargetFamilyKey,
                FamilyUserIds = effective.Families.Where(f => f.SourceUserIds.Any(r.FamilyUserIds.Contains)).Select(f => f.StudyFamilyId).Order().ToArray(),
            }).ToArray() };
        return new(true, false, studies, collapsedMap, effective.Families.ToDictionary(f => f.StudyFamilyId, f => f.DisplayName),
            FamilyReportThreshold.Build(effective.RunShares, project, collapsedMap),
            effective.Families.ToDictionary(f => f.StudyFamilyId, f => (IReadOnlyList<int>)f.SourceUserIds));
    }

    public ReportResultPacket Apply(ReportResultPacket original)
    {
        if (!UsesPanel || Studies.FamilyRunShares == null || Grouping == null) return original;
        var exports = new Dictionary<string, string>(original.CsvExports, StringComparer.Ordinal);
        string csv = ReportStudyExports.Headline(Grouping, Studies.FamilyProteinCounts, Labels);
        exports.Add("panel_family_composition_headline.csv", csv);
        exports.Add("panel_family_run_means.csv", ReportStudyExports.MeanShares(Studies.FamilyRunShares, Labels));
        exports.Add("panel_family_run_shares.csv", ReportStudyExports.RunShares(Studies.FamilyRunShares, Labels));
        exports.Add("panel_family_run_totals.csv", ReportStudyExports.RunTotals(Studies.FamilyRunShares));
        exports.Add("panel_family_others_members.csv", ReportStudyExports.Others(Grouping, Labels));
        var headline = ReportCsvTable.Build("panel-family-run-means", "Headline panel-family composition: mean of per-run shares", csv,
            "Effective panel keys are deduplicated before shared-peptide credit. Mean and sample SD use available runs; within-family parts use pooled signal. Percentages use six significant digits; native fraction columns are retained in panel_family_composition_headline.csv.",
            "family", "mean_pct", "sample_sd_pct", "mean_measured_pct", "sample_sd_measured_pct", "greedy_protein_estimate", "unique_peptide_proteins")
            with { Columns = new[] { "Family", "Mean share (%)", "Sample SD (pp)", "Whole measured (%)", "Whole-signal SD (pp)", "Proteins needed (greedy)", "Proteins with own peptide" } };
        headline = ReportTableSupplements.WithStoredPercentages(headline, csv,
            new[] { "family", "mean_pct", "sample_sd_pct", "mean_measured_pct", "sample_sd_measured_pct", "greedy_protein_estimate", "unique_peptide_proteins" });
        var legacy = new HashSet<string>(new[] { "composition-assigned", "composition-measured", "family-run-means", "family-run-shares", "family-run-totals",
            "protein-counts", "protein-count-support", "subclass-parts", "domain-parts", "product-parts", "pooled-minus-mean", "others-members" }, StringComparer.Ordinal);
        var sections = original.Sections.Select(section =>
        {
            var blocks = section.Blocks.Where(b => b.Id != "family-paired-intro" && !b.Id.StartsWith("family-paired:", StringComparison.Ordinal))
                .Select(b => b is ReportTable t && legacy.Contains(t.Id) ? t with { SupplementOnly = true } : b).ToList();
            if (section.Id == ReportSectionIds.Results)
            {
                blocks.Insert(0, headline);
                var rows = ReportCsvTable.Build("panel-paired-source", "Panel paired source", csv, "", "family_user_id", "family", "status", "mean_pct", "greedy_protein_estimate");
                int index = 1;
                foreach (var row in rows.Rows.Where(r => r[2] == "Listed"))
                    blocks.Insert(index++, new ReportParagraph("panel-family-paired:" + row[0], row[1] + " " + row[3] + "% ("
                        + (row[4].Length == 0 ? "protein count unavailable" : row[4] + (row[4] == "1" ? " protein" : " proteins")) + ")"));
            }
            if (section.Id == ReportSectionIds.Summary) blocks.Add(new ReportParagraph("panel-headline-unit", PanelNotice));
            return section with { Blocks = blocks };
        }).ToArray();
        return new(sections, exports);
    }
}
