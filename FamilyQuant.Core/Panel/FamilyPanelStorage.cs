using System.Text.Json.Serialization;

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        /// <summary>The editable user family panel. Stored in fqproj curation.json; absent in old files.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Panels.FamilyPanel? FamilyPanel { get; set; }
    }
}

namespace FamilyQuant.Evidence
{
    public sealed partial class EvidenceRecipe
    {
        /// <summary>A snapshot of the user panel used for this recipe, including import decisions.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Panels.FamilyPanel? FamilyPanel { get; set; }
    }
}
