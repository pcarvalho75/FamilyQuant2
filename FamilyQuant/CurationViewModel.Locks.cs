using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed partial class CurationViewModel
{
    public int UnlockFamilies(IEnumerable<int> families)
    {
        int[] selected = families.Distinct().Where(Document.LockedUserIds.Contains).ToArray();
        if (selected.Length == 0) return 0;
        var undo = GroupingSnapshot.CaptureEvidenceIdentityUnlock(Document);
        Evidence.EvidenceApply.PreserveIdentitiesForUnlock(Document, selected);
        Document.LockedUserIds.ExceptWith(selected);
        UndoStack.Add(undo);
        if (UndoStack.Count > 50) UndoStack.RemoveAt(0);
        NotifyDocumentChanged();
        return selected.Length;
    }
}
