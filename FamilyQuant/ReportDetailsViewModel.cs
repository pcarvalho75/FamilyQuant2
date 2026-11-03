using FamilyQuant.Report;
using System;

namespace FamilyQuant;

/// <summary>A private draft. Only Save details invokes the project's undoable apply action.</summary>
public sealed class ReportDetailsViewModel
{
    private readonly ReportDetails original;
    private readonly Action<ReportDetails> apply;
    public string Title { get; set; }
    public string Authors { get; set; }
    public string SampleDescription { get; set; }
    public string Workflow { get; set; }
    public string DatabaseKind { get; set; }
    public string WrittenInterpretation { get; set; }
    public string InputProvenanceText { get; }

    public ReportDetailsViewModel(ReportDetails? details, Action<ReportDetails> apply, ReportInputProvenance? provenance = null)
    {
        original = details ?? new();
        this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
        Title = original.Title; Authors = original.Authors; SampleDescription = original.SampleDescription;
        Workflow = original.Workflow; DatabaseKind = original.DatabaseKind; WrittenInterpretation = original.WrittenInterpretation;
        InputProvenanceText = ReportInputCapture.Describe(provenance);
    }

    public bool Save()
    {
        var details = new ReportDetails
        {
            Title = Title ?? "", Authors = Authors ?? "", SampleDescription = SampleDescription ?? "",
            Workflow = Workflow ?? "", DatabaseKind = DatabaseKind ?? "", WrittenInterpretation = WrittenInterpretation ?? ""
        };
        if (details == original) return false;
        apply(details);
        return true;
    }
}
