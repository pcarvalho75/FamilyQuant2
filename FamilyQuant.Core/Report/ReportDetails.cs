using System.Text.Json.Serialization;

namespace FamilyQuant.Report;

/// <summary>
/// User-authored report text, independent of measured results. Empty fields are allowed.
/// WrittenInterpretation is plain multi-paragraph text; consumers preserve its paragraph breaks.
/// The editor stores this value in the project and report writers print each field in its own place.
/// </summary>
public sealed record ReportDetails
{
    public string Title { get; init; } = string.Empty;
    public string Authors { get; init; } = string.Empty;
    public string SampleDescription { get; init; } = string.Empty;
    public string Workflow { get; init; } = string.Empty;
    public string DatabaseKind { get; init; } = string.Empty;
    public string WrittenInterpretation { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiInterpretationProvenance? AiInterpretation { get; init; }
}

public sealed record AiInterpretationProvenance(string Model, string InputSha256);
