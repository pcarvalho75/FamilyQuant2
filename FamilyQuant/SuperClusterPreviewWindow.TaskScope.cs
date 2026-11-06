using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    private void CopyTaskTaxa_CanExecute(object sender, CanExecuteRoutedEventArgs args)
    {
        args.CanExecute = taskTaxaGrid.Items.OfType<ReviewTaskTaxon>().Any();
        args.Handled = true;
    }

    private void CopyTaskTaxa_Executed(object sender, ExecutedRoutedEventArgs args)
    {
        var columns = taskTaxaGrid.Columns.Where(column => column.Visibility == Visibility.Visible)
            .OrderBy(column => column.DisplayIndex).ToArray();
        var selected = new HashSet<object>(taskTaxaGrid.SelectedItems.Cast<object>());
        var rows = taskTaxaGrid.Items.OfType<ReviewTaskTaxon>().Where(row => selected.Count == 0 || selected.Contains(row));
        var lines = new List<string> { string.Join("\t", columns.Select(column => ClipboardCell(column.Header))) };
        lines.AddRange(rows.Select(row => string.Join("\t", columns.Select(column => ClipboardCell(column.OnCopyingCellClipboardContent(row))))));
        GridCopy.ToClipboard(string.Join(Environment.NewLine, lines), this);
        args.Handled = true;
    }

    private static string ClipboardCell(object? value)
    {
        string text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        return text.IndexOfAny(['\t', '\r', '\n', '"']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }
}
