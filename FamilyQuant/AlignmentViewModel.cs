using FamilyQuant.Evidence.Alignment;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

/// <summary>One sequence the alignment window offers, with its tick and, once aligned, its identity to
/// the first protein.</summary>
public sealed class AlignmentCandidateRow : ScreenViewModel
{
    private readonly Action<AlignmentCandidateRow> tickChanged;
    private bool ticked;
    private string identity = "";

    public AlignmentCandidate Candidate { get; }
    public bool IsFirst { get; }
    public bool CanTick => !IsFirst;
    public string Name => Candidate.Label;
    public string Source => Candidate.OriginText;
    public string Family => Candidate.Family;
    public int Length => Candidate.Length;
    public int Cysteines => Candidate.Cysteines;
    public string Notes => string.Join("; ", new[]
    {
        IsFirst ? "First: the others are aligned to it" : "",
        Candidate.IsNeighbour ? "Nearest reviewed protein in the proposal" : "",
        Candidate.Origin == AlignmentOrigin.Highlighted && Candidate.Reviewed ? "Reviewed" : "",
        Candidate.Origin == AlignmentOrigin.Highlighted && !Candidate.Reviewed ? "Not reviewed" : "",
        Candidate.Fragment ? "Fragment" : "",
        Candidate.DomainNote,
    }.Where(n => n.Length > 0));
    public string Details => Candidate.Identifier.Length > 0 && Candidate.Identifier != Candidate.Label
        ? Candidate.Identifier + "\n" + Notes : Notes;

    /// <summary>Identity to the first protein after the last alignment; empty while unticked.</summary>
    public string Identity { get => identity; internal set => Set(ref identity, value); }

    public bool Ticked
    {
        get => ticked;
        set
        {
            if (IsFirst && !value) { Changed(); return; }
            if (ticked == value) return;
            ticked = value; Changed(); tickChanged(this);
        }
    }

    internal void Untick() { if (ticked) { ticked = false; Changed(nameof(Ticked)); } }

    internal AlignmentCandidateRow(AlignmentCandidate candidate, bool isFirst, Action<AlignmentCandidateRow> changed)
    {
        Candidate = candidate; IsFirst = isFirst; tickChanged = changed;
        ticked = isFirst || candidate.Preselected;
    }
}

/// <summary>
/// The Align selected window: the first highlighted protein, its nearest reviewed protein and reviewed
/// members of its family, aligned by <see cref="ProteinAlignment.CenterStar"/> whenever a tick changes.
/// Read only: nothing in the proposal, the ticks of the review or the file changes.
/// </summary>
public sealed class AlignmentViewModel : ScreenViewModel
{
    public const string IdentityNote = "Identity to the first protein counts identical columns over the columns between the first and last residue "
        + "the two share; gaps inside count, overhangs at either end do not. The proposal's identity comes from its own search and can differ slightly.";

    private MultipleAlignment? alignment;
    private string status = "";
    private bool realigning;

    public AlignmentCandidateSet Source { get; }
    public ObservableCollection<AlignmentCandidateRow> Candidates { get; } = new();
    public string Title { get; }
    public string Families { get; }
    public string Notes { get; }
    public bool HasNotes => Notes.Length > 0;
    public string Method => ProteinAlignment.Method;

    public MultipleAlignment? Alignment { get => alignment; private set { if (Set(ref alignment, value)) { Changed(nameof(Summary)); Changed(nameof(CanCopy)); } } }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool CanCopy => Alignment != null;
    public int TickedCount => Candidates.Count(c => c.Ticked);

    public string Summary => Alignment == null ? "No alignment."
        : Count(Alignment.Rows.Count, "sequence") + ", " + Count(Alignment.Columns, "column") + "; "
          + Count(ProteinAlignment.CysteineColumns(Alignment).Count, "column") + " with a cysteine";

    public AlignmentViewModel(AlignmentCandidateSet source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (source.Candidates.Count == 0) throw new ArgumentException("There is nothing to align.", nameof(source));
        for (int i = 0; i < source.Candidates.Count; i++)
            Candidates.Add(new AlignmentCandidateRow(source.Candidates[i], i == 0, TickChanged));
        Title = "Align " + source.Candidates[0].Label + " with its reviewed relatives";
        Families = source.Families.Count == 0 ? "No family is known for the highlighted proteins."
            : (source.Families.Count == 1 ? "Family: " : "Families: ") + string.Join(", ", source.Families);
        Notes = string.Join(" ", source.Notes);
        Realign();
    }

    /// <summary>The aligned sequences as FASTA with gaps, for another program.</summary>
    public string FastaText => Alignment?.ToFasta() ?? string.Empty;

    /// <summary>The alignment as blocks of 60 columns with names and residue numbers, for reading.</summary>
    public string AlignedText => Alignment?.ToAlignedText() ?? string.Empty;

    private void TickChanged(AlignmentCandidateRow row)
    {
        if (realigning) return;
        if (row.Ticked && TickedCount > ProteinAlignment.MaximumSequences)
        {
            row.Untick();
            Status = "An alignment holds at most " + ProteinAlignment.MaximumSequences.ToString(CultureInfo.CurrentCulture)
                + " sequences. Untick one before adding another.";
            return;
        }
        Realign();
    }

    /// <summary>Aligns the ticked sequences in list order, the first protein first.</summary>
    internal void Realign()
    {
        realigning = true;
        try
        {
            var ticked = Candidates.Where(c => c.Ticked).ToList();
            Alignment = ProteinAlignment.CenterStar(ticked.Select(c => c.Candidate.ToSequence()).ToArray());
            var identities = Alignment.Rows.Skip(1).Select(r => r.IdentityPercent).ToList();
            foreach (AlignmentCandidateRow row in Candidates) row.Identity = "";
            ticked[0].Identity = "First";
            for (int i = 1; i < ticked.Count; i++)
                ticked[i].Identity = identities[i - 1] is double percent
                    ? percent.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "No overlap";
            Status = ticked.Count == 1 ? "Tick a relative to align it with the first protein." : "";
        }
        catch (ArgumentException ex)
        {
            Alignment = null;
            Status = ex.Message;
        }
        finally { realigning = false; }
    }

    /// <summary>Shows a short message under the list, for example after a copy.</summary>
    internal void Tell(string message) => Status = message;

    private static string Count(int value, string noun) =>
        value.ToString("N0", CultureInfo.CurrentCulture) + " " + noun + (value == 1 ? "" : "s");
}
