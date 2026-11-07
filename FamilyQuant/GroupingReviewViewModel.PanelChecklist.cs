using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed partial class GroupingReviewViewModel
{
    public string ChecklistScopePreviewText => ScopePreviewSummary + (ScopePreviewFamilies.Count == 0 ? ""
        : Environment.NewLine + string.Join(" | ", ScopePreviewFamilies.Select(family => family.Family + " " + family.Proteins.ToString("N0"))));
    public bool CanAddChecklistPhrase => !IsBusy && Evidence?.SelectedChecklistPhrase is { } phrase
        && WordCluster.ParseKeywords(QuoteScopeKeyword(phrase.Text)).SingleOrDefault() is { } term
        && !WordCluster.ParseKeywords(ScopeText).Contains(term, StringComparer.OrdinalIgnoreCase);
    public bool CanPlaceChecklistMatches => CanEditManual && Evidence?.SelectedChecklistFamily is { TargetKey: not null, Outside: > 0 } family
        && family.Proteins.Where(protein => !protein.Placed).All(protein => !protein.IsLocked);
    public string ChecklistHandHint
    {
        get
        {
            var family = Evidence?.SelectedChecklistFamily;
            if (family?.TargetKey == null) return "The panel decision excludes this family.";
            return family.Proteins.Any(protein => !protein.Placed && protein.IsLocked)
                ? "Unlock matching proteins in Curate families before placing them by hand."
                : "Review the matching proteins and optional note before confirming Added by hand; Apply grouping commits the choices.";
        }
    }
    public void AddChecklistPhrase()
    {
        if (!CanAddChecklistPhrase) return;
        AddScopeKeywords(new[] { Evidence!.SelectedChecklistPhrase!.Text });
    }
    public IReadOnlyList<ReviewProteinRow> ChecklistOutsideRows()
    {
        if (!CanPlaceChecklistMatches) throw new InvalidOperationException(ChecklistHandHint);
        var indices = Evidence!.SelectedChecklistFamily!.Proteins.Where(protein => !protein.Placed).Select(protein => protein.ProteinIndex).ToHashSet();
        return Evidence.Proteins.Where(protein => indices.Contains(protein.ProteinIndex)).ToArray();
    }
    private void NotifyChecklistActions()
    {
        Changed(nameof(CanAddChecklistPhrase)); Changed(nameof(CanPlaceChecklistMatches)); Changed(nameof(ChecklistHandHint));
    }
}
