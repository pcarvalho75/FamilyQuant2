using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FamilyViewTests()
    {
        EnsureApplication();
        var file = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>
        {
            new() { SequenceIdentifier = "family_view_1", Description = "First fixture protein", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 },
            new() { SequenceIdentifier = "family_view_2", Description = "First fixture protein", Sequence = "PEPTIDER", UserID = 1, USearchID = 1 },
            new() { SequenceIdentifier = "family_view_3", Description = "Second fixture protein", Sequence = "PEPTIDEA", UserID = 2, USearchID = 2 }
        } };
        var screen = new ClusterGenerationControl(file);
        bool ready = screen.FindName("DataGridFamilies") is DataGrid && typeof(CurationViewModel).GetProperty("Families") != null;
        Check("family curation provides a family overview beside the protein table", ready);
        if (!ready) return;
        var model = screen.ViewModel;
        var families = (DataGrid)screen.FindName("DataGridFamilies");
        var proteins = UiField<DataGrid>(screen, "DataGridResultGrid")!;
        var window = new Window { Content = screen, Width = 1366, Height = 820, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show(); PumpDispatcher(); window.UpdateLayout();
        try
        {
            int changes = 0; screen.DocumentChanged += (_, _) => changes++;
            string before = file.ToJsonText();
            Check("family rows use the production family name and whole-family target count",
                model.Families.Count == 2 && model.Families[0].Proteins == 2
                && model.Families[0].Name == FamilyNaming.Name(file.MyExtendedFastaItems.Where(p => p.UserID == 1)));
            families.SelectedItem = model.Families.First(r => r.FamilyId == 1); PumpDispatcher();
            Check("native family selection shows only its proteins without changing the document",
                proteins.Items.Cast<FastaItemExtended>().Select(p => p.SequenceIdentifier).SequenceEqual(new[] { "family_view_1", "family_view_2" })
                && before == file.ToJsonText() && changes == 0);
            families.SelectedItem = model.Families.First(r => r.FamilyId == 2); PumpDispatcher();
            Check("selecting another family replaces the beside-table projection",
                proteins.Items.Count == 1 && ((FastaItemExtended)proteins.Items[0]).SequenceIdentifier == "family_view_3");
            model.SelectedFamily = null; PumpDispatcher();
            Check("all-protein mode restores every filtered row without a curation change", proteins.Items.Count == 3 && changes == 0);

            families.SelectedItem = model.Families.First(r => r.FamilyId == 1); PumpDispatcher();
            var family = model.SelectedFamily!;
            var nameColumn = UiField<DataGridTextColumn>(screen, "ColumnFamilyName")!;
            families.ScrollIntoView(family, nameColumn); families.CurrentCell = new DataGridCellInfo(family, nameColumn);
            families.BeginEdit(); PumpDispatcher();
            var editor = nameColumn.GetCellContent(family) as TextBox;
            if (editor != null) editor.Text = "Reviewed fixture family";
            families.CommitEdit(DataGridEditingUnit.Cell, true); families.CommitEdit(DataGridEditingUnit.Row, true); PumpDispatcher();
            Check("native family name editing updates all target annotations and records one Undo",
                editor != null && file.MyExtendedFastaItems.Where(p => p.UserID == 1).All(p => p.UserAnnotation == "Reviewed fixture family")
                && changes == 1 && model.UndoStack.Count == 1);
            UiField<Button>(screen, "ButtonUndo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            Check("Undo from the family view restores names and retains the selected family",
                file.MyExtendedFastaItems.All(p => p.UserAnnotation.Length == 0) && model.SelectedFamily?.FamilyId == 1
                && model.UndoStack.Count == 0 && changes == 2);

            family = model.SelectedFamily!;
            families.ScrollIntoView(family); PumpDispatcher(); window.UpdateLayout();
            var row = (DataGridRow)families.ItemContainerGenerator.ContainerFromItem(family);
            var tick = WalkTree(row).OfType<CheckBox>().First();
            ((System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.CheckBoxAutomationPeer(tick)
                .GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)!).Toggle();
            PumpDispatcher();
            Check("the accessible family lock action updates the stored lock and one Undo step",
                file.LockedUserIds.Contains(1) && model.UndoStack.Count == 1 && changes == 3);
            UiField<Button>(screen, "ButtonUndo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            Check("Undo restores family lock state in both the document and overview",
                !file.LockedUserIds.Contains(1) && model.SelectedFamily?.IsLocked == false && changes == 4);
            screen.SetFullXicQuant(new[]
            {
                new QuantRow { Side = "identified", Run = "family_fixture", Sequence = "PEPTIDEK", Charge = 2,
                    PrecursorMz = 500, EnvelopeSum = 100, IsotopeAreas = new[] { 100.0 } },
                new QuantRow { Side = "identified", Run = "family_fixture", Sequence = "PEPTIDEA", Charge = 2,
                    PrecursorMz = 600, EnvelopeSum = 200, IsotopeAreas = new[] { 200.0 } }
            }, new Dictionary<string, double> { ["family_fixture"] = 1000 });
            PumpDispatcher();
            var share = model.Families.Single(r => r.FamilyId == 1);
            Check("family overview uses one production family share instead of summing repeated protein shares",
                share.Proteins == 2 && share.FullXicShare is double signal && Math.Abs(signal - 100.0 / 300) < 1e-12
                && file.MyExtendedFastaItems.Where(p => p.UserID == 1).All(p => p.FullXicShare == share.FullXicShare));
            Check("attaching stored quantitation retains the family selection without a curation edit",
                model.SelectedFamily?.FamilyId == 1 && proteins.Items.Count == 2 && changes == 4);
        }
        finally { window.Close(); }

        var largeFile = new FamilyClusterFile { MyExtendedFastaItems = Enumerable.Range(1, 39996).Select(i =>
            new FastaItemExtended { SequenceIdentifier = "large_family_" + i, Description = "Fixture family " + i,
                Sequence = "PEPTIDEK", UserID = i, USearchID = i }).ToList() };
        var largeScreen = new ClusterGenerationControl(largeFile);
        var largeWindow = new Window { Content = largeScreen, Width = 1366, Height = 820, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        largeWindow.Show(); PumpDispatcher(); largeWindow.UpdateLayout();
        try
        {
            var grid = (DataGrid)largeScreen.FindName("DataGridFamilies");
            var last = largeScreen.ViewModel.Families[^1];
            grid.ScrollIntoView(last); PumpDispatcher(); largeWindow.UpdateLayout();
            grid.SelectedItem = last; PumpDispatcher();
            Check("39996 family rows retain recycling virtualization and select the last family without realizing the collection",
                grid.Items.Count == 39996 && grid.EnableRowVirtualization && grid.EnableColumnVirtualization
                && VirtualizingPanel.GetVirtualizationMode(grid) == VirtualizationMode.Recycling
                && WalkTree(grid).OfType<DataGridRow>().Count() < 100
                && grid.ItemContainerGenerator.ContainerFromItem(last) is DataGridRow
                && UiField<DataGrid>(largeScreen, "DataGridResultGrid")!.Items.Count == 1);
        }
        finally { largeWindow.Close(); }

    }
}
