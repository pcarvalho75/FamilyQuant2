using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

public sealed record ChecklistScopePhrase(string Text, int Targets)
{
    public string DisplayName => Text + " (" + Targets.ToString("N0", CultureInfo.CurrentCulture) + " targets)";
}

public sealed partial class EvidenceReviewViewModel
{
    private PanelChecklistFamily? selectedChecklistFamily;
    private ChecklistScopePhrase? selectedChecklistPhrase;
    public IReadOnlyList<PanelChecklistFamily> PanelChecklist => Source.PanelChecklist ?? Array.Empty<PanelChecklistFamily>();
    public PanelChecklistFamily? SelectedChecklistFamily
    {
        get => selectedChecklistFamily ?? PanelChecklist.FirstOrDefault();
        set
        {
            if (!Set(ref selectedChecklistFamily, value)) return;
            selectedChecklistPhrase = null;
            Changed(nameof(ChecklistDetail)); Changed(nameof(ChecklistPhrases)); Changed(nameof(SelectedChecklistPhrase));
        }
    }
    public IReadOnlyList<ChecklistScopePhrase> ChecklistPhrases => (SelectedChecklistFamily?.Proteins ?? Array.Empty<PanelChecklistProtein>())
        .Where(protein => !protein.Placed).SelectMany(protein => (protein.ScopePhrases ?? Array.Empty<string>()).Select(phrase => (phrase, protein.ProteinIndex)))
        .GroupBy(item => item.phrase, StringComparer.OrdinalIgnoreCase)
        .Select(group => new ChecklistScopePhrase(group.Key, group.Select(item => item.ProteinIndex).Distinct().Count()))
        .OrderByDescending(phrase => phrase.Targets).ThenBy(phrase => phrase.Text, StringComparer.CurrentCultureIgnoreCase).ToArray();
    public ChecklistScopePhrase? SelectedChecklistPhrase { get => selectedChecklistPhrase ?? ChecklistPhrases.FirstOrDefault(); set => Set(ref selectedChecklistPhrase, value); }
    public string ChecklistDetail
    {
        get
        {
            var row = SelectedChecklistFamily;
            if (row == null) return "";
            if (row.Matches == 0) return row.Decision + ". " + row.ContaminantReason
                + " No database targets match this panel family's stored names, source family links or Pfam domains.";
            string types = string.Join(" | ", new[] { PanelChecklistMatch.UniProtFamily, PanelChecklistMatch.Pfam, PanelChecklistMatch.NameText, PanelChecklistMatch.HandChoice, PanelChecklistMatch.ProposedPlacement }
                .Where(type => row.Proteins.Any(protein => protein.Match.HasFlag(type)))
                .Select(type => MatchName(type) + " " + row.Proteins.Count(protein => protein.Match.HasFlag(type)).ToString("N0", CultureInfo.CurrentCulture)));
            string reasons = string.Join(Environment.NewLine, row.Proteins.Where(protein => !protein.Placed).GroupBy(protein => protein.Reason)
                .Select(group => group.Count().ToString("N0", CultureInfo.CurrentCulture) + (group.Count() == 1 ? " entry. " : " entries. ") + group.Key));
            return row.Decision + ". " + row.ContaminantReason + Environment.NewLine + types
                + Environment.NewLine + (reasons.Length == 0 ? "All matching targets are placed in the proposal." : reasons);
        }
    }
    public static string MatchName(PanelChecklistMatch match) => match switch
    {
        PanelChecklistMatch.UniProtFamily => "UniProt family", PanelChecklistMatch.Pfam => "Pfam",
        PanelChecklistMatch.NameText => "Name text", PanelChecklistMatch.HandChoice => "Added by hand",
        PanelChecklistMatch.ProposedPlacement => "Proposal placement", _ => "",
    };
}
