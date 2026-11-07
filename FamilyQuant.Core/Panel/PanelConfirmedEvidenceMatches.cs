using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.Panels;

public enum PanelEvidenceMatchKind { UniProtFamily, Keyword, TissuePhrase, Pfam }

public sealed record PanelEvidenceChoice(PanelEvidenceMatchKind Kind, string Value);

public sealed record PanelConfirmedEvidenceMatches
{
    public bool UserConfirmed { get; init; }
    public IReadOnlyList<string> NamesUsed { get; init; } = new List<string>();
    public IReadOnlyList<PanelEvidenceChoice> Choices { get; init; } = new List<PanelEvidenceChoice>();
}

public sealed partial class FamilyPanel
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, PanelConfirmedEvidenceMatches>? ConfirmedEvidenceMatches { get; set; }
}
