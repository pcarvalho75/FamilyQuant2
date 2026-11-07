using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ManualFamilyUiTests(string? output = null)
    {
        Section("Hand-added families at the interface - highlighted rows, clear choices and refusal");
        EnsureApplication();
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (Width: 1366, Height: 768), (Width: 1920, Height: 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var file = ManualChoiceFile(); string before = file.ToJsonText();
                var session = new EvidenceGroupingSession(file, "manual UI fixture");
                var input = session.Propose("scope");
                var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null),
                    input, session.Propose, session.CountMotif, initialScope: "scope", evidenceMode: true,
                    addToFamily: session.AddToFamily, removeFromFamily: session.RemoveFromFamily, updateManualChoices: session.UpdateManualChoices)
                { Width = size.Width, Height = size.Height, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    var tabs = (TabControl)window.FindName("evidenceTabs");
                    tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Unassigned"); PumpDispatcher();
                    var unassigned = (DataGrid)window.FindName("unassignedGrid"); unassigned.SelectedItem = model.Evidence!.Unassigned.Single();
                    window.ManualFamilyDialogOverride = editor => { editor.SelectedOption = editor.Options.First(o => !o.IsNew); editor.Note = "Supplied UI note"; return true; };
                    ((Button)window.FindName("addUnassignedFamilyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    string label = theme + " " + size.Width;
                    var added = model.Evidence!.Proteins.Single(p => p.ProteinIndex == 2);
                    Check(label + " Unassigned Add opens the actual hand-choice path and focuses its ticked row", added.Tier == "Added by hand" && added.Selected
                        && added.Reason.Contains("Supplied UI note", StringComparison.Ordinal) && (string)((TabItem)tabs.SelectedItem).Header == "Proteins"
                        && model.Evidence.ProteinDetails.Contains("Automatic evidence before the hand choice", StringComparison.Ordinal));
                    added.Selected = false; FinishReviewWork(model.UpdateProposalAsync); PumpDispatcher();
                    Check(label + " Update proposal retains the hand choice, note and deliberate untick", model.Evidence!.Proteins.Single(p => p.ProteinIndex == 2) is { Tier: "Added by hand", Selected: false }
                        && model.Evidence.Proteins.Single(p => p.ProteinIndex == 2).Reason.Contains("Supplied UI note", StringComparison.Ordinal));
                    var grid = (DataGrid)window.FindName("evidenceGrid");
                    model.Evidence.SelectedFamily = null; grid.SelectedItem = model.Evidence.Proteins.Single(p => p.ProteinIndex == 2); PumpDispatcher();
                    ((Button)window.FindName("removeFamilyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    Check(label + " actual Remove restores the unassigned automatic row", model.Evidence!.Proteins.Single(p => p.ProteinIndex == 2) is { Family: "", Tier: "Unassigned", Selected: false });
                    model.Evidence.Proteins.Single(p => p.ProteinIndex == 1).Selected = false;
                    grid.SelectedItem = model.Evidence.Proteins.Single(p => p.ProteinIndex == 2);
                    window.ManualFamilyDialogOverride = editor => { editor.SelectedOption = editor.Options.Single(o => o.IsNew); editor.NewName = "Invented UI family"; editor.Note = "New UI family note"; return true; };
                    ((Button)window.FindName("addFamilyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    Check(label + " New family uses the typed name and preserves other unticks", model.Evidence!.Proteins.Single(p => p.ProteinIndex == 2).FamilyDisplay == "Invented UI family"
                        && model.Evidence.Proteins.Single(p => p.ProteinIndex == 2).Selected && !model.Evidence.Proteins.Single(p => p.ProteinIndex == 1).Selected
                        && model.ApplyHint.Contains("added by hand", StringComparison.Ordinal));
                    if (output != null)
                    {
                        string path = Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Width + "x" + size.Height + "-review.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!); ScreenHarness.Save(window, path, size.Width, size.Height);
                    }
                    model.Evidence.SelectedFamily = model.Evidence.Families.Single(f => f.Name == "Invented UI family");
                    tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Architecture"); PumpDispatcher();
                    var architectureControl = (ArchitectureReviewControl)window.FindName("architectureReview");
                    var architectureGrid = (DataGrid)architectureControl.FindName("groupGrid");
                    var manualGroup = model.Evidence.SelectedArchitecture!.Groups.Single();
                    var nameColumn = architectureGrid.Columns.OfType<DataGridTextColumn>().Single(c => (string)c.Header == "New family name");
                    Check(label + " the native Architecture name cell displays the typed hand-family name", manualGroup.Name == "Invented UI family (no domain calls)"
                        && nameColumn.GetCellContent(manualGroup) is TextBlock nameCell && nameCell.Text == manualGroup.Name);
                    if (output != null) ScreenHarness.Save(window, Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Width + "-architecture.png"), size.Width, size.Height);
                    tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "Proteins"); PumpDispatcher();
                    var editorModel = new ManualFamilyViewModel(model.Evidence, new[] { model.Evidence.Proteins.Single(p => p.ProteinIndex == 2) });
                    editorModel.SelectedOption = editorModel.Options.Single(o => o.IsNew);
                    Check(label + " dialog refuses blank and duplicate names and multiline notes", !editorModel.CanConfirm);
                    editorModel.NewName = "Invented family";
                    Check(label + " dialog directs duplicate names to the existing family", !editorModel.CanConfirm && editorModel.Validation.Contains("already proposed", StringComparison.Ordinal));
                    editorModel.NewName = "Another invented family"; editorModel.Note = "bad\nnote";
                    Check(label + " dialog refuses multiline notes", !editorModel.CanConfirm);
                    editorModel.Note = "A supplied one-line explanation";
                    var dialog = new ManualFamilyWindow(editorModel) { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false };
                    try
                    {
                        dialog.Show(); PumpDispatcher();
                        Check(label + " dialog has one enabled primary action and visible hand-choice consequence", ((Button)dialog.FindName("addManualButton")).IsEnabled
                            && WalkTree(dialog).OfType<Button>().Count(b => ReferenceEquals(b.Style, dialog.FindResource("PrimaryButton"))) == 1
                            && editorModel.Validation.Contains("ticked as Added by hand", StringComparison.Ordinal));
                        if (output != null) ScreenHarness.Save(dialog, Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Width + "-dialog.png"), 820, 540);
                    }
                    finally { dialog.Close(); }
                    var locked = new EvidenceReviewViewModel(input with { Proteins = input.Proteins.Select(p => p.ProteinIndex == 0 ? p with { IsLocked = true } : p).ToArray() });
                    var lockedEditor = new ManualFamilyViewModel(locked, new[] { locked.Proteins[0] });
                    Check(label + " dialog refuses a locked protein with an actionable reason", !lockedEditor.CanConfirm
                        && lockedEditor.Validation.Contains("Locked proteins cannot be added", StringComparison.Ordinal));
                    Check(label + " dialog and proposal edits never write the project", file.ToJsonText() == before);
                }
                finally { window.Close(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
        EvidenceApplyTests();
        EvidenceSessionApplyTests();
        EvidenceGroupingFlowTests();
    }
}
