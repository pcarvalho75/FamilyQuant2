using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.Panels;

public sealed partial class FamilyPanel
{
    /// <summary>Source text only. It does not define families, matching rules or measured quantities.</summary>
    [JsonIgnore]
    public IReadOnlyList<PanelWorkbookSourceNote> SourceNotes => Import?.SourceNotes is { } notes
        ? notes : Array.Empty<PanelWorkbookSourceNote>();
}

public sealed partial class FamilyPanelImport
{
    /// <summary>Notes accepted with the workbook, including sheet names, cell addresses and verbatim text.
    /// Kept once here so replay and panel serialization preserve the same source material.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PanelWorkbookSourceNote>? SourceNotes { get; set; }
}
