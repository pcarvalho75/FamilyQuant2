using System.Collections.Generic;

namespace FamilyQuant.Panels;

/// <summary>A user-confirmed link, including an explicit decision to leave a context outside
/// the panel (null family key). Source contexts and candidate families are derived from evidence.</summary>
public sealed class FamilyPanelEvidenceLink
{
    public string SourceFamily { get; set; } = string.Empty;
    public string ArchitectureKey { get; set; } = string.Empty;
    public string? PanelFamilyKey { get; set; }
    public string PanelDefinitionFingerprint { get; set; } = string.Empty;
    public List<string> SupportingAccessions { get; set; } = new();
    public string Source { get; set; } = string.Empty;
}
