using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>One section per panel row in source order. User text is never interpreted as a result.</summary>
public static class ReportPanelSections
{
    public static IReadOnlyList<ReportSection> Build(FamilyClusterFile curation,
        IReadOnlyDictionary<int, IReadOnlyList<ReportTable>>? observed = null)
    {
        var panel = curation.FamilyPanel;
        if (panel == null) return Array.Empty<ReportSection>();
        var sections = new List<ReportSection>();
        foreach (var family in panel.Families)
        {
            string id = ReportSectionIds.FamilyPrefix + family.Key;
            string blockPrefix = "panel-block:" + Uri.EscapeDataString(family.Key) + ":";
            var rows = new List<IReadOnlyList<string>>
            {
                new[] { "Panel key", family.Key }, new[] { "Panel decision", family.SourceDecision },
                new[] { "Action", Action(family.Decision) },
            };
            if (family.MergeTargetFamilyKey.Length > 0) rows.Add(new[] { "Merge target key", family.MergeTargetFamilyKey });
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
                int[] ids = curation.ProteinSubclassLabels?.Where(l => string.Equals(l.PanelFamilyKey, family.Key, StringComparison.Ordinal)
                    && l.FamilyUserId.HasValue).Select(l => l.FamilyUserId!.Value).Distinct().OrderBy(i => i).ToArray() ?? Array.Empty<int>();
                bool hasObserved = false;
                if (observed != null)
                    foreach (int userId in ids)
                        if (observed.TryGetValue(userId, out var tables))
                            foreach (var table in tables)
                            {
                                blocks.Add(table with { Id = blockPrefix + "observed:" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    + ":" + Uri.EscapeDataString(table.Id) });
                                hasObserved = true;
                            }
                if (!hasObserved) blocks.Add(new ReportParagraph(blockPrefix + "observation-status", "Panel-linked quantitative observations: not computed in this project. Missing observations are not zero abundance."));
            }
            sections.Add(new ReportSection(id, family.DisplayName, blocks));
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
