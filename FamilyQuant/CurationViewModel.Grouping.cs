using FamilyQuant.Evidence;
using System;
using System.IO;

namespace FamilyQuant;

public sealed partial class CurationViewModel
{
    private EvidenceProvenance? checkedGroupingSource;
    public bool HasStoredEvidence => Document.EvidenceSource is { Provider: "UniProt" };
    public bool HasAppliedEvidenceGrouping => Document.EvidenceRecipe != null;
    public bool HasStoredFamilyWorld { get; private set; }
    public bool HasComputedFullXic { get; private set; }
    public string EvidenceStepStatus => !HasStoredEvidence ? "Not yet"
        : string.IsNullOrWhiteSpace(Document.EvidenceSource!.Release) ? "Downloaded (UniProt release not recorded)"
        : "Downloaded (UniProt " + Document.EvidenceSource.Release.Trim() + ")";
    public string EvidenceGroupingStepStatus => HasAppliedEvidenceGrouping ? "Applied" : "Not yet";
    public string FamilyWorldStepStatus => HasStoredFamilyWorld ? "Downloaded" : "Not yet";
    public string FullXicStepStatus => HasComputedFullXic ? "Computed" : "Not yet";
    public int NextGroupingStep => !HasStoredEvidence ? 1 : !HasAppliedEvidenceGrouping ? 2
        : !HasStoredFamilyWorld ? 3 : !HasComputedFullXic ? 4 : 0;

    public void SetQuantManifest(QuantManifest? manifest)
    {
        // Saving an uncomputed project also creates a manifest. Only a completed run records TICs,
        // including a completed extraction that found no quantifiable peptides.
        bool computed = manifest?.PerRunTic.Count > 0;
        if (HasComputedFullXic == computed) return;
        HasComputedFullXic = computed;
        Changed(nameof(HasComputedFullXic));
        Changed(nameof(FullXicStepStatus));
        Changed(nameof(NextGroupingStep));
    }

    private void RefreshGroupingWorkflow()
    {
        // Fetch and Undo replace the stored source. Validate once per source, rather than hashing
        // the entire family world whenever a display filter or family selection changes.
        if (!ReferenceEquals(checkedGroupingSource, Document.EvidenceSource))
        {
            checkedGroupingSource = Document.EvidenceSource;
            HasStoredFamilyWorld = false;
            if (Document.EvidenceSource?.FamilyWorld != null)
            {
                try { _ = FamilyWorldFetcher.Stored(Document); HasStoredFamilyWorld = true; }
                catch (InvalidDataException) { }
                catch (InvalidOperationException) { }
            }
        }
        Changed(nameof(HasStoredEvidence));
        Changed(nameof(HasAppliedEvidenceGrouping));
        Changed(nameof(HasStoredFamilyWorld));
        Changed(nameof(EvidenceStepStatus));
        Changed(nameof(EvidenceGroupingStepStatus));
        Changed(nameof(FamilyWorldStepStatus));
        Changed(nameof(NextGroupingStep));
    }
}
