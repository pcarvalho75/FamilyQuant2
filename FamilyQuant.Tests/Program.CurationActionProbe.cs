using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void CurationReviewTextSnapshot(string output)
    {
        EnsureApplication();
        int windows = Application.Current.Windows.Count;
        var view = new DeNovoControl();
        view.Measure(new Size(1280, 900)); view.Arrange(new Rect(0, 0, 1280, 900)); view.UpdateLayout();
        PumpDispatcher();
        if (view.IsVisible || Application.Current.Windows.Count != windows)
            throw new InvalidOperationException("Review text capture must not show or create a window.");
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = UiField<TextBlock>(view, "TextBlockDeNovoStatus")!.Text,
            ["per-run"] = UiField<TextBlock>(view, "TextBlockPerRunBreakdown")!.Text,
            ["alternative-label"] = (string)UiField<Label>(view, "LabelOverlapDivergent")!.Content,
            ["exact-label"] = (string)UiField<Label>(view, "LabelOverlapExact")!.Content,
            ["precursor-label"] = (string)UiField<Label>(view, "LabelOverlapPrecursor")!.Content,
            ["reconcile-state"] = UiField<TextBlock>(view, "TextBlockDeNovoLayer2State")!.Text,
            ["homology-state"] = UiField<TextBlock>(view, "TextBlockDeNovoLayer4State")!.Text,
            ["confidence-tooltip"] = (string)UiField<TextBox>(view, "TextBoxMinAlc")!.ToolTip,
            ["load-tooltip"] = (string)UiField<Button>(view, "ButtonLoadAndFilter")!.ToolTip,
            ["curation-help"] = HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique),
            ["normalization-explanation"] = NormalizationMode.IdentifiedSignal.Explanation(),
        };
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "review-current-text.json"), values);
    }

    private sealed record CurationActionCase(string Id, string[] Handlers, string Compared, string Actual, bool Passed,
        string[]? Observations = null);

    private static List<CurationActionCase> CurationActionProbe(string output, FeatureLedger.Observation[] frozen, bool negative)
    {
        Directory.CreateDirectory(output);
        EnsureApplication();
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var oldFile = ShellDialogs.FilePresenter;
        var oldMessage = ShellMessages.Presenter;
        var oldSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(output, "preferences.json");
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        string nextFile = "";
        bool accept = false;
        var dialogs = new List<string>();
        var pickerValues = new List<(string Id, string Filter, string DefaultName)>();
        ShellDialogs.FilePresenter = (_, id, dialog) =>
        {
            dialogs.Add(id);
            pickerValues.Add((id, dialog.Filter, dialog.FileName));
            dialog.FileName = nextFile;
            return accept;
        };
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "sp|X|PROT1", Description = "Alpha fixture common", Sequence = "PEPTIDEA", UserID = 11, USearchID = 11, XICUser = 0.4 },
            new() { SequenceIdentifier = "target_b", Description = "Beta fixture common", Sequence = "PEPTIDER", UserID = 22, USearchID = 22, XICUser = 0 },
            new() { SequenceIdentifier = SequenceLabels.DecoyPrefix + "owned", Description = "Decoy fixture common", Sequence = "KEDITPEP", UserID = 33, USearchID = 33, XICUser = 0.2 },
            new() { SequenceIdentifier = SequenceLabels.ContaminantPrefix + "owned", Description = "Contaminant fixture common", Sequence = "PEPTIDEK", UserID = 44, USearchID = 44, XICUser = 0.1 }
        } };
        var screen = new ClusterGenerationControl(file);
        var window = OffscreenProofWindow(new Window { Content = screen, Width = 1366, Height = 820 });
        var cases = new List<CurationActionCase>();
        var table = UiField<DataGrid>(screen, "DataGridResultGrid")!;
        string IDs() => string.Join("|", table.Items.Cast<FastaItemExtended>().Select(item => item.SequenceIdentifier));
        void Record(string id, string[] handlers, string compared, bool matched, string actual, string[]? observations = null)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            bool rectangleRead = GetWindowRect(handle, out var bounds);
            bool isolated = HasProofWindowPlacement(window) && rectangleRead && bounds.Right < -10000 && bounds.Bottom < -10000 && GetForegroundWindow() != handle;
            if (!isolated) actual += "\nISOLATION FAILURE: placement=" + HasProofWindowPlacement(window)
                + "; rectangle=" + bounds.Left + "," + bounds.Top + "," + bounds.Right + "," + bounds.Bottom + "; foreground=" + (GetForegroundWindow() == handle);
            matched &= isolated;
            Check("curation native " + id + ": " + compared, matched);
            cases.Add(new(id, handlers, compared, actual, matched, observations));
        }
        void Click(string name)
        {
            if (name == "ButtonSaveReport") typeof(ClusterGenerationControl).GetMethod("ExportVisibleProteinTable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(screen, new object[] { screen });
            else if (name == "ButtonExportComposition") typeof(ClusterGenerationControl).GetMethod("ButtonExportComposition_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(screen, new object[] { screen, new RoutedEventArgs() });
            else
            {
                var button = UiField<Button>(screen, name)!;
                if (!button.IsEnabled || !WalkTree(screen).Contains(button)) throw new InvalidOperationException("Action is not attached and enabled: " + name);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            }
            FinishBusyOperation(screen.Operations); PumpDispatcher();
        }
        void Sort(DataGridColumn column)
        {
            typeof(DataGrid).GetMethod("OnSorting", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(table, [new DataGridSortingEventArgs(column)]);
            PumpDispatcher();
        }
        try
        {
            window.Show(); PumpDispatcher(); window.UpdateLayout();
            string original = file.ToJsonText();
            var reverse = UiField<CheckBox>(screen, "CheckBoxExcludeReverse")!;
            var contaminants = UiField<CheckBox>(screen, "CheckBoxExcludeContaminants")!;
            var quantified = UiField<CheckBox>(screen, "CheckBoxShowXICOnly")!;
            reverse.IsChecked = false; PumpDispatcher();
            string all = IDs();
            reverse.IsChecked = true; PumpDispatcher();
            bool hidden = table.Items.Count == 3 && table.Items.Cast<FastaItemExtended>().All(item => !item.SequenceIdentifier.StartsWith(SequenceLabels.DecoyPrefix, StringComparison.Ordinal));
            reverse.IsChecked = false; PumpDispatcher();
            Record("decoy-filter", ["CheckBoxExcludeReverse_CheckedChanged"], "actual Checked/Unchecked restores every persisted row and leaves exact curation bytes unchanged",
                hidden && all == IDs() && table.Items.Count == 4 && file.ToJsonText() == original, IDs());
            contaminants.IsChecked = true; PumpDispatcher();
            bool noContaminants = table.Items.Count == 3;
            contaminants.IsChecked = false; quantified.IsChecked = true; PumpDispatcher();
            bool positive = table.Items.Count == 3 && table.Items.Cast<FastaItemExtended>().All(item => item.XICUser > 0);
            quantified.IsChecked = false; PumpDispatcher();
            Record("display-filters", ["CheckBoxShowXICOnly_CheckedChanged"], "contaminant and positive-share Checked/Unchecked change the visible projection and restore all four rows without editing the document",
                noContaminants && positive && table.Items.Count == 4 && file.ToJsonText() == original, IDs());
            var column = UiField<DataGridTextColumn>(screen, "ColumnUserId")!;
            Sort(column); string ascending = IDs(); Sort(column); string descending = IDs(); Sort(column);
            Record("sort-toggle", ["DataGridResultGrid_Sorting"], "native Sorting toggles ascending/descending and restores the identical ascending order with unchanged stored values",
                ascending == all && descending == string.Join("|", file.MyExtendedFastaItems.AsEnumerable().Reverse().Select(item => item.SequenceIdentifier))
                && ascending == IDs() && original == file.ToJsonText(), ascending + " -> " + descending + " -> " + IDs());
            window.UpdateLayout();
            var rows = WalkTree(table).OfType<DataGridRow>().Where(row => row.Item is FastaItemExtended).ToArray();
            Record("row-numbers", ["DataGridResultGrid_LoadingRow"], "actual realized grid rows display their own one-based index",
                rows.Length == 4 && rows.All(row => row.Header?.ToString() == (row.GetIndex() + 1).ToString(CultureInfo.CurrentCulture)),
                string.Join("|", rows.Select(row => row.Header)));

            table.SelectedItems.Clear(); table.SelectedItems.Add(file.MyExtendedFastaItems[0]); PumpDispatcher();
            bool oneDisabled = !UiField<Button>(screen, "ButtonAlignSelected")!.IsEnabled;
            table.SelectedItems.Add(file.MyExtendedFastaItems[1]); PumpDispatcher();
            Record("selection-gate", ["DataGridResultGrid_SelectionChanged"], "one selected protein disables alignment and two enable it, without a document change",
                oneDisabled && UiField<Button>(screen, "ButtonAlignSelected")!.IsEnabled && original == file.ToJsonText(), table.SelectedItems.Count.ToString(CultureInfo.InvariantCulture));
            UiField<TextBox>(screen, "TextBoxBatchUserId")!.Text = "77";
            UiField<TextBox>(screen, "TextBoxBatchAnnotation")!.Text = "Owned batch annotation";
            var batchTargets = table.SelectedItems.Cast<FastaItemExtended>().ToArray();
            var notSelected = file.MyExtendedFastaItems.Except(batchTargets).Select(item => (Item: item, Id: item.UserID)).ToArray();
            Click("ButtonApplyToSelected");
            bool edited = batchTargets.Length == 2 && batchTargets.All(item => item.UserID == 77 && item.UserAnnotation == "Owned batch annotation")
                && notSelected.All(value => value.Item.UserID == value.Id) && screen.ViewModel.UndoStack.Count == 1;
            string batchActual = string.Join("|", batchTargets.Select(item => item.SequenceIdentifier + ":" + item.UserID + ":" + item.UserAnnotation));
            Click("ButtonUndo");
            Record("batch-and-undo", ["ButtonApplyToSelected_Click"], "actual batch button edits only selected family IDs/annotations, records one Undo and restores the complete pre-edit document",
                edited && file.ToJsonText() == original && screen.ViewModel.UndoStack.Count == 0, batchActual + "\nUndo:\n" + file.ToJsonText());

            // BeginEdit can activate an editor's owning HWND. Emit the actual grid event with
            // its actual column binding on a detached editor instead; never request input focus.
            var target = file.MyExtendedFastaItems[0];
            table.SelectedItems.Clear();
            var editor = new TextBox { DataContext = target };
            BindingOperations.SetBinding(editor, TextBox.TextProperty, column.Binding);
            editor.SetCurrentValue(TextBox.TextProperty, "501");
            var edit = new DataGridCellEditEndingEventArgs(column, new DataGridRow { Item = target }, editor, DataGridEditAction.Commit);
            typeof(DataGrid).GetMethod("OnCellEditEnding", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(table, [edit]);
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); PumpDispatcher();
            bool cellEdited = !edit.Cancel && target.UserID == 501 && file.LockedUserIds.Contains(11) && file.LockedUserIds.Contains(501)
                && screen.ViewModel.UndoStack.Count == 1;
            Click("ButtonUndo");
            Record("cell-edit", ["DataGridResultGrid_CellEditEnding"], "the actual grid CellEditEnding event and column binding renumber, lock both families, record one Undo and restore every curation byte without activating an editor",
                cellEdited && original == file.ToJsonText(), file.ToJsonText());

            string plp = WriteDenominatorFixture(output, "owned.plp", "NONE");
            string plpHash = FeatureLedgerEvidence.Hash(plp);
            accept = false; dialogs.Clear(); Click("ButtonLoadQuantData");
            bool cancelledPicker = dialogs.SequenceEqual(new[] { "curation.patternlab" }) && !screen.ViewModel.Quantitation.HasPatternLab && original == file.ToJsonText();
            nextFile = plp; accept = true; Click("ButtonLoadQuantData");
            Record("patternlab-load", ["ButtonLoadQuantData_Click"], "the actual PLP chooser Cancel preserves the document; acceptance loads the native fixture, publishes displayed mode and leaves input bytes unchanged",
                cancelledPicker && screen.ViewModel.Quantitation.HasPatternLab && screen.ViewModel.Quantitation.DisplayedMode == NormalizationMode.IdentifiedSignal
                && FeatureLedgerEvidence.Hash(plp) == plpHash && target.XICUser > 0, screen.ShellStatusText);

            string beforeAnnotation = file.ToJsonText();
            table.SelectedItems.Clear();
            var annotationTargets = table.Items.Cast<FastaItemExtended>().Take(2).ToArray();
            foreach (var item in annotationTargets) table.SelectedItems.Add(item);
            var unchangedNumbers = file.MyExtendedFastaItems.Select(item => (item.UserID, item.XICUser, item.FullXicShare)).ToArray();
            const string annotation = "Owned annotation, \"quoted\"";
            UiField<TextBox>(screen, "TextBoxBatchUserId")!.Text = "";
            UiField<TextBox>(screen, "TextBoxBatchAnnotation")!.Text = annotation;
            Click("ButtonApplyToSelected");
            bool numbersUnchanged = unchangedNumbers.SequenceEqual(file.MyExtendedFastaItems.Select(item => (item.UserID, item.XICUser, item.FullXicShare)));
            string annotationFile = Path.Combine(output, "annotations.fcf");
            file.Save(annotationFile);
            var reopened = FamilyClusterFile.Load(annotationFile);
            string annotationCsv = CsvReport.Build(file.MyExtendedFastaItems, screen.ViewModel.Quantitation.DisplayedMode);
            File.WriteAllText(Path.Combine(output, "annotations.csv"), annotationCsv);
            bool annotationsPersisted = annotationTargets.Length == 2
                && reopened.MyExtendedFastaItems.Count(item => item.UserAnnotation == annotation) == 2
                && annotationTargets.All(item => reopened.MyExtendedFastaItems.Single(saved => saved.SequenceIdentifier == item.SequenceIdentifier).UserAnnotation == annotation)
                && annotationCsv.Split('"' + annotation.Replace("\"", "\"\"", StringComparison.Ordinal) + '"', StringSplitOptions.None).Length == 3;
            Click("ButtonUndo");
            Record("annotation-storage", [], "with PatternLab quantitation loaded, the annotation-only native batch leaves every family ID and share value equal through recalculation, saves and reopens the two exact annotations through .fcf, exports both quoted CSV fields, and Undo restores all pre-edit document bytes",
                numbersUnchanged && annotationsPersisted && file.ToJsonText() == beforeAnnotation, annotationCsv,
                ["FQ15-B2F050B733BC"]);

            double oldShare = target.XICUser;
            UiField<ComboBox>(screen, "ComboBoxNormalization")!.SelectedIndex = (int)NormalizationMode.TotalChromatogram;
            UiField<ComboBox>(screen, "ComboBoxSharedPeptides")!.SelectedIndex = 1; PumpDispatcher();
            bool pending = target.XICUser == oldShare && screen.ViewModel.Quantitation.DisplayedMode == NormalizationMode.IdentifiedSignal;
            Click("ButtonRecalculateQuantitation");
            Record("normalization-recalculate", ["ComboBoxNormalization_SelectionChanged", "ButtonRecalculateQuantitation_Click"],
                "both native selectors leave old displayed shares labelled with their original mode until the Recalculate button publishes the chosen mode",
                pending && screen.ViewModel.Quantitation.DisplayedMode == NormalizationMode.TotalChromatogram && target.XICUser != oldShare,
                screen.ViewModel.Quantitation.DisplayedMode + ":" + target.XICUser.ToString("R", CultureInfo.InvariantCulture));
            string reportPath = Path.Combine(output, "visible.csv"); nextFile = reportPath; accept = true;
            Click("ButtonSaveReport");
            string heading = (string)typeof(ClusterGenerationControl).GetMethod("FullXicCsvHeader", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(screen, null)!;
            string expectedCsv = CsvReport.Build(table.Items.Cast<FastaItemExtended>(), screen.ViewModel.Quantitation.DisplayedMode, heading);
            bool exactReport = File.ReadAllText(reportPath) == expectedCsv;
            string reportHash = FeatureLedgerEvidence.Hash(reportPath);
            File.SetAttributes(reportPath, File.GetAttributes(reportPath) | FileAttributes.ReadOnly);
            try { Click("ButtonSaveReport"); }
            finally { File.SetAttributes(reportPath, File.GetAttributes(reportPath) & ~FileAttributes.ReadOnly); }
            Record("protein-export", ["ButtonSaveReport_Click"], "the actual export button writes every currently visible row with the displayed denominator; a native write refusal preserves the previous file bytes",
                exactReport && reportHash == FeatureLedgerEvidence.Hash(reportPath) && !screen.Operations.IsBusy, expectedCsv);

            screen.SetFullXicQuant(new[] { new QuantRow { Run = "owned", Side = "identified", Sequence = "PEPTIDEA", Charge = 2,
                PrecursorMz = 500, EnvelopeSum = 100, IsotopeAreas = new[] { 100.0 } } }, new Dictionary<string, double> { ["owned"] = 1000 });
            UiField<ComboBox>(screen, "ComboBoxFullXicBasis")!.SelectedIndex = 1; PumpDispatcher();
            Record("fullxic-basis", ["ComboBoxFullXicBasis_SelectionChanged"], "native Full XIC basis selection applies the recorded TIC denominator to the attached synthetic envelope",
                screen.HasFullXic && target.FullXicShare is double share && Math.Abs(share - 0.1) < 1e-12,
                target.FullXicShare?.ToString("R", CultureInfo.InvariantCulture) ?? "unavailable");
            string composition = screen.FullXicCompositionCsv()!;
            string compositionPath = Path.Combine(output, "composition.csv"); nextFile = compositionPath;
            Click("ButtonExportComposition");
            Record("composition-export", ["ButtonExportComposition_Click"], "the actual composition export button writes the exact production table for the current denominator and shared-peptide choice",
                composition.Length > 0 && File.ReadAllText(compositionPath) == composition, composition);

            string FrozenValue(string id) => System.Text.Json.JsonSerializer.Deserialize<string>(frozen.Single(row => row.Id == id).Detail)!;
            var proteinPicker = pickerValues.First(value => value.Id == "curation.proteins-csv");
            var compositionPicker = pickerValues.First(value => value.Id == "curation.composition-csv");
            var plpPicker = pickerValues.First(value => value.Id == "curation.patternlab");
            string observedCsvFilter = proteinPicker.Filter + (negative ? " PLANTED WRONG FILTER" : "");
            string patterns(string filter) => string.Join("|", filter.Split('|').Where((_, index) => index % 2 == 1));
            bool pickersMatch = observedCsvFilter == FrozenValue("FQ15-93D5A5132CAD")
                && proteinPicker.DefaultName == FrozenValue("FQ15-F31EFE95C8DB")
                && compositionPicker.Filter == FrozenValue("FQ15-32A336B58FA8")
                && compositionPicker.DefaultName == FrozenValue("FQ15-4C83255498C2")
                && patterns(plpPicker.Filter) == patterns(FrozenValue("FQ15-B3488AD4ABFA"));
            Record("native-picker-values", [], "executed protein/composition CSV chooser filters and default filenames equal their frozen literals before the presenter supplies a path; the PLP chooser retains the frozen *.plp selection pattern and its accepted file was actually loaded",
                pickersMatch, System.Text.Json.JsonSerializer.Serialize(new
                {
                    protein = new { proteinPicker.Filter, proteinPicker.DefaultName },
                    composition = new { compositionPicker.Filter, compositionPicker.DefaultName },
                    patternlab = new { plpPicker.Filter, plpPicker.DefaultName }, observedCsvFilter
                }),
                ["FQ15-93D5A5132CAD", "FQ15-F31EFE95C8DB", "FQ15-32A336B58FA8", "FQ15-4C83255498C2", "FQ15-B3488AD4ABFA"]);

            file.LockedUserIds.Add(11); string beforeWords = file.ToJsonText();
            UiField<DecimalUpDown>(screen, "DecimalUpDownWordReCluster")!.Value = 0;
            Click("ButtonWordCluster");
            bool regrouped = target.UserID == 11 && file.MyExtendedFastaItems.Skip(1).Select(item => item.UserID).Distinct().Count() == 1;
            Click("ButtonUndo");
            Record("word-grouping", ["ButtonWordCluster_Click"], "the native word-grouping button honors a user lock, groups the remaining synthetic descriptions and Undo restores the pre-grouping document",
                regrouped && beforeWords == file.ToJsonText(), file.ToJsonText());
            int previews = 0;
            screen.ConfirmSuperClusterOverride = preview => { previews++; preview.KeywordsText = "fixture"; preview.ExcludeText = "excluded"; preview.Match = KeywordMatch.Any; preview.Refresh(); return false; };
            Click("ButtonSuperCluster");
            bool cancelledReview = previews == 1 && beforeWords == file.ToJsonText();
            screen.ConfirmSuperClusterOverride = preview => { previews++; preview.KeywordsText = "fixture"; preview.ExcludeText = "excluded"; preview.Match = KeywordMatch.Any; preview.Refresh(); return true; };
            Click("ButtonSuperCluster");
            bool grouped = previews == 2 && file.IsKeywordSuperCluster && target.UserID == 11 && screen.ViewModel.CanUndo;
            Click("ButtonUndo");
            Record("keyword-grouping", ["ButtonSuperCluster_Click"], "the actual keyword button prepares its review, Cancel preserves all bytes, accepted supplied settings group unlocked families and one Undo restores the document",
                cancelledReview && grouped && beforeWords == file.ToJsonText(), file.ToJsonText());
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "actions.json"), cases);
            return cases;
        }
        finally
        {
            window.Close(); ShellDialogs.FilePresenter = oldFile; ShellMessages.Presenter = oldMessage;
            MainWindow.ShellSettingsPathOverride = oldSettings; SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }
}
