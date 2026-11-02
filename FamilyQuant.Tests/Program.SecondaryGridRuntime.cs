using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record SecondaryCopyRow(string Locus, string Description);

    private static void SecondaryGridRuntime(Assembly legacy, string output, Themes.AppTheme theme, SecondaryAdd add)
    {
        const string source = "FamilyQuant/GridCopy.cs";
        Type oldType = legacy.GetType("FamilyQuant.GridCopy", true)!;
        var oldCopies = new List<string>(); var copies = new List<string>(); var oldUrls = new List<string>(); var urls = new List<string>();
        var oldClipboard = oldType.GetProperty("ClipboardOverride")!.GetValue(null); var oldOpen = oldType.GetProperty("OpenUrlOverride")!.GetValue(null);
        var savedClipboard = GridCopy.ClipboardOverride; var savedOpen = GridCopy.OpenUrlOverride;
        oldType.GetProperty("ClipboardOverride")!.SetValue(null, (Action<string>)oldCopies.Add);
        oldType.GetProperty("OpenUrlOverride")!.SetValue(null, (Action<string>)oldUrls.Add);
        GridCopy.ClipboardOverride = copies.Add; GridCopy.OpenUrlOverride = urls.Add;
        var rows = new[] { new SecondaryCopyRow("sp|P00001|NATIVE_A", "first native description"),
            new SecondaryCopyRow("tr|Q00002|NATIVE_B", "second native description"), new SecondaryCopyRow("other_native", "third native description") };
        CountingGrid Table(bool frozen)
        {
            var table = new CountingGrid { ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.All, SelectionMode = DataGridSelectionMode.Extended };
            table.Columns.Add(new DataGridTextColumn { Header = "Locus", Binding = new Binding(nameof(SecondaryCopyRow.Locus)) });
            table.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new Binding(nameof(SecondaryCopyRow.Description)) });
            Func<object, string?> locus = value => ((SecondaryCopyRow)value).Locus, text = value => ((SecondaryCopyRow)value).Description;
            if (frozen) SecondaryCall(null, oldType, "Attach", table, locus, text, oldType.GetMethod("Attach")!.GetParameters()[3].DefaultValue);
            else GridCopy.Attach(table, locus, text);
            return table;
        }
        var oldGrid = Table(true); var grid = Table(false);
        var oldHost = SecondaryHost(new Window { Content = oldGrid }); var host = SecondaryHost(new Window { Content = grid });
        try
        {
            IEnumerable<DependencyObject> Children(DependencyObject root)
            {
                yield return root;
                for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                    foreach (var child in Children(VisualTreeHelper.GetChild(root, index))) yield return child;
            }
            DataGridRow Row(DataGrid table, int index) => (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(index);
            UIElement Cell(DataGrid table, int index, DataGridColumn column) => Children(Row(table, index)).OfType<DataGridCell>().Single(cell => cell.Column == column);
            UIElement Header(DataGrid table, int index) => Children(Row(table, index)).OfType<DataGridRowHeader>().First();
            void Press(UIElement element) => element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
            void OpenMenu(DataGrid table, int index)
            {
                table.SelectedItem = rows[index]; table.CurrentCell = new DataGridCellInfo(rows[index], table.Columns[1]);
                UIElement cell = Cell(table, index, table.Columns[1]);
                var opening = (ContextMenuEventArgs)typeof(ContextMenuEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(object), typeof(bool), typeof(double), typeof(double) }, null)!.Invoke(new object[] { cell, true, 0d, 0d });
                opening.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent; cell.RaiseEvent(opening);
            }
            string Copy(CountingGrid table, List<string> captured)
            {
                int before = captured.Count, own = table.OwnCopies; ApplicationCommands.Copy.Execute(null, table);
                return table.OwnCopies > own ? "row" : captured.Count > before ? captured.Last() : "none";
            }
            foreach (var table in new[] { oldGrid, grid }) { table.SelectedItem = rows[1]; table.CurrentCell = new DataGridCellInfo(rows[1], table.Columns[1]); table.UpdateLayout(); }
            string[] oldHeaders = oldGrid.ContextMenu.Items.OfType<MenuItem>().Select(item => Convert.ToString(item.Header, CultureInfo.InvariantCulture)!).ToArray();
            string[] headers = grid.ContextMenu.Items.OfType<MenuItem>().Select(item => Convert.ToString(item.Header, CultureInfo.InvariantCulture)!).ToArray();
            add(source, 35, 35, "native default description-copy label", oldType.GetMethod("Attach")!.GetParameters()[3].DefaultValue,
                typeof(GridCopy).GetMethod("Attach")!.GetParameters()[3].DefaultValue, Equals(oldType.GetMethod("Attach")!.GetParameters()[3].DefaultValue,
                    typeof(GridCopy).GetMethod("Attach")!.GetParameters()[3].DefaultValue), "GridCopy.Attach default textLabel");
            add(source, 74, 74, "native code-copy menu caption", oldHeaders[0], headers[0], oldHeaders[0] == headers[0], "GridCopy code menu item");
            add(source, 84, 84, "native browser menu caption", oldHeaders[2], headers[2], oldHeaders[2] == headers[2], "GridCopy URL menu item",
                row => row.Kind == "code fact" && row.Detail == "\"Open in UniProt\"");
            add(source, 134, 134, "native context menu contains three actionable MenuItems", oldGrid.ContextMenu.Items.Count, grid.ContextMenu.Items.Count,
                oldHeaders.SequenceEqual(headers) && grid.ContextMenu.Items.Count == 3, "GridCopy native MenuItems", row => row.Kind == "code control");
            OpenMenu(oldGrid, 1); OpenMenu(grid, 1);
            for (int index = 0; index < 3; index++)
            {
                ((MenuItem)oldGrid.ContextMenu.Items[index]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                ((MenuItem)grid.ContextMenu.Items[index]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            bool menuActions = oldCopies.SequenceEqual(copies) && oldUrls.SequenceEqual(urls) && copies.Count == 2 && urls.Count == 1
                && copies[0] == "Q00002" && copies[1] == rows[1].Description && urls[0].Contains("Q00002", StringComparison.Ordinal);
            add(source, 135, 135, "actual menu-item Click events invoke code, text and URL boundaries with the same selected row",
                new { Copies = oldCopies.ToArray(), Urls = oldUrls.ToArray() }, new { Copies = copies.ToArray(), Urls = urls.ToArray() }, menuActions,
                "GridCopy.NewItem Click -> supplied clipboard/browser seams", row => row.Kind == "handler");
            var keys = new[] { Key.Left, Key.Right, Key.Tab, Key.Up, Key.Down, Key.Home, Key.End, Key.PageUp, Key.PageDown };
            var oldEffects = new List<string>(); var effects = new List<string>();
            bool unmodified = Keyboard.Modifiers == ModifierKeys.None;
            foreach (Key key in keys)
            {
                foreach (var pair in new[] { (oldGrid, oldCopies, oldEffects), (grid, copies, effects) })
                {
                    pair.Item1.SelectedItem = rows[1]; pair.Item1.CurrentCell = new DataGridCellInfo(rows[1], pair.Item1.Columns[1]);
                    Press(Header(pair.Item1, 1));
                    bool rowBefore = Copy(pair.Item1, pair.Item2) == "row";
                    pair.Item1.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(pair.Item1), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    pair.Item3.Add(key + "/" + rowBefore + "/" + Copy(pair.Item1, pair.Item2));
                }
            }
            bool navigation = unmodified && oldEffects.SequenceEqual(effects) && effects.All(value => value.Contains("/True/", StringComparison.Ordinal)
                && value.EndsWith(rows[1].Description, StringComparison.Ordinal));
            add(source, 49, 49, "actual PreviewKeyDown handlers restore column-copy mode for unmodified navigation keys", oldEffects, effects, navigation,
                "GridCopy PreviewKeyDown", row => row.Kind == "handler");
            add(source, 52, 53, "actual Left/Right/Tab/Up/Down/Home/End/PageUp/PageDown navigation preserves column-only copy", oldEffects, effects, navigation,
                "GridCopy unmodified keyboard navigation", row => row.Kind == "shortcut");
        }
        finally
        {
            host.Close(); oldHost.Close(); GridCopy.ClipboardOverride = savedClipboard; GridCopy.OpenUrlOverride = savedOpen;
            oldType.GetProperty("ClipboardOverride")!.SetValue(null, oldClipboard); oldType.GetProperty("OpenUrlOverride")!.SetValue(null, oldOpen);
        }
    }
}
