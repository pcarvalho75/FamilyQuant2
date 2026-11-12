using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    private double simpleProteinMinimum = 296;

    private void ConstrainReviewLayout()
    {
        if (!IsLoaded || model.ModeIndex != 1 || !EvidenceLayout.IsVisible) return;
        double height = Math.Max(0, ReviewRootGrid.RowDefinitions[1].ActualHeight
            - EvidenceLayout.TranslatePoint(new Point(), ReviewModeTabs).Y - EvidenceLayout.Margin.Bottom - 2);
        if (Math.Abs(EvidenceLayout.MaxHeight - height) > .5) EvidenceLayout.MaxHeight = height;
        double available = Math.Max(0, height - ReviewScopeHeader.ActualHeight - ReviewScopeHeader.Margin.Top - ReviewScopeHeader.Margin.Bottom
            - (ShowAllOptions ? MotifRow.ActualHeight + MotifRow.Margin.Top + MotifRow.Margin.Bottom : 0));
        double minimum = Math.Min(ShowAllOptions ? 470 : 390, Math.Max(0, available - (ShowAllOptions ? Math.Min(48, available / 5) : 0)));
        if (Math.Abs(ReviewTablesRow.MinHeight - minimum) > .5) ReviewTablesRow.MinHeight = minimum;
        ConstrainReviewTables();
    }

    private void ConstrainReviewTables()
    {
        if (!IsLoaded || model.ModeIndex != 1 || !EvidenceLayout.IsVisible) return;
        double tableHeight = LayoutInformation.GetLayoutSlot(ReviewTablesGrid).Height;
        if (tableHeight > 0 && Math.Abs(ReviewTablesGrid.MaxHeight - tableHeight) > .5) ReviewTablesGrid.MaxHeight = tableHeight;
        if (ChecklistLayout.IsVisible)
        {
            double checklistHeight = LayoutInformation.GetLayoutSlot(ChecklistLayout).Height;
            if (checklistHeight > 0)
            {
                if (Math.Abs(ChecklistLayout.MaxHeight - checklistHeight) > .5) ChecklistLayout.MaxHeight = checklistHeight;
                double checklistRoom = Math.Max(0, checklistHeight - PanelChecklistHintText.ActualHeight
                    - PanelChecklistHintText.Margin.Top - PanelChecklistHintText.Margin.Bottom);
                double checklistMinimum = Math.Min(110, checklistRoom * .55);
                if (Math.Abs(ChecklistTableRow.MinHeight - checklistMinimum) > .5) ChecklistTableRow.MinHeight = checklistMinimum;
                double toolsMaximum = Math.Max(0, checklistRoom - checklistMinimum);
                if (Math.Abs(ChecklistToolsScroller.MaxHeight - toolsMaximum) > .5) ChecklistToolsScroller.MaxHeight = toolsMaximum;
            }
        }
        if (!ProteinLayout.IsVisible) return;
        double proteinHeight = LayoutInformation.GetLayoutSlot(ProteinLayout).Height;
        if (proteinHeight <= 0) return;
        if (Math.Abs(ProteinLayout.MaxHeight - proteinHeight) > .5) ProteinLayout.MaxHeight = proteinHeight;
        double room = Math.Max(0, proteinHeight - ProteinLayout.RowDefinitions[1].ActualHeight
            - ProteinDetailsHeader.ActualHeight - ProteinDetailsHeader.Margin.Top - ProteinDetailsHeader.Margin.Bottom);
        if (!ShowAllOptions && evidenceGrid.ItemContainerGenerator.ContainerFromIndex(0) is DataGridRow row && row.ActualHeight > 0)
        {
            double top = row.TranslatePoint(new Point(), evidenceGrid).Y;
            if (top >= 0) simpleProteinMinimum = Math.Max(296, top + 8 * row.ActualHeight + 2);
        }
        double tableMinimum = ShowAllOptions ? Math.Min(120, room * .6) : Math.Min(simpleProteinMinimum, Math.Max(0, room - 75));
        if (Math.Abs(ProteinTableRow.MinHeight - tableMinimum) > .5) ProteinTableRow.MinHeight = tableMinimum;
        double detailsMaximum = Math.Max(0, room - tableMinimum);
        if (Math.Abs(proteinDetails.MaxHeight - detailsMaximum) > .5) proteinDetails.MaxHeight = detailsMaximum;
    }
}
