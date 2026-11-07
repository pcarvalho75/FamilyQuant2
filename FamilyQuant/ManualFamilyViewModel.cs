using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed record ManualFamilyOption(string Key, string Name, bool IsNew = false)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Key + " (name missing)" : Name;
}

public sealed class ManualFamilyViewModel : ScreenViewModel
{
    private ManualFamilyOption? selectedOption;
    private string newName = "", note = "";
    public IReadOnlyList<ReviewProteinRow> Proteins { get; }
    public IReadOnlyList<ManualFamilyOption> Options { get; }
    public string Heading => Proteins.Count.ToString("N0") + (Proteins.Count == 1 ? " highlighted protein" : " highlighted proteins");
    public ManualFamilyOption? SelectedOption { get => selectedOption; set { selectedOption = value; Changed(); Changed(nameof(IsNewFamily)); NotifyChoice(); } }
    public bool IsNewFamily => SelectedOption?.IsNew == true;
    public string NewName { get => newName; set { newName = value; Changed(); NotifyChoice(); } }
    public string Note { get => note; set { note = value; Changed(); NotifyChoice(); } }
    public string Validation => Proteins.Count == 0 ? "Highlight proteins before choosing a family."
        : Proteins.Any(p => p.Source.IsLocked) ? "Locked proteins cannot be added; unlock their current family in Curate families first."
        : SelectedOption == null ? "Choose a proposed family or New family."
        : !IsNewFamily && string.IsNullOrWhiteSpace(SelectedOption.Name) ? "Name this proposed family in the review before adding proteins."
        : IsNewFamily && string.IsNullOrWhiteSpace(NewName) ? "Name the new family."
        : IsNewFamily && Options.Any(o => !o.IsNew && o.Name.Equals(NewName.Trim(), StringComparison.OrdinalIgnoreCase))
            ? "This name is already proposed; choose that family from the list."
        : !ManualFamilyChoices.ValidNote(Note) ? "Use one line of at most 512 characters for the optional note."
        : "These proteins will be ticked as Added by hand; Apply grouping commits the choice.";
    public bool CanConfirm => Proteins.Count > 0 && !Proteins.Any(p => p.Source.IsLocked) && SelectedOption != null && (IsNewFamily || !string.IsNullOrWhiteSpace(SelectedOption.Name))
        && (!IsNewFamily || !string.IsNullOrWhiteSpace(NewName) && !Options.Any(o => !o.IsNew && o.Name.Equals(NewName.Trim(), StringComparison.OrdinalIgnoreCase)))
        && ManualFamilyChoices.ValidNote(Note);
    public string PrimaryLabel => IsNewFamily ? "Create family and add" : "Add to family";
    public ManualFamilyViewModel(EvidenceReviewViewModel proposal, IReadOnlyList<ReviewProteinRow> proteins)
    {
        Proteins = proteins.ToArray();
        Options = proposal.Families.Select(f => new ManualFamilyOption(f.Family, f.Name)).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .Append(new ManualFamilyOption("", "New family", true)).ToArray();
        selectedOption = Options.FirstOrDefault(o => o.Key == proposal.SelectedFamily?.Family) ?? Options[0];
    }
    public EvidenceManualRequest Capture()
    {
        if (!CanConfirm) throw new InvalidOperationException(Validation);
        return new(Proteins.Select(p => p.ProteinIndex).ToArray(), SelectedOption!.Key,
            IsNewFamily ? NewName.Trim() : SelectedOption.Name.Trim(), IsNewFamily, string.IsNullOrWhiteSpace(Note) ? null : Note.Trim());
    }
    private void NotifyChoice() { Changed(nameof(Validation)); Changed(nameof(CanConfirm)); Changed(nameof(PrimaryLabel)); }
}
