using FamilyQuant.Evidence;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelLinkNotesCommands() =>
    [new("--test-panel-link-notes", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-panel-link-notes", (_, _) =>
        {
            EnsureApplication(); PanelLinkNotesTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void PanelLinkNotesTests()
    {
        Section("J37 optional panel link notes");
        PanelLinkChoiceFilterTests();
        var file = PanelEvidenceFile();
        string before = file.ToJsonText();
        var session = new EvidenceGroupingSession(file, "fixture");
        var input = session.Propose("selected");
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
        var window = new SuperClusterPreviewWindow(preview, input, session.Propose, session.CountMotif,
            initialScope: "selected", evidenceMode: true, confirmPanelLinks: session.ConfirmPanelLinks)
        { Width = 1280, Height = 780, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.ShowAllOptions = true;
            window.Show(); PumpDispatcher(); window.UpdateLayout();
            var model = (GroupingReviewViewModel)window.DataContext;
            var grid = (DataGrid)window.FindName("panelLinksGrid");
            var noteProperty = typeof(PanelLinkReviewRow).GetProperty("Note", BindingFlags.Public | BindingFlags.Instance);
            var noteColumn = grid.Columns.OfType<DataGridTextColumn>().SingleOrDefault(c => (string?)c.Header == "Note (optional)");
            Check("J37 the confirmation grid offers an optional single-line link note", noteProperty != null && noteColumn != null
                && noteColumn.Binding is Binding binding && binding.Path.Path == "Note");
            if (noteProperty == null || noteColumn == null) return;
            var choices = model.Evidence!.PanelLinks.Where(r => r.Source.NeedsConfirmation).ToArray();
            Check("J37 unconfirmed links start with empty user notes", choices.Length > 0 && choices.All(r => (string?)noteProperty.GetValue(r) == ""));
            foreach (var row in choices) row.SelectedOption = row.Options.Single(o => o.Key == "narrow");
            const string note = "Reviewed source note <&>";
            var tab = WalkTree(window).OfType<TabItem>().Single(item => (string?)item.Header == "Panel links");
            tab.IsSelected = true; PumpDispatcher(); window.UpdateLayout();
            grid.ScrollIntoView(choices[0], noteColumn); PumpDispatcher();
            grid.CurrentCell = new DataGridCellInfo(choices[0], noteColumn);
            bool editing = grid.BeginEdit(); PumpDispatcher();
            var editor = noteColumn.GetCellContent(choices[0]) as TextBox;
            if (editor != null)
            {
                editor.Text = note; editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); PumpDispatcher();
            }
            Check("J37 the native note editor writes the user's text into the link choice", editing && editor != null
                && (string?)noteProperty.GetValue(choices[0]) == note);
            Check("J37 the entered note travels with the selected link", model.Evidence.CapturePanelLinkChoices()
                .Any(choice => choice.LinkKey == choices[0].Source.Key && choice.Source == note));
            string chosenKey = choices[0].Source.Key;
            Check("J37 link changes require the proposal update", model.CanUpdatePanelLinks && !model.CanConfirm);
            FinishReviewWork(model.UpdatePanelLinksAsync);
            var confirmed = model.Evidence!.PanelLinks.Single(r => r.Source.Key == chosenKey);
            Check("J37 confirmation retains the entered note", (string?)noteProperty.GetValue(confirmed) == note && model.CanConfirm);
            noteProperty.SetValue(confirmed, "Edited source note");
            Check("J37 an edit to an existing note requires an update even without changing the family", model.CanUpdatePanelLinks && !model.CanConfirm);
            FinishReviewWork(model.UpdatePanelLinksAsync);
            confirmed = model.Evidence!.PanelLinks.Single(r => r.Source.Key == chosenKey);
            Check("J37 updating an existing choice retains its edited note", (string?)noteProperty.GetValue(confirmed) == "Edited source note");
            noteProperty.SetValue(confirmed, "");
            FinishReviewWork(model.UpdatePanelLinksAsync);
            confirmed = model.Evidence!.PanelLinks.Single(r => r.Source.Key == chosenKey);
            Check("J37 a saved note can be cleared without inventing replacement text", (string?)noteProperty.GetValue(confirmed) == "");
            noteProperty.SetValue(confirmed, "First line\nSecond line");
            Check("J37 multiline notes cannot be submitted", !model.CanUpdatePanelLinks && !model.CanConfirm);
            noteProperty.SetValue(confirmed, "First\u2028Second");
            Check("J37 Unicode line separators cannot be submitted", !model.CanUpdatePanelLinks && !model.CanConfirm);
            noteProperty.SetValue(confirmed, new string('x', PanelEvidenceClassifier.MaximumLinkNoteLength + 1));
            Check("J37 overly long notes cannot be submitted", !model.CanUpdatePanelLinks && !model.CanConfirm);
            noteProperty.SetValue(confirmed, note);
            FinishReviewWork(model.UpdatePanelLinksAsync);
            confirmed = model.Evidence!.PanelLinks.Single(r => r.Source.Key == chosenKey);
            var invalid = new PanelFamilyLinkSelection { LinkKey = chosenKey, PanelFamilyKey = confirmed.SelectedOption!.Key, Source = "First\nSecond" };
            Check("J37 the core refuses a multiline note from non-UI callers", Throws<ArgumentException>(() =>
                PanelEvidenceClassifier.ConfirmLinks(file.FamilyPanel!, session.Proposal!.PanelContext!.Links, new[] { invalid })));
            var tooLong = new PanelFamilyLinkSelection { LinkKey = chosenKey, PanelFamilyKey = confirmed.SelectedOption.Key,
                Source = new string('x', PanelEvidenceClassifier.MaximumLinkNoteLength + 1) };
            Check("J37 the core refuses an oversized note from non-UI callers", Throws<ArgumentException>(() =>
                PanelEvidenceClassifier.ConfirmLinks(file.FamilyPanel!, session.Proposal!.PanelContext!.Links, new[] { tooLong })));
            var result = session.Apply(model.Evidence.CaptureSelection());
            Check("J37 apply stores exactly the user's note without changing unnoted choices", result.Changed && result.Undo != null
                && file.FamilyPanel!.EvidenceLinks.Single(link => PanelEvidenceClassifier.LinkKey(link.SourceFamily, link.ArchitectureKey) == chosenKey).Source == note
                && file.FamilyPanel.EvidenceLinks.Where(link => PanelEvidenceClassifier.LinkKey(link.SourceFamily, link.ArchitectureKey) != chosenKey).All(link => link.Source == ""));
            string output = Path.Combine(Path.GetTempPath(), "FamilyQuant.J37", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            string project = Path.Combine(output, "project.fqproj");
            ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
            var reopened = ProjectStore.ReadFqproj(project).fcf;
            Check("J37 the note survives project save and reopen", reopened.FamilyPanel!.EvidenceLinks
                .Single(link => PanelEvidenceClassifier.LinkKey(link.SourceFamily, link.ArchitectureKey) == chosenKey).Source == note);
            var reopenedSession = new EvidenceGroupingSession(reopened, "fixture");
            var reopenedInput = reopenedSession.Propose("selected");
            var reopenedRow = new EvidenceReviewViewModel(reopenedInput).PanelLinks.Single(link => link.Source.Key == chosenKey);
            Check("J37 reopening the confirmation screen shows the saved note", (string?)noteProperty.GetValue(reopenedRow) == note);
            Check("J37 the presentation-only note does not change serialized panel context", !System.Text.Json.JsonSerializer
                .Serialize(reopenedSession.Proposal!.PanelContext).Contains("SourceNote", StringComparison.Ordinal));
            CheckLinkNoteReport(reopened, note);
            result.Undo!.Restore(file);
            Check("J37 one Undo restores the complete previous document including link notes", file.ToJsonText() == before);
            EvidenceApply.ApplyRecipe(file, result.Recipe);
            Check("J37 saved recipe replay retains the link note", file.FamilyPanel!.EvidenceLinks
                .Single(link => PanelEvidenceClassifier.LinkKey(link.SourceFamily, link.ArchitectureKey) == chosenKey).Source == note);
            result.Undo.Restore(file);
        }
        finally { window.Close(); PumpDispatcher(); }
    }

    private static void CheckLinkNoteReport(FamilyClusterFile project, string note)
    {
        var document = ReportDocumentBuilder.Build(project, new QuantManifest());
        var method = document.Sections.Single(section => section.Id == ReportSectionIds.Methods);
        var table = method.Blocks.OfType<ReportTable>().SingleOrDefault(t => t.Id == "methods-panel-links");
        Check("J37 report methods include a table of confirmed links", table != null);
        if (table == null) return;
        int noteColumn = table.Columns.ToList().IndexOf("User note");
        Check("J37 the methods table prints the exact note and leaves empty notes empty", noteColumn >= 0
            && table.Rows.Any(row => row[noteColumn] == note) && table.Rows.Any(row => row[noteColumn] == ""));
        var layout = ReportSupplementLayout.Apply(document, new Dictionary<string, string>());
        byte[] word = WordReportWriter.BuildBytes(layout.Document);
        using (var archive = new ZipArchive(new MemoryStream(word)))
        using (var xml = archive.GetEntry("word/document.xml")!.Open())
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            Check("J37 the exported Word document contains the user's exact note", XDocument.Load(xml).Descendants(w + "t").Any(t => t.Value == note));
        }
        byte[] workbook = ReportSupplementWorkbook.BuildBytes(layout.Document, layout.Sheets);
        var book = ReadSupplement(workbook);
        var index = book["Contents"].Skip(1).SingleOrDefault(row => row[3] == table.Id);
        Check("J37 the report supplement contains the same link note and blank values", index != null
            && book[index[0]].Skip(1).Any(row => row.Contains(note)) && book[index[0]].Skip(1).Any(row => row[noteColumn] == ""));
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        var previousUiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.CurrentCulture;
            Check("J37 Word and supplement note output are culture invariant", word.SequenceEqual(WordReportWriter.BuildBytes(layout.Document))
                && workbook.SequenceEqual(ReportSupplementWorkbook.BuildBytes(layout.Document, layout.Sheets)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
            System.Globalization.CultureInfo.CurrentUICulture = previousUiCulture;
        }
        var unnamed = FamilyClusterFile.FromJsonText(project.ToJsonText());
        string key = unnamed.FamilyPanel!.EvidenceLinks.Single(link => link.Source == note).PanelFamilyKey!;
        unnamed.FamilyPanel.Families.Single(family => family.Key == key).DisplayName = "";
        var unnamedTable = ReportMethods.Build(unnamed, new QuantManifest()).Blocks.OfType<ReportTable>().Single(t => t.Id == "methods-panel-links");
        int familyColumn = unnamedTable.Columns.ToList().IndexOf("Panel family choice");
        Check("J37 a link without a display name retains its panel key", unnamedTable.Rows.Single(row => row[noteColumn] == note)[familyColumn] == key);
        var many = FamilyClusterFile.FromJsonText(project.ToJsonText());
        var template = many.FamilyPanel!.EvidenceLinks.First();
        many.FamilyPanel.EvidenceLinks = Enumerable.Range(0, 25).Select(i => new FamilyQuant.Panels.FamilyPanelEvidenceLink
        {
            SourceFamily = "Source " + i.ToString(System.Globalization.CultureInfo.InvariantCulture), ArchitectureKey = template.ArchitectureKey,
            PanelFamilyKey = template.PanelFamilyKey, PanelDefinitionFingerprint = template.PanelDefinitionFingerprint,
            Source = "Note " + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).ToList();
        var large = new ReportDocument { Sections = new[] { ReportMethods.Build(many, new QuantManifest()) } };
        var largeLayout = ReportSupplementLayout.Apply(large, new Dictionary<string, string>());
        Check("J37 every confirmed link remains in the Word methods table even when there are many",
            largeLayout.Document.Sections.Single().Blocks.OfType<ReportTable>().Any(t => t.Id == "methods-panel-links" && t.Rows.Count == 25));
        var largeBook = ReadSupplement(ReportSupplementWorkbook.BuildBytes(largeLayout.Document, largeLayout.Sheets));
        var largeIndex = largeBook["Contents"].Skip(1).Single(row => row[3] == "methods-panel-links");
        Check("J37 the supplement retains every note when there are many links", largeBook[largeIndex[0]].Length == 26
            && largeBook[largeIndex[0]].Skip(1).Any(row => row[noteColumn] == "Note 24"));
        many.FamilyPanel.Families[0].Pfam.Clear();
        var stale = ReportMethods.Build(many, new QuantManifest()).Blocks.OfType<ReportTable>().Single(t => t.Id == "methods-panel-links");
        int stateColumn = stale.Columns.ToList().IndexOf("Status");
        Check("J37 report marks a saved choice whose panel definition changed", stale.Rows.All(row => row[stateColumn] == "Panel definition changed"));
    }
}
