using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Only panel-family briefs use the optional collapsed-key unit. Headline, figures and
/// their canonical Others membership keep the original UserID unit.</summary>
public static class ReportEffectivePanelFamilies
{
    public const string UnitNotice = "The headline and composition figures use the stored UserID studies; effective panel-family briefs collapse candidate UserIDs to panel keys before shared-peptide credit, so their assigned-signal denominators can differ.";

    public static IReadOnlyList<ReportSection> Apply(FamilyClusterFile curation, QuantManifest manifest,
        IReadOnlyList<ReportSection> sections, FamilyReportThresholdResult? grouping)
    {
        var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(curation);
        if (!map.IsCurrent) return sections;
        var effective = manifest.EffectivePanelFamilies;
        var hidden = ReportWordMembership.HiddenIds(grouping);
        return sections.Select(section =>
        {
            if (!section.Id.StartsWith(ReportSectionIds.FamilyPrefix, StringComparison.Ordinal)) return section;
            string key = section.Id[ReportSectionIds.FamilyPrefix.Length..];
            int[] users = map.UserFamilies.Values.Where(f => !f.HasConflict && f.PanelFamilyKey == key
                && f.Decision is Panels.FamilyPanelDecision.Quantify or Panels.FamilyPanelDecision.ReportUnderOthers)
                .Select(f => f.FamilyUserId).Order().ToArray();
            if (users.Length == 0 || (effective == null && users.Length < 2)) return section;
            string prefix = "panel-block:" + Uri.EscapeDataString(key) + ":";
            var blocks = section.Blocks.Where(b => !b.Id.StartsWith(prefix + "observed:", StringComparison.Ordinal)
                && !b.Id.StartsWith(prefix + "unavailable:", StringComparison.Ordinal)).ToList();
            bool supplementOnly = users.Any(hidden.Contains);
            if (effective == null)
                blocks.Add(new ReportTable(prefix + "observed:effective:availability", "Effective panel-family availability", new[] { "Panel family key", "Status" },
                    new[] { new[] { key, "Not computed" } },
                    "Source UserID summaries cannot reconstruct this unit. Compute the native effective panel-family study before reporting its total.") { SupplementOnly = supplementOnly });
            else
            {
                var slot = effective.Families.Single(f => f.PanelFamilyKey == key && f.Kind == EffectivePanelFamilyKind.PanelFamily);
                var share = effective.RunShares.Families.Single(f => f.FamilyUserId == slot.StudyFamilyId);
                var count = effective.ProteinCounts.Families.Single(f => f.FamilyUserId == slot.StudyFamilyId);
                string N(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "Not available";
                string I(int value) => value.ToString(CultureInfo.InvariantCulture);
                void Table(string id, string title, string[] columns, IReadOnlyList<IReadOnlyList<string>> rows, string caption) =>
                    blocks.Add(new ReportTable(prefix + "observed:effective:" + id, title, columns, rows, caption) { SupplementOnly = supplementOnly });
                if (share.NoPeptideObserved)
                    Table("status", "Family observation status", new[] { "Panel family key", "Status" },
                        new[] { new[] { key, FamilyUnobservedStudies.Status } }, "The completed collapsed-key study assigned no peptide to this family; this does not establish biological absence.");
                Table("summary", "Effective panel-family share",
                    new[] { "Panel family key", "Source User IDs", "Available runs", "Mean assigned share", "Sample SD", "CV", "Pooled signal", "Pooled assigned share" },
                    new[] { new[] { key, string.Join("; ", slot.SourceUserIds.Select(I)), I(share.AvailableRunCount), N(share.MeanShare), N(share.StandardDeviation),
                        N(share.CoefficientOfVariation), N(share.PooledSignal), N(share.PooledShare) } },
                    "One stored total per effective panel key. Shares are fractions of the collapsed study's credited signal. Sample SD uses runs; pooled parts use all runs pooled. " + effective.Method);
                Table("run-shares", "Effective panel-family run shares", new[] { "Panel family key", "Run", "Signal", "Assigned share", "Whole measured share", "RAW TIC share" },
                    share.Runs.Select(r => (IReadOnlyList<string>)new[] { key, r.Run, N(r.Signal), N(r.ShareOfAssignedSignal), N(r.ShareOfMeasuredSignal), N(r.ShareOfTic) }).ToArray(),
                    effective.RunShares.Method);
                Table("counts", "Effective panel-family protein counts", new[] { "Panel family key", "Proteins needed (greedy)", "Proteins with unique peptides", "Observed peptides", "Unexplained peptides" },
                    new[] { new[] { key, I(count.GreedyProteinCount), I(count.UniquePeptideProteinCount), I(count.ObservedPeptideCount), I(count.UnexplainedPeptides.Count) } }, effective.ProteinCounts.Method);
                Table("parts", "Effective panel-family pooled parts", new[] { "Part kind", "Named part", "Status", "Pooled signal", "Within-family fraction", "Reason" },
                    effective.Parts.Where(p => p.PanelFamilyKey == key).Select(p => (IReadOnlyList<string>)new[]
                    { p.Kind.ToString(), p.Label, State(p.State), p.Signal.HasValue ? N(p.Signal) : State(p.State),
                        p.ShareOfFamily.HasValue ? N(p.ShareOfFamily) : State(p.State), p.Reason }).ToArray(),
                    "Parts use the same collapsed candidate membership and shared-peptide policy as this family's pooled total. Subclass, domain and product rows are separate partitions and must not be added across kinds. Missing observed or measurable support is explicit and is never replaced by zero.");
            }
            return section with { Blocks = blocks };
        }).ToArray();
    }

    public static string State(EffectivePanelPartState state) => state switch
    {
        EffectivePanelPartState.Observed => "Observed",
        EffectivePanelPartState.NotMeasured => "Not measured",
        EffectivePanelPartState.NotResolved => "Not resolved",
        _ => "Not computed",
    };
}
