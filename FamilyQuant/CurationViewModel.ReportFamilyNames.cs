using System;

namespace FamilyQuant;

public sealed partial class CurationViewModel
{
    public bool ApplyReportFamilyNames(ReportFamilyNameChoice choice)
    {
        if (!Enum.IsDefined(choice)) throw new ArgumentOutOfRangeException(nameof(choice));
        if (Document.ReportFamilyNames == choice) return false;
        Document.ReportFamilyNames = choice;
        NotifyDocumentChanged();
        return true;
    }
}
