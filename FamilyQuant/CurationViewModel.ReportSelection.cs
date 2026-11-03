using FamilyQuant.Report;
using System;

namespace FamilyQuant;

public sealed partial class CurationViewModel
{
    public bool ApplyReportSelection(ReportSelection selection)
        => ApplyReportOptions(selection, Document.ReportThreshold ?? FamilyReportThreshold.DefaultThreshold);

    public bool ApplyReportOptions(ReportSelection selection, double threshold)
    {
        if (!double.IsFinite(threshold) || threshold < 0 || threshold > 1) throw new ArgumentOutOfRangeException(nameof(threshold));
        bool thresholdChanged = (Document.ReportThreshold ?? FamilyReportThreshold.DefaultThreshold) != threshold;
        bool choicesChanged = ReportSelectionStore.Apply(Document, selection);
        if (!thresholdChanged && !choicesChanged) return false;
        if (thresholdChanged) Document.ReportThreshold = threshold == FamilyReportThreshold.DefaultThreshold ? null : threshold;
        NotifyDocumentChanged();
        return true;
    }
}
