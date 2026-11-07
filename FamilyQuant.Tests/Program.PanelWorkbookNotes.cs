using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using XL = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelWorkbookNotesCommands() =>
    [new("--test-panel-workbook-notes", 21606, TestCommandPhase.Raw,
        args => args is ["--test-panel-workbook-notes"], (_, _) =>
        {
            PanelWorkbookNotesTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void PanelWorkbookNotesTests()
    {
        Section("Z16a6 workbook notes remain source text through confirmed import and saved projects");
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.WorkbookNotes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var importer = new FamilyPanelSpreadsheetImporter();
        string path = Path.Combine(root, "notes-fixture.xlsx");
        WriteNeutralWorkbook(path, includeDecision: false);
        const string originalCell = "  First invented line\r\nSecond invented line\nThird line\t<&> \"quote\" \u03a9  ";
        using (var document = SpreadsheetDocument.Open(path, true, new OpenSettings { AutoSave = false }))
        {
            var book = document.WorkbookPart!;
            var sheet = book.Workbook!.Sheets!.Elements<XL.Sheet>().Single(sheet => sheet.Name == "Data 4");
            var part = (WorksheetPart)book.GetPartById(sheet.Id!);
            var worksheet = part.Worksheet!;
            worksheet.Descendants<XL.Cell>().Single(cell => cell.CellReference == "B2").InlineString =
                new XL.InlineString(new XL.Text(originalCell) { Space = SpaceProcessingModeValues.Preserve });
            // Entitize CR so XML newline normalization retains the cell's distinct CRLF and LF.
            using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
            using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize });
            worksheet.WriteTo(writer);
        }
        var proposal = importer.ProposeWorkbook(path);
        string[] noteSheets = ["Data 4", "Data 5", "Data 6", "Data 2"];
        Check("Z16a6 the synthetic workbook separates source notes and keeps the legend text", proposal.Workbook.Sheets.Count == 6
            && proposal.Notes.Select(note => note.SheetName).SequenceEqual(noteSheets));
        Check("Z16a6 proposed notes preserve addresses, whitespace and original line breaks",
            proposal.Notes[0].Text.Contains("B2: " + originalCell + "\n", StringComparison.Ordinal));
        var choices = new FamilyPanelImport
        {
            ImportVersion = PanelImportRules.Current, FamilySheet = proposal.FamilySheet,
            HeaderRowNumber = proposal.FamilyTable!.HeaderRowNumber, SourceSha256 = proposal.Workbook.SourceSha256,
            ColumnHeaders = new() { [PanelImportFields.FamilyKey] = "h01", [PanelImportFields.DisplayName] = "h02" },
        };
        var baseline = importer.Check(proposal.FamilyTable, choices, Array.Empty<FamilyPanelDecisionMapping>(), "Notes fixture");
        Check("Z16a6 the family-only control imports its forty source families", baseline.Panel?.Families.Count == 40,
            string.Join("; ", baseline.Problems.Where(problem => problem.Severity == PanelImportSeverity.Blocking).Select(problem => problem.Message)));
        if (baseline.Panel == null) return;
        var oldFile = new FamilyClusterFile { FamilyPanel = baseline.Panel };
        string oldJson = oldFile.ToJsonText();
        Check("Z16a6 old panels omit the optional notes field and reopen unchanged", !oldJson.Contains("SourceNotes", StringComparison.Ordinal)
            && FamilyClusterFile.FromJsonText(oldJson).ToJsonText() == oldJson);
        string legacy = Path.Combine(root, "legacy-without-notes.fqproj");
        ProjectStore.WriteFqproj(legacy, oldFile, Array.Empty<QuantRow>(), new QuantManifest());
        using (var zip = ZipFile.Open(legacy, ZipArchiveMode.Update))
        {
            zip.GetEntry(ProjectStore.CurationFormat2EntryName)!.Delete();
            zip.GetEntry(ProjectStore.CurationEntryName)!.Delete();
            using (var writer = new StreamWriter(zip.CreateEntry(ProjectStore.CurationEntryName).Open(), new UTF8Encoding(false))) writer.Write(oldJson);
            var entry = zip.GetEntry(ProjectStore.ManifestEntryName)!;
            string text; using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) text = reader.ReadToEnd();
            var manifest = JsonNode.Parse(text)!.AsObject(); manifest["FormatVersion"] = 2;
            entry.Delete();
            using var manifestWriter = new StreamWriter(zip.CreateEntry(ProjectStore.ManifestEntryName).Open(), new UTF8Encoding(false));
            manifestWriter.Write(manifest.ToJsonString());
        }
        var oldRead = ProjectStore.ReadFqproj(legacy);
        Check("Z16a6 a synthetic format-1 project without notes opens byte unchanged", oldRead.fcf.ToJsonText() == oldJson
            && oldRead.manifest.FormatVersion == 2);
        string oldSaved = Path.Combine(root, "old-resaved.fqproj");
        ProjectStore.WriteFqproj(oldSaved, oldRead.fcf, oldRead.quant, oldRead.manifest);
        Check("Z16a6 the old project resaves as format 2 with identical canonical content", ProjectStore.ReadFqproj(oldSaved).fcf.ToJsonText() == oldJson);

        var api = typeof(FamilyPanel).Assembly.GetType("FamilyQuant.Panels.Import.PanelWorkbookNotes");
        var confirmed = api?.GetMethod("CheckConfirmedWorkbook", BindingFlags.Public | BindingFlags.Static);
        var attach = api?.GetMethod("WithConfirmedNotes", BindingFlags.Public | BindingFlags.Static);
        var storedNotes = typeof(FamilyPanelImport).GetProperty("SourceNotes");
        var panelNotes = typeof(FamilyPanel).GetProperty("SourceNotes");
        Check("Z16a6 the explicit confirmed-notes and replay contract exists", confirmed != null && attach != null && storedNotes != null && panelNotes != null);
        if (confirmed == null || attach == null || storedNotes == null || panelNotes == null) return;
        string Notes(FamilyPanel panel) => JsonSerializer.Serialize((IReadOnlyList<PanelWorkbookSourceNote>)panelNotes.GetValue(panel)!);
        string expectedNotes = JsonSerializer.Serialize(proposal.Notes), beforeProposal = JsonSerializer.Serialize(proposal), beforeChoices = JsonSerializer.Serialize(choices);
        var accepted = (PanelImportCheck)confirmed.Invoke(null, new object[] { importer, proposal, choices, Array.Empty<FamilyPanelDecisionMapping>(), "Notes fixture" })!;
        Check("Z16a6 only confirmed Check attaches every note in source order", accepted.Panel != null && Notes(accepted.Panel) == expectedNotes);
        if (accepted.Panel == null) return;
        var panel = accepted.Panel;
        string panelJson = JsonSerializer.Serialize(panel);
        Check("Z16a6 accepting is detached from proposal and caller choices", JsonSerializer.Serialize(proposal) == beforeProposal
            && JsonSerializer.Serialize(choices) == beforeChoices && storedNotes.GetValue(choices) == null);
        var blocked = JsonSerializer.Deserialize<PanelWorkbookProposal>(beforeProposal)!;
        blocked.Problems.Add(new() { Kind = PanelImportProblemKind.FileUnreadable, Severity = PanelImportSeverity.Blocking,
            SheetName = "Data 4", Message = "Invented source read failure." });
        var refused = (PanelImportCheck)confirmed.Invoke(null, new object[] { importer, blocked, choices, Array.Empty<FamilyPanelDecisionMapping>(), "Notes fixture" })!;
        Check("Z16a6 a blocking note-sheet read refuses confirmed acceptance", refused.Panel == null
            && refused.Problems.Any(problem => problem.Severity == PanelImportSeverity.Blocking && problem.SheetName == "Data 4"));
        var warned = JsonSerializer.Deserialize<PanelWorkbookProposal>(beforeProposal)!;
        var warning = new PanelImportProblem { Kind = PanelImportProblemKind.FormulaWithoutResult, Severity = PanelImportSeverity.Note,
            SheetName = proposal.FamilySheet, RowNumber = 2, Column = "h01", Message = "Invented source warning." };
        warned.FamilyTable!.Problems.Add(warning); warned.Problems.Add(warning);
        var otherWarning = JsonSerializer.Deserialize<PanelImportProblem>(JsonSerializer.Serialize(warning))!;
        otherWarning.SheetName = "Data 4"; warned.Problems.Add(otherWarning);
        var checkedWarnings = (PanelImportCheck)confirmed.Invoke(null, new object[] { importer, warned, choices, Array.Empty<FamilyPanelDecisionMapping>(), "Notes fixture" })!;
        Check("Z16a6 family read warnings appear once while other-sheet warnings stay distinct", checkedWarnings.Panel != null
            && checkedWarnings.Problems.Count(problem => problem.Kind == PanelImportProblemKind.FormulaWithoutResult) == 2);
        Check("Z16a6 notes never create, match or quantify families", JsonSerializer.Serialize(panel.Families) == JsonSerializer.Serialize(baseline.Panel.Families)
            && PanelEvidenceClassifier.DefinitionFingerprint(panel) == PanelEvidenceClassifier.DefinitionFingerprint(baseline.Panel));
        var attached = (FamilyPanel)attach.Invoke(null, new object[] { baseline.Panel, proposal.Notes })!;
        Check("Z16a6 attaching explicit notes returns the same detached panel", JsonSerializer.Serialize(attached) == panelJson
            && storedNotes.GetValue(baseline.Panel.Import) == null);
        proposal.Notes[0].Text = "Changed after acceptance";
        Check("Z16a6 later proposal edits cannot alter confirmed source text", Notes(panel) == expectedNotes);
        proposal.Notes[0].Text = JsonSerializer.Deserialize<List<PanelWorkbookSourceNote>>(expectedNotes)![0].Text;
        Check("Z16a6 replay reproduces the complete imported panel byte for byte", JsonSerializer.Serialize(importer.Replay(path, panel)) == panelJson);
        string[] blocksBefore = ReportMethods.Build(oldFile, new QuantManifest()).Blocks.Select(block => block.Id).ToArray();
        var file = new FamilyClusterFile { FamilyPanel = panel, EvidenceRecipe = new() { FamilyPanel = FamilyPanelValidation.Clone(panel) } };
        string project = Path.Combine(root, "notes.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        var reopened = ProjectStore.ReadFqproj(project).fcf;
        Check("Z16a6 Save and reopen retain note bytes and the whole panel", Notes(reopened.FamilyPanel!) == expectedNotes
            && JsonSerializer.Serialize(reopened.FamilyPanel) == panelJson && JsonSerializer.Serialize(reopened.EvidenceRecipe!.FamilyPanel) == panelJson);
        ((List<PanelWorkbookSourceNote>)storedNotes.GetValue(reopened.EvidenceRecipe!.FamilyPanel!.Import)!).First().Text = "Recipe copy only";
        Check("Z16a6 format-2 recipe deduplication restores detached notes", Notes(reopened.FamilyPanel!) == expectedNotes);
        string fcf = Path.Combine(root, "notes-format2.fcf");
        using (var stream = File.Create(fcf)) CurationFormat.Write(file, stream);
        var fcfRead = FamilyClusterFile.Load(fcf);
        Check("Z16a6 format-2 fcf preserves exact source note bytes", CurationFormat.Detect(() => File.OpenRead(fcf)) == 2
            && Notes(fcfRead.FamilyPanel!) == expectedNotes && JsonSerializer.Serialize(fcfRead.FamilyPanel) == panelJson);
        string savedFcf = Path.Combine(root, "notes-save.fcf"); file.Save(savedFcf);
        Check("Z16a6 ordinary fcf Save and reopen preserve source notes", Notes(FamilyClusterFile.Load(savedFcf).FamilyPanel!) == expectedNotes);
        var methods = ReportMethods.Build(file, new QuantManifest());
        var notesTable = methods.Blocks.OfType<ReportTable>().SingleOrDefault(table => table.Id == "methods-panel-source-notes");
        Check("Z16a6 Methods lists every sheet and where its text is kept", notesTable != null
            && notesTable.Rows.Select(row => row[0]).SequenceEqual(noteSheets)
            && notesTable.Rows.All(row => row[1].Contains("kept with the panel", StringComparison.Ordinal)));
        Check("Z16a6 Methods lists names without printing full source text", !JsonSerializer.Serialize(methods).Contains("First invented line", StringComparison.Ordinal)
            && !blocksBefore.Contains("methods-panel-source-notes", StringComparer.Ordinal));

        EnsureApplication(); var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var editor = new PanelEditorWindow(panel) { ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    editor.Show(); PumpDispatcher();
                    var familyGrid = (DataGrid)editor.FindName("FamilyGrid");
                    Check("Z16a6 " + theme + " source notes do not squeeze out family editing", familyGrid.ActualHeight >= 100,
                        "family_grid_height=" + familyGrid.ActualHeight);
                    ScreenHarness.Save(editor, Path.Combine(root, "notes-families-" + theme + ".png"), 1080, 720);
                    ((TabItem)editor.FindName("SourceNotesTab")).IsSelected = true; PumpDispatcher();
                    var tabs = (TabControl)editor.FindName("SourceNotesTabs"); tabs.SelectedIndex = 0; PumpDispatcher();
                    var box = NotesVisuals(tabs).OfType<TextBox>().FirstOrDefault();
                    Check("Z16a6 " + theme + " editor shows each notes sheet with read-only verbatim text", tabs.Items.Count == noteSheets.Length
                        && tabs.Items.Cast<PanelWorkbookSourceNote>().Select(note => note.SheetName).SequenceEqual(noteSheets)
                        && box is { IsReadOnly: true } && box.Text == JsonSerializer.Deserialize<List<PanelWorkbookSourceNote>>(expectedNotes)![0].Text);
                    Check("Z16a6 " + theme + " applying an unchanged editor preserves source notes", JsonSerializer.Serialize(editor.Draft.Build()) == panelJson);
                    ScreenHarness.Save(editor, Path.Combine(root, "notes-editor-" + theme + ".png"), 1080, 720);
                }
                finally { editor.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
        Console.WriteLine("Z16a6 synthetic artifacts: " + root);
    }

    private static IEnumerable<DependencyObject> NotesVisuals(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var nested in NotesVisuals(child)) yield return nested;
        }
    }
}
