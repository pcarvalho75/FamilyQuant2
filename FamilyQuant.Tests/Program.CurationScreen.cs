using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string CurationDefaultVisibilityAssertion = "curation shows search IDs and hides decoys by default while keeping all document rows";

    private static void CurationScreenTests()
    {
        EnsureApplication();
        var file = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>
        {
            new() { SequenceIdentifier = "sp|P31987|example", Sequence = "PEPTIDEK", Description = "Fixture protein", UserID = 1, USearchID = 1 },
            new() { SequenceIdentifier = SequenceLabels.DecoyPrefix + "sp|P31987|example", Sequence = "KEDITPEP", Description = "Fixture decoy", UserID = 2, USearchID = 2 }
        } };
        var screen = new ClusterGenerationControl(file);
        var window = new Window { Content = screen, Width = 1024, Height = 768,
            Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show(); PumpDispatcher(); window.UpdateLayout();
        var table = UiField<DataGrid>(screen, "DataGridResultGrid")!;
        Check("curation always shows its table without Controls or Data Browser tabs",
            !WalkTree(screen).OfType<TabControl>().Any() && table.IsVisible && table.ActualHeight > 200);
        Check(CurationDefaultVisibilityAssertion,
            UiField<DataGridTextColumn>(screen, "ColumnUSearchId")!.Visibility == Visibility.Visible
            && UiField<CheckBox>(screen, "CheckBoxExcludeReverse")!.IsChecked == true
            && table.Items.Count == 1 && file.MyExtendedFastaItems.Count == 2);
        Check("curation shows accession in Locus while retaining the complete protein identifier",
            WalkTree(table).OfType<TextBlock>().Any(t => t.Text == "P31987")
            && !WalkTree(table).OfType<TextBlock>().Any(t => t.Text == "sp|P31987|example")
            && file.MyExtendedFastaItems[0].SequenceIdentifier == "sp|P31987|example");
        Check("the accession cell keeps the full database identifier in its tooltip",
            WalkTree(table).OfType<TextBlock>().Any(t => t.Text == "P31987" && t.ToolTip as string == "sp|P31987|example"));
        Check("curation is backed by its presentation model and a separate quantitation panel",
            screen.DataContext?.GetType().Name == "CurationViewModel"
            && screen.FindName("QuantitationPanel") is FrameworkElement);
        int documentChanges = 0;
        screen.DocumentChanged += (_, _) => documentChanges++;
        var annotationColumn = UiField<DataGridTextColumn>(screen, "ColumnUserAnnotation")!;
        var target = file.MyExtendedFastaItems[0];
        table.ScrollIntoView(target, annotationColumn); PumpDispatcher(); window.UpdateLayout();
        table.CurrentCell = new DataGridCellInfo(target, annotationColumn);
        bool editing = table.BeginEdit(); PumpDispatcher();
        var editor = annotationColumn.GetCellContent(target) as TextBox;
        if (editor != null) editor.Text = "Reviewed family name";
        table.CommitEdit(DataGridEditingUnit.Cell, true); table.CommitEdit(DataGridEditingUnit.Row, true);
        PumpDispatcher();
        Check("a native protein annotation edit marks the document and records one Undo step",
            editing && editor != null && target.UserAnnotation == "Reviewed family name"
            && documentChanges == 1 && screen.ViewModel.UndoStack.Count == 1);
        UiField<Button>(screen, "ButtonUndo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpDispatcher();
        Check("Undo restores the edited protein annotation and emits one more document change",
            target.UserAnnotation.Length == 0 && documentChanges == 2 && !screen.ViewModel.CanUndo);

        window.Close();
    }
}
