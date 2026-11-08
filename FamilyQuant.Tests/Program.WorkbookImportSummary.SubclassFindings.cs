using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using XL = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static readonly string[] FindingClassNames = ["Tier-A", "Tier-B", "Tier-C", "Tier-D"];

    private static void WriteSubclassFindingWorkbook(string path, bool exclusive = false, string? expectedText = null)
    {
        WriteNeutralWorkbook(path, includeLegend: false, includeDecision: false);
        using var document = SpreadsheetDocument.Open(path, true);
        var list = NeutralWorkbookPart(document.WorkbookPart!, "Data 9");
        var listRows = list.Worksheet!.GetFirstChild<XL.SheetData>()!;
        int expectedColumn = Array.FindIndex(listRows.Elements<XL.Row>().First().Elements<XL.Cell>().ToArray(), cell => cell.InnerText == "h13");
        foreach (var row in listRows.Elements<XL.Row>().Skip(1))
        {
            var cells = row.Elements<XL.Cell>().ToArray();
            cells[expectedColumn].InlineString = new XL.InlineString(new XL.Text(cells[0].InnerText == "F07"
                ? expectedText ?? "Tier-A; Tier-B (Tier-B1, Tier-B2); Tier-C [detail]; Tier-D (more detail); Unplaced explanatory text; Tier-AX" : ""));
        }
        list.Worksheet.Save();
        var subclass = NeutralWorkbookPart(document.WorkbookPart!, "Data 3");
        var data = subclass.Worksheet!.GetFirstChild<XL.SheetData>()!;
        foreach (var row in data.Elements<XL.Row>().Skip(5).ToArray()) row.Remove();
        int index = 0;
        foreach (var row in data.Elements<XL.Row>().Skip(1))
        {
            var cells = row.Elements<XL.Cell>().ToArray();
            cells[0].InlineString = new XL.InlineString(new XL.Text("F07"));
            cells[1].InlineString = new XL.InlineString(new XL.Text(FindingClassNames[index]));
            cells[2].InlineString = new XL.InlineString(new XL.Text("+ DomainOne (PF01234)"));
            string extra = index switch { 0 => exclusive ? "- DomainTwo (PF05678)" : "", 1 => "+ DomainTwo (PF05678)",
                2 => "+ DomainThree (PF09090)", _ => "+ DomainTwo (PF05678); + DomainThree (PF09090)" };
            cells[3].InlineString = new XL.InlineString(new XL.Text(extra));
            cells[4].InlineString = new XL.InlineString(new XL.Text("Synthetic architecture note " + index++));
        }
        subclass.Worksheet.Save();
    }

    private static FamilyClusterFile SubclassFindingProject() => new() { MyExtendedFastaItems = new() { new()
    {
        SequenceIdentifier = "synthetic-subclass-reference", Sequence = "ACDEFGHIK",
        Evidence = new() { Pfam = new() { new() { Accession = "PF01234", Name = "DomainOne" },
            new() { Accession = "PF05678", Name = "DomainTwo" }, new() { Accession = "PF09090", Name = "DomainThree" } } }
    } } };

    private static PanelSubclassAcceptance[] SelectFindingClasses(WorkbookImportSummary summary, params string[] names) => summary.SubclassProposals
        .Where(proposal => proposal.HasMatchedTableRow).Select(proposal => new PanelSubclassAcceptance
        {
            ProposalId = proposal.ProposalId, Accepted = names.Contains(proposal.DisplayName, StringComparer.Ordinal),
            FamilyKey = proposal.FamilyKey, Key = proposal.Key, DisplayName = proposal.DisplayName,
            Rules = proposal.Rules.Select(rule => new PanelSubclassRuleAcceptance { RuleId = rule.RuleId, Accepted = true, Kind = rule.Kind }).ToList()
        }).ToArray();

    private static WorkbookImportItem[] SubclassFindings(WorkbookImportSummary summary, string label) => summary.Sections
        .Single(section => section.Kind == WorkbookImportSectionKind.Subclasses).Items.Where(item => item.Label == label).ToArray();

    private static void WorkbookSummarySubclassFindingTests(string root)
    {
        string path = Path.Combine(root, "subclass-findings.xlsx"); WriteSubclassFindingWorkbook(path);
        var importer = new FamilyPanelSpreadsheetImporter(); var project = SubclassFindingProject();
        var matching = new PanelEvidenceMatching(project); var evidence = new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>>();
        var source = ReviewSummaryFixture(importer.ProposeWorkbook(path), importer);
        string before = JsonSerializer.Serialize(source), projectBefore = project.ToJsonText(), hash = TestFileHash.Upper(path);
        var initial = WorkbookImportSummaries.Build(source, matching, reviewedEvidence: evidence);
        Check("Z12p5y1 qualified expected names use the actual table class names", initial.ExpectedSubclassNames.Select(name => name.DisplayName)
            .Order(StringComparer.Ordinal).SequenceEqual(FindingClassNames.Order(StringComparer.Ordinal)));
        Check("Z12p5y1 expected annotations and unmatched prose add no extra subclass proposals", initial.SubclassProposals.Count == 4
            && initial.SubclassProposals.All(proposal => proposal.HasMatchedTableRow));
        var notes = SubclassFindings(initial, "Subclass notes");
        Check("Z12p5y1 unmatched prose and a partial-word prefix stay in subclass notes with source locations", notes.Any(note =>
            note.Value.Contains("Unplaced explanatory text", StringComparison.Ordinal) && note.Value.Contains("Tier-AX", StringComparison.Ordinal)
            && note.Sources.Any(origin => origin.SheetName == "Data 9" && origin.RowNumbers.Contains(8) && origin.Columns.Contains("h13"))));

        var selected = SelectFindingClasses(initial, FindingClassNames);
        var all = WorkbookImportSummaries.Build(source, matching, selected, evidence);
        var overlaps = SubclassFindings(all, "Overlapping subclass rules");
        Check("Z12p5y1 every compatible selected pair is named once before import", overlaps.Length == 6
            && overlaps.All(item => item.Value.Contains("F07", StringComparison.Ordinal) && !item.Required && item.Page == WorkbookImportPage.Subclasses));
        Check("Z12p5y1 overlap advice names the corrective choice without blocking acceptance", overlaps.Length > 0
            && overlaps.All(item => item.Reason.Contains("Leave one out", StringComparison.Ordinal) && item.Reason.Contains("Absent", StringComparison.Ordinal)
                && item.Reason.Contains("sheet", StringComparison.Ordinal)) && !all.HasBlockingItems);
        var accepted = WorkbookImportSummaries.AcceptAll(all, importer, selected, evidence, broadPfamConfirmed: true);
        Check("Z12p5y1 overlapping rules remain an explicit importable choice", accepted.CanImport
            && accepted.Panel!.Families.Single(family => family.Key == "F07").Subclasses.Count == 4);
        Check("Z12p5y1 subclass notes survive accepted import instead of becoming executable definitions", accepted.Panel?.SourceNotes
            .Any(note => note.Text.Contains("Unplaced explanatory text", StringComparison.Ordinal) && note.Text.Contains("Tier-AX", StringComparison.Ordinal)) == true);
        if (accepted.Panel is { } panel)
        {
            var label = SubclassAssignment.ClassifyDirect(panel.Families.Single(family => family.Key == "F07"), new ProteinEvidence
                { Fragment = false, Pfam = new() { new() { Accession = "PF01234" }, new() { Accession = "PF05678" }, new() { Accession = "PF09090" } } });
            Check("Z12p5y1 the synthetic witness reaches the existing native conflict outcome", label.Status == ProteinSubclassStatus.Unknown
                && label.Reason.Contains("conflicting", StringComparison.Ordinal));
        }

        var pairChoices = SelectFindingClasses(initial, "Tier-A", "Tier-B");
        var pair = WorkbookImportSummaries.Build(source, matching, pairChoices, evidence);
        Check("Z12p5y1 unselected subclasses do not produce overlap findings", SubclassFindings(pair, "Overlapping subclass rules").Length == 1);
        pairChoices.Single(choice => choice.DisplayName == "Tier-B").Accepted = false;
        Check("Z12p5y1 leaving one out removes the overlap finding", SubclassFindings(WorkbookImportSummaries.Build(source, matching, pairChoices, evidence),
            "Overlapping subclass rules").Length == 0);
        pairChoices.Single(choice => choice.DisplayName == "Tier-B").Accepted = true;
        pairChoices.Single(choice => choice.DisplayName == "Tier-B").FamilyKey = "F08";
        Check("Z12p5y1 definitions in different reviewed families do not overlap", SubclassFindings(WorkbookImportSummaries.Build(source, matching, pairChoices, evidence),
            "Overlapping subclass rules").Length == 0);
        pairChoices.Single(choice => choice.DisplayName == "Tier-B").FamilyKey = "F07";
        foreach (var rule in pairChoices.Single(choice => choice.DisplayName == "Tier-B").Rules) rule.Accepted = false;
        Check("Z12p5y1 a notes-only selected definition has no domain overlap", SubclassFindings(WorkbookImportSummaries.Build(source, matching, pairChoices, evidence),
            "Overlapping subclass rules").Length == 0);

        var reviewedPolarity = SelectFindingClasses(initial, "Tier-A", "Tier-B");
        reviewedPolarity.Single(choice => choice.DisplayName == "Tier-A").Rules.Single().Kind = PanelSubclassRuleKind.AbsentPfam;
        Check("Z12p5y1 overlap checks the reviewed rule kind rather than its source proposal kind",
            SubclassFindings(WorkbookImportSummaries.Build(source, matching, reviewedPolarity, evidence), "Overlapping subclass rules").Length == 0);
        var invalidKind = SelectFindingClasses(initial, "Tier-A", "Tier-B");
        invalidKind.Single(choice => choice.DisplayName == "Tier-B").Rules[0].Kind = (PanelSubclassRuleKind)999;
        var invalidSummary = WorkbookImportSummaries.Build(source, matching, invalidKind, evidence);
        Check("Z12p5y1 unresolved selected rules retain their blocker without a false executable-overlap finding", invalidSummary.HasBlockingItems
            && SubclassFindings(invalidSummary, "Overlapping subclass rules").Length == 0);

        string boundaryPath = Path.Combine(root, "subclass-prefix-boundaries.xlsx");
        WriteSubclassFindingWorkbook(boundaryPath, expectedText: "tier-a (context); Tier-B; Tier-B [another note]; Tier-C; Tier-D; Tier-Ax; Tier-A_1; Tier-Aé");
        var boundarySource = ReviewSummaryFixture(importer.ProposeWorkbook(boundaryPath), importer);
        var boundary = WorkbookImportSummaries.Build(boundarySource, matching, reviewedEvidence: evidence);
        Check("Z12p5y1 whole-word prefix matching is case invariant and duplicate annotations share one table class",
            boundary.ExpectedSubclassNames.Select(name => name.DisplayName).Order(StringComparer.Ordinal).SequenceEqual(FindingClassNames.Order(StringComparer.Ordinal))
            && boundary.SubclassProposals.Count == 4);
        var boundaryNotes = SubclassFindings(boundary, "Subclass notes");
        Check("Z12p5y1 letter and connector suffixes are not table-class word boundaries", boundaryNotes.Any(note =>
            new[] { "Tier-Ax", "Tier-A_1", "Tier-Aé" }.All(value => note.Value.Contains(value, StringComparison.Ordinal))));

        string exclusivePath = Path.Combine(root, "subclass-exclusive.xlsx"); WriteSubclassFindingWorkbook(exclusivePath, exclusive: true);
        var exclusiveSource = ReviewSummaryFixture(importer.ProposeWorkbook(exclusivePath), importer);
        var exclusiveInitial = WorkbookImportSummaries.Build(exclusiveSource, matching, reviewedEvidence: evidence);
        var exclusiveChoices = SelectFindingClasses(exclusiveInitial, "Tier-A", "Tier-B");
        var exclusive = WorkbookImportSummaries.Build(exclusiveSource, matching, exclusiveChoices, evidence);
        Check("Z12p5y1 a missing Absent rule added in the sheet makes the reviewed pair exclusive", SubclassFindings(exclusive, "Overlapping subclass rules").Length == 0);
        var exclusivePanel = WorkbookImportSummaries.AcceptAll(exclusive, importer, exclusiveChoices, evidence, broadPfamConfirmed: true).Panel;
        Check("Z12p5y1 the corrected sheet yields distinct native assignments", exclusivePanel != null
            && SubclassAssignment.ClassifyDirect(exclusivePanel.Families.Single(family => family.Key == "F07"), new ProteinEvidence
                { Fragment = false, Pfam = new() { new() { Accession = "PF01234" } } }).SubclassKey == "Tier-A"
            && SubclassAssignment.ClassifyDirect(exclusivePanel.Families.Single(family => family.Key == "F07"), new ProteinEvidence
                { Fragment = false, Pfam = new() { new() { Accession = "PF01234" }, new() { Accession = "PF05678" } } }).SubclassKey == "Tier-B");
        Check("Z12p5y1 finding construction and acceptance leave input objects and workbook bytes unchanged", before == JsonSerializer.Serialize(source)
            && projectBefore == project.ToJsonText() && hash == TestFileHash.Upper(path));
    }

    private static void WorkbookSubclassFindingUiTests(string root)
    {
        string path = Path.Combine(root, "subclass-finding-ui.xlsx"); WriteSubclassFindingWorkbook(path);
        string hash = TestFileHash.Upper(path);
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (Width: 1366, Height: 768), (Width: 1920, Height: 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var project = SubclassFindingProject(); string before = project.ToJsonText();
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(project)),
                    workbookSource: new FamilyListWorkbookCoreSource(), openWorkbookReview: true);
                var window = new FamilyListImportWindow(model) { Width = size.Width, Height = size.Height,
                    Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                string label = "Z12p5y1 " + theme + " " + size.Width;
                try
                {
                    window.Show(); model.ConfirmWorkbookSheetChange = _ => true;
                    FinishReviewWork(() => model.LoadAsync(path));
                    FinishReviewWork(() => model.SelectWorkbookFamilySheetAsync("Data 9"));
                    // Model the explicit column choice independently of header inference.
                    model.Columns.Single(column => column.Role == WorkbookImportSummaryFields.Subclasses).Header = "h13";
                    ResolveWorkbookUiDraft(model); window.UpdateLayout(); PumpDispatcher();
                    foreach (var choice in model.SubclassChoices)
                    {
                        choice.Use = choice.Name is "Tier-A" or "Tier-B";
                        foreach (var rule in choice.Rules)
                        {
                            rule.Kind = ImportSubclassRuleKind.RequiredPfam;
                            rule.Use = choice.Use && rule.Accession.Length > 0;
                        }
                    }
                    window.UpdateLayout(); PumpDispatcher();
                    var findings = (ItemsControl)window.FindName("SubclassFindingList");
                    var scroll = (ScrollViewer)window.FindName("SubclassFindingScroll");
                    Check(label + " summary names the overlap and corrective choice before acceptance", model.ReviewingWorkbook
                        && findings.IsVisible && model.SubclassReviewFindings.Count(item => item.Label == "Overlapping subclass rules") == 1
                        && WalkTree(findings).OfType<TextBlock>().Any(text => text.Text.Contains("Tier-A and Tier-B", StringComparison.Ordinal))
                        && WalkTree(findings).OfType<TextBlock>().Any(text => text.Text.Contains("Leave one out", StringComparison.Ordinal)));
                    var overview = (FamilyListWorkbookOverview)window.FindName("WorkbookOverview");
                    var families = WalkTree(overview).OfType<DataGrid>().Single(grid => grid.ItemsSource is IEnumerable<ImportWorkbookFamily>);
                    Check(label + " findings leave a usable family preview table", families.IsVisible && families.ActualHeight >= 90,
                        "Family preview height: " + families.ActualHeight);
                    Check(label + " prose is retained as notes instead of extra selectable classes", model.SubclassChoices.Count == 4
                        && model.SubclassReviewFindings.Any(item => item.Label == "Subclass notes" && item.Value.Contains("Tier-AX", StringComparison.Ordinal)),
                        "Classes: " + string.Join(", ", model.SubclassChoices.Select(item => item.Name)) + "; findings: "
                            + string.Join("; ", model.SubclassReviewFindings.Select(item => item.Label + ": " + item.Value)));
                    Check(label + " overlap findings are read only and do not add an import blocker", model.CanAcceptWorkbook
                        && findings.ItemsSource is IReadOnlyList<ImportWorkbookFinding> && project.ToJsonText() == before,
                        string.Join("; ", model.WorkbookReviewProblems));
                    ScreenHarness.Save(window, Path.Combine(root, "finding-summary-" + theme + "-" + size.Width + ".png"), size.Width, size.Height);
                    FinishReviewWork(() => model.ReviewWorkbookSectionAsync("subclasses")); window.UpdateLayout(); PumpDispatcher();
                    var review = (FrameworkElement)window.FindName("WorkbookSubclassReview");
                    Check(label + " subclass page keeps the same findings visible without pushing choices off screen", model.ReviewingSubclasses
                        && findings.IsVisible && scroll.ActualHeight > 0 && scroll.ActualHeight <= 64 && review.ActualHeight >= 180
                        && review.TransformToAncestor(window).Transform(new Point(0, review.ActualHeight)).Y <= window.ActualHeight);
                    ScreenHarness.Save(window, Path.Combine(root, "finding-subclasses-" + theme + "-" + size.Width + ".png"), size.Width, size.Height);
                    model.SubclassChoices.Single(choice => choice.Name == "Tier-B").Use = false;
                    window.UpdateLayout(); PumpDispatcher();
                    Check(label + " leaving one out refreshes the finding on both pages without applying", model.SubclassReviewFindings.All(item => item.Label != "Overlapping subclass rules")
                        && project.ToJsonText() == before && hash == TestFileHash.Upper(path));
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }
}
