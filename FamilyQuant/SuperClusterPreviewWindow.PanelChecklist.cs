using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    private void AddChecklistPhrase_Click(object sender, RoutedEventArgs e)
    {
        model.AddChecklistPhrase();
        TaskScopeBox.BringIntoView(); TaskScopeBox.Focus();
    }
    private void PlaceChecklistMatches_Click(object sender, RoutedEventArgs e)
    {
        if (!model.CanPlaceChecklistMatches || model.Evidence?.SelectedChecklistFamily is not { } family) return;
        var rows = model.ChecklistOutsideRows();
        string target = family.TargetKey!;
        string name = model.Evidence.Source.PanelNames?.GetValueOrDefault(target) ?? family.Name;
        var families = model.Evidence.Source.Families.Where(row => row.Family != target).Append(new ReviewFamilyInput(target, name, 0, 0)).ToArray();
        var source = model.Evidence.Source with { Families = families };
        var editor = new ManualFamilyViewModel(new EvidenceReviewViewModel(source), rows);
        editor.SelectedOption = editor.Options.Single(option => option.Key == target);
        Evidence.EvidenceManualRequest? choice = null;
        if (ManualFamilyDialogOverride != null)
        {
            if (ManualFamilyDialogOverride(editor) && editor.CanConfirm) choice = editor.Capture();
        }
        else
        {
            var dialog = new ManualFamilyWindow(editor) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            if (dialog.ShowDialog() == true) choice = dialog.Choice;
        }
        if (choice == null) return;
        try
        {
            model.AddToFamily(choice);
            model.Evidence!.SelectedChecklistFamily = model.Evidence.PanelChecklist.Single(row => row.Key == family.Key);
        }
        catch (Exception error) { Operations.LogFailure(error); model.ReportManualFailure(error.Message); }
    }
}
