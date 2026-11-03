using System;
using System.Collections.Generic;
using System.Linq;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelEditorCommandTable() =>
    [
        new("--test-panel-editor", 74, TestCommandPhase.Raw,
            args => args.Contains("--test-panel-editor", StringComparer.Ordinal), (_, _) =>
            {
                PanelEditorTests();
                PanelEditorTemplateRoundtripTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void PanelEditorTests()
    {
        var apply = typeof(CurationViewModel).GetMethod("ApplyFamilyPanel", new[] { typeof(FamilyPanel) });
        Check("J1d panel application is available", apply != null);
        if (apply == null) return;
        var file = new FamilyClusterFile { FamilyPanel = new FamilyPanel { Name = "Before", Families = new() {
            new FamilyPanelFamily { Key = "old", DisplayName = "Old", TextFields = new() { new FamilyPanelText { Name = "Note", Text = "Retain" } } } } } };
        file.FamilyPanel.Families[0].Pfam.Add(new FamilyPanelDomain { Accession = "PF1", Name = "domain name" });
        file.FamilyPanel.Families[0].Subclasses.Add(new FamilyPanelSubclass { Key = "part", DisplayName = "Part name", RequiredPfam = new() { "PF1" } });
        var draft = new PanelEditorDraft(file.FamilyPanel);
        draft.Families[0].Name = "Edited";
        var changed = draft.Build();
        Check("J1d edits preserve domain and subclass details", changed.Families[0].Pfam[0].Name == "domain name"
            && changed.Families[0].Subclasses[0].RequiredPfam.SequenceEqual(new[] { "PF1" })
            && file.FamilyPanel.Families[0].DisplayName == "Old");
        var invalidDraft = new PanelEditorDraft(null);
        invalidDraft.Families.Add(new PanelEditorRow { Key = "new" });
        bool blankNameRefused = false;
        try { invalidDraft.Build(); } catch (ArgumentException) { blankNameRefused = true; }
        Check("J1e requires a name for an added family", blankNameRefused);
        var model = new CurationViewModel(file);
        var panel = FamilyPanelValidation.Clone(file.FamilyPanel);
        panel.Name = "After";
        panel.Families.Add(new FamilyPanelFamily { Key = "new", DisplayName = "New" });
        apply.Invoke(model, new object[] { panel });
        panel.Families[0].DisplayName = "External mutation";
        Check("J1d apply stores a detached panel and leaves metadata intact", file.FamilyPanel!.Name == "After"
            && file.FamilyPanel.Families[0].DisplayName == "Old" && file.FamilyPanel.Families[0].TextFields[0].Text == "Retain");
        Check("J1d apply creates one undo step", model.UndoStack.Count == 1);
        model.Undo();
        Check("J1d undo restores the prior panel", file.FamilyPanel!.Name == "Before" && file.FamilyPanel.Families.Count == 1);
        var invalid = new FamilyPanel { Families = new() { new FamilyPanelFamily { Key = "" } } };
        bool refused = false;
        try { apply.Invoke(model, new object[] { invalid }); } catch (System.Reflection.TargetInvocationException e) when (e.InnerException is ArgumentException) { refused = true; }
        Check("J1d invalid edits leave the project and undo stack untouched", refused && file.FamilyPanel.Name == "Before" && !model.CanUndo);
        EnsureApplication();
        var screen = new PanelEditorWindow(file.FamilyPanel, new PanelReaderFixture(), pfamReview: new PanelPfamFixture());
        screen.LoadColumns("fixture.xlsx", "Sheet", 2);
        ((ComboBox)screen.FindName("KeyColumn")).SelectedItem = "Code";
        ((ComboBox)screen.FindName("NameColumn")).SelectedItem = "Name";
        ((ComboBox)screen.FindName("SourceDecisionColumn")).SelectedItem = "Choice";
        ((ComboBox)screen.FindName("PfamColumn")).SelectedItem = "Domains";
        var preview = (Button)screen.FindName("ButtonPreviewPanel");
        preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check("J1d unmapped spreadsheet decisions do not change the draft", screen.Draft.Name == "Before"
            && ((TextBlock)screen.FindName("ImportStatus")).Text.Contains("map every decision", StringComparison.Ordinal));
        screen.DecisionMappings.Single(x => x.Source == "keep").Decision = FamilyPanelDecision.Quantify;
        screen.DecisionMappings.Single(x => x.Source == "join").Decision = FamilyPanelDecision.MergeIntoAnotherFamily;
        screen.DecisionMappings.Single(x => x.Source == "join").MergeTarget = "one";
        preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var imported = screen.Draft.Build();
        Check("J1d native column and decision choices preview the importer result without changing the project",
            imported.Families.Count == 2 && imported.Families[1].MergeTargetFamilyKey == "one"
            && imported.Families[0].Pfam[0].Accession == "PF12345" && file.FamilyPanel.Name == "Before"
            && imported.Import!.HeaderRowNumber == 2 && imported.DecisionMappings.Count == 2);
        ((Button)screen.FindName("ButtonCheckPfam")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var displayedChecks = ((DataGrid)screen.FindName("PfamGrid")).ItemsSource as IReadOnlyList<PanelPfamDisplayRow>;
        Check("J1d native Pfam action displays check rows without modifying the panel", displayedChecks?.Count == 1
            && displayedChecks[0].Status == "Name mismatch" && screen.Draft.Build().Families[0].Key == "one");
        screen.Close();
        var modal = new PanelEditorWindow(file.FamilyPanel) { WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        Exception? uiError = null;
        modal.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                modal.Draft.Families[0].Name = "Changed from screen";
                ((Button)modal.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex) { uiError = ex; modal.Close(); }
        }));
        var staleProject = new FamilyClusterFile { FamilyPanel = FamilyPanelValidation.Clone(file.FamilyPanel!),
            PanelProteinAssignments = new(), ProteinSubclassLabels = new() };
        string fingerprint = PanelDerivedDataCurrentness.Fingerprint(staleProject.FamilyPanel);
        staleProject.PanelProteinAssignmentsPanelFingerprint = PanelDerivedDataCurrentness.FamilyFingerprint(staleProject.FamilyPanel);
        staleProject.ProteinSubclassLabelsPanelFingerprint = fingerprint;
        var staleModel = new CurationViewModel(staleProject);
        var changedRules = FamilyPanelValidation.Clone(staleProject.FamilyPanel);
        changedRules.Families.Add(new FamilyPanelFamily { Key = "added", DisplayName = "Added" });
        staleModel.ApplyFamilyPanel(changedRules);
        bool warned = new PanelAssignmentReadiness().DescribeAfterChange(staleProject).Contains("Rebuild", StringComparison.Ordinal);
        staleModel.Undo();
        Check("J1d changed rules report stale assignments and Undo restores currentness", warned
            && !PanelDerivedDataCurrentness.Check(staleProject).IsOutOfDate && staleProject.ProteinSubclassLabels != null);
        var subclassOnly = FamilyPanelValidation.Clone(staleProject.FamilyPanel!);
        subclassOnly.Families[0].Subclasses[0].RequiredPfam.Add("PF2");
        staleModel.ApplyFamilyPanel(subclassOnly);
        var splitStatus = PanelDerivedDataCurrentness.Check(staleProject);
        Check("J1d subclass-only edits keep family assignments current while marking labels stale",
            splitStatus.FamilyAssignments == PanelDerivedDataStatus.Current && splitStatus.SubclassLabels == PanelDerivedDataStatus.OutOfDate);
        staleModel.Undo();
        bool? confirmed = modal.ShowDialog();
        if (confirmed == true && modal.Result != null) model.ApplyFamilyPanel(modal.Result);
        bool appliedFromScreen = uiError == null && confirmed == true && file.FamilyPanel!.Families[0].DisplayName == "Changed from screen";
        model.Undo();
        Check("J1d native apply changes the project and Undo restores it", appliedFromScreen && file.FamilyPanel!.Families[0].DisplayName == "Old",
            uiError?.Message ?? string.Empty);
        var refreshType = typeof(MainWindow).Assembly.GetType("FamilyQuant.PanelSubclassRefresh");
        var changedMethod = refreshType?.GetMethod("DefinitionsChanged");
        var refreshMethod = refreshType?.GetMethod("RecomputeIfCurrentAsync", new[] { typeof(bool), typeof(Func<bool>), typeof(Func<Task<bool>>) });
        Check("J1d subclass refresh coordinator is available", changedMethod != null && refreshMethod != null);
        if (changedMethod != null && refreshMethod != null)
        {
            var before = FamilyPanelValidation.Clone(file.FamilyPanel!);
            var after = FamilyPanelValidation.Clone(before);
            after.Families[0].Subclasses[0].RequiredPfam.Add("PF2");
            bool subclassChanged = (bool)changedMethod.Invoke(null, new object?[] { before, after })!;
            int runs = 0;
            Func<Task<bool>> run = () => { runs++; return Task.FromResult(true); };
            var skipped = (Task<bool>)refreshMethod.Invoke(null, new object[] { subclassChanged, new Func<bool>(() => false), run })!;
            var applied = (Task<bool>)refreshMethod.Invoke(null, new object[] { subclassChanged, new Func<bool>(() => true), run })!;
            Check("J1d subclass changes recompute only with current family assignments", subclassChanged && !skipped.GetAwaiter().GetResult()
                && applied.GetAwaiter().GetResult() && runs == 1);
            bool familyCurrent = false;
            int reviews = 0, refreshes = 0;
            Func<Task> review = () => { reviews++; familyCurrent = true; return Task.CompletedTask; };
            Func<Task<bool>> refresh = () => { refreshes++; return Task.FromResult(true); };
            bool both = PanelSubclassRefresh.ReviewThenRecomputeAsync(true, true, () => familyCurrent, () => true, review, refresh).GetAwaiter().GetResult();
            Check("J1d stale family review precedes subclass recomputation", both && reviews == 1 && refreshes == 1);
            familyCurrent = false; reviews = refreshes = 0;
            bool declined = PanelSubclassRefresh.ReviewThenRecomputeAsync(true, true, () => familyCurrent, () => false, review, refresh).GetAwaiter().GetResult();
            Check("J1d declining family review leaves subclass labels unchanged", !declined && reviews == 0 && refreshes == 0);
            familyCurrent = false;
            PanelSubclassRefresh.ReviewThenRecomputeAsync(true, false, () => familyCurrent, () => true, review, refresh).GetAwaiter().GetResult();
            Check("J1d family-only changes can request family review without a subclass run", reviews == 1 && refreshes == 0);
            reviews = refreshes = 0; familyCurrent = false;
            Func<Task> noCommit = () => { reviews++; return Task.CompletedTask; };
            bool notCommitted = PanelSubclassRefresh.ReviewThenRecomputeAsync(true, true, () => familyCurrent, () => true, noCommit, refresh).GetAwaiter().GetResult();
            Check("J1d cancelled or incomplete family review does not recompute subclasses", !notCommitted && reviews == 1 && refreshes == 0);
            familyCurrent = false; reviews = refreshes = 0;
            bool alreadyUpdated = PanelSubclassRefresh.ReviewThenRecomputeAsync(true, true, () => familyCurrent,
                () => true, review, refresh, () => true).GetAwaiter().GetResult();
            Check("J1d family review that already updated subclasses does not run them twice",
                alreadyUpdated && reviews == 1 && refreshes == 0);
            familyCurrent = false; reviews = refreshes = 0;
            bool reviewRefreshFailed = PanelSubclassRefresh.ReviewThenRecomputeAsync(true, true, () => familyCurrent,
                () => true, review, refresh, () => false).GetAwaiter().GetResult();
            Check("J1d failed or cancelled review refresh is not silently retried",
                !reviewRefreshFailed && reviews == 1 && refreshes == 0);
            familyCurrent = true; reviews = refreshes = 0;
            bool subclassOnlyUpdated = PanelSubclassRefresh.ReviewThenRecomputeAsync(false, true, () => familyCurrent,
                () => true, review, refresh, () => false).GetAwaiter().GetResult();
            Check("J1d subclass-only edits refresh without another family review",
                subclassOnlyUpdated && reviews == 0 && refreshes == 1);
        }
    }
    private sealed class PanelPfamFixture : IPanelPfamReview
    {
        public Task<IReadOnlyList<PanelPfamDisplayRow>> CheckAsync(FamilyPanel panel, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PanelPfamDisplayRow>>(new[] { new PanelPfamDisplayRow("one", "PF12345", "Name mismatch", "Review name") });
    }
    private sealed class PanelReaderFixture : IPanelSpreadsheetReader
    {
        public IReadOnlyList<string> ReadSheetNames(string path) => new[] { "Sheet" };
        public PanelSpreadsheetTable ReadTable(string path, string sheetName, int headerRowNumber = 1) => new()
        {
            SourceName = "fixture.xlsx", SourceSha256 = "fixture", SheetName = sheetName, HeaderRowNumber = headerRowNumber,
            Headers = new() { "Code", "Name", "Choice", "Domains" }, Rows = new()
            {
                new() { Cells = new() { ["Code"] = "one", ["Name"] = "First", ["Choice"] = "keep", ["Domains"] = "PF12345" } },
                new() { Cells = new() { ["Code"] = "two", ["Name"] = "Second", ["Choice"] = "join", ["Domains"] = "" } }
            }
        };
    }
}
