using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.Evidence
{
    public sealed class EvidenceFamilyMembership
    {
        public string Family { get; set; } = string.Empty;
        public string Subfamily { get; set; } = string.Empty;
        public string Hierarchy { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
    }

    public sealed partial class ProteinEvidence
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<EvidenceFamilyMembership>? FamilyMemberships { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? SimilarityAnnotations { get; set; }
    }
}
