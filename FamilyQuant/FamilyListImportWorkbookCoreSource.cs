using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System.Collections.Generic;
using System.Threading;

namespace FamilyQuant;

/// <summary>Adapts published Core contracts without interpreting workbook content.</summary>
public sealed class FamilyListWorkbookCoreSource : IFamilyListWorkbookSource
{
    private readonly IPanelWorkbookReader reader;
    private readonly IFamilyPanelWorkbookImporter importer;
    public FamilyListWorkbookCoreSource(IPanelWorkbookReader? reader = null, IFamilyPanelWorkbookImporter? importer = null)
    { this.reader = reader ?? new PanelSpreadsheetReader(); this.importer = importer ?? new FamilyPanelSpreadsheetImporter(); }
    public PanelWorkbookProposal Read(string path, IReadOnlyDictionary<string, PanelWorkbookSheetRole> roles, string familySheet, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var workbook = reader.ReadWorkbook(path);
        cancellation.ThrowIfCancellationRequested();
        var proposal = importer.ProposeWorkbook(workbook, familySheet.Length > 0 ? familySheet : null, roles);
        cancellation.ThrowIfCancellationRequested();
        return proposal;
    }
    public FamilyPanel CompletePanel(FamilyPanel panel, PanelWorkbookProposal workbook) =>
        PanelWorkbookNotes.WithConfirmedNotes(panel, workbook.Notes);
}
