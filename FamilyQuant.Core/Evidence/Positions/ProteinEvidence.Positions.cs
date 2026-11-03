using System.Text.Json.Serialization;

namespace FamilyQuant.Evidence;

public sealed partial class ProteinEvidence
{
    /// <summary>Persisted canonical positions with source sequence identity and independent provenance
    /// for InterPro/Pfam and UniProt. Null is an older or not-yet-fetched evidence record.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Positions.ProteinPositionEvidence? Positions { get; set; }
}
