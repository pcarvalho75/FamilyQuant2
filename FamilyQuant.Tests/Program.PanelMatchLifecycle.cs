using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelMatchLifecycleCommandTable() =>
    [
        new("--test-panel-match-lifecycle", 74, TestCommandPhase.Raw, args => args.Contains("--test-panel-match-lifecycle", StringComparer.Ordinal), (args, _) =>
        {
            PanelMatchLifecycleTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.PanelMatchLifecycle"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static void PanelMatchLifecycleTests(string output)
    {
        var previous = EvidenceMatchingFile().FamilyPanel!;
        previous = PanelEvidenceMatching.WithConfirmed(previous, "stable-key", PanelEvidenceMatching.Confirm(new[] { "Catalytic violet" },
            new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Keyword, "Action alpha") }));
        string before = JsonSerializer.Serialize(previous);
        var draft = new PanelEditorDraft(previous); draft.Families[0].Key = "renamed-key";
        var renamed = draft.Build();
        Check("Z12p4-fix editor rekeys confirmed records by original row identity and reports the move",
            renamed.ConfirmedEvidenceMatches!.ContainsKey("renamed-key") && !renamed.ConfirmedEvidenceMatches.ContainsKey("stable-key")
            && draft.MatchChangeNotice.Contains("stable-key to renamed-key", StringComparison.Ordinal) && JsonSerializer.Serialize(previous) == before);
        var removed = new PanelEditorDraft(previous); removed.Families.RemoveAt(0);
        Check("Z12p4-fix removing a family drops only its confirmed records and reports the removal",
            removed.Build().ConfirmedEvidenceMatches == null && removed.MatchChangeNotice.Contains("Dropped confirmed matches for stable-key", StringComparison.Ordinal));
        var fresh = FamilyPanelValidation.Clone(previous); fresh.ConfirmedEvidenceMatches = null; fresh.Families[0].DisplayName = "Edited supplied display name";
        var carried = PanelEvidenceMatching.CarryConfirmedMatches(previous, fresh);
        Check("Z12p4-fix re-import keeps confirmed records by stable key independently of display name",
            carried.ConfirmedEvidenceMatches!.ContainsKey("stable-key") && JsonSerializer.Serialize(previous) == before && fresh.ConfirmedEvidenceMatches == null);
        fresh.Families[0].Key = "different-key";
        Check("Z12p4-fix re-import cannot move matches by a coincident display name",
            PanelEvidenceMatching.CarryConfirmedMatches(previous, fresh).ConfirmedEvidenceMatches == null);
        fresh = PanelEvidenceMatching.WithConfirmed(previous, "stable-key", PanelEvidenceMatching.Confirm(new[] { "Edited name" },
            new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Keyword, "Explicit replacement") }));
        Check("Z12p4-fix re-import preserves a newly confirmed replacement over an older record",
            PanelEvidenceMatching.CarryConfirmedMatches(previous, fresh).ConfirmedEvidenceMatches!["stable-key"].Choices.Single().Value == "Explicit replacement");

        using var scratch = new FetchFixtureScratch();
        string changedPath = Path.Combine(scratch.Root, "renamed.xlsx"), removedPath = Path.Combine(scratch.Root, "removed.xlsx");
        FamilyPanelTemplate.Write(changedPath, previous); FamilyPanelTemplate.Write(removedPath, previous);
        foreach (string path in new[] { changedPath, removedPath })
        {
            using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, true);
            var workbook = document.WorkbookPart!;
            var sheet = workbook.Workbook!.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Single(item => item.Name == "Families");
            var part = (DocumentFormat.OpenXml.Packaging.WorksheetPart)workbook.GetPartById(sheet.Id!);
            var row = part.Worksheet!.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().ElementAt(2);
            if (path == changedPath) row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().ElementAt(1).InlineString!.Text!.Text = "renamed-key";
            else row.Remove();
            part.Worksheet.Save();
        }
        var templateRenamed = FamilyPanelTemplate.Read(changedPath);
        Check("Z12p4-fix edited template moves records using its stable record ID and emits a notice",
            templateRenamed.ConfirmedEvidenceMatches!.ContainsKey("renamed-key")
            && templateRenamed.ConfirmedMatchChangeNotice.Contains("stable-key to renamed-key", StringComparison.Ordinal));
        var templateRemoved = FamilyPanelTemplate.Read(removedPath);
        Check("Z12p4-fix edited template drops removed-family records instead of throwing",
            templateRemoved.Families.Count == 0 && templateRemoved.ConfirmedEvidenceMatches == null
            && templateRemoved.ConfirmedMatchChangeNotice.Contains("Dropped", StringComparison.Ordinal));

        EnsureApplication(); Directory.CreateDirectory(output);
        var oldTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var window = new PanelEditorWindow(previous) { Width = 1100, Height = 720, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    var grid = (DataGrid)window.FindName("FamilyGrid");
                    var row = window.Draft.Families[0]; grid.SelectedItem = row;
                    grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[0]); grid.BeginEdit(); PumpDispatcher();
                    var text = (TextBox)grid.Columns[0].GetCellContent(row);
                    text.Text = "renamed-key"; text.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                    grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); PumpDispatcher();
                    var status = (TextBlock)window.FindName("StatusText");
                    Check("Z12p4-fix " + theme + " native key edit shows which matches move",
                        status.Text.Contains("stable-key to renamed-key", StringComparison.Ordinal) && window.Draft.Build().ConfirmedEvidenceMatches!.ContainsKey("renamed-key"));
                    ScreenHarness.Save(window, Path.Combine(output, "editor-rename-" + theme + ".png"), 1100, 720);
                    WalkTree(window).OfType<Button>().Single(button => button.Content as string == "Remove selected").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check("Z12p4-fix " + theme + " native removal reports dropped records without applying",
                        status.Text.Contains("Dropped", StringComparison.Ordinal) && window.Draft.Build().ConfirmedEvidenceMatches == null
                        && JsonSerializer.Serialize(previous) == before);
                    window.LoadTemplate(changedPath); window.UpdateLayout(); PumpDispatcher();
                    Check("Z12p4-fix " + theme + " edited template notice is visible before panel Apply",
                        status.Text.Contains("stable-key to renamed-key", StringComparison.Ordinal));
                    ScreenHarness.Save(window, Path.Combine(output, "template-rename-" + theme + ".png"), 1100, 720);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(oldTheme); }
    }
}
