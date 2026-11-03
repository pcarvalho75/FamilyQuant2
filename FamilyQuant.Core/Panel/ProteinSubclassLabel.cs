using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.Panels
{
    public enum ProteinSubclassStatus { Unknown, Assigned }

    /// <summary>One row per target protein, including proteins outside the panel and unresolved parts.
    /// SubclassKey is the reported part after following ReportAsSubclassKey; DefinitionKey is the
    /// original matching rule. Null keys and Unknown are explicit, never omitted target rows.</summary>
    public sealed class ProteinSubclassLabel
    {
        public int ProteinIndex { get; set; }
        public string Identifier { get; set; } = string.Empty;
        public int? FamilyUserId { get; set; }
        public string? PanelFamilyKey { get; set; }
        public string? SubclassKey { get; set; }
        public string? DefinitionKey { get; set; }
        public ProteinSubclassStatus Status { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Panels.ProteinSubclassLabel>? ProteinSubclassLabels { get; set; }
    }
}

namespace FamilyQuant.Evidence
{
    public sealed partial class EvidenceRecipe
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Panels.ProteinSubclassLabel>? ProteinSubclassLabels { get; set; }
    }
}
