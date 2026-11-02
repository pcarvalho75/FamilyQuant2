using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FamilyQuant.Operations;

namespace FamilyQuant;

public sealed partial class CurationViewModel : ScreenViewModel
{
    public FamilyClusterFile Document { get; }
    public CurationQuantitationViewModel Quantitation { get; }
    public List<GroupingSnapshot> UndoStack { get; } = new();
    public IReadOnlyList<FastaItemExtended> Rows { get; private set; } = Array.Empty<FastaItemExtended>();
    public string SearchText { get; set; } = "";
    public bool OnlyQuantified { get; set; }
    public bool HideDecoys { get; set; } = true;
    public bool HideContaminants { get; set; }
    public string? SortMemberPath { get; set; }
    public bool SortDescending { get; set; }
    public string KeywordText { get; private set; } = "";
    public string KeywordExclude { get; private set; } = "";
    public KeywordMatch KeywordMatch { get; private set; } = KeywordMatch.All;
    public string FamilyCounter => ReportView.FamilyCounter(FamilyFilter.CountFamilies(Rows), FamilyFilter.CountFamilies(Document.MyExtendedFastaItems));
    public string LockSummary => Document.LockedUserIds.Count == 0 ? "No families locked" : Document.LockedUserIds.Count.ToString("N0", CultureInfo.CurrentCulture)
        + (Document.LockedUserIds.Count == 1 ? " family locked" : " families locked");
    public bool CanUndo => UndoStack.Count > 0;
    public event EventHandler? DocumentChanged;
    public CurationViewModel(FamilyClusterFile document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Quantitation = new(document);
    }
    public void RefreshDocument()
    {
        // Stored evidence recipes address original positions. Legacy files retain the stable physical ordering.
        if (Document.EvidenceRecipe == null)
            Document.MyExtendedFastaItems = Document.MyExtendedFastaItems.OrderBy(p => p.UserID).ToList();
        RefreshRows();
    }
    public void RefreshRows()
    {
        Quantitation.RefreshFullXicShares();
        Rows = ReportView.Sort(FamilyFilter.Apply(Document.MyExtendedFastaItems.OrderBy(p => p.UserID),
            SearchText, OnlyQuantified, HideDecoys, HideContaminants), SortMemberPath, SortDescending);
        Changed(nameof(Rows)); Changed(nameof(FamilyCounter)); Changed(nameof(LockSummary)); Changed(nameof(CanUndo));
    }
    public void PushUndo(IEnumerable<FastaItemExtended>? annotations = null)
    {
        UndoStack.Add(annotations == null ? GroupingSnapshot.Capture(Document) : GroupingSnapshot.Capture(Document, annotations));
        if (UndoStack.Count > 50) UndoStack.RemoveAt(0);
        Changed(nameof(CanUndo));
    }
    public void Undo()
    {
        if (!CanUndo) return;
        var snapshot = UndoStack[^1]; UndoStack.RemoveAt(UndoStack.Count - 1);
        snapshot.Restore(Document);
        NotifyDocumentChanged();
    }
    public void RecordFamilyEdit(FastaItemExtended protein, int family)
    {
        if (family < 0) throw new ArgumentOutOfRangeException(nameof(family));
        if (protein.UserID == family) return;
        PushUndo(); Document.LockedUserIds.Add(protein.UserID); Document.LockedUserIds.Add(family);
    }
    public void NotifyDocumentChanged()
    {
        RefreshDocument();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }
    public (bool Changed, string Status) ApplyBatch(IReadOnlyList<FastaItemExtended> selected, string idText, string annotation)
    {
        idText = idText.Trim(); annotation = annotation.Trim(); int? family = null;
        if (idText.Length > 0)
        {
            if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < 0)
                return (false, "Nothing was changed: \"" + idText + "\" is not a family number. Next step: type a whole number, 0 or higher, or leave the box empty.");
            family = number;
        }
        if (family == null && annotation.Length == 0) return (false, "Nothing was changed: type a User ID, an annotation, or both.");
        if (selected.Count == 0) return (false, "Nothing was changed: no row is selected. Next step: select rows in the table, then apply.");
        PushUndo(annotation.Length > 0 ? selected : Array.Empty<FastaItemExtended>());
        var outcome = BatchCuration.Apply(Document, selected, family, annotation.Length > 0 ? annotation : null);
        if (outcome.Moved == 0 && outcome.Annotated == 0)
        {
            UndoStack.RemoveAt(UndoStack.Count - 1); Changed(nameof(CanUndo));
            return (false, "Nothing was changed: the " + selected.Count.ToString("N0", CultureInfo.CurrentCulture) + " selected row(s) already carry that User ID and annotation.");
        }
        NotifyDocumentChanged();
        var parts = new List<string>();
        if (outcome.Moved > 0) parts.Add(outcome.Moved.ToString("N0", CultureInfo.CurrentCulture) + " row(s) moved to family " + family!.Value.ToString(CultureInfo.CurrentCulture) + " (now locked)");
        if (outcome.Annotated > 0) parts.Add(outcome.Annotated.ToString("N0", CultureInfo.CurrentCulture) + " row(s) annotated \"" + annotation + "\"");
        return (true, string.Join("; ", parts) + ". Undo steps it back.");
    }
    public SuperClusterPreview PrepareKeywordReview(IReadOnlyList<string> ignoredWords)
    {
        var scope = WordCluster.ScopeItems(Document.MyExtendedFastaItems, SearchText);
        var families = string.IsNullOrWhiteSpace(SearchText) ? null : new HashSet<int>(scope.Select(p => p.UserID));
        return new SuperClusterPreview(Document.MyExtendedFastaItems.ToList(), families, KeywordText, KeywordExclude,
            KeywordMatch, ignoredWords, new HashSet<int>(Document.LockedUserIds));
    }
    public void ApplyKeywordReview(SuperClusterPreview preview)
    {
        PushUndo();
        var join = preview.Join(Document.MyExtendedFastaItems);
        Document.IsKeywordSuperCluster = true;
        WordCluster.SyncUnlockedIds(join.Moved, Document.LockedUserIds);
        if (join.SplitFamilies) Document.LockedUserIds.Add(join.Family);
        KeywordText = preview.KeywordsText; KeywordExclude = preview.ExcludeText; KeywordMatch = preview.Match;
        NotifyDocumentChanged();
    }
    public void ApplyWordGrouping(IReadOnlyList<List<int>> groups, IReadOnlyCollection<FastaItemExtended>? scope, double threshold)
    {
        PushUndo();
        var mapping = WordCluster.BuildStableMapping(groups.ToList());
        foreach (var protein in Document.MyExtendedFastaItems)
            if (mapping.TryGetValue(protein.UserID, out int family)) protein.UserID = family;
        Document.WordSimilarity = threshold; Document.IsKeywordSuperCluster = false;
        WordCluster.SyncUnlockedIds(scope ?? Document.MyExtendedFastaItems, Document.LockedUserIds);
        NotifyDocumentChanged();
    }

    public async Task<string> ReclusterByWordsAsync(double threshold, IReadOnlyList<string> ignored,
        CancellationToken cancellation = default, Action<Action>? publish = null)
    {
        var scope = WordCluster.ScopeItems(Document.MyExtendedFastaItems, SearchText);
        bool scoped = !string.IsNullOrWhiteSpace(SearchText);
        int before = FamilyFilter.CountFamilies(scope);
        var words = new WordCluster(scope, ignored.ToList());
        var locks = new HashSet<int>(Document.LockedUserIds);
        var groups = await Task.Run(() => words.GroupByWords(threshold, locks), cancellation);
        cancellation.ThrowIfCancellationRequested();
        void Apply() => ApplyWordGrouping(groups, scoped ? scope : null, threshold);
        if (publish == null) Apply(); else publish(Apply);
        return before.ToString("N0", CultureInfo.CurrentCulture) + (scoped ? " matching families became " : " families became ")
            + groups.Count.ToString("N0", CultureInfo.CurrentCulture) + ". Undo reverses the grouping."
            + (scoped ? " Hidden families kept their numbers." : "");
    }

    public Task<string> AlignAsync(IReadOnlyList<FastaItemExtended> proteins, OperationContext operation) =>
        Task.Run(() => USearchTools.Align(proteins.ToList(), operation), operation.CancellationToken);
}
