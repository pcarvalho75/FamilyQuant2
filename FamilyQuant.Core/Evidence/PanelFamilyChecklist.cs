using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant.Evidence;

[Flags]
public enum PanelChecklistMatch { None = 0, UniProtFamily = 1, Pfam = 2, NameText = 4, HandChoice = 8, ProposedPlacement = 16 }

public sealed record PanelChecklistProtein(int ProteinIndex, string Identifier, PanelChecklistMatch Match,
    bool? Reviewed, bool Placed, string Reason, IReadOnlyList<string>? ScopePhrases = null, bool IsLocked = false);

public sealed record PanelChecklistFamily(string Key, string Name, string Decision, string? TargetKey,
    IReadOnlyList<PanelChecklistProtein> Proteins)
{
    public int Matches => Proteins.Count;
    public int Reviewed => Proteins.Count(protein => protein.Reviewed == true);
    public int Placed => Proteins.Count(protein => protein.Placed);
    public int Outside => Matches - Placed;
    public bool HasOutside => Outside > 0;
    public string OutsideSummary => Matches == 0 ? "No database targets matched." : Outside == 0 ? "All matching targets are placed."
        : string.Join(" | ", Proteins.Where(protein => !protein.Placed).Select(protein => protein.Reason).Distinct(StringComparer.Ordinal));
    public string MatchTypes => string.Join(" + ", new[]
    {
        (PanelChecklistMatch.UniProtFamily, "UniProt family"), (PanelChecklistMatch.Pfam, "Pfam"),
        (PanelChecklistMatch.NameText, "Name text"), (PanelChecklistMatch.HandChoice, "Added by hand"),
        (PanelChecklistMatch.ProposedPlacement, "Proposal placement"),
    }.Where(type => Proteins.Any(protein => protein.Match.HasFlag(type.Item1))).Select(type => type.Item2));
}

/// <summary>Read-only candidate discovery. Matching text or domains never changes the proposal.</summary>
public static class PanelFamilyChecklist
{
    public static IReadOnlyList<PanelChecklistFamily> Build(FamilyClusterFile file, EvidenceProposal proposal, FamilyPanel? selectedPanel = null)
    {
        if (proposal.PanelContext == null) return Array.Empty<PanelChecklistFamily>();
        if (proposal.SourceFingerprint != EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems))
            throw new InvalidOperationException("The proteins changed. Update the proposal before reading its panel checklist.");
        var panel = FamilyPanelValidation.Clone(selectedPanel ?? file.FamilyPanel
            ?? throw new InvalidOperationException("Import a family panel before reading its checklist."));
        if (PanelDerivedDataCurrentness.FamilyFingerprint(panel) != proposal.PanelContext.AppliedPanelFingerprint)
            throw new InvalidOperationException("The panel changed. Update the proposal before reading its checklist.");
        var context = PanelFamilyResolver.CreateAuditContext(panel, proposal.PanelContext.Links);
        var targets = proposal.Proteins.Where(row => !NearestReviewed.IsDecoy(file.MyExtendedFastaItems[row.ProteinIndex])
            && !SequenceLabels.IsContaminant(row.Identifier)).ToArray();
        return panel.Families.Select(family =>
        {
            string[] names = family.Names.Select(name => name.Text).Append(family.DisplayName).Append(family.Key)
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var patterns = names.Select(name => new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(name.Trim()) + @"(?![\p{L}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).ToArray();
            var domains = family.Pfam.Select(domain => domain.Accession).ToHashSet(StringComparer.Ordinal);
            string? targetKey = FamilyPanelValidation.ResolveFamilyKey(panel, family.Key);
            var matches = new List<PanelChecklistProtein>();
            foreach (var row in targets)
            {
                var protein = file.MyExtendedFastaItems[row.ProteinIndex];
                var sourceFamilies = PanelEvidenceClassifier.SourceFamilies(protein.Evidence);
                string? mapped = context.Resolve(protein.Evidence).Family;
                PanelChecklistMatch match = PanelChecklistMatch.None;
                if (sourceFamilies.Any(source => names.Contains(source, StringComparer.OrdinalIgnoreCase))
                    || sourceFamilies.Length > 0 && mapped == family.Key) match |= PanelChecklistMatch.UniProtFamily;
                if (EvidenceDomains.Ordered(protein.Evidence).Any(domains.Contains)) match |= PanelChecklistMatch.Pfam;
                if (patterns.Any(pattern => pattern.IsMatch(FamilyNaming.ProteinName(protein.Description)))) match |= PanelChecklistMatch.NameText;
                bool placed = targetKey != null && row.Family == targetKey;
                if (row.Family == family.Key && row.Tier == EvidenceTier.AddedByHand) match |= PanelChecklistMatch.HandChoice;
                if (row.Family == family.Key && match == PanelChecklistMatch.None) match = PanelChecklistMatch.ProposedPlacement;
                if (match == PanelChecklistMatch.None) continue;
                string? mappedName = mapped == null ? null : panel.Families.Single(item => item.Key == mapped).DisplayName;
                string reason = placed ? row.Reason : PanelAuditCsv.ExplainScopeExclusion(protein.Evidence, proposal.Options.ScopeTerms, mappedName, row.Reason);
                if (!placed && row.Family.Length > 0)
                    reason = "Proposed in " + panel.Families.Single(item => item.Key == row.Family).DisplayName + ". " + reason;
                var phrases = (protein.Evidence?.Keywords ?? new List<string>()).Concat(protein.Evidence?.TissueSpecificity ?? new List<string>())
                    .Where(phrase => !string.IsNullOrWhiteSpace(phrase)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(phrase => phrase, StringComparer.Ordinal).ToArray();
                matches.Add(new(row.ProteinIndex, row.Identifier, match, protein.Evidence?.Reviewed, placed, reason, phrases, file.LockedUserIds.Contains(protein.UserID)));
            }
            string decision = family.Decision switch
            {
                FamilyPanelDecision.NotAFamily => "Excluded by panel", FamilyPanelDecision.ReportUnderOthers => "Report under Others",
                FamilyPanelDecision.MergeIntoAnotherFamily => targetKey == null ? "Merged family is excluded by panel"
                    : "Merge into " + panel.Families.Single(item => item.Key == targetKey).DisplayName,
                _ => "Quantify",
            };
            return new PanelChecklistFamily(family.Key, string.IsNullOrWhiteSpace(family.DisplayName) ? family.Key : family.DisplayName, decision, targetKey, matches);
        }).ToArray();
    }
}
