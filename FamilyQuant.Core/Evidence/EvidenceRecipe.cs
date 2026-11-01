using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.Evidence
{
    public sealed partial class EvidenceRecipe
    {
        public List<EvidenceArchitectureChoice> ArchitectureChoices { get; set; } = new List<EvidenceArchitectureChoice>();
    }
}

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Evidence.EvidenceRecipe? EvidenceRecipe { get; set; }
    }
}
