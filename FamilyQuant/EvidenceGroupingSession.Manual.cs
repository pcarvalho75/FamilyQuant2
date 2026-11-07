using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed partial class EvidenceGroupingSession
{
    private Dictionary<int, EvidenceManualChoice> manualChoices = new();
    private bool manualInitialized;
    private EvidenceProposal? automaticProposal;
    private IReadOnlyDictionary<string, FamilyProfile>? coherenceProfiles;
    private FamilyPanel? selectedReviewPanel;

    private void InitializeManualChoices()
    {
        if (manualInitialized) return;
        manualInitialized = true;
        foreach (var saved in file.EvidenceRecipe?.ManualChoices ?? new List<EvidenceManualChoice>())
        {
            var matches = file.MyExtendedFastaItems.Select((p, i) => (Protein: p, Index: i)).Where(p => p.Protein.SequenceIdentifier == saved.Identifier
                && ManualFamilyChoices.SequenceHash(p.Protein.Sequence ?? "") == saved.SequenceSha256).ToArray();
            if (matches.Length != 1) continue;
            var match = matches[0];
            manualChoices[match.Index] = saved with { ProteinIndex = match.Index, Selected = saved.Selected && !file.LockedUserIds.Contains(match.Protein.UserID) };
        }
    }

    public EvidenceReviewInput AddToFamily(EvidenceManualRequest request)
    {
        EnsureCurrent();
        var automatic = automaticProposal ?? throw new InvalidOperationException("Update proposal before adding proteins by hand.");
        int[] indices = request.ProteinIndices.Distinct().ToArray();
        if (indices.Length == 0) throw new ArgumentException("Highlight proteins before adding them to a family.");
        string name = request.Name.Trim();
        string? note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (name.Length == 0 || !ManualFamilyChoices.ValidNote(note)) throw new ArgumentException("Enter a family name and an optional one-line note of at most 512 characters.");
        string family;
        bool created;
        if (request.NewFamily)
        {
            if (Proposal!.Families.Any(f => f.SuggestedName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("That family name is already proposed; choose it from the list.");
            family = ManualFamilyChoices.NewKey(name); created = true;
        }
        else
        {
            family = Proposal!.Families.FirstOrDefault(f => f.Family == request.Family)?.Family
                ?? reviewPanel?.Families.Where(f => f.Key == request.Family && FamilyPanelValidation.ResolveFamilyKey(reviewPanel, f.Key) != null).Select(f => f.Key).SingleOrDefault()
                ?? throw new ArgumentException("Choose a proposed family or New family.");
            created = manualChoices.Values.Any(c => c.Family == family && c.NewFamily);
        }
        var next = new Dictionary<int, EvidenceManualChoice>(manualChoices);
        foreach (int index in indices)
        {
            if (!automatic.Proteins.Any(p => p.ProteinIndex == index)) throw new ArgumentException("Only proteins in this proposal can be added by hand.");
            var protein = file.MyExtendedFastaItems[index];
            var choice = new EvidenceManualChoice { ProteinIndex = index, Identifier = protein.SequenceIdentifier,
                SequenceSha256 = ManualFamilyChoices.SequenceHash(protein.Sequence ?? ""), Family = family, Name = name,
                NewFamily = created, Note = note, Selected = true };
            ManualFamilyChoices.Validate(file, new[] { choice }, refuseAllLocked: true);
            next[index] = choice;
        }
        return UpdateManualChoices(next.Values.ToArray());
    }

    public EvidenceReviewInput RemoveFromFamily(IReadOnlyList<int> indices)
    {
        EnsureCurrent();
        var selected = indices.Distinct().Where(manualChoices.ContainsKey).ToArray();
        if (selected.Length == 0) throw new ArgumentException("Highlight hand-added proteins to remove their hand choices.");
        ManualFamilyChoices.Validate(file, selected.Select(i => manualChoices[i]).ToArray(), refuseAllLocked: true);
        return UpdateManualChoices(manualChoices.Values.Where(c => !selected.Contains(c.ProteinIndex)).ToArray());
    }

    public EvidenceReviewInput UpdateManualChoices(IReadOnlyList<EvidenceManualChoice> choices)
    {
        EnsureCurrent();
        var automatic = automaticProposal ?? throw new InvalidOperationException("Update proposal before editing hand choices.");
        var ordered = choices.OrderBy(c => c.ProteinIndex).ToArray();
        ManualFamilyChoices.Validate(file, ordered);
        var panel = reviewPanel == null ? null : ManualFamilyChoices.ExtendPanel(reviewPanel, ordered);
        if (panel != null) automatic = PanelEvidenceClassifier.Propose(file, automatic.Options, Search, References, panel);
        var proposal = ManualFamilyChoices.Overlay(automatic, ordered, file, coherenceProfiles);
        var presentation = EvidenceProposalPresentation.Present(proposal, file, coherenceProfiles, panel);
        EnsureCurrent();
        manualChoices = ordered.ToDictionary(c => c.ProteinIndex);
        automaticProposal = automatic; selectedReviewPanel = panel; Proposal = proposal; checkedMotif = null;
        return presentation;
    }
}
