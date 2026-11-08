using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static TextBox WizardPolishRowEditor(Window window, string name) => WalkTree(window).OfType<TextBox>()
        .Single(box => AutomationProperties.GetName(box) == name);

    private static (DataGridCell? Cell, TextBlock? Text) WizardPolishMessage(DataGrid grid, ImportSpecialRow item)
    {
        grid.ScrollIntoView(item); grid.UpdateLayout(); PumpDispatcher();
        if (grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row) return default;
        var cell = WalkTree(row).OfType<DataGridCell>().FirstOrDefault(candidate => ReferenceEquals(candidate.Column, grid.Columns[2]));
        return cell == null ? default : (cell, WalkTree(cell).OfType<TextBlock>().FirstOrDefault(text => text.Text == item.Message));
    }

    private static bool WizardPolishMessageInk(DataGrid grid, ImportSpecialRow item, out string detail)
    {
        var (cell, text) = WizardPolishMessage(grid, item);
        detail = $"Message ink={text?.Foreground}; cell ink={cell?.Foreground}.";
        return text?.Foreground is SolidColorBrush actual && cell?.Foreground is SolidColorBrush expected
            && actual.Color == expected.Color && actual.Opacity == expected.Opacity;
    }

    private static bool WizardPolishMessageFits(DataGrid grid, ImportSpecialRow item, out string detail)
    {
        var (_, text) = WizardPolishMessage(grid, item);
        if (text == null || text.ActualWidth <= 0) { detail = "The review message is not realized."; return false; }
        var full = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize,
            FontWeight = text.FontWeight, FontStyle = text.FontStyle, FontStretch = text.FontStretch,
            FlowDirection = text.FlowDirection, TextWrapping = TextWrapping.Wrap, Padding = text.Padding,
            LineHeight = text.LineHeight, LineStackingStrategy = text.LineStackingStrategy };
        full.Measure(new Size(text.ActualWidth, double.PositiveInfinity));
        detail = FormattableString.Invariant($"Message actual height={text.ActualHeight:F1}; complete wrapped height={full.DesiredSize.Height:F1}.");
        return text.ActualHeight + 2 >= full.DesiredSize.Height;
    }

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
