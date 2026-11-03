using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

/// <summary>Literal observed rows followed by explicit zero rows for current quantitative panel
/// families omitted by a completed native study. Reading this view never changes the saved studies.</summary>
public sealed record FamilyUnobservedRows(IReadOnlyList<FamilyRunShareSummary> Shares,
    IReadOnlyList<FamilyProteinCount> ProteinCounts);

public static class FamilyUnobservedStudies
{
    public const string Status = "no peptide observed";

    /// <summary>One read-only report view for the canonical partition, CSVs, figures and panel rows.
    /// Clone only containers/lists that Store appends to; every pre-existing observation stays literal.</summary>
    public static QuantManifest View(FamilyClusterFile curation, QuantManifest manifest)
    {
        var view = manifest.CopyForFamilyStudies();
        if (manifest.FamilyRunShares is { } shares) view.FamilyRunShares = new()
        { MembershipSha256 = shares.MembershipSha256, Method = shares.Method, Policy = shares.Policy, Runs = shares.Runs, Families = shares.Families.ToList() };
        if (manifest.FamilyProteinCounts is { } counts) view.FamilyProteinCounts = new()
        { MembershipSha256 = counts.MembershipSha256, Method = counts.Method, Families = counts.Families.ToList() };
        if (manifest.FamilySubclassParts is { } subclasses) view.FamilySubclassParts = new()
        { MembershipSha256 = subclasses.MembershipSha256, Method = subclasses.Method, Policy = subclasses.Policy, Families = subclasses.Families.ToList() };
        if (manifest.FamilyDomainParts is { } domains) view.FamilyDomainParts = new()
        { MembershipSha256 = domains.MembershipSha256, Method = domains.Method, Policy = domains.Policy, Domains = domains.Domains.ToList(), Products = domains.Products.ToList() };
        if (manifest.FamilyPeptideLocalization is { } localization) view.FamilyPeptideLocalization = new()
        { MembershipSha256 = localization.MembershipSha256, Method = localization.Method, Policy = localization.Policy, Segments = localization.Segments, Families = localization.Families.ToList() };
        Store(curation, view);
        return view;
    }

    /// <summary>The original native builders are atomic and enumerate every peptide-reached family:
    /// an omitted family in their membership-bound universe has no peptide, rather than a failed
    /// per-family computation. Recognize their exact persisted method contract for old projects.
    /// Unrecognized/partial or absent studies do not establish zero. Panel conflicts remain unavailable.</summary>
    public static FamilyUnobservedRows Read(FamilyClusterFile curation, QuantManifest manifest)
    {
        FamilyStudyMembership.RequireCurrent(curation, manifest);
        var shares = manifest.FamilyRunShares?.Families.ToList() ?? new();
        var counts = manifest.FamilyProteinCounts?.Families.ToList() ?? new();
        var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(curation);
        if (!map.IsCurrent) return new(shares, counts);
        int[] scope = map.UserFamilies.Values.Where(f => !f.HasConflict && f.PanelFamilyKey != null
            && f.Decision is FamilyPanelDecision.Quantify or FamilyPanelDecision.ReportUnderOthers)
            .Select(f => f.FamilyUserId).Order().ToArray();
        bool completeShares = manifest.FamilyRunShares is { } runs
            && runs.Method == FamilyCompositionStudy.RunSharesMethod && runs.Runs.Count > 0;
        bool completeCounts = manifest.FamilyProteinCounts?.Method == FamilyCompositionStudy.ProteinCountsMethod;
        var shareIds = shares.Select(f => f.FamilyUserId).ToHashSet();
        var countIds = counts.Select(f => f.FamilyUserId).ToHashSet();
        foreach (int id in scope)
        {
            // A contradictory observed row in the other study is evidence of a missing computation,
            // not permission to declare no peptide. Keep that missing value unavailable.
            if (completeShares && !shareIds.Contains(id) && !counts.Any(f => f.FamilyUserId == id && f.ObservedPeptideCount > 0))
                shares.Add(ZeroShare(id, manifest.FamilyRunShares!.Runs));
            if (completeCounts && !countIds.Contains(id) && !shares.Any(f => f.FamilyUserId == id && !f.NoPeptideObserved))
                counts.Add(new() { FamilyUserId = id });
        }
        return new(shares, counts);
    }

    internal static FamilyRunShareSummary ZeroShare(int id, IReadOnlyList<FamilyRunTotal> totals)
    {
        static bool Positive(double value) => double.IsFinite(value) && value > 0;
        static double? Zero(bool available) => available ? 0 : null;
        int available = totals.Count(t => Positive(t.AssignedSignal));
        bool measured = totals.Count > 0 && totals.All(t => Positive(t.MeasuredSignal));
        bool tic = totals.Count > 0 && totals.All(t => t.TotalIonCurrent.HasValue && Positive(t.TotalIonCurrent.Value));
        return new()
        {
            FamilyUserId = id, NoPeptideObserved = true, AvailableRunCount = available,
            MeanShare = Zero(available > 0), StandardDeviation = Zero(available > 1),
            PooledShare = Zero(available > 0), PooledMinusMeanShare = Zero(available > 0),
            MeanShareOfMeasuredSignal = Zero(measured), StandardDeviationOfMeasuredSignal = Zero(measured && totals.Count > 1),
            MeanShareOfTic = Zero(tic), StandardDeviationOfTic = Zero(tic && totals.Count > 1),
            Runs = totals.Select(t => new FamilyRunShare
            {
                Run = t.Run, ShareOfAssignedSignal = Zero(Positive(t.AssignedSignal)),
                ShareOfMeasuredSignal = Zero(Positive(t.MeasuredSignal)),
                ShareOfTic = Zero(t.TotalIonCurrent.HasValue && Positive(t.TotalIonCurrent.Value)),
            }).ToList(),
        };
    }

    /// <summary>Persist the same explicit zero rows at native compute completion. Empty parts keep
    /// all stored composition studies on the same family universe without assigning any signal.</summary>
    public static void Store(FamilyClusterFile curation, QuantManifest manifest)
    {
        FamilyUnobservedRows rows = Read(curation, manifest);
        if (manifest.FamilyRunShares != null) manifest.FamilyRunShares.Families = rows.Shares.ToList();
        if (manifest.FamilyProteinCounts != null) manifest.FamilyProteinCounts.Families = rows.ProteinCounts.ToList();
        foreach (var family in rows.Shares.Where(f => f.NoPeptideObserved))
        {
            void Part(List<FamilySignalBreakdown>? parts, string unresolved)
            {
                if (parts != null && parts.All(f => f.FamilyUserId != family.FamilyUserId))
                    parts.Add(new() { FamilyUserId = family.FamilyUserId, Parts = new()
                    { new() { IsUnresolved = true, Label = unresolved } } });
            }
            Part(manifest.FamilySubclassParts?.Families, "Subclass not resolved");
            Part(manifest.FamilyDomainParts?.Domains, "Position not resolved");
            Part(manifest.FamilyDomainParts?.Products, "Position not resolved");
            if (manifest.FamilyPeptideLocalization is { } localization && localization.Families.All(f => f.FamilyUserId != family.FamilyUserId))
                localization.Families.Add(new() { FamilyUserId = family.FamilyUserId });
        }
    }
}
