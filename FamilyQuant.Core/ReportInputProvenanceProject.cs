using FamilyQuant.Report;
using System.Text.Json.Serialization;

namespace FamilyQuant;

public partial class FamilyClusterFile
{
    /// <summary>Read-only input facts for report methods, independent of user-authored text.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReportInputProvenance? ReportInputProvenance { get; set; }
}
