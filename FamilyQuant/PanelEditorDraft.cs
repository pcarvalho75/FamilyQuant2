using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FamilyQuant.Panels;
using FamilyQuant.Evidence;
using FamilyQuant.Panels.Import;

namespace FamilyQuant;

/// <summary>A detached editable view. Unedited domain and subclass definitions retain all their details.</summary>
public sealed class PanelEditorDraft
{
    private readonly FamilyPanel original;
    private readonly string initialMatchNotice;
    private readonly Dictionary<PanelEditorRow, (string Key, PanelConfirmedEvidenceMatches Matches)> pendingMatches = new();
    public string MatchChangeNotice { get; private set; } = "";
    public string Name { get; set; }
    public ObservableCollection<PanelEditorRow> Families { get; }
    public IReadOnlyList<PanelWorkbookSourceNote> SourceNotes => original.SourceNotes;
    public bool HasSourceNotes => SourceNotes.Count > 0;

    public PanelEditorDraft(FamilyPanel? panel)
    {
        original = panel == null ? new FamilyPanel() : FamilyPanelValidation.Clone(panel);
        initialMatchNotice = panel?.ConfirmedMatchChangeNotice ?? "";
        MatchChangeNotice = initialMatchNotice;
        Name = original.Name;
        Families = new ObservableCollection<PanelEditorRow>(original.Families.Select(f => new PanelEditorRow(f)));
    }

    public FamilyPanel Build()
    {
        var result = FamilyPanelValidation.Clone(original);
        result.Name = Name ?? string.Empty;
        result.Families = Families.Select(x => x.Build()).ToList();
        var retained = Families.Where(row => row.OriginalKey.Length > 0).ToDictionary(row => row.OriginalKey, row => row.Key, StringComparer.Ordinal);
        result = PanelEvidenceMatching.RekeyConfirmedMatches(original, result, retained);
        var notices = new List<string> { initialMatchNotice, result.ConfirmedMatchChangeNotice };
        foreach (var pair in pendingMatches)
        {
            bool originalRecord = original.ConfirmedEvidenceMatches?.ContainsKey(pair.Key.OriginalKey) == true;
            if (!Families.Contains(pair.Key))
            {
                if (!originalRecord) notices.Add("Dropped confirmed matches for " + pair.Key.Key + ".");
                continue;
            }
            result.ConfirmedEvidenceMatches ??= new(StringComparer.Ordinal);
            result.ConfirmedEvidenceMatches[pair.Key.Key] = PanelEvidenceMatching.Confirm(pair.Value.Matches.NamesUsed, pair.Value.Matches.Choices);
            if (!originalRecord && pair.Value.Key != pair.Key.Key)
                notices.Add("Moved confirmed matches from " + pair.Value.Key + " to " + pair.Key.Key + ".");
        }
        MatchChangeNotice = string.Join(" ", notices.Where(text => text.Length > 0).Distinct(StringComparer.Ordinal));
        if (result.Families.Any(x => string.IsNullOrWhiteSpace(x.DisplayName)))
            throw new ArgumentException("Every family needs a name.");
        FamilyPanelValidation.Validate(result);
        return result;
    }
    public void SetConfirmedMatches(PanelEditorRow row, PanelConfirmedEvidenceMatches matches)
    {
        if (!Families.Contains(row)) throw new ArgumentException("Choose a family in this panel draft.", nameof(row));
        var updated = PanelEvidenceMatching.WithConfirmed(Build(), row.Key, matches);
        pendingMatches[row] = (row.Key, updated.ConfirmedEvidenceMatches![row.Key]);
    }
}

public sealed class PanelEditorRow : ScreenViewModel
{
    private readonly FamilyPanelFamily original;
    private readonly string originalPfam;
    private readonly string originalSubclasses;
    internal string OriginalKey => original.Key;
    private string key = "", name = "";
    public string Key
    {
        get => key;
        set { if (Set(ref key, value ?? "")) { Changed(nameof(KeyDisplay)); Changed(nameof(HasGeneratedKey)); Changed(nameof(FamilyChoiceLabel)); } }
    }
    public string Name
    {
        get => name;
        set { if (Set(ref name, value ?? "")) { Changed(nameof(KeyDisplay)); Changed(nameof(FamilyChoiceLabel)); } }
    }
    public bool HasGeneratedKey => ManualFamilyChoices.IsGeneratedKey(Key);
    public string KeyDisplay { get => HasGeneratedKey ? Name : Key; set { if (!HasGeneratedKey) Key = value; } }
    public string FamilyChoiceLabel => HasGeneratedKey || Key.Length == 0 ? Name : Name + " (" + Key + ")";
    public FamilyPanelDecision Decision { get; set; }
    public string MergeTarget { get; set; }
    public string Pfam { get; set; }
    public string Subclasses { get; set; }

    public PanelEditorRow() : this(new FamilyPanelFamily()) { }
    public PanelEditorRow(FamilyPanelFamily family)
    {
        original = FamilyPanelValidation.Clone(new FamilyPanel { Families = new() { family } }).Families[0];
        Key = original.Key; Name = original.DisplayName; Decision = original.Decision;
        MergeTarget = original.MergeTargetFamilyKey;
        originalPfam = Pfam = string.Join("; ", original.Pfam.Select(x => x.Accession));
        originalSubclasses = Subclasses = string.Join("; ", original.Subclasses.Select(x => x.Key));
    }

    public FamilyPanelFamily Build()
    {
        var copy = FamilyPanelValidation.Clone(new FamilyPanel { Families = new() { original } }).Families[0];
        copy.Key = Key ?? string.Empty; copy.DisplayName = Name ?? string.Empty;
        copy.Decision = Decision; copy.MergeTargetFamilyKey = MergeTarget ?? string.Empty;
        if (Pfam != originalPfam)
        {
            var existing = copy.Pfam.GroupBy(x => x.Accession, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            copy.Pfam = Parse(Pfam).Select(x => existing.TryGetValue(x, out var domain) ? domain : new FamilyPanelDomain { Accession = x }).ToList();
        }
        if (Subclasses != originalSubclasses)
        {
            var existing = copy.Subclasses.ToDictionary(x => x.Key, StringComparer.Ordinal);
            copy.Subclasses = Parse(Subclasses).Select(x => existing.TryGetValue(x, out var part) ? part : new FamilyPanelSubclass { Key = x, DisplayName = x }).ToList();
        }
        return copy;
    }

    private static IEnumerable<string> Parse(string? text) => (text ?? string.Empty)
        .Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim()).Where(x => x.Length > 0);
}
