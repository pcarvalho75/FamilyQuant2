using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FamilyQuant;

public partial class FamilyListImportWindow
{
    private void FitPreviewColumns()
    {
        if (closing || SheetPreviewGrid.Columns.Count < 2 || SheetPreviewGrid.ActualWidth <= 0) return;
        int visibleColumns = Math.Min(6, SheetPreviewGrid.Columns.Count - 1);
        double available = SheetPreviewGrid.ActualWidth - SheetPreviewGrid.BorderThickness.Left - SheetPreviewGrid.BorderThickness.Right
            - SystemParameters.VerticalScrollBarWidth - SheetPreviewGrid.Columns[0].ActualWidth - 4;
        double width = Math.Clamp(Math.Floor(available / visibleColumns), 110, 145);
        foreach (var column in SheetPreviewGrid.Columns.Skip(1)) column.Width = width;
    }

    // Preview only: preserve original cells and selection while allowing a horizontal merged heading to use its empty neighbours.
    private static int PreviewTitleSpan(PanelPreviewRow row, int column)
    {
        if (column >= row.Cells.Count || string.IsNullOrEmpty(row.Cells[column].Text)) return 1;
        var cell = row.Cells[column];
        var match = Regex.Match(cell.MergeRange.Replace("$", "", StringComparison.Ordinal), @"^([A-Za-z]+)(\d+):([A-Za-z]+)(\d+)$", RegexOptions.CultureInvariant);
        if (!match.Success || !match.Groups[1].Value.Equals(cell.ColumnLetter, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int firstRow)
            || !int.TryParse(match.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int lastRow)
            || firstRow != row.RowNumber || lastRow != firstRow) return 1;
        int Index(string letters)
        {
            int result = 0;
            foreach (char letter in letters.ToUpperInvariant())
            {
                if (result > 16384 / 26) return -1;
                result = result * 26 + letter - 'A' + 1;
            }
            return result - 1;
        }
        int start = Index(match.Groups[1].Value), end = Index(match.Groups[3].Value);
        if (start < 0 || end <= start || column + end - start >= row.Cells.Count) return 1;
        int span = end - start + 1;
        return row.Cells.Skip(column + 1).Take(span - 1).All(next => next.Text.Length == 0
            || next.MergeRange == cell.MergeRange && next.Text == cell.Text) ? span : 1;
    }

    private sealed record PreviewTitle(PanelPreviewRow Row, int Column, int Span, Border Box, TextBlock Text)
    {
        public FrameworkElement? Source { get; set; }
    }
    private readonly List<PreviewTitle> previewTitles = new();
    private bool positioningPreviewTitles;

    private static T? PreviewChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (PreviewChild<T>(VisualTreeHelper.GetChild(root, index)) is { } child) return child;
        return null;
    }

    private void BuildPreviewTitles(PanelSheetPreview preview)
    {
        previewTitles.Clear(); PreviewMergedTitles.Children.Clear();
        foreach (var row in preview.Rows)
        for (int column = 0; column < row.Cells.Count; column++)
        {
            int span = PreviewTitleSpan(row, column);
            if (span <= 1) continue;
            var text = new TextBlock { Text = row.Cells[column].Text, Margin = new Thickness(4, 2, 4, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            var box = new Border { Child = text, ClipToBounds = true, Visibility = Visibility.Collapsed };
            previewTitles.Add(new(row, column, span, box, text)); PreviewMergedTitles.Children.Add(box);
        }
    }

    private void PositionPreviewTitles()
    {
        if (closing || positioningPreviewTitles || PreviewMergedTitles == null) return;
        positioningPreviewTitles = true;
        try
        {
            foreach (var title in previewTitles)
            {
                if (SheetPreviewGrid.Columns.Count <= title.Column + 1) { title.Box.Visibility = Visibility.Collapsed; continue; }
                var source = SheetPreviewGrid.Columns[title.Column + 1].GetCellContent(title.Row);
                var border = source == null ? null : PreviewChild<Border>(source);
                if (border == null || !border.IsVisible || border.Child is not TextBlock original)
                { title.Box.Visibility = Visibility.Collapsed; continue; }
                DependencyObject? parent = border;
                while (parent != null && parent is not DataGridCell) parent = VisualTreeHelper.GetParent(parent);
                if (parent is not DataGridCell cell) { title.Box.Visibility = Visibility.Collapsed; continue; }
                DependencyObject? scrollParent = cell;
                while (scrollParent != null && scrollParent is not ScrollViewer) scrollParent = VisualTreeHelper.GetParent(scrollParent);
                if (scrollParent is ScrollViewer { HorizontalOffset: > .1 }) { title.Box.Visibility = Visibility.Collapsed; continue; }
                var point = cell.TranslatePoint(new Point(), PreviewMergedTitles);
                if (point.Y < SheetPreviewGrid.ColumnHeaderHeight || point.Y < 20
                    || point.Y + cell.ActualHeight > PreviewMergedTitles.ActualHeight - SystemParameters.HorizontalScrollBarHeight)
                { title.Box.Visibility = Visibility.Collapsed; continue; }
                if (!ReferenceEquals(title.Source, border))
                {
                    title.Source = border;
                    title.Text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(TextBlock.Foreground)) { Source = original });
                    title.Text.SetBinding(TextBlock.FontFamilyProperty, new Binding(nameof(TextBlock.FontFamily)) { Source = original });
                    title.Text.SetBinding(TextBlock.FontSizeProperty, new Binding(nameof(TextBlock.FontSize)) { Source = original });
                    title.Text.SetBinding(TextBlock.FontWeightProperty, new Binding(nameof(TextBlock.FontWeight)) { Source = original });
                    title.Text.SetBinding(TextBlock.FontStyleProperty, new Binding(nameof(TextBlock.FontStyle)) { Source = original });
                    var background = new MultiBinding { Converter = new PreviewTitleBackground() };
                    background.Bindings.Add(new Binding(nameof(Border.Background)) { Source = border });
                    background.Bindings.Add(new Binding(nameof(Control.Background)) { Source = cell });
                    background.Bindings.Add(new Binding(nameof(Control.Background)) { Source = SheetPreviewGrid });
                    title.Box.SetBinding(Border.BackgroundProperty, background);
                }
                double merged = SheetPreviewGrid.Columns.Skip(title.Column + 1).Take(title.Span).Sum(column => column.ActualWidth);
                title.Box.Width = Math.Max(0, Math.Min(merged - 2, PreviewMergedTitles.ActualWidth - point.X - SystemParameters.VerticalScrollBarWidth - 4));
                title.Box.Height = Math.Max(0, cell.ActualHeight - 2);
                Canvas.SetLeft(title.Box, point.X + 1); Canvas.SetTop(title.Box, point.Y + 1);
                title.Box.Visibility = Visibility.Visible;
            }
        }
        finally { positioningPreviewTitles = false; }
    }

    private sealed class PreviewTitleBackground : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.OfType<Brush>()
            .FirstOrDefault(brush => brush.Opacity > 0 && (brush is not SolidColorBrush solid || solid.Color.A > 0)) ?? (object)DependencyProperty.UnsetValue;
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
