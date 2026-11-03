using System;
using System.Linq;
using FamilyQuant.Panels;

namespace FamilyQuant.Evidence;

/// <summary>A detached snapshot, verified against current assignment inputs when created.
/// Recheck IsCurrent before publishing work calculated asynchronously from this snapshot.</summary>
public sealed class PanelFamilyResolver
{
    private readonly PanelEvidenceClassifier.ClassificationContext context;
    private readonly PanelEvidenceClassifier.ClassificationContext auditContext;
    private readonly string fingerprint;

    internal PanelFamilyResolver(FamilyClusterFile file)
    {
        PanelAssignmentEvidenceStore.RequireCurrent(file);
        var panel = FamilyPanelValidation.Clone(file.FamilyPanel!);
        var links = file.EvidenceRecipe?.PanelContext?.Links
            ?? throw new InvalidOperationException("Current panel-link provenance is unavailable. Rebuild the evidence grouping.");
        context = new(panel, EvidenceApply.Clone(links));
        // A confirmed panel choice can remain relevant to an audit even when the current scope
        // contains no reference from that source. It must not change the classification resolver.
        var auditLinks = EvidenceApply.Clone(links).ToList();
        var keys = auditLinks.Select(link => link.Key).ToHashSet(StringComparer.Ordinal);
        string definition = PanelEvidenceClassifier.DefinitionFingerprint(panel);
        foreach (var saved in panel.EvidenceLinks)
        {
            string key = PanelEvidenceClassifier.LinkKey(saved.SourceFamily, saved.ArchitectureKey);
            if (saved.PanelDefinitionFingerprint != definition || keys.Contains(key)
                || saved.PanelFamilyKey != null && !panel.Families.Any(f => f.Key == saved.PanelFamilyKey)) continue;
            auditLinks.Add(new PanelFamilyLinkProposal
            {
                Key = key, SourceFamily = saved.SourceFamily, ArchitectureKey = saved.ArchitectureKey,
                CandidateFamilyKeys = saved.PanelFamilyKey == null ? Array.Empty<string>() : new[] { saved.PanelFamilyKey },
                IsConfirmed = true, SelectedFamilyKey = saved.PanelFamilyKey,
                SupportingAccessions = saved.SupportingAccessions?.ToArray() ?? Array.Empty<string>(),
            });
            keys.Add(key);
        }
        auditContext = new(panel, auditLinks);
        fingerprint = file.PanelProteinAssignmentsFingerprint!;
    }

    /// <summary>Map any source record, including an external reviewed record. This resolves its
    /// family/architecture context; it does not replace scope, identity or coverage gates.</summary>
    public string? PanelFamilyFor(ProteinEvidence? evidence) => evidence == null ? null : context.Resolve(evidence).Family;

    internal string? PanelFamilyForAudit(ProteinEvidence? evidence) => evidence == null ? null : auditContext.Resolve(evidence).Family;

    public bool IsCurrent(FamilyClusterFile file) => file.PanelProteinAssignmentsFingerprint == fingerprint
        && PanelAssignmentEvidenceStore.Current(file) != null;
}

public static partial class PanelEvidenceClassifier
{
    public static PanelFamilyResolver CreateCurrentResolver(FamilyClusterFile file) => new(file);
}
