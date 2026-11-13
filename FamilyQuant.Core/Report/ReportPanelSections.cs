using FamilyQuant.Panels;
using FamilyQuant.Evidence;
using System;
using FamilyQuant.Operations;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>One section per panel row in source order. User text is never interpreted as a result.</summary>
public static class ReportPanelSections
{
    /// <summary>Retained exact entry point for existing callers and reflection-based integrations.</summary>
    public static IReadOnlyList<ReportSection> BuildFromResults(FamilyClusterFile curation, QuantManifest manifest) =>
        BuildFromResults(curation, manifest, (OperationContext?)null, 0, null);

    /// <summary>Build sections from the studies stored in the project manifest. Shares are fractions.
    /// Completed native studies retain explicit no-peptide zeros. Other missing results stay unavailable.
    /// An ambiguous UserID is not assigned to a panel family.</summary>
    public static IReadOnlyList<ReportSection> BuildFromResults(FamilyClusterFile curation, QuantManifest manifest,
        OperationContext? operation = null, int sectionOffset = 0, int? sectionTotal = null)
    {
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(manifest);
        bool currentMap = curation.FamilyPanel == null || PanelAssignmentEvidenceStore.Current(curation) != null;
        return BuildFromResults(curation, manifest, manifest.FamilyRunShares == null || !currentMap ? null : FamilyReportThreshold.Build(manifest.FamilyRunShares, curation),
            operation, sectionOffset, sectionTotal);
    }

    public static IReadOnlyList<ReportSection> BuildFromResults(FamilyClusterFile curation, QuantManifest manifest,
        FamilyReportThresholdResult? grouping, OperationContext? operation = null, int sectionOffset = 0, int? sectionTotal = null)
    {
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(manifest);
        FamilyStudyMembership.RequireCurrent(curation, manifest);
        var observations = FamilyUnobservedStudies.Read(curation, manifest);
        var tables = new Dictionary<int, List<ReportTable>>();
        List<ReportTable> For(int id)
        {
            if (!tables.TryGetValue(id, out var value)) tables.Add(id, value = new List<ReportTable>());
            return value;
        }
        static string Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "Not available";
        static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);
        if (manifest.FamilyRunShares != null)
        {
            if (manifest.FamilyRunShares.Families.GroupBy(f => f.FamilyUserId).Any(g => g.Count() > 1))
                throw new InvalidOperationException("The stored family share result repeats a User ID.");
            string basis = "Signal uses observed identified envelope plus measured de novo envelope. Shared-peptide policy: "
                + manifest.FamilyRunShares.Policy.DisplayName() + ". " + (string.IsNullOrWhiteSpace(manifest.FamilyRunShares.Method)
                    ? "Method not recorded." : manifest.FamilyRunShares.Method);
            foreach (var family in observations.Shares)
            {
                string id = Integer(family.FamilyUserId);
                var target = For(family.FamilyUserId);
                if (family.NoPeptideObserved)
                    target.Add(new ReportTable("observation-status", "Family observation status", new[] { "User ID", "Status" },
                        new[] { new[] { id, FamilyUnobservedStudies.Status } },
                        "No peptide was assigned to this family in the completed stored study. Zero assigned signal does not establish biological absence; undefined shares and statistics remain unavailable."));
                target.Add(new ReportTable("share-summary", "Observed family share",
                    new[] { "User ID", "Available runs", "Mean assigned share", "Sample SD", "CV" },
                    new[] { new[] { id, Integer(family.AvailableRunCount), Number(family.MeanShare),
                        Number(family.StandardDeviation), Number(family.CoefficientOfVariation) } },
                    "Shares are fractions of assigned family signal. SD is the sample standard deviation; CV is SD divided by the mean. " + basis));
                target.Add(new ReportTable("run-shares", "Observed run shares",
                    new[] { "User ID", "Run", "Signal", "Share of assigned signal", "Share of measured signal" },
                    family.Runs.Select(r => (IReadOnlyList<string>)new[] { id, r.Run, Number(r.Signal),
                        Number(r.ShareOfAssignedSignal), Number(r.ShareOfMeasuredSignal) }).ToArray(),
                    "Measured signal is the deduplicated peptide envelope, not total ion current. Shares are fractions. " + basis));
            }
        }
        if (manifest.FamilyProteinCounts != null)
        {
            if (manifest.FamilyProteinCounts.Families.GroupBy(f => f.FamilyUserId).Any(g => g.Count() > 1))
                throw new InvalidOperationException("The stored protein count result repeats a User ID.");
            foreach (var family in observations.ProteinCounts)
                For(family.FamilyUserId).Add(new ReportTable("protein-counts", "Observed database protein counts",
                    new[] { "User ID", "Greedy protein count", "Proteins with unique peptides", "Observed peptides", "Unexplained peptides" },
                    new[] { new[] { Integer(family.FamilyUserId), Integer(family.GreedyProteinCount),
                        Integer(family.UniquePeptideProteinCount), Integer(family.ObservedPeptideCount), Integer(family.UnexplainedPeptides.Count) } },
                    "The greedy protein count approximates the fewest database proteins explaining peptides with a database match; it is not a gene count. Unexplained peptides remain separate."));
        }
        var currentAssignments = PanelAssignmentEvidenceStore.Current(curation);
        foreach (int id in (currentAssignments ?? Array.Empty<PanelProteinAssignment>())
            .Where(l => l.FamilyUserId.HasValue).Select(l => l.FamilyUserId!.Value).Distinct())
        {
            var target = For(id);
            void Missing(string key, string title) => target.Add(new ReportTable(key, title,
                new[] { "User ID", "Status" }, new[] { new[] { Integer(id), "Not available" } },
                "The project does not contain this result for this User ID."));
            if (!target.Any(t => t.Id == "share-summary")) Missing("missing-shares", "Family share availability");
            if (!target.Any(t => t.Id == "protein-counts")) Missing("missing-counts", "Protein count availability");
        }
        var sections = Build(curation, tables.ToDictionary(p => p.Key, p => (IReadOnlyList<ReportTable>)p.Value), grouping,
            operation, sectionOffset, sectionTotal);
        return ReportEffectivePanelFamilies.Apply(curation, manifest, sections, grouping);
    }

    public static IReadOnlyList<ReportSection> Build(FamilyClusterFile curation,
        IReadOnlyDictionary<int, IReadOnlyList<ReportTable>>? observed = null, OperationContext? operation = null,
        int sectionOffset = 0, int? sectionTotal = null)
        => Build(curation, observed, null, operation, sectionOffset, sectionTotal);

    public static IReadOnlyList<ReportSection> Build(FamilyClusterFile curation,
        IReadOnlyDictionary<int, IReadOnlyList<ReportTable>>? observed, FamilyReportThresholdResult? grouping,
        OperationContext? operation = null, int sectionOffset = 0, int? sectionTotal = null)
    {
        ArgumentNullException.ThrowIfNull(curation);
        var panel = curation.FamilyPanel;
        if (panel == null) return Array.Empty<ReportSection>();
        var assignments = PanelAssignmentEvidenceStore.Current(curation);
        var familyMap = assignments == null ? null : PanelAssignmentEvidenceStore.BuildFamilyMap(panel, assignments);
        bool subclassCurrent = SubclassAssignment.IsCurrent(curation);
        var others = grouping?.OtherFamilies.Select(f => f.FamilyUserId).ToHashSet() ?? new HashSet<int>();
        var sections = new List<ReportSection>();
        int total = sectionTotal ?? panel.Families.Count;
        if (sectionOffset == 0) operation?.Report("report-prepare-section", "Preparing panel report sections", 0, total);
        foreach (var family in panel.Families)
        {
            int ordinal = sectionOffset + sections.Count + 1;
            operation?.Report("report-prepare-section", "Section " + ordinal.ToString(CultureInfo.InvariantCulture) + " of "
                + total.ToString(CultureInfo.InvariantCulture) + ": " + ReportFamilyNames.DisplayName(panel, family, curation.ReportFamilyNames), ordinal, total);
            string id = ReportSectionIds.FamilyPrefix + family.Key;
            string blockPrefix = "panel-block:" + Uri.EscapeDataString(family.Key) + ":";
            var rows = new List<IReadOnlyList<string>>
            {
                new[] { "Panel decision", family.SourceDecision },
                new[] { "Action", Action(family.Decision) },
            };
            if (!ManualFamilyChoices.IsGeneratedKey(family.Key)) rows.Insert(0, new[] { "Panel key", family.Key });
            if (family.MergeTargetFamilyKey.Length > 0)
            {
                if (!ManualFamilyChoices.IsGeneratedKey(family.MergeTargetFamilyKey)) rows.Add(new[] { "Merge target key", family.MergeTargetFamilyKey });
                else if (panel.Families.FirstOrDefault(target => target.Key == family.MergeTargetFamilyKey) is { } target)
                    rows.Add(new[] { "Merge target", ReportFamilyNames.DisplayName(panel, target, curation.ReportFamilyNames) });
            }
            rows.AddRange(family.Names.Select(n => (IReadOnlyList<string>)new[] { n.Name, n.Text }));
            rows.AddRange(family.TextFields.Select(n => (IReadOnlyList<string>)new[] { n.Name, n.Text }));
            var blocks = new List<ReportBlock>
            {
                new ReportTable(blockPrefix + "text", "User panel text", new[] { "Field", "Text as supplied" }, rows,
                    "Decisions, literature abundance, notes and sources are user panel text. They are distinct from this project's measured results."),
            };
            if (family.Pfam.Count > 0)
                blocks.Add(new ReportTable(blockPrefix + "domains", "Domains supplied in the panel", new[] { "Accession", "Name" },
                    family.Pfam.Select(d => (IReadOnlyList<string>)new[] { d.Accession, d.Name }).ToArray(),
                    "Domain accessions and names as supplied in the panel; this table does not claim independent validation."));
            if (family.Subclasses.Count > 0)
                blocks.Add(new ReportTable(blockPrefix + "subclass-definitions", "Subclass definitions supplied in the panel",
                    new[] { "Key", "Name", "Required domains", "Absent domains", "Source subfamilies", "Reported as" },
                    family.Subclasses.Select(s => (IReadOnlyList<string>)new[] { s.Key, s.DisplayName, string.Join("; ", s.RequiredPfam),
                        string.Join("; ", s.AbsentPfam), string.Join("; ", s.UniProtSubfamilies), s.ReportAsSubclassKey }).ToArray(),
                    "Definitions are user data. Observed signal supports a subclass only under the program's recorded evidence and peptide-uniqueness rules."));
            if (family.Decision == FamilyPanelDecision.NotAFamily)
                blocks.Add(new ReportParagraph(blockPrefix + "observation-status", "Excluded from quantitative families by the panel decision."));
            else if (family.Decision == FamilyPanelDecision.MergeIntoAnotherFamily)
                blocks.Add(new ReportParagraph(blockPrefix + "observation-status", "Quantitative observations belong to the panel's named merge target; this row is not an additional family total."));
            else
            {
                if (!subclassCurrent)
                    blocks.Add(new ReportParagraph(blockPrefix + "subclass-unavailable", "Subclass labels have not been computed or are out of date."));
                if (familyMap == null)
                    blocks.Add(new ReportParagraph(blockPrefix + "family-unavailable", "Panel family assignments are unavailable or out of date."));
                var mappings = familyMap?.UserFamilies.Values ?? Enumerable.Empty<PanelFamilyUserMap>();
                var matchingGroups = mappings.Where(g => g.PanelKeys.Contains(family.Key, StringComparer.Ordinal)).ToArray();
                int[] ids = matchingGroups.Where(g => !g.HasConflict && string.Equals(g.PanelFamilyKey, family.Key, StringComparison.Ordinal))
                    .Select(g => g.FamilyUserId).OrderBy(i => i).ToArray();
                if (ids.Any(others.Contains))
                    blocks.Add(new ReportParagraph(blockPrefix + "others-status",
                        "Quantitative observations assigned to Others are retained in the companion workbook. Panel text remains reference material."));
                if (matchingGroups.Any(g => g.HasConflict))
                    blocks.Add(new ReportParagraph(blockPrefix + "ambiguous", "Some User IDs are shared with other or unknown panel families; their observations cannot be assigned to this panel family."));
                bool hasObserved = false;
                foreach (int userId in ids)
                {
                    bool hasUserId = false;
                    if (observed != null && observed.TryGetValue(userId, out var tables))
                            foreach (var table in tables)
                            {
                                blocks.Add(table with { Id = blockPrefix + "observed:" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    + ":" + Uri.EscapeDataString(table.Id), SupplementOnly = table.SupplementOnly || others.Contains(userId) });
                                hasObserved = hasUserId = true;
                            }
                    if (!hasUserId) blocks.Add(new ReportParagraph(blockPrefix + "unavailable:" + userId.ToString(CultureInfo.InvariantCulture),
                        "Observations for User ID " + userId.ToString(CultureInfo.InvariantCulture) + " are not available in this project."));
                }
                if (!hasObserved && ids.Length == 0) blocks.Add(new ReportParagraph(blockPrefix + "observation-status", "Panel-linked quantitative observations are not available. Missing observations are not zero abundance."));
            }
            blocks = blocks.Select(block => block is ReportTable table && table.Id.EndsWith(":subclass-definitions", StringComparison.Ordinal)
                ? table with { ListColumns = new[]
                {
                    new ReportListColumn(2, "Required domains", family.Subclasses.Select(s => (IReadOnlyList<string>)s.RequiredPfam.ToArray()).ToArray()),
                    new ReportListColumn(3, "Absent domains", family.Subclasses.Select(s => (IReadOnlyList<string>)s.AbsentPfam.ToArray()).ToArray()),
                    new ReportListColumn(4, "Source subfamilies", family.Subclasses.Select(s => (IReadOnlyList<string>)s.UniProtSubfamilies.ToArray()).ToArray()),
                } } : block).ToList();
            sections.Add(new ReportSection(id, ReportFamilyNames.DisplayName(panel, family, curation.ReportFamilyNames), blocks));
        }
        return sections;
    }

    private static string Action(FamilyPanelDecision decision) => decision switch
    {
        FamilyPanelDecision.Quantify => "Quantify",
        FamilyPanelDecision.ReportUnderOthers => "Report under others",
        FamilyPanelDecision.NotAFamily => "Not a quantitative family",
        FamilyPanelDecision.MergeIntoAnotherFamily => "Merge into the named family",
        _ => throw new ArgumentOutOfRangeException(nameof(decision)),
    };
}
