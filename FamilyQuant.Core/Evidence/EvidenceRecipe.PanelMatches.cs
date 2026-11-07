using System.Text.Json.Serialization;

namespace FamilyQuant.Evidence;

public sealed partial class EvidenceRecipe
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceConfirmedMatchesFingerprint { get; set; }
}
