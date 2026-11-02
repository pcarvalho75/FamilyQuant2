using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed class CurationFamilyRow : ScreenViewModel
{
    private string name;
    private bool locked;
    private readonly Action<bool>? lockChanged;
    public int FamilyId { get; }
    public string Name { get => name; set => Set(ref name, value); }
    public int Proteins { get; }
    public int VisibleProteins { get; }
    public double? FullXicShare { get; }
    public double? PatternLabShare { get; }
    public bool IsLocked
    {
        get => locked;
        set { if (Set(ref locked, value)) lockChanged?.Invoke(value); }
    }
    public bool CanEdit => Proteins > 0;
    public string CountHelp => Proteins.ToString("N0") + " target proteins in the whole family; "
        + VisibleProteins.ToString("N0") + " protein rows are visible under the current filters.";
    public CurationFamilyRow(int id, string name, int proteins, int visible, double? fullXic, double? patternLab,
        bool locked, Action<bool>? lockChanged = null)
    {
        FamilyId = id; this.name = name; Proteins = proteins; VisibleProteins = visible;
        FullXicShare = fullXic; PatternLabShare = patternLab; this.locked = locked;
        this.lockChanged = lockChanged;
    }
}

public sealed partial class CurationViewModel
{
    private Dictionary<int, FastaItemExtended[]> allFamilyMembers = new();
    private Dictionary<int, FastaItemExtended[]> visibleFamilyMembers = new();
    private Dictionary<int, string> familyNames = new();
    private CurationFamilyRow? selectedFamily;
    private bool refreshingFamilies;
    public IReadOnlyList<CurationFamilyRow> Families { get; private set; } = Array.Empty<CurationFamilyRow>();
    public CurationFamilyRow? SelectedFamily
    {
        get => selectedFamily;
        set
        {
            if (refreshingFamilies || !Set(ref selectedFamily, value)) return;
            Changed(nameof(SelectedFamilyProteins)); Changed(nameof(ProteinHeading));
        }
    }
    public IReadOnlyList<FastaItemExtended> SelectedFamilyProteins => SelectedFamily == null ? Rows
        : visibleFamilyMembers.TryGetValue(SelectedFamily.FamilyId, out var members) ? members : Array.Empty<FastaItemExtended>();
    public string ProteinHeading => SelectedFamily == null ? "All visible proteins" : "Proteins in family " + SelectedFamily.FamilyId;

    private void RefreshFamilyIndex()
    {
        allFamilyMembers = Document.MyExtendedFastaItems.GroupBy(p => p.UserID).ToDictionary(g => g.Key, g => g.ToArray());
        familyNames = FamilyNaming.Names(Document.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)));
    }
    private void RefreshFamilies()
    {
        int? selectedId = selectedFamily?.FamilyId;
        visibleFamilyMembers = Rows.GroupBy(p => p.UserID).ToDictionary(g => g.Key, g => g.ToArray());
        var rows = new List<CurationFamilyRow>(visibleFamilyMembers.Count);
        foreach (var pair in visibleFamilyMembers.OrderBy(p => p.Key))
        {
            if (!allFamilyMembers.TryGetValue(pair.Key, out var all)) continue;
            var targets = all.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)).ToArray();
            var first = targets.FirstOrDefault();
            rows.Add(new CurationFamilyRow(pair.Key, familyNames.TryGetValue(pair.Key, out var name) ? name : "",
                targets.Length, pair.Value.Length, first?.FullXicShare,
                Quantitation.DisplayedMode == null ? null : first?.XICUser, Document.LockedUserIds.Contains(pair.Key),
                value => SetFamilyLock(pair.Key, value)));
        }
        refreshingFamilies = true;
        try
        {
            Families = rows;
            selectedFamily = selectedId.HasValue ? rows.FirstOrDefault(r => r.FamilyId == selectedId.Value) : null;
            Changed(nameof(Families)); Changed(nameof(SelectedFamily));
            Changed(nameof(SelectedFamilyProteins)); Changed(nameof(ProteinHeading));
        }
        finally { refreshingFamilies = false; }
    }

    public bool RenameFamily(int family, string name)
    {
        if (!allFamilyMembers.TryGetValue(family, out var members)) return false;
        var targets = members.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)).ToArray();
        if (targets.Length == 0 || targets.All(p => p.UserAnnotation == name)) return false;
        PushUndo(targets);
        foreach (var target in targets) target.UserAnnotation = name;
        NotifyDocumentChanged();
        return true;
    }
    public bool SetFamilyLock(int family, bool locked)
    {
        if (Document.LockedUserIds.Contains(family) == locked) return false;
        PushUndo();
        if (locked) Document.LockedUserIds.Add(family); else Document.LockedUserIds.Remove(family);
        NotifyDocumentChanged();
        return true;
    }
}
