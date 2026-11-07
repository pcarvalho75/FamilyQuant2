using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FamilyQuant.Evidence;

/// <summary>An explicit user instruction, never an automatically inferred family membership.</summary>
public sealed record EvidenceManualChoice
{
    public int ProteinIndex { get; init; }
    public string Identifier { get; init; } = "";
    public string SequenceSha256 { get; init; } = "";
    public string Family { get; init; } = "";
    public string Name { get; init; } = "";
    public bool NewFamily { get; init; }
    public bool Selected { get; init; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; init; }
}

public sealed record EvidenceManualRequest(IReadOnlyList<int> ProteinIndices, string Family, string Name, bool NewFamily, string? Note = null);

public static class ManualFamilyChoices
{
    public static string SequenceHash(string sequence) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sequence)));
    public static string NewKey(string name) => "manual:" + SequenceHash(name.Trim().ToUpperInvariant());
    public static string Reason(EvidenceManualChoice choice) => "Added by hand" + (string.IsNullOrWhiteSpace(choice.Note) ? "." : ": " + choice.Note!.Trim());
    public static bool ValidNote(string? note) => note == null || note.Length <= 512 && !note.Any(char.IsControl);

    public static void Validate(FamilyClusterFile file, IReadOnlyList<EvidenceManualChoice> choices, bool refuseAllLocked = false)
    {
        if (choices.Select(c => c.ProteinIndex).Distinct().Count() != choices.Count) throw new ArgumentException("A protein can have only one hand-added family choice.");
        foreach (var choice in choices)
        {
            if (choice.ProteinIndex < 0 || choice.ProteinIndex >= file.MyExtendedFastaItems.Count) throw new ArgumentException("A hand choice no longer identifies a protein.");
            var protein = file.MyExtendedFastaItems[choice.ProteinIndex];
            if (protein.SequenceIdentifier != choice.Identifier || SequenceHash(protein.Sequence ?? "") != choice.SequenceSha256)
                throw new ArgumentException("A hand choice does not match the target protein and sequence.");
            if (NearestReviewed.IsDecoy(protein) || SequenceLabels.IsContaminant(protein.SequenceIdentifier))
                throw new ArgumentException("A contaminant or decoy never joins a family, so it was not added.");
            if (string.IsNullOrWhiteSpace(choice.Family) || string.IsNullOrWhiteSpace(choice.Name) || !ValidNote(choice.Note))
                throw new ArgumentException("Choose a family and enter a name; the optional note must be one line of at most 512 characters.");
            if (file.LockedUserIds.Contains(protein.UserID))
            {
                bool saved = !refuseAllLocked && !choice.Selected && file.EvidenceRecipe?.ManualChoices?.Any(c =>
                    c.Identifier == choice.Identifier && c.SequenceSha256 == choice.SequenceSha256 && c.Family == choice.Family
                    && c.NewFamily == choice.NewFamily && c.Note == choice.Note) == true;
                if (!saved) throw new InvalidOperationException("Locked proteins cannot be added or removed here. Unlock their current family in Curate families first; no hand choices were changed.");
            }
        }
        foreach (var group in choices.Where(c => c.NewFamily).GroupBy(c => c.Family, StringComparer.Ordinal))
            if (group.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != 1)
                throw new ArgumentException("Members of one hand-created family must share its name.");
    }

    public static FamilyPanel ExtendPanel(FamilyPanel source, IReadOnlyList<EvidenceManualChoice> choices)
    {
        var panel = FamilyPanelValidation.Clone(source);
        foreach (var group in choices.Where(c => c.NewFamily).GroupBy(c => c.Family, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            if (!panel.Families.Any(f => f.Key == group.Key)) panel.Families.Add(PanelFamily(group.First()));
        FamilyPanelValidation.Validate(panel);
        return panel;
    }
    public static FamilyPanelFamily PanelFamily(EvidenceManualChoice choice) => new()
    { Key = choice.Family, DisplayName = choice.Name, Decision = FamilyPanelDecision.Quantify, SourceDecision = "Added by hand" };

    public static EvidenceProposal Overlay(EvidenceProposal automatic, IReadOnlyList<EvidenceManualChoice> choices, FamilyClusterFile file,
        IReadOnlyDictionary<string, FamilyProfile>? profiles = null)
    {
        if (choices.Count == 0) return automatic;
        Validate(file, choices);
        var byIndex = choices.ToDictionary(c => c.ProteinIndex);
        var comparer = automatic.PanelContext == null ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var known = automatic.Families.Select(f => f.Family).ToHashSet(comparer);
        known.UnionWith(choices.Where(c => c.NewFamily).Select(c => c.Family));
        if (file.FamilyPanel != null) known.UnionWith(file.FamilyPanel.Families.Select(f => f.Key));
        if (automatic.PanelContext != null && choices.Any(c => !known.Contains(c.Family)))
            throw new ArgumentException("Choose a proposed panel family or create a new family explicitly.");
        var available = automatic.Proteins.Select(p => p.ProteinIndex).ToHashSet();
        if (choices.Any(c => !available.Contains(c.ProteinIndex))) throw new ArgumentException("Only proteins in this proposal can be added by hand.");
        var rows = automatic.Proteins.Select(row =>
        {
            if (!byIndex.TryGetValue(row.ProteinIndex, out var choice)) return row;
            var original = row.AutomaticEvidence ?? row;
            var flags = original.Flags.Where(f => !f.Code.StartsWith("coherence.", StringComparison.Ordinal))
                .Select(f => new EvidenceFlag { Code = f.Code, Message = "Automatic proposal: " + f.Message }).ToList();
            if (profiles != null) flags.AddRange(CoherenceCheck.Check(profiles, choice.Family, file.MyExtendedFastaItems[row.ProteinIndex])
                .Select(f => new EvidenceFlag { Code = f.Code, Message = f.Message }));
            var node = JsonSerializer.SerializeToNode(original)!.AsObject();
            node[nameof(EvidenceProteinProposal.Family)] = choice.Family;
            node[nameof(EvidenceProteinProposal.Tier)] = (int)EvidenceTier.AddedByHand;
            node[nameof(EvidenceProteinProposal.Reason)] = Reason(choice);
            node[nameof(EvidenceProteinProposal.Flags)] = JsonSerializer.SerializeToNode(flags);
            node[nameof(EvidenceProteinProposal.AutomaticEvidence)] = JsonSerializer.SerializeToNode(original);
            return node.Deserialize<EvidenceProteinProposal>()!;
        }).ToArray();
        var counts = rows.Where(r => r.Family.Length > 0).GroupBy(r => r.Family, comparer).ToDictionary(g => g.Key,
            g => (All: g.Count(), Reviewed: g.Count(r => file.MyExtendedFastaItems[r.ProteinIndex].Evidence?.Reviewed == true)), comparer);
        var manualNames = choices.GroupBy(c => c.Family, comparer)
            .ToDictionary(g => g.Key, g => g.First().Name, comparer);
        var families = automatic.Families.Select(f => (f.Family, f.SuggestedName))
            .Concat(choices.Select(c => (c.Family, c.Name))).GroupBy(f => f.Family, comparer)
            .Select(g => new EvidenceFamilyProposal
            {
                Family = g.First().Family, SuggestedName = manualNames.GetValueOrDefault(g.Key) ?? g.First().Item2,
                Proteins = counts.GetValueOrDefault(g.Key).All,
                ReviewedProteins = counts.GetValueOrDefault(g.Key).Reviewed,
            }).ToArray();
        return new EvidenceProposal { SourceFingerprint = automatic.SourceFingerprint, Options = automatic.Options, Proteins = rows,
            Families = families, Markers = automatic.Markers, PanelContext = automatic.PanelContext, PanelAssignments = automatic.PanelAssignments,
            ManualChoices = choices.OrderBy(c => c.ProteinIndex).ToArray() };
    }

    public static void ValidateProposal(FamilyClusterFile file, EvidenceProposal proposal)
    {
        var choices = proposal.ManualChoices ?? Array.Empty<EvidenceManualChoice>();
        Validate(file, choices);
        var manual = proposal.Proteins.Where(r => r.Tier == EvidenceTier.AddedByHand || r.AutomaticEvidence != null).ToArray();
        if (manual.Length != choices.Count || manual.Any(r => !choices.Any(c => c.ProteinIndex == r.ProteinIndex && c.Identifier == r.Identifier
            && c.Family == r.Family && r.Tier == EvidenceTier.AddedByHand && r.Reason == Reason(c) && r.AutomaticEvidence?.ProteinIndex == r.ProteinIndex)))
            throw new ArgumentException("Every hand-added row must have its declared user choice and original automatic evidence.");
        if (manual.Any(r => r.AutomaticEvidence!.Tier == EvidenceTier.AddedByHand || r.AutomaticEvidence.AutomaticEvidence != null
            || r.AutomaticEvidence.Identifier != r.Identifier || r.AutomaticEvidence.PreviousFamilyId != r.PreviousFamilyId))
            throw new ArgumentException("The original automatic evidence must identify the same source protein.");
    }

    public static void ValidateRecipe(FamilyClusterFile file, EvidenceRecipe recipe)
    {
        var choices = recipe.ManualChoices ?? new List<EvidenceManualChoice>();
        Validate(file, choices);
        foreach (var assignment in recipe.Assignments.Where(a => a.Tier == EvidenceTier.AddedByHand || a.AutomaticEvidence != null))
            if (!choices.Any(c => c.Selected && c.ProteinIndex == assignment.ProteinIndex && c.Identifier == assignment.Identifier
                && c.Family == assignment.Family && assignment.Tier == EvidenceTier.AddedByHand && assignment.Reason == Reason(c)
                && assignment.AutomaticEvidence?.ProteinIndex == assignment.ProteinIndex))
                throw new ArgumentException("A saved hand-added assignment does not match its recorded user choice.");
        if (recipe.Assignments.Where(a => a.Tier == EvidenceTier.AddedByHand).Any(a => a.AutomaticEvidence == null
            || a.AutomaticEvidence.Tier == EvidenceTier.AddedByHand || a.AutomaticEvidence.AutomaticEvidence != null
            || a.AutomaticEvidence.Identifier != a.Identifier || a.AutomaticEvidence.PreviousFamilyId != a.PreviousFamilyId))
            throw new ArgumentException("The saved original evidence does not identify the hand-added source protein.");
    }
}
