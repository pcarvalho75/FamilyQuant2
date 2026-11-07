using System.Collections.Generic;
using System.Text.Json.Serialization;
using FamilyQuant.Panels.Import;

namespace FamilyQuant.Panels;

/// <summary>Z12p1 replay choices. Each is omitted when unset, so panels imported before keep their exact JSON.</summary>
public sealed partial class FamilyPanelImport
{
    /// <summary>PanelImportRules version; 0 replays the J1b rules.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ImportVersion { get; set; }

    /// <summary>User choices for single rows: skip, keep, rename, merge, split or report under others.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FamilyPanelRowChoice>? RowChoices { get; set; }

    /// <summary>The user's name for each fill colour of the colour-category column.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FamilyPanelColorLabel>? ColorLabels { get; set; }

    /// <summary>The TextFields name for the colour category; the column header plus " colour" when empty.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ColorCategoryName { get; set; }
}
