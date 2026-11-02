using System;
using FamilyQuant.Evidence;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    // Presentation inputs mirror the published proposal contract without classifying any protein.
    public sealed record ReviewProteinInput(int ProteinIndex, string Identifier, int PreviousFamilyId,
        string Family, string Tier, string Reason, bool HasScopeAnnotation, string Neighbour,
        IReadOnlyList<string> Flags, bool SuggestedSelected, string Description = "", string Domains = "", bool IsLocked = false,
        bool? Reviewed = null, int? Length = null, int? Cysteines = null, bool? HasPfamCalls = null);
    public sealed record ReviewFamilyInput(string Family, string SuggestedName, int Proteins, int ReviewedProteins);
    public sealed record ReviewMarkerInput(string Accession, string Name, string Family, int FamilyCarriers, int AllCarriers,
        int LabelledCarriers = 0, double VoteFraction = 0);
    public sealed record ReviewLeftOutInput(string Family, string Accession, string Identifier, string Description,
        int Length, bool? Reviewed, bool Fragment, string MarkerAccession, string MarkerName, string AssignedFamily, string Reason);
    public sealed record EvidenceReviewInput(string SourceFingerprint, IReadOnlyList<ReviewProteinInput> Proteins,
        IReadOnlyList<ReviewFamilyInput> Families, IReadOnlyList<ReviewMarkerInput> Markers,
        IReadOnlyList<ReviewArchitectureInput>? Architectures = null, IReadOnlyList<ReviewLeftOutInput>? LeftOut = null,
        string SourceLabel = "", string RuleSummary = "");
    public sealed record EvidenceReviewSelection(string SourceFingerprint, IReadOnlyList<int> ProteinIndices,
        IReadOnlyDictionary<string, string> Names, IReadOnlyList<ReviewArchitectureChoice>? ArchitectureChoices = null,
        string MotifPattern = "", IReadOnlyList<EvidenceProteinOverride>? ProteinOverrides = null);

    public sealed class ReviewProteinRow : ScreenViewModel
    {
        private readonly Action<ReviewProteinRow, int> selectionChanged;
        private bool selected;
        public ReviewProteinInput Source { get; }
        public int ProteinIndex => Source.ProteinIndex;
        public string Identifier => Source.Identifier;
        public string Accession { get { string value = FamilyNaming.Accession(Identifier); return value.Length == 0 ? Identifier : value; } }
        public int PreviousFamilyId => Source.PreviousFamilyId;
        public string Family => Source.Family;
        public string Tier => Source.Tier;
        public string Reason => Source.Reason;
        public string Neighbour => Source.Neighbour;
        public string Description => Source.Description;
        public string Domains => Source.Domains;
        public string Flags => string.Join("; ", Source.Flags);
        public bool IsFlagged => Source.Flags.Count > 0;
        public bool HasScopeAnnotation => Source.HasScopeAnnotation;
        public bool CanSelect => Source.Family.Length > 0 && !Source.IsLocked;
        public string SelectionHelp => Source.IsLocked
            ? "This protein belongs to a locked family. Unlock it in Families before including it; it stays unticked here."
            : Source.Family.Length == 0 ? "This protein has no proposed family and stays outside the selection."
            : "Select this protein for the proposed family. Unticking leaves its current family unchanged.";
        public bool Selected
        {
            get => selected;
            set
            {
                if (value && !CanSelect) { Changed(); return; }
                if (selected == value) return;
                selected = value; Changed(); selectionChanged(this, value ? 1 : -1);
            }
        }
        internal ReviewProteinRow(ReviewProteinInput source, Action<ReviewProteinRow, int> changed)
        {
            Source = source; selectionChanged = changed;
            selected = source.SuggestedSelected && CanSelect;
        }
    }

    public sealed class ReviewFamilyRow : ScreenViewModel
    {
        private readonly Action changed;
        private readonly Action<IEnumerable<ReviewProteinRow>, bool> setSelected;
        private string name;
        private int selectedCount;
        public ReviewFamilyInput Source { get; }
        public string Family => Source.Family;
        public int Proteins => Source.Proteins;
        public int ReviewedProteins => Source.ReviewedProteins;
        public IReadOnlyList<ReviewProteinRow> Members { get; }
        public int SelectedCount => selectedCount;
        public bool CanSelect => Members.Any(p => p.CanSelect);
        public string Name { get => name; set { if (name == value) return; name = value; Changed(); changed(); } }
        public bool? Selected
        {
            get => selectedCount == 0 ? false : selectedCount == Members.Count ? true : null;
            set => setSelected(Members, value == true);
        }
        internal ReviewFamilyRow(ReviewFamilyInput source, IReadOnlyList<ReviewProteinRow> members, Action changed,
            Action<IEnumerable<ReviewProteinRow>, bool> setSelected)
        {
            Source = source; Members = members; this.changed = changed;
            this.setSelected = setSelected;
            name = source.SuggestedName; selectedCount = members.Count(p => p.Selected);
        }
        internal void SelectionDelta(int delta, bool notify)
        {
            selectedCount += delta; if (notify) NotifySelection();
        }
        internal void NotifySelection() { Changed(nameof(SelectedCount)); Changed(nameof(Selected)); }
    }

    /// <summary>Selection, naming and display only. The proposal's tiers, reasons and flags are immutable.</summary>
    public sealed class EvidenceReviewViewModel : ScreenViewModel
    {
        private readonly Dictionary<string, ReviewFamilyRow> familyByKey;
        private int selectedCount;
        private int bulkDepth;
        private readonly HashSet<ReviewFamilyRow> bulkFamilies = new();
        private ReviewFamilyRow? selectedFamily;
        private string searchText = "";
        public EvidenceReviewInput Source { get; }
        public IReadOnlyList<ReviewProteinRow> Proteins { get; }
        public IReadOnlyList<ReviewFamilyRow> Families { get; }
        public IReadOnlyList<ReviewProteinRow> Unassigned { get; }
        public IReadOnlyList<ReviewLeftOutInput> LeftOut { get; }
        public IReadOnlyList<ArchitectureReviewViewModel> Architectures { get; }
        public ArchitectureReviewViewModel? SelectedArchitecture => Architectures.FirstOrDefault(a => a.Family == SelectedFamily?.Family);
        public IReadOnlyList<ReviewMarkerInput> Markers => Source.Markers;
        public int SelectedCount => selectedCount;
        public bool CanConfirm => selectedCount > 0 && Families.Where(f => f.SelectedCount > 0).All(f => !string.IsNullOrWhiteSpace(f.Name))
            && Architectures.Where(a => familyByKey.TryGetValue(a.Family, out var family) && family.SelectedCount > 0).All(a => a.IsValid);
        public string Summary => (Source.SourceLabel.Length == 0 ? "" : Source.SourceLabel + " ") + selectedCount.ToString("N0") + " proteins selected in "
            + Families.Count(f => f.SelectedCount > 0).ToString("N0") + " families. Nothing has changed yet.";
        public ReviewFamilyRow? SelectedFamily
        {
            get => selectedFamily;
            set { if (ReferenceEquals(selectedFamily, value)) return; selectedFamily = value; Changed(); Changed(nameof(VisibleProteins)); Changed(nameof(SelectedArchitecture)); }
        }
        public string SearchText { get => searchText; set { if (searchText == value) return; searchText = value; Changed(); Changed(nameof(VisibleProteins)); } }
        public IEnumerable<ReviewProteinRow> VisibleProteins => (SelectedFamily?.Members ?? Proteins).Where(Matches);

        public EvidenceReviewViewModel(EvidenceReviewInput source)
        {
            ArgumentNullException.ThrowIfNull(source);
            source = source with
            {
                Proteins = source.Proteins.Select(p => p with { Flags = p.Flags.ToArray() }).ToArray(),
                Families = source.Families.ToArray(), Markers = source.Markers.ToArray(), LeftOut = source.LeftOut?.ToArray()
            };
            Source = source;
            if (string.IsNullOrEmpty(source.SourceFingerprint)) throw new ArgumentException("The proposal needs a source fingerprint.", nameof(source));
            if (source.Proteins.Select(p => p.ProteinIndex).Distinct().Count() != source.Proteins.Count)
                throw new ArgumentException("A proposal cannot repeat a protein index.", nameof(source));
            familyByKey = new Dictionary<string, ReviewFamilyRow>(StringComparer.Ordinal);
            Proteins = source.Proteins.Select(p => new ReviewProteinRow(p, SelectionChanged)).ToArray();
            var groups = Proteins.GroupBy(p => p.Family, StringComparer.Ordinal).ToDictionary(g => g.Key, g => (IReadOnlyList<ReviewProteinRow>)g.ToArray(), StringComparer.Ordinal);
            foreach (var family in source.Families)
            {
                if (family.Family.Length == 0 || familyByKey.ContainsKey(family.Family))
                    throw new ArgumentException("Proposed families need distinct, nonempty keys.", nameof(source));
                familyByKey.Add(family.Family, new ReviewFamilyRow(family,
                    groups.GetValueOrDefault(family.Family) ?? Array.Empty<ReviewProteinRow>(), SelectionOrNameChanged, SetSelected));
            }
            if (Proteins.Any(p => p.Family.Length > 0 && !familyByKey.ContainsKey(p.Family)))
                throw new ArgumentException("A protein refers to a family missing from the proposal.", nameof(source));
            Families = source.Families.Select(f => familyByKey[f.Family]).ToArray();
            var proteinsByIndex = source.Proteins.ToDictionary(p => p.ProteinIndex);
            Architectures = (source.Architectures ?? Array.Empty<ReviewArchitectureInput>())
                .Select(a => new ArchitectureReviewViewModel(a, proteinsByIndex, SelectionOrNameChanged)).ToArray();
            Unassigned = Proteins.Where(p => p.Family.Length == 0).ToArray();
            LeftOut = source.LeftOut ?? Array.Empty<ReviewLeftOutInput>();
            selectedCount = Proteins.Count(p => p.Selected);
            selectedFamily = Families.FirstOrDefault();
        }

        private bool Matches(ReviewProteinRow row) => searchText.Length == 0 || row.Identifier.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || row.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase) || row.Reason.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || row.Flags.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        private void SelectionChanged(ReviewProteinRow row, int delta)
        {
            if (row.Family.Length == 0)
            {
                if (row.Selected) row.Selected = false;
                return;
            }
            var family = familyByKey[row.Family];
            selectedCount += delta; family.SelectionDelta(delta, bulkDepth == 0);
            if (bulkDepth > 0) bulkFamilies.Add(family); else SelectionOrNameChanged();
        }
        private void SelectionOrNameChanged()
        {
            Changed(nameof(SelectedCount)); Changed(nameof(CanConfirm)); Changed(nameof(Summary));
        }
        public void SetSelected(IEnumerable<ReviewProteinRow> rows, bool selected)
        {
            bulkDepth++;
            try { foreach (var row in rows) row.Selected = selected; }
            finally
            {
                if (--bulkDepth == 0 && bulkFamilies.Count > 0)
                {
                    foreach (var family in bulkFamilies) family.NotifySelection();
                    bulkFamilies.Clear(); SelectionOrNameChanged();
                }
            }
        }
        public void UntickFlagged() => SetSelected(Proteins.Where(p => p.IsFlagged), false);
        public EvidenceReviewSelection CaptureSelection()
        {
            if (!CanConfirm) throw new InvalidOperationException("Select proteins and name every selected family before applying.");
            return new EvidenceReviewSelection(Source.SourceFingerprint, Proteins.Where(p => p.Selected).Select(p => p.ProteinIndex).OrderBy(i => i).ToArray(),
                Families.Where(f => f.SelectedCount > 0).ToDictionary(f => f.Family, f => f.Name.Trim(), StringComparer.Ordinal),
                Architectures.SelectMany(a => a.Capture()).ToArray());
        }
    }
}
