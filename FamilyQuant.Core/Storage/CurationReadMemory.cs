using System;
using System.Runtime;

namespace FamilyQuant;

/// <summary>Reclaims large temporary curation reads before the next document copy is allocated.</summary>
public static class CurationReadMemory
{
    public static void ReleaseIfLarge(long allocatedBytes)
    {
        if (allocatedBytes < 128L * 1024 * 1024) return;
        // A read-back graph or replaced document is already out of scope. Compact both heaps so
        // its freed strings do not keep several document-sized segments committed through export.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.Collect();
    }
}
