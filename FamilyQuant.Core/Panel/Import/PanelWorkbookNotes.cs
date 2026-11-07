using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Panels.Import;

/// <summary>Detached import operations called after the user accepts the workbook summary.</summary>
public static class PanelWorkbookNotes
{
    public static PanelImportCheck CheckConfirmedWorkbook(this IFamilyPanelSpreadsheetImporter importer,
        PanelWorkbookProposal proposal, FamilyPanelImport confirmedChoices,
        IReadOnlyList<FamilyPanelDecisionMapping> confirmedDecisions, string panelName = "")
    {
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(confirmedChoices);
        ArgumentNullException.ThrowIfNull(confirmedDecisions);
        var preflight = new PanelImportCheck { Problems = Copy(proposal.Problems) };
        if (proposal.ColorsState != PanelWorkbookReadState.Read)
            preflight.Problems.Add(new() { Kind = PanelImportProblemKind.PreviewMismatch, Severity = PanelImportSeverity.Blocking,
                Message = "Workbook colours have not been read. Read them before accepting the workbook." });
        if (proposal.DecisionsState != PanelWorkbookReadState.Read)
            preflight.Problems.Add(new() { Kind = PanelImportProblemKind.PreviewMismatch, Severity = PanelImportSeverity.Blocking,
                Message = "Workbook decisions have not been read. Read them before accepting the workbook." });
        var table = proposal.FamilyTable;
        if (table == null || proposal.NeedsFamilySheetChoice)
            preflight.Problems.Add(new() { Kind = table == null ? PanelImportProblemKind.SheetMissing : PanelImportProblemKind.NoFamilies,
                Severity = PanelImportSeverity.Blocking, SheetName = proposal.FamilySheet,
                Message = "Choose the family sheet before accepting the workbook." });
        if (preflight.Problems.Any(problem => problem.Severity == PanelImportSeverity.Blocking)) return preflight;
        var choices = Copy(confirmedChoices);
        choices.SourceNotes = null;
        // Check only the selected family table. Source notes never enter its family-row parser.
        var check = importer.Check(table!, choices, confirmedDecisions, panelName);
        var problemKeys = check.Problems.Select(problem => JsonSerializer.Serialize(problem)).ToHashSet(StringComparer.Ordinal);
        foreach (var problem in preflight.Problems)
            if (problemKeys.Add(JsonSerializer.Serialize(problem))) check.Problems.Add(problem);
        if (check.Problems.Any(problem => problem.Severity == PanelImportSeverity.Blocking)) check.Panel = null;
        else if (check.Panel != null) check.Panel = WithConfirmedNotes(check.Panel, proposal.Notes);
        return check;
    }

    public static FamilyPanel WithConfirmedNotes(FamilyPanel panel, IReadOnlyList<PanelWorkbookSourceNote> confirmedNotes)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var notes = CopyNotes(confirmedNotes);
        var result = FamilyPanelValidation.Clone(panel);
        if (result.Import == null)
        {
            if (notes != null) throw new ArgumentException("This panel has no stored import choices for source notes.", nameof(panel));
        }
        else result.Import.SourceNotes = notes;
        return result;
    }

    private static List<PanelWorkbookSourceNote>? CopyNotes(IReadOnlyList<PanelWorkbookSourceNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Count == 0) return null;
        var result = new List<PanelWorkbookSourceNote>(notes.Count);
        foreach (var note in notes)
        {
            if (note == null || string.IsNullOrEmpty(note.SheetName) || note.Text == null)
                throw new ArgumentException("Each source note needs a sheet name and original text.", nameof(notes));
            result.Add(new() { SheetName = note.SheetName, Text = note.Text });
        }
        return result;
    }

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
