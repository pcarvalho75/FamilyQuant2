using FamilyQuant.DatabaseUpdate;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

/// <summary>What the user chose in the update preview.</summary>
public enum DatabaseUpdateOutcome { Cancel, Apply, CompareAgain }

/// <summary>One line of the counts table: what happened, how many target proteins, and a detail.</summary>
public sealed record DatabaseUpdateCountRow(string What, string Proteins, string Detail);

/// <summary>One protein that is not unchanged, as the preview table shows it.</summary>
public sealed class DatabaseUpdateRowView
{
    public DatabaseUpdateRow Row { get; }
    public string Change { get; }
    public string OldIdentifier => Row.OldIdentifier;
    public string NewIdentifier => Row.NewIdentifier;
    public string Family => Row.FamilyUserId?.ToString(CultureInfo.CurrentCulture) ?? "";
    public string FamilyName => Row.FamilyName;
    public string PlacedBy => Row.PlacedBy;
    public string Identity => Row.PlacedByIdentityPercent?.ToString("0.0", CultureInfo.CurrentCulture) ?? "";
    public string Aligned => Row.PlacedByAlignmentLength?.ToString("N0", CultureInfo.CurrentCulture) ?? "";
    public string Note => Row.Note;
    public bool Decoy => Row.Decoy;

    public DatabaseUpdateRowView(DatabaseUpdateRow row)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        Change = DatabaseUpdateViewModel.KindText(row) + (row.Decoy ? " (decoy)" : row.Contaminant ? " (contaminant)" : "");
    }
}

/// <summary>One choice inside an ambiguous case: a pair, or no pair at all.</summary>
public sealed record DatabaseUpdatePickOption(string Label, DatabaseUpdatePick? Pick);

/// <summary>An ambiguous case with the user's pick.</summary>
public sealed class DatabaseUpdateCaseView : ScreenViewModel
{
    private readonly Action changed;
    private DatabaseUpdatePickOption selected;
    public DatabaseUpdateAmbiguity Ambiguity { get; }
    public int Case => Ambiguity.Case;
    public string FoundBy => DatabaseUpdateViewModel.StepText(Ambiguity.Step);
    public string Reason => Ambiguity.Reason;
    public IReadOnlyList<DatabaseUpdatePickOption> Options { get; }
    public DatabaseUpdatePickOption Selected
    {
        get => selected;
        set { if (value == null || ReferenceEquals(selected, value)) return; selected = value; Changed(); changed(); }
    }

    internal DatabaseUpdateCaseView(DatabaseUpdateAmbiguity ambiguity, IReadOnlyList<DatabaseUpdatePickOption> options,
        DatabaseUpdatePick? current, Action changed)
    {
        Ambiguity = ambiguity; Options = options; this.changed = changed;
        selected = options.FirstOrDefault(o => o.Pick == current) ?? options[0];
    }
}

/// <summary>A family whose shown name would change, with the Keep the old name tick.</summary>
public sealed class DatabaseUpdateNameView : ScreenViewModel
{
    private bool keep;
    public DatabaseUpdateNameChange Change { get; }
    public int Family => Change.FamilyUserId;
    public string OldName => Change.OldName;
    public string NewName => Change.NewName;
    public string OldNameSource => Change.OldNameFromAnnotation ? "An annotation" : "Protein names";
    public string Replaces => Change.DifferingAnnotationMembers.Count == 0 ? ""
        : string.Join(", ", Change.DifferingAnnotationMembers);
    public string Help => Change.DifferingAnnotationMembers.Count > 0
        ? "Keeping the old name replaces the different annotation of the members listed, so it starts unticked."
        : Change.OldNameFromAnnotation ? "The old name came from an annotation, so it is kept by default."
        : "The old name came from protein descriptions, so by default it follows them.";
    public bool Keep { get => keep; set => Set(ref keep, value); }

    internal DatabaseUpdateNameView(DatabaseUpdateNameChange change, bool keep)
    {
        Change = change; this.keep = keep;
    }
}

/// <summary>
/// The Update database preview: the counts, every protein that is not unchanged, the ambiguous cases with
/// the user's picks and the family names with Keep the old name. Formats what the core computed; it decides
/// nothing about matching or placement. Nothing changes in the project until the shell applies the preview.
/// </summary>
public sealed class DatabaseUpdateViewModel : ScreenViewModel
{
    public const string RowFilterAll = "Every change";
    private static readonly string[] Filters = { RowFilterAll, "Added", "Removed", "Sequence or header changed", "Ambiguous" };
    private bool showDecoys;
    private string rowFilter = RowFilterAll;
    private string status = "";

    public DatabaseUpdatePreview Preview { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public IReadOnlyList<DatabaseUpdateCountRow> Counts { get; }
    public string DecoySummary { get; }
    public string Warnings { get; }
    public bool HasWarnings => Warnings.Length > 0;
    public IReadOnlyList<DatabaseUpdateRowView> Rows { get; }
    public ObservableCollection<DatabaseUpdateCaseView> Cases { get; } = new();
    public ObservableCollection<DatabaseUpdateNameView> Names { get; } = new();
    public IReadOnlyList<string> RowFilters => Filters;
    public string CasesHeader => "Ambiguous (" + Cases.Count.ToString("N0", CultureInfo.CurrentCulture) + ")";
    public string NamesHeader => "Family names (" + Names.Count.ToString("N0", CultureInfo.CurrentCulture) + ")";
    public string RowsHeader => "Proteins (" + Rows.Count(r => !r.Decoy).ToString("N0", CultureInfo.CurrentCulture) + ")";
    public bool HasCases => Cases.Count > 0;
    public bool HasNoCases => Cases.Count == 0;
    public bool HasNames => Names.Count > 0;

    public bool ShowDecoys { get => showDecoys; set { if (Set(ref showDecoys, value)) { Changed(nameof(VisibleRows)); Changed(nameof(RowsShown)); } } }
    public string RowFilter { get => rowFilter; set { if (Set(ref rowFilter, value ?? RowFilterAll)) { Changed(nameof(VisibleRows)); Changed(nameof(RowsShown)); } } }
    public IReadOnlyList<DatabaseUpdateRowView> VisibleRows => Rows.Where(r => (showDecoys || !r.Decoy) && Matches(r.Row)).ToArray();
    public string RowsShown => "Showing " + VisibleRows.Count.ToString("N0", CultureInfo.CurrentCulture) + " of "
        + Rows.Count.ToString("N0", CultureInfo.CurrentCulture) + " rows" + (showDecoys ? "." : "; decoy rows are hidden.");

    /// <summary>True when a pick differs from the one this preview was computed with: compare again first.</summary>
    public bool PicksPending => Cases.Any(c => c.Selected.Pick != Current(c.Case));
    public bool CanApply => !PicksPending;
    public string PicksStatus => PicksPending
        ? "A pick changed. Compare again to see where its proteins go; Apply update waits until then."
        : Cases.Count == 0 ? "" : "The preview reflects these picks.";
    public string Status { get => status; private set => Set(ref status, value); }
    public DatabaseUpdateOutcome Outcome { get; private set; } = DatabaseUpdateOutcome.Cancel;

    /// <summary>The picks to compare again with, one per case that has a pair chosen.</summary>
    public IReadOnlyList<DatabaseUpdatePick> SelectedPicks => Cases.Where(c => c.Selected.Pick != null).Select(c => c.Selected.Pick!).ToArray();

    /// <summary>The families whose old name the user keeps.</summary>
    public IReadOnlyList<int> KeptNames => Names.Where(n => n.Keep).Select(n => n.Family).ToArray();

    /// <summary>The user's Keep the old name choices, to carry into a preview computed again.</summary>
    public IReadOnlyDictionary<int, bool> NameChoices => Names.ToDictionary(n => n.Family, n => n.Keep);

    public DatabaseUpdateViewModel(DatabaseUpdatePreview preview, IReadOnlyList<string> projectIdentifiers, string newFileName,
        string projectName, IReadOnlyDictionary<int, bool>? earlierNameChoices = null)
    {
        Preview = preview ?? throw new ArgumentNullException(nameof(preview));
        ArgumentNullException.ThrowIfNull(projectIdentifiers);
        Title = "Update database from " + newFileName;
        Subtitle = "Compared with " + projectName + ". Nothing in the project changes until Apply update.";
        DatabaseUpdateCounts c = preview.Counts;
        var targets = preview.Rows.Where(r => !r.Decoy).ToList();
        var joined = targets.Where(r => r.NewIndex != null && r.OldIndex == null && r.Placement is DatabaseUpdatePlacementKind.NearestKeptNeighbour
            or DatabaseUpdatePlacementKind.FollowedAddedRelative).ToList();
        int joinedFamilies = joined.Select(r => r.FamilyUserId).Distinct().Count();
        int lockedFamilies = joined.Where(r => r.JoinedLockedFamily).Select(r => r.FamilyUserId).Distinct().Count();
        int keptByDefault = preview.NameChanges.Count(n => n.KeepByDefault);
        Counts = new[]
        {
            Line("Unchanged", c.Unchanged, ""),
            Line("Header changed (for example now reviewed, tr to sp)", c.HeaderChanged, "Family, number, name and evidence kept"),
            Line("Same sequence under a new accession", c.NewAccessionSameSequence, "Family kept; evidence starts again"),
            Line("Sequence changed (kept in its family, flagged)", c.SequenceChanged, "Evidence cleared; the note says if it still fits"),
            Line("Replaced by a longer record that contains it", c.ReplacedByLonger, "The new record takes the family"),
            Line("Added, joining an existing family", c.AddedJoinedFamilies, c.AddedJoinedFamilies == 0 ? ""
                : "Into " + Number(joinedFamilies) + (joinedFamilies == 1 ? " family" : " families")
                  + (lockedFamilies == 0 ? "" : ", " + Number(lockedFamilies) + " of them locked (they stay locked)")),
            Line("Added, in new families", c.AddedInNewFamilies, c.NewFamilies == 0 ? "" : "In " + Number(c.NewFamilies) + " new " + (c.NewFamilies == 1 ? "family" : "families")),
            Line("Added, close to several families", c.AddedCloseToSeveralFamilies, c.AddedCloseToSeveralFamilies == 0 ? "" : "Each joins the nearest; families are not merged"),
            Line("Removed", c.Removed, c.FamiliesEmptied == 0 ? "" : Number(c.FamiliesEmptied) + (c.FamiliesEmptied == 1 ? " family emptied" : " families emptied")),
            Line("Ambiguous, needs your choice", c.AmbiguousCases, c.AmbiguousCases == 0 ? ""
                : Plural(c.AmbiguousOldRows, "project row", "project rows") + " and " + Plural(c.AmbiguousNewRecords, "new record", "new records")
                  + "; without a pick they are removed and added", "case", "cases"),
            Line("Family names that would change", preview.NameChanges.Count, preview.NameChanges.Count == 0 ? ""
                : Number(keptByDefault) + " kept by default, " + Number(preview.NameChanges.Count - keptByDefault) + " to decide", "family", "families"),
        };
        DatabaseUpdateCounts d = preview.DecoyCounts;
        int decoyRows = d.Unchanged + d.HeaderChanged + d.SequenceChanged + d.NewAccessionSameSequence + d.ReplacedByLonger + d.Removed + d.Added
            + d.AmbiguousOldRows + d.AmbiguousNewRecords;
        DecoySummary = decoyRows == 0 ? "" : "Decoys follow the same rules: " + Number(d.Added + d.AmbiguousNewRecords) + " added, "
            + Number(d.Removed + d.AmbiguousOldRows) + " removed, " + Number(d.SequenceChanged) + " sequences changed. Tick Show decoy rows to list them.";
        var warnings = preview.Warnings.ToList();
        int newTargets = c.Added + c.AmbiguousNewRecords;
        int sourceTargets = preview.Comparison.Source.Records.Count(r => !SequenceLabels.IsDecoy(r.Identifier));
        if (sourceTargets > 0 && newTargets * 3 > sourceTargets)
            warnings.Add("More than a third of the new database is new to the project. Build families may be the better choice; the update still runs if you apply it.");
        Warnings = string.Join(" ", warnings);
        Rows = preview.Rows.Select(r => new DatabaseUpdateRowView(r)).ToArray();

        var records = preview.Comparison.Source.Records;
        string Old(int i) => i >= 0 && i < projectIdentifiers.Count ? projectIdentifiers[i] : "row " + (i + 1).ToString(CultureInfo.CurrentCulture);
        foreach (DatabaseUpdateAmbiguity ambiguity in preview.Comparison.Ambiguities)
        {
            var options = new List<DatabaseUpdatePickOption> { new("No pair: its project rows are removed and its new records added", null) };
            foreach (int i in ambiguity.OldIndices)
                foreach (int j in ambiguity.NewIndices)
                    options.Add(new(Old(i) + " becomes " + records[j].Identifier, new DatabaseUpdatePick(ambiguity.Case, i, j)));
            Cases.Add(new DatabaseUpdateCaseView(ambiguity, options, Current(ambiguity.Case), PicksChanged));
        }
        foreach (DatabaseUpdateNameChange change in preview.NameChanges)
        {
            bool keep = earlierNameChoices != null && earlierNameChoices.TryGetValue(change.FamilyUserId, out bool earlier) ? earlier : change.KeepByDefault;
            Names.Add(new DatabaseUpdateNameView(change, keep));
        }

        DatabaseUpdateCountRow Line(string what, int count, string detail, string one = "", string many = "") =>
            new(what, Number(count) + (one.Length == 0 ? "" : " " + (count == 1 ? one : many)), detail);
    }

    private DatabaseUpdatePick? Current(int ambiguityCase) => Preview.Comparison.Picks.FirstOrDefault(p => p.Case == ambiguityCase);

    private void PicksChanged()
    {
        Changed(nameof(PicksPending)); Changed(nameof(CanApply)); Changed(nameof(PicksStatus));
    }

    private bool Matches(DatabaseUpdateRow row) => rowFilter switch
    {
        "Added" => row.Kind == DatabaseUpdateKind.Added,
        "Removed" => row.Kind == DatabaseUpdateKind.Removed,
        "Sequence or header changed" => row.Kind is DatabaseUpdateKind.HeaderChanged or DatabaseUpdateKind.SequenceChanged
            or DatabaseUpdateKind.NewAccessionSameSequence or DatabaseUpdateKind.ReplacedByLonger,
        "Ambiguous" => row.Kind == DatabaseUpdateKind.Ambiguous || row.AmbiguousCase != null,
        _ => true,
    };

    /// <summary>The visible rows as CSV, with the columns of the saved change export.</summary>
    public string VisibleCsv() => DatabaseUpdateCsv.Build(VisibleRows.Select(r => r.Row).ToArray());

    /// <summary>Every row, decoys included, as CSV.</summary>
    public string AllCsv() => DatabaseUpdateCsv.Build(Preview.Rows);

    internal void Tell(string message) => Status = message;

    /// <summary>Records the user's decision; the window closes after it.</summary>
    public bool Choose(DatabaseUpdateOutcome outcome)
    {
        if (outcome == DatabaseUpdateOutcome.Apply && !CanApply) { Tell(PicksStatus); return false; }
        if (outcome == DatabaseUpdateOutcome.CompareAgain && !PicksPending) { Tell("No pick changed; the preview already reflects them."); return false; }
        Outcome = outcome;
        return true;
    }

    internal static string KindText(DatabaseUpdateRow row) => row.Kind switch
    {
        DatabaseUpdateKind.HeaderChanged => "Header changed",
        DatabaseUpdateKind.SequenceChanged => "Sequence changed",
        DatabaseUpdateKind.NewAccessionSameSequence => "Same sequence, new accession",
        DatabaseUpdateKind.ReplacedByLonger => "Replaced by a longer record",
        DatabaseUpdateKind.Removed => "Removed",
        DatabaseUpdateKind.Added => row.Placement == DatabaseUpdatePlacementKind.NewFamily ? "Added, new family" : "Added",
        DatabaseUpdateKind.Ambiguous => "Ambiguous, case " + (row.AmbiguousCase ?? 0).ToString(CultureInfo.CurrentCulture),
        _ => row.Picked ? "Unchanged, picked" : "Unchanged",
    };

    internal static string StepText(DatabaseUpdateStep step) => step switch
    {
        DatabaseUpdateStep.Key => "Same accession",
        DatabaseUpdateStep.RepeatedKey => "Repeated accession",
        DatabaseUpdateStep.SameSequence => "Same sequence",
        _ => "Contained in a longer record",
    };

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string Plural(int value, string one, string many) => Number(value) + " " + (value == 1 ? one : many);
}
