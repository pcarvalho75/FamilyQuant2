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
    public string PanelChecklistSummary => "Your list: " + PanelChecklist.Count.ToString("N0", CultureInfo.CurrentCulture)
        + " families, " + PanelChecklist.Count(row => row.State == PanelChecklistState.WithProteins).ToString("N0", CultureInfo.CurrentCulture)
        + " with proteins, " + PanelChecklist.Count(row => row.State == PanelChecklistState.NeedsChoice).ToString("N0", CultureInfo.CurrentCulture)
        + " waiting for a choice, " + PanelChecklist.Count(row => row.State == PanelChecklistState.NoPfamCode).ToString("N0", CultureInfo.CurrentCulture)
        + " with no Pfam code, " + PanelChecklist.Count(row => row.State == PanelChecklistState.NoDatabaseMatch).ToString("N0", CultureInfo.CurrentCulture)
        + " matching no database protein.";
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
            string state = row.StateText + ". " + (row.NeedsLinkChoice ? "Resolve candidate links in Panel links, then Use link choices. "
                : row.State == PanelChecklistState.NoPfamCode ? "Add a Pfam code to this family list row to supply domain evidence. "
                : row.State == PanelChecklistState.NeedsChoice ? "Review the outside-match reasons below, then choose a scope phrase or place matches by hand. " : "");
            if (row.Matches == 0) return state + row.Decision + ". " + row.ContaminantReason
                + " No database targets match this panel family's stored names, source family links or Pfam domains.";
            string types = string.Join(" | ", new[] { PanelChecklistMatch.UniProtFamily, PanelChecklistMatch.Pfam, PanelChecklistMatch.NameText, PanelChecklistMatch.HandChoice,
                PanelChecklistMatch.ProposedPlacement, PanelChecklistMatch.ConfirmedPhrase, PanelChecklistMatch.ConfirmedMatch }
                .Where(type => row.Proteins.Any(protein => protein.Match.HasFlag(type)))
                .Select(type => MatchName(type) + " " + row.Proteins.Count(protein => protein.Match.HasFlag(type)).ToString("N0", CultureInfo.CurrentCulture)));
            string reasons = string.Join(Environment.NewLine, row.Proteins.Where(protein => !protein.Placed).GroupBy(protein => protein.Reason)
                .Select(group => group.Count().ToString("N0", CultureInfo.CurrentCulture) + (group.Count() == 1 ? " entry. " : " entries. ") + group.Key));
            return state + row.Decision + ". " + row.ContaminantReason + Environment.NewLine + types
                + Environment.NewLine + (reasons.Length == 0 ? "All matching targets are placed in the proposal." : reasons);
        }
    }
    public static string MatchName(PanelChecklistMatch match) => match switch
    {
        PanelChecklistMatch.UniProtFamily => "UniProt family", PanelChecklistMatch.Pfam => "Pfam",
        PanelChecklistMatch.NameText => "Name text", PanelChecklistMatch.HandChoice => "Added by hand",
        PanelChecklistMatch.ProposedPlacement => "Proposal placement", PanelChecklistMatch.ConfirmedPhrase => "Confirmed phrase",
        PanelChecklistMatch.ConfirmedMatch => "Confirmed match", _ => "",
    };
}
