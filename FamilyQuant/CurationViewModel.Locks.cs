using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed partial class CurationViewModel
{
    public int UnlockFamilies(IEnumerable<int> families)
    {
        int[] selected = families.Distinct().Where(Document.LockedUserIds.Contains).ToArray();
        if (selected.Length == 0) return 0;
        PushUndo();
        Document.LockedUserIds.ExceptWith(selected);
        NotifyDocumentChanged();
        return selected.Length;
    }
}
