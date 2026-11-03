using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant;

public sealed record PanelLinkOption(string? Key, string DisplayName);

/// <summary>Display and explicit user choices only. The engine supplies every candidate and support entry.</summary>
public sealed class PanelLinkReviewRow : ScreenViewModel
{
    private readonly Action changed;
    private readonly bool hadChoice;
    private readonly string originalNote;
    private PanelLinkOption? selectedOption;
    private string note;
    public PanelFamilyLinkProposal Source { get; }
    public string SourceFamily => Source.SourceFamily.Length > 0 ? Source.SourceFamily : "Domain combination";
    public string Architecture => Source.ArchitectureKey.Length == 0 ? "Any observed combination"
        : string.Join(" + ", JsonSerializer.Deserialize<string[]>(Source.ArchitectureKey) ?? Array.Empty<string>());
    public string SupportingAccessions => string.Join(", ", Source.SupportingAccessions);
    public string Candidates => string.Join(", ", Options.Where(o => o.Key != null).Select(o => o.DisplayName));
    public string State => !IsNoteValid ? "Enter a short single-line note" : HasUnappliedChoice ? "Ready to update" : Source.IsConfirmed ? "Confirmed"
        : Source.NeedsConfirmation ? "Choose a family or leave outside" : Source.CandidateFamilyKeys.Count == 1 ? "One supported family" : "No supported link";
    public IReadOnlyList<PanelLinkOption> Options { get; }
    public bool HasUnappliedChoice => selectedOption != null && (!hadChoice || selectedOption.Key != Source.SelectedFamilyKey || note != originalNote);
    public bool IsNoteValid => Source.IsConfirmed && note == originalNote && selectedOption?.Key == Source.SelectedFamilyKey
        || PanelEvidenceClassifier.IsValidLinkNote(note);
    public string Note
    {
        get => note;
        set
        {
            value ??= string.Empty;
            if (note == value) return;
            note = value; Changed(); Changed(nameof(IsNoteValid)); Changed(nameof(HasUnappliedChoice)); Changed(nameof(State)); changed();
        }
    }
    public PanelLinkOption? SelectedOption
    {
        get => selectedOption;
        set
        {
            if (value == null && hadChoice) return;
            if (ReferenceEquals(selectedOption, value)) return;
            if (value != null && !Options.Contains(value)) throw new ArgumentException("Choose an evidence-derived option.", nameof(value));
            selectedOption = value; Changed(); Changed(nameof(IsNoteValid)); Changed(nameof(HasUnappliedChoice)); Changed(nameof(State)); changed();
        }
    }

    public PanelLinkReviewRow(PanelFamilyLinkProposal source, IReadOnlyDictionary<string, string>? names, Action changed)
    {
        Source = source; this.changed = changed;
        originalNote = note = source.SourceNote ?? string.Empty;
        Options = source.CandidateFamilyKeys.Select(key => new PanelLinkOption(key,
            (names?.GetValueOrDefault(key) is { Length: > 0 } name ? name : key) + " (" + key + ")"))
            .Append(new PanelLinkOption(null, "Leave outside panel")).ToArray();
        hadChoice = source.IsConfirmed || source.CandidateFamilyKeys.Count == 1;
        if (hadChoice) selectedOption = Options.First(option => option.Key == source.SelectedFamilyKey);
    }

    public PanelFamilyLinkSelection Capture() => HasUnappliedChoice
        ? IsNoteValid ? new() { LinkKey = Source.Key, PanelFamilyKey = SelectedOption!.Key, Source = Note }
            : throw new InvalidOperationException("Enter a short single-line note before updating links.")
        : throw new InvalidOperationException("This row has no unapplied choice.");
}
