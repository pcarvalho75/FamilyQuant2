using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace FamilyQuant;

/// <summary>The wizard consumes a detached Core proposal through this boundary; it never classifies a sheet.</summary>
public interface IFamilyListWorkbookSource
{
    PanelWorkbookProposal Read(string path, IReadOnlyDictionary<string, PanelWorkbookSheetRole> roles,
        string familySheet, CancellationToken cancellation);
    FamilyPanel CompletePanel(FamilyPanel panel, PanelWorkbookProposal workbook);
}

public sealed record ImportSheetRoleOption(string Label, PanelWorkbookSheetRole Value);

public sealed class ImportWorkbookSheet : ScreenViewModel
{
    private PanelWorkbookSheetRole role;
    public string Name { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Visibility { get; init; } = "";
    public string Review { get; init; } = "";
    public bool IsFamilySheet { get; init; }
    public PanelWorkbookSheetRole Role { get => role; set { if (Set(ref role, value)) Changed(nameof(Contribution)); } }
    public string Contribution => Role == PanelWorkbookSheetRole.Notes ? "Kept as source notes"
        : Role == PanelWorkbookSheetRole.ColorLegend ? "Names and meanings"
        : Role == PanelWorkbookSheetRole.SubclassTable ? "Subclass proposals" : "Family rows";
}
