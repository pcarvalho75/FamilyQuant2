using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FamilyQuant.Panels;

namespace FamilyQuant;

/// <summary>A detached editable view. Unedited domain and subclass definitions retain all their details.</summary>
public sealed class PanelEditorDraft
{
    private readonly FamilyPanel original;
    public string Name { get; set; }
    public ObservableCollection<PanelEditorRow> Families { get; }

    public PanelEditorDraft(FamilyPanel? panel)
    {
        original = panel == null ? new FamilyPanel() : FamilyPanelValidation.Clone(panel);
        Name = original.Name;
        Families = new ObservableCollection<PanelEditorRow>(original.Families.Select(f => new PanelEditorRow(f)));
    }

    public FamilyPanel Build()
    {
        var result = FamilyPanelValidation.Clone(original);
        result.Name = Name ?? string.Empty;
        result.Families = Families.Select(x => x.Build()).ToList();
        if (result.Families.Any(x => string.IsNullOrWhiteSpace(x.DisplayName)))
            throw new ArgumentException("Every family needs a name.");
        FamilyPanelValidation.Validate(result);
        return result;
    }
}

public sealed class PanelEditorRow
{
    private readonly FamilyPanelFamily original;
    private readonly string originalPfam;
    private readonly string originalSubclasses;
    public string Key { get; set; }
    public string Name { get; set; }
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
