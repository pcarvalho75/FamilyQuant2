using FamilyQuant.Report;
using System;

namespace FamilyQuant;

public sealed partial class CurationViewModel
{
    public bool ApplyReportDetails(ReportDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if ((Document.ReportDetails ?? new ReportDetails()) == details) return false;
        UndoStack.Add(GroupingSnapshot.CaptureReportDetails(Document));
        if (UndoStack.Count > 50) UndoStack.RemoveAt(0);
        Document.ReportDetails = details;
        NotifyDocumentChanged();
        return true;
    }
}
