using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

/// <summary>Only projects the shared stored-evidence matcher; no matching rules live in the wizard.</summary>
public sealed class FamilyListStoredEvidenceMatches : IFamilyListImportMatches
{
    private readonly FamilyClusterFile project;
    private readonly PanelEvidenceMatching matching;
    internal PanelEvidenceMatching WorkbookSnapshot => matching;
    private readonly FamilyPanel previousPanel;
    public string ChangeNotice { get; private set; } = "";
    public string SourceStatus => matching.ReferencePanelComplete switch
    { true => "All reference entries are available.", false => "Some reference entries are unavailable; counts use the stored entries.", _ => "Only evidence stored with your database is available." };
    public FamilyListStoredEvidenceMatches(FamilyClusterFile project)
    { this.project = project; matching = new PanelEvidenceMatching(project); previousPanel = FamilyPanelValidation.Clone(project.FamilyPanel ?? new FamilyPanel()); }

    public IReadOnlyList<ImportMatchFamily> Suggest(FamilyClusterFile source, FamilyPanel panel)
    {
        RequireCurrent();
        var carried = PanelEvidenceMatching.CarryConfirmedMatches(previousPanel, panel);
        ChangeNotice = carried.ConfirmedMatchChangeNotice ?? "";
        var suggestions = matching.SuggestForPanel(carried);
        return carried.Families.Where(f => FamilyPanelValidation.ResolveFamilyKey(carried, f.Key) == f.Key).Select(family =>
        {
            var confirmed = carried.ConfirmedEvidenceMatches?.GetValueOrDefault(family.Key);
            var rows = (suggestions.GetValueOrDefault(family.Key) ?? Array.Empty<PanelEvidenceSuggestion>())
                .Select(suggestion => Project(suggestion, selectSheet: confirmed?.UserConfirmed != true)).ToList();
            foreach (var choice in confirmed?.Choices ?? Array.Empty<PanelEvidenceChoice>())
            {
                var count = matching.Coverage(choice.Kind, choice.Value);
                int existing = rows.FindIndex(row => row.Key == count.Key);
                count = existing >= 0
                    ? count with { NameCoverage = rows[existing].NameCoverage, IsAdditional = rows[existing].IsAdditional,
                        IsFromSheet = rows[existing].SheetValue != null, Reason = rows[existing].SheetValue != null ? "from your sheet" : count.Reason }
                    : count with { IsAdditional = true };
                var previous = Project(count, choice.Value);
                if (existing >= 0) rows[existing] = previous; else rows.Add(previous);
            }
            return new ImportMatchFamily { Key = family.Key, Name = family.DisplayName, Suggestions = rows,
                ShowAdditional = rows.Any(row => row.Use && row.IsAdditional),
                PreviouslyConfirmed = confirmed?.UserConfirmed == true,
                Choice = confirmed?.UserConfirmed == true ? "Kept from previous panel" : "Needs a choice" };
        }).ToArray();
    }

    public FamilyPanel Accept(FamilyPanel panel, IReadOnlyList<ImportMatchFamily> choices)
    {
        RequireCurrent();
        var result = PanelEvidenceMatching.CarryConfirmedMatches(previousPanel, panel);
        foreach (var family in choices.Where(f => f.Choice == "Confirmed"))
        {
            var definition = panel.Families.Single(f => f.Key == family.Key);
            var selected = EvidenceChoices(family.Suggestions.Where(s => s.Use));
            result = PanelEvidenceMatching.WithConfirmed(result, family.Key, PanelEvidenceMatching.Confirm(Names(definition), selected));
        }
        return result;
    }

    public string ConfirmationWarning(IReadOnlyList<ImportEvidenceMatch> choices)
    {
        RequireCurrent();
        return matching.ConfirmationWarning(EvidenceChoices(choices));
    }

    private static IEnumerable<PanelEvidenceChoice> EvidenceChoices(IEnumerable<ImportEvidenceMatch> choices) =>
        choices.Select(choice => PanelEvidenceMatching.ChoiceFromKey(choice.Key) with { Value = choice.Text });

    private ImportEvidenceMatch Project(PanelEvidenceSuggestion suggestion, string? previous = null, bool selectSheet = false) => new()
    {
        Key = suggestion.Key, Text = previous ?? suggestion.Value, Type = KindName(suggestion.Kind), Proteins = suggestion.DatabaseTargets,
        Reviewed = suggestion.ReviewedTargets, Details = Details(suggestion), PreviousValue = previous, Use = previous != null || selectSheet && suggestion.IsFromSheet,
        SheetValue = suggestion.IsFromSheet ? suggestion.Value : null,
        NameCoverage = suggestion.NameCoverage, IsAdditional = suggestion.IsAdditional, IsFamilyLevelMatch = suggestion.IsFamilyLevelMatch,
        Coverage = value => { var count = matching.Coverage(suggestion.Kind, value); return new ImportMatchCoverage(count.DatabaseTargets, count.ReviewedTargets, Details(count)); },
    };

    private void RequireCurrent()
    { if (!matching.IsCurrent(project)) throw new InvalidOperationException("The stored evidence changed. Reopen the import wizard to review matching suggestions."); }
    private static IEnumerable<string> Names(FamilyPanelFamily family) => new[] { family.DisplayName }.Concat(family.Names.Select(n => n.Text));
    private string Details(PanelEvidenceSuggestion suggestion) => (suggestion.Name.Length == 0 ? "" : suggestion.Name + "\n") + suggestion.Reason
        + "\n" + suggestion.DatabaseTargets.ToString("N0", CultureInfo.CurrentCulture) + " distinct database target entries; "
        + suggestion.ReviewedTargets.ToString("N0", CultureInfo.CurrentCulture) + " reviewed; "
        + suggestion.ReferenceEntries.ToString("N0", CultureInfo.CurrentCulture) + " distinct stored reference entries.\n" + SourceStatus;
    private static string KindName(PanelEvidenceMatchKind kind) => kind switch
    { PanelEvidenceMatchKind.UniProtFamily => "UniProt family", PanelEvidenceMatchKind.Keyword => "Keyword", PanelEvidenceMatchKind.TissuePhrase => "Tissue phrase", PanelEvidenceMatchKind.Pfam => "Pfam domain", _ => "Stored evidence" };
}
