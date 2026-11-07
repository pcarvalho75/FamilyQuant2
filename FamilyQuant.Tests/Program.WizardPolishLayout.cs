using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static string WizardPolishProposalState(FamilyListImportViewModel model, Window window) =>
        $"Action={model.SelectedSpecialRow?.Action}; enabled={model.CanUseRowChoice}; blocking={model.SelectedSpecialRow?.Blocking}; chosen={model.SelectedSpecialRow?.Chosen}; next={model.CanNext}; displayed={((ComboBox)window.FindName("RowActionChoice")).SelectedValue}.";

    private static bool WizardPolishTableFits(DataGrid grid, out string detail)
    {
        grid.UpdateLayout(); PumpDispatcher();
        DependencyObject? parent = VisualTreeHelper.GetParent(grid);
        while (parent != null && parent is not ScrollContentPresenter) parent = VisualTreeHelper.GetParent(parent);
        if (parent is not ScrollContentPresenter outer)
        { detail = "No page viewport surrounds the category table."; return false; }
        Point top = grid.TranslatePoint(new Point(), outer);
        // The grid can scroll its own rows; its enclosing page must not crop the table itself.
        detail = FormattableString.Invariant($"Table top={top.Y:F1}, bottom={top.Y + grid.ActualHeight:F1}, page viewport={outer.ActualHeight:F1}.");
        return top.Y >= -1 && top.Y + grid.ActualHeight <= outer.ActualHeight + 1;
    }
}
