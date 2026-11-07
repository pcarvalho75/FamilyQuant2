using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

public sealed record PanelEvidenceMatchContext(PanelEvidenceMatching Evidence, FamilyPanelFamily Family,
    PanelConfirmedEvidenceMatches? Confirmed, Func<bool> IsCurrent, FamilyPanel? Panel = null);

public sealed record PanelEvidenceKindOption(PanelEvidenceMatchKind Kind, string Name);

public sealed class PanelEvidenceMatchRow : ScreenViewModel
{
    private readonly PanelEvidenceMatching evidence;
    private readonly Action changed;
    private readonly string basis;
    private readonly string initialName;
    private PanelEvidenceSuggestion coverage;
    private PanelEvidenceMatchKind kind;
    private string value;
    private bool selected, edited;
    public PanelEvidenceMatchKind Kind { get => kind; set { if (Set(ref kind, value)) Refresh(); } }
    public string Value { get => value; set { if (Set(ref this.value, value ?? "")) Refresh(); } }
    public bool Selected { get => selected; set { if (Set(ref selected, value)) changed(); } }
    public string Name => !edited ? initialName : coverage.Name;
    public int Targets => coverage.DatabaseTargets;
    public int Reviewed => coverage.ReviewedTargets;
    public int References => coverage.ReferenceEntries;
    public bool IsAdditional { get; }
    public bool IsFromSheet { get; }
    public string Reason
    {
        get
        {
            if (!edited) return basis;
            return coverage.DatabaseTargets + coverage.ReferenceEntries == 0 ? "Edited label has no exact match in the stored evidence."
                : "Edited label. Exact stored-label coverage is shown.";
        }
    }
    public bool IsValid => Enum.IsDefined(Kind) && !string.IsNullOrWhiteSpace(Value);
    public PanelEvidenceMatchRow(PanelEvidenceMatching evidence, PanelEvidenceSuggestion suggestion, bool selected, Action changed)
    {
        this.evidence = evidence; this.changed = changed; basis = suggestion.Reason;
        initialName = suggestion.Name; IsFromSheet = suggestion.IsFromSheet;
        IsAdditional = suggestion.IsAdditional;
        kind = suggestion.Kind; value = suggestion.Value; this.selected = selected; coverage = evidence.Coverage(kind, value);
    }
    private void Refresh()
    {
        edited = true; coverage = evidence.Coverage(Kind, Value);
        Changed(nameof(Name)); Changed(nameof(Targets)); Changed(nameof(Reviewed)); Changed(nameof(References)); Changed(nameof(Reason));
        changed();
    }
}

public sealed class PanelEvidenceMatchViewModel : ScreenViewModel
{
    private readonly PanelEvidenceMatchContext context;
    private readonly string[] names;
    private string error = "";
    private bool stale, showMore;
    public static IReadOnlyList<PanelEvidenceKindOption> KindOptions { get; } = new[]
    {
        new PanelEvidenceKindOption(PanelEvidenceMatchKind.UniProtFamily, "UniProt family"), new(PanelEvidenceMatchKind.Keyword, "Keyword"),
        new(PanelEvidenceMatchKind.TissuePhrase, "Tissue phrase"), new(PanelEvidenceMatchKind.Pfam, "Pfam"),
    };
    public string Heading => "Match " + (string.IsNullOrWhiteSpace(context.Family.DisplayName) ? context.Family.Key : context.Family.DisplayName);
    public string NamesText => "Display name - " + context.Family.DisplayName + string.Concat(context.Family.Names.Select(name => " | " + name.Name + " - " + name.Text));
    public ObservableCollection<PanelEvidenceMatchRow> Rows { get; } = new();
    public ObservableCollection<PanelEvidenceMatchRow> VisibleRows { get; } = new();
    public bool ShowMore { get => showMore; set { if (Set(ref showMore, value)) RefreshVisibleRows(); } }
    public string FamilyMatchStatus { get; }
    public Func<string, bool>? BroadMatchConfirmation { get; set; }
    public bool CanConfirm => !stale && error.Length == 0 && Rows.Where(row => row.Selected).All(row => row.IsValid);
    public string ConfirmHint => stale ? "Close this window and reopen the matches to review the changed stored evidence."
        : Rows.Any(row => row.Selected && !row.IsValid) ? "Fill in or untick each empty selected label before confirming."
        : "Returns the ticked labels to the pending draft. Use Apply grouping in Group by evidence or Apply panel in the panel editor to commit one project change; Undo restores the previous state.";
    public string Summary
    {
        get
        {
            if (error.Length > 0) return error;
            int selected = Rows.Count(row => row.Selected);
            return selected.ToString("N0", CultureInfo.CurrentCulture) + (selected == 1 ? " label selected. " : " labels selected. ")
                + (context.Evidence.ReferencePanelComplete == false ? "Stored reference panel is incomplete; counts use available entries."
                    : context.Evidence.ReferencePanelComplete == true ? "Stored reference panel is complete." : "No stored reference panel.");
        }
    }
    public PanelEvidenceMatchViewModel(PanelEvidenceMatchContext context)
    {
        this.context = context;
        names = context.Family.Names.Select(name => name.Text).Prepend(context.Family.DisplayName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToArray();
        var saved = context.Confirmed?.UserConfirmed == true ? context.Confirmed.Choices : Array.Empty<PanelEvidenceChoice>();
        var selected = saved.Select(choice => context.Evidence.Coverage(choice.Kind, choice.Value).Key).ToHashSet(StringComparer.Ordinal);
        var suggestions = context.Panel == null ? context.Evidence.SuggestForFamily(context.Family) : context.Evidence.SuggestForPanel(context.Panel)[context.Family.Key];
        FamilyMatchStatus = suggestions.Any(row => row.IsFamilyLevelMatch) ? "Family and domain names first" : "No family-level match";
        foreach (var suggestion in suggestions)
            Rows.Add(new(context.Evidence, suggestion, context.Confirmed?.UserConfirmed == true ? selected.Contains(suggestion.Key) : suggestion.IsFromSheet, NotifySelection));
        foreach (var choice in saved)
        {
            var coverage = context.Evidence.Coverage(choice.Kind, choice.Value);
            if (Rows.Any(row => row.Kind == choice.Kind && row.Value.Equals(coverage.Value, StringComparison.OrdinalIgnoreCase))) continue;
            Rows.Add(new(context.Evidence, coverage with { Reason = "Previously confirmed choice." }, true, NotifySelection));
        }
        Rows.CollectionChanged += (_, _) => { NotifySelection(); RefreshVisibleRows(); };
        RefreshVisibleRows();
    }
    public PanelEvidenceMatchRow AddLabel()
    {
        var row = new PanelEvidenceMatchRow(context.Evidence, new(PanelEvidenceMatchKind.UniProtFamily, "", "", 0, 0, "Enter your evidence label."), true, NotifySelection);
        Rows.Add(row); return row;
    }
    public bool TryConfirm(out PanelConfirmedEvidenceMatches? confirmed, Func<string, bool>? confirmBroad = null)
    {
        confirmed = null;
        if (!context.IsCurrent()) { stale = true; error = "Stored evidence changed. Close this window and reopen the matches."; NotifySelection(false); return false; }
        if (!CanConfirm) return false;
        try
        {
            var choices = Rows.Where(row => row.Selected).Select(row => new PanelEvidenceChoice(row.Kind, row.Value)).ToArray();
            string warning = context.Evidence.ConfirmationWarning(choices);
            if (warning.Length > 0 && (confirmBroad ?? BroadMatchConfirmation)?.Invoke(warning) != true) return false;
            if (!context.IsCurrent()) { stale = true; error = "Stored evidence changed. Close this window and reopen the matches."; NotifySelection(false); return false; }
            if (!choices.SequenceEqual(Rows.Where(row => row.Selected).Select(row => new PanelEvidenceChoice(row.Kind, row.Value)))) return false;
            confirmed = PanelEvidenceMatching.Confirm(names, choices);
            return true;
        }
        catch (ArgumentException invalid) { error = invalid.Message; NotifySelection(false); return false; }
    }
    private void NotifySelection() => NotifySelection(true);
    private void RefreshVisibleRows()
    {
        VisibleRows.Clear();
        foreach (var row in Rows.Where(row => ShowMore || !row.IsAdditional || row.Selected)) VisibleRows.Add(row);
    }
    private void NotifySelection(bool clearError)
    {
        if (clearError && !stale) error = "";
        Changed(nameof(CanConfirm)); Changed(nameof(Summary)); Changed(nameof(ConfirmHint));
    }
}
