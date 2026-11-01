using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FamilyQuant
{
    /// <summary>
    /// Getting text out of a table of proteins. Ana, 2026-10-31: she goes to UniProt all the time to check
    /// who is who, could select a row but not copy from it, and was typing the codes by hand.
    ///
    /// A DataGrid selecting whole rows copies whole rows, every column of them, and offers no right-click.
    /// Here Ctrl+C gives the column that was clicked, for every selected row, and a right-click gives the
    /// UniProt code, the description, and the UniProt page itself. Selection stays on whole rows, because
    /// the Data Browser's own buttons read <see cref="MultiSelector.SelectedItems"/>.
    /// </summary>
    public static class GridCopy
    {
        /// <summary>The suite's seams: no test writes to the real clipboard or opens a browser.</summary>
        public static Action<string>? ClipboardOverride { get; set; }
        public static Action<string>? OpenUrlOverride { get; set; }

        /// <param name="locusOf">The identifier of a row's protein, or null for a table with no proteins,
        /// which then offers only <paramref name="textLabel"/>.</param>
        /// <param name="textOf">The text of a row.</param>
        public static void Attach(DataGrid grid, Func<object, string?>? locusOf, Func<object, string?> textOf,
                                  string textLabel = "Copy description")
        {
            // A click on a row number moves WPF's current column to the first one, which nobody clicked:
            // that selection means the row. A right-click inside a selection moves nothing, and neither
            // does the grip that resizes a row, so they say nothing about what the selection means.
            bool rowNumberClicked = false;
            grid.PreviewMouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left && RowAt(grid, e.OriginalSource)?.IsSelected == true) return;
                if (Ancestor<Thumb>(e.OriginalSource) != null) return;

                if (Ancestor<DataGridRowHeader>(e.OriginalSource) != null) rowNumberClicked = true;
                else if (Ancestor<DataGridCell>(e.OriginalSource) != null) rowNumberClicked = false;
            };
            grid.PreviewKeyDown += (_, e) =>
            {
                bool extending = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (e.Key is Key.Left or Key.Right or Key.Tab
                    || !extending && e.Key is Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
                    rowNumberClicked = false;
            };

            // A binding on the grid runs before DataGrid's own Copy, which would copy every column and, on a
            // clipboard held by another program, fail without a word. Declining hands the row case to that
            // Copy, which already gives rows as text and as a table.
            grid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
                (_, _) => ToClipboard(ColumnText(grid, grid.CurrentColumn)),
                (_, e) => e.CanExecute = !rowNumberClicked && grid.CurrentColumn != null && grid.SelectedItems.Count > 0));

            // What was selected when the menu opened, not when an item is pressed: opening it commits any
            // cell being edited, and the refresh that follows can clear the selection underneath the menu.
            var rows = new List<object>();
            object? clicked = null;

            var menu = new ContextMenu();
            MenuItem? code = null;
            MenuItem? open = null;
            if (locusOf != null)
            {
                code = NewItem("Copy UniProt code",
                    "Copies the code alone, P31982 from sp|P31982|VM2I_CROCC, one line per selected row.",
                    () => ToClipboard(Codes(rows.Select(locusOf))));
                menu.Items.Add(code);
            }
            MenuItem text = NewItem(textLabel, "Copies this text as the database has it, one line per selected row.",
                () => ToClipboard(Lines(rows.Select(textOf))));
            menu.Items.Add(text);
            if (locusOf != null)
            {
                open = NewItem("Open in UniProt", "Opens the UniProt page of the row you clicked in your browser.",
                    () => OpenUrl(FamilyNaming.UniProtUrl(FamilyNaming.Accession(locusOf(clicked!)))));
                menu.Items.Add(open);
            }
            grid.ContextMenu = menu;

            // DataGrid has already moved the focus, the current cell and the selection to a row that was
            // not selected, and committed an open edit, before this runs. A row inside a selection keeps it.
            grid.ContextMenuOpening += (_, e) =>
            {
                clicked = RowAt(grid, e.OriginalSource)?.Item;
                if (clicked == null)
                {
                    e.Handled = true;
                    return;
                }

                rows = SelectedInOrder(grid);
                if (!rows.Contains(clicked)) rows = new List<object> { clicked };

                // An item with nothing to copy is greyed out: enabled, it would leave the previous code on
                // the clipboard to be pasted as if it were this protein's.
                text.IsEnabled = Lines(rows.Select(textOf)).Length > 0;
                if (code != null) code.IsEnabled = Codes(rows.Select(locusOf!)).Length > 0;
                if (open != null) open.IsEnabled = FamilyNaming.UniProtUrl(FamilyNaming.Accession(locusOf!(clicked))).Length > 0;
            };
        }

        /// <summary>The codes of these identifiers, one line each; a decoy has none and leaves its line empty.</summary>
        public static string Codes(IEnumerable<string?> identifiers) => Lines(identifiers.Select(FamilyNaming.Accession));

        /// <summary>
        /// One line per row, empty lines included, so a paste next to another column stays aligned with it:
        /// dropping a blank would silently put codes beside the wrong descriptions. Nothing at all only when
        /// every line is blank.
        /// </summary>
        public static string Lines(IEnumerable<string?> texts)
        {
            List<string?> lines = texts.ToList();
            return lines.All(string.IsNullOrWhiteSpace) ? string.Empty
                : string.Join(Environment.NewLine, lines.Select(t => t ?? string.Empty));
        }

        /// <summary>One column of the selected rows, as DataGrid itself would read each cell.</summary>
        private static string ColumnText(DataGrid grid, DataGridColumn column) =>
            Lines(SelectedInOrder(grid).Select(row =>
                Convert.ToString(column.OnCopyingCellClipboardContent(row), CultureInfo.CurrentCulture)));

        private static MenuItem NewItem(string header, string tooltip, Action run)
        {
            var item = new MenuItem { Header = header, ToolTip = tooltip };
            item.Click += (_, _) => run();
            return item;
        }

        private static DataGridRow? RowAt(DataGrid grid, object? source) =>
            source is DependencyObject element ? ItemsControl.ContainerFromElement(grid, element) as DataGridRow : null;

        private static T? Ancestor<T>(object? source) where T : DependencyObject
        {
            DependencyObject? node = source as DependencyObject;
            while (node != null)
            {
                if (node is T found) return found;
                node = node is Visual || node is Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            return null;
        }

        /// <summary>In the order on screen, not the order they were clicked.</summary>
        private static List<object> SelectedInOrder(DataGrid grid)
        {
            var selected = new HashSet<object>(grid.SelectedItems.Cast<object>());
            return grid.Items.Cast<object>().Where(selected.Contains).ToList();
        }

        /// <summary>A blank selection empties the clipboard, so the next paste cannot be the previous protein's.</summary>
        private static void ToClipboard(string text)
        {
            if (ClipboardOverride != null) { ClipboardOverride(text); return; }

            try
            {
                if (text.Length == 0) Clipboard.Clear();
                else Clipboard.SetText(text);
            }
            catch (ExternalException)
            {
                MessageBox.Show("Nothing was copied: another program is holding the clipboard.\n\n" +
                                "Next step: try again in a moment.", "Copy",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void OpenUrl(string url)
        {
            if (url.Length == 0) return;
            if (OpenUrlOverride != null) { OpenUrlOverride(url); return; }

            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                MessageBox.Show("The UniProt page could not be opened: " + ex.Message + "\n\n" +
                                "Next step: copy the UniProt code and paste it into uniprot.org.", "Open in UniProt",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
