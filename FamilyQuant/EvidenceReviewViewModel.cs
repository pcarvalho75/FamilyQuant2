using System;
using FamilyQuant.Evidence;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace FamilyQuant
{
    // Presentation inputs mirror the published proposal contract without classifying any protein.
    public sealed record ReviewProteinInput(int ProteinIndex, string Identifier, int PreviousFamilyId,
        string Family, string Tier, string Reason, bool HasScopeAnnotation, string Neighbour,
        IReadOnlyList<string> Flags, bool SuggestedSelected, string Description = "", string Domains = "", bool IsLocked = false,
        bool? Reviewed = null, int? Length = null, int? Cysteines = null, bool? HasPfamCalls = null, string OriginalEvidence = "");
    public sealed record ReviewFamilyInput(string Family, string SuggestedName, int Proteins, int ReviewedProteins);
    public sealed record ReviewMarkerInput(string Accession, string Name, string Family, int FamilyCarriers, int AllCarriers,
        int LabelledCarriers = 0, double VoteFraction = 0)
    {
        public string SupportText => LabelledCarriers > 0 ? VoteFraction.ToString("P1", CultureInfo.CurrentCulture) : "Not supplied";
        public string SupportHelp => LabelledCarriers > 0
            ? FamilyCarriers.ToString("N0") + " of " + LabelledCarriers.ToString("N0")
                + " labelled scope proteins carrying this domain support this family; all " + AllCarriers.ToString("N0") + " domain carriers are counted separately."
            : "No labelled-carrier denominator was supplied; support is unavailable.";
    }
    public sealed record ReviewLeftOutInput(string Family, string Accession, string Identifier, string Description,
        int Length, bool? Reviewed, bool Fragment, string MarkerAccession, string MarkerName, string AssignedFamily, string Reason);
    public sealed record EvidenceReviewInput(string SourceFingerprint, IReadOnlyList<ReviewProteinInput> Proteins,
        IReadOnlyList<ReviewFamilyInput> Families, IReadOnlyList<ReviewMarkerInput> Markers,
        IReadOnlyList<ReviewArchitectureInput>? Architectures = null, IReadOnlyList<ReviewLeftOutInput>? LeftOut = null,
        string SourceLabel = "", string RuleSummary = "", IReadOnlyList<PanelFamilyLinkProposal>? PanelLinks = null,
        IReadOnlyDictionary<string, string>? PanelNames = null, IReadOnlyDictionary<string, FamilyProfile>? CoherenceProfiles = null,
        IReadOnlyList<EvidenceManualChoice>? ManualChoices = null, IReadOnlyList<PanelChecklistFamily>? PanelChecklist = null);
    public sealed record EvidenceReviewSelection(string SourceFingerprint, IReadOnlyList<int> ProteinIndices,
        IReadOnlyDictionary<string, string> Names, IReadOnlyList<ReviewArchitectureChoice>? ArchitectureChoices = null,
        string MotifPattern = "", IReadOnlyList<EvidenceProteinOverride>? ProteinOverrides = null, IReadOnlyList<EvidenceManualChoice>? ManualChoices = null);

    public sealed class ReviewProteinRow : ScreenViewModel
    {
        private readonly Action<ReviewProteinRow, int> selectionChanged;
        private bool selected;
        private readonly EvidenceManualChoice? manualChoice;
        public ReviewProteinInput Source { get; }
        public int ProteinIndex => Source.ProteinIndex;
        public string Identifier => Source.Identifier;
        public string Accession { get { string value = FamilyNaming.Accession(Identifier); return value.Length == 0 ? Identifier : value; } }
        public int PreviousFamilyId => Source.PreviousFamilyId;
        public string Family => Source.Family;
        public string FamilyDisplay => manualChoice?.Name ?? Family;
        public string Tier => Source.Tier;
        public string Reason => Source.Reason;
        public string Neighbour => Source.Neighbour;
        public string Description => Source.Description;
        public string Domains => Source.Domains;
        public string Flags => string.Join("; ", Source.Flags.Select(f => EvidenceWarningText.Describe(f).Label));
        public FamilyProfile? CoherenceProfile { get; }
        public string FlagsHelp => string.Join(Environment.NewLine + Environment.NewLine, Source.Flags.Select(f => EvidenceWarningText.Describe(f).Help))
            + (CoherenceProfile != null && Source.Flags.Any(f => f.StartsWith("coherence.", StringComparison.Ordinal))
                ? Environment.NewLine + Environment.NewLine + CoherenceProfilePresentation.CompleteSources(CoherenceProfile) : "");
        public bool IsFlagged => Source.Flags.Count > 0;
        public bool HasScopeAnnotation => Source.HasScopeAnnotation;
        public string ScopeWordsText => Source.HasScopeAnnotation ? "Yes" : "No";
        public bool CanSelect => Source.Family.Length > 0 && !Source.IsLocked;
        public string SelectionHelp => Source.IsLocked
            ? "This protein belongs to a locked family. Unlock it in Curate families before including it; it stays unticked here."
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
        internal ReviewProteinRow(ReviewProteinInput source, Action<ReviewProteinRow, int> changed, FamilyProfile? coherenceProfile = null, EvidenceManualChoice? manualChoice = null)
        {
            Source = source; selectionChanged = changed;
            CoherenceProfile = coherenceProfile;
            this.manualChoice = manualChoice;
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
            get => selectedCount == 0 ? false : selectedCount == Members.Count(p => p.CanSelect) ? true : null;
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
    public sealed partial class EvidenceReviewViewModel : ScreenViewModel
    {
        private readonly Dictionary<string, ReviewFamilyRow> familyByKey;
        private readonly Dictionary<int, ReviewProteinRow> proteinByIndex;
        private readonly Dictionary<int, EvidenceManualChoice> manualByIndex;
        private int selectedCount;
        private int bulkDepth;
        private readonly HashSet<ReviewFamilyRow> bulkFamilies = new();
        private ReviewFamilyRow? selectedFamily;
        private ReviewProteinRow? selectedProtein;
        private string searchText = "";
        public EvidenceReviewInput Source { get; }
        public IReadOnlyList<ReviewProteinRow> Proteins { get; }
        public IReadOnlyList<ReviewFamilyRow> Families { get; }
        public IReadOnlyList<ReviewProteinRow> Unassigned { get; }
        public IReadOnlyList<ReviewLeftOutInput> LeftOut { get; }
        public string LeftOutSummary => "These marker carriers are not deleted; still quantified in their current or assigned family ("
            + LeftOut.Count.ToString("N0") + " listed).";
        public IReadOnlyList<ArchitectureReviewViewModel> Architectures { get; }
        public ArchitectureReviewViewModel? SelectedArchitecture => Architectures.FirstOrDefault(a => a.Family == SelectedFamily?.Family);
        public IReadOnlyList<ReviewMarkerInput> Markers => Source.Markers;
        public IReadOnlyList<PanelLinkReviewRow> PanelLinks { get; }
        public bool HasPanelRules => Source.PanelLinks != null;
        public bool HasUnappliedPanelLinkChoices => PanelLinks.Any(row => row.HasUnappliedChoice);
        public bool HasInvalidPanelLinkNotes => PanelLinks.Any(row => !row.IsNoteValid);
        public IReadOnlyList<PanelFamilyLinkSelection> CapturePanelLinkChoices() => PanelLinks.Where(row => row.HasUnappliedChoice).Select(row => row.Capture()).ToArray();
        public int SelectedCount => selectedCount;
        public int ManualSelectedCount => manualByIndex.Keys.Count(i => proteinByIndex[i].Selected);
        public bool CanConfirm => selectedCount > 0 && !HasUnappliedPanelLinkChoices && !HasInvalidPanelLinkNotes && PanelLinks.All(row => !row.Source.NeedsConfirmation)
            && Families.Where(f => f.SelectedCount > 0).All(f => !string.IsNullOrWhiteSpace(f.Name))
            && Architectures.Where(a => familyByKey.TryGetValue(a.Family, out var family) && family.SelectedCount > 0).All(a => a.IsValid);
        public string Summary
        {
            get
            {
                int families = Families.Count(f => f.SelectedCount > 0);
                return (Source.SourceLabel.Length == 0 ? "" : Source.SourceLabel + " ") + selectedCount.ToString("N0")
                    + (selectedCount == 1 ? " protein selected in " : " proteins selected in ") + families.ToString("N0")
                    + (families == 1 ? " family. Nothing has changed yet." : " families. Nothing has changed yet.");
            }
        }
        public ReviewFamilyRow? SelectedFamily
        {
            get => selectedFamily;
            set { if (ReferenceEquals(selectedFamily, value)) return; selectedFamily = value; Changed(); RefreshVisibleProteins(); Changed(nameof(SelectedArchitecture)); }
        }
        public string SearchText { get => searchText; set { if (searchText == value) return; searchText = value; Changed(); RefreshVisibleProteins(); } }
        public IEnumerable<ReviewProteinRow> VisibleProteins => (SelectedFamily?.Members ?? Proteins).Where(Matches);
        public ReviewProteinRow? SelectedProtein
        {
            get => selectedProtein;
            set { if (ReferenceEquals(selectedProtein, value)) return; selectedProtein = value; Changed(); Changed(nameof(ProteinDetails)); Changed(nameof(SelectedProfile)); Changed(nameof(CanShowProfileMembers)); }
        }
        private ReviewProteinRow? DetailProtein => SelectedProtein ?? VisibleProteins.FirstOrDefault();
        public FamilyProfile? SelectedProfile => DetailProtein?.CoherenceProfile;
        public bool CanShowProfileMembers => SelectedProfile?.ReviewedMembers.Count > 0;
        public string ProteinDetails
        {
            get
            {
                var row = DetailProtein;
                if (row == null) return "Select a protein to read its full evidence and what its tick does.";
                string family = familyByKey.TryGetValue(row.Family, out var proposed) ? proposed.Name : row.Family;
                string Supplied(string value) => string.IsNullOrWhiteSpace(value) ? "Not supplied" : value;
                return row.Accession + " | Current family ID: " + row.PreviousFamilyId.ToString(CultureInfo.CurrentCulture)
                    + (row.CoherenceProfile == null ? "" : "\n" + CoherenceProfilePresentation.CompleteSources(row.CoherenceProfile))
                    + "\nReason: " + Supplied(row.Reason)
                    + (row.Source.OriginalEvidence.Length == 0 ? "" : "\nAutomatic evidence before the hand choice:\n" + row.Source.OriginalEvidence)
                    + "\nFlags: " + (row.IsFlagged ? row.FlagsHelp : "None supplied")
                    + "\nReviewed relative: " + Supplied(row.Neighbour)
                    + "\nDomains: " + Supplied(row.Domains)
                    + "\nScope words found: " + row.ScopeWordsText
                    + "\n" + (row.Selected ? "Ticked: joins " + family + " on Apply grouping."
                        : "Unticked: stays in its current family and is still quantified.")
                    + (row.Source.IsLocked ? "\n" + row.SelectionHelp : "");
            }
        }

        public EvidenceReviewViewModel(EvidenceReviewInput source)
        {
            ArgumentNullException.ThrowIfNull(source);
            source = source with
            {
                Proteins = source.Proteins.Select(p => p with { Flags = p.Flags.ToArray() }).ToArray(),
                Families = source.Families.ToArray(), Markers = source.Markers.ToArray(), LeftOut = source.LeftOut?.ToArray()
            };
            Source = source;
            PanelLinks = (source.PanelLinks ?? Array.Empty<PanelFamilyLinkProposal>()).Select(link =>
                new PanelLinkReviewRow(link, source.PanelNames, () => { Changed(nameof(HasUnappliedPanelLinkChoices)); Changed(nameof(HasInvalidPanelLinkNotes)); Changed(nameof(CanConfirm)); })).ToArray();
            if (string.IsNullOrEmpty(source.SourceFingerprint)) throw new ArgumentException("The proposal needs a source fingerprint.", nameof(source));
            if (source.Proteins.Select(p => p.ProteinIndex).Distinct().Count() != source.Proteins.Count)
                throw new ArgumentException("A proposal cannot repeat a protein index.", nameof(source));
            familyByKey = new Dictionary<string, ReviewFamilyRow>(StringComparer.Ordinal);
            manualByIndex = (source.ManualChoices ?? Array.Empty<EvidenceManualChoice>()).ToDictionary(c => c.ProteinIndex);
            Proteins = source.Proteins.Select(p => new ReviewProteinRow(p, SelectionChanged,
                source.CoherenceProfiles?.GetValueOrDefault(p.Family), manualByIndex.GetValueOrDefault(p.ProteinIndex))).ToArray();
            proteinByIndex = Proteins.ToDictionary(p => p.ProteinIndex);
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

        private void RefreshVisibleProteins()
        {
            Changed(nameof(VisibleProteins));
            if (SelectedProtein != null && !VisibleProteins.Contains(SelectedProtein)) SelectedProtein = null;
            Changed(nameof(ProteinDetails)); Changed(nameof(SelectedProfile)); Changed(nameof(CanShowProfileMembers));
        }

        private bool Matches(ReviewProteinRow row) => searchText.Length == 0 || row.Identifier.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || row.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase) || row.Reason.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || row.Flags.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || row.Source.Flags.Any(f => f.Contains(searchText, StringComparison.OrdinalIgnoreCase));
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
            Changed(nameof(ProteinDetails));
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
                Architectures.SelectMany(a => a.Capture()).ToArray(), ManualChoices: CaptureManualChoices());
        }
        public IReadOnlyList<EvidenceManualChoice> CaptureManualChoices() => manualByIndex.Values
            .Select(c => c with { Selected = proteinByIndex[c.ProteinIndex].Selected,
                Name = string.IsNullOrWhiteSpace(familyByKey[c.Family].Name) ? c.Name : familyByKey[c.Family].Name.Trim() }).ToArray();
        public EvidenceReviewSelection CaptureReviewState() => new(Source.SourceFingerprint,
            Proteins.Where(p => p.Selected).Select(p => p.ProteinIndex).ToArray(), Families.ToDictionary(f => f.Family, f => f.Name),
            Architectures.SelectMany(a => a.Capture()).ToArray(), ManualChoices: CaptureManualChoices());
        public void RestoreReviewState(EvidenceReviewSelection previous, IReadOnlyCollection<int>? forceOn = null, IReadOnlyCollection<int>? useDefaults = null)
        {
            foreach (var family in Families) if (previous.Names.TryGetValue(family.Family, out string? name)) family.Name = name;
            var selected = previous.ProteinIndices.ToHashSet();
            foreach (var group in Proteins.GroupBy(row => forceOn?.Contains(row.ProteinIndex) == true
                || (useDefaults?.Contains(row.ProteinIndex) == true ? row.Source.SuggestedSelected : selected.Contains(row.ProteinIndex)))) SetSelected(group, group.Key);
            foreach (var choice in previous.ArchitectureChoices ?? Array.Empty<ReviewArchitectureChoice>())
            {
                var group = Architectures.FirstOrDefault(a => a.Family == choice.Family)?.Groups.FirstOrDefault(g => g.Key == choice.ArchitectureKey);
                if (group != null) { group.Name = choice.Name; group.Split = choice.Split; }
            }
        }
    }
}
