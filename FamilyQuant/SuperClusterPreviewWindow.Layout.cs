using System;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
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
        double tableHeight = LayoutInformation.GetLayoutSlot(ReviewTablesGrid).Height;
        if (tableHeight > 0 && Math.Abs(ReviewTablesGrid.MaxHeight - tableHeight) > .5) ReviewTablesGrid.MaxHeight = tableHeight;
        if (!ProteinLayout.IsVisible) return;
        double proteinHeight = LayoutInformation.GetLayoutSlot(ProteinLayout).Height;
        if (proteinHeight <= 0) return;
        if (Math.Abs(ProteinLayout.MaxHeight - proteinHeight) > .5) ProteinLayout.MaxHeight = proteinHeight;
        double room = Math.Max(0, proteinHeight - ProteinLayout.RowDefinitions[1].ActualHeight
            - ProteinDetailsHeader.ActualHeight - ProteinDetailsHeader.Margin.Top - ProteinDetailsHeader.Margin.Bottom);
        double tableMinimum = Math.Min(120, room * .6);
        if (Math.Abs(ProteinTableRow.MinHeight - tableMinimum) > .5) ProteinTableRow.MinHeight = tableMinimum;
        double detailsMaximum = Math.Max(0, room - tableMinimum);
        if (Math.Abs(proteinDetails.MaxHeight - detailsMaximum) > .5) proteinDetails.MaxHeight = detailsMaximum;
    }
}
