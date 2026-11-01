using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// One locked family as the manager lists it.
    ///
    /// <see cref="Exists"/> is a fact about the CURRENT grouping, not about the lock: a lock can outlive
    /// its family, because a later merge can take its number out of use. Such a lock is listed rather
    /// than hidden, since the number becomes live again the moment a family adopts it.
    /// </summary>
    public class LockedFamilyRow
    {
        public int UserId { get; set; }

        /// <summary>Proteins currently carrying this family number; 0 when no family does.</summary>
        public int Members { get; set; }

        /// <summary>The family's name (<see cref="FamilyNaming"/>), which is what the table shows for it.</summary>
        public string Description { get; set; } = string.Empty;

        public bool Exists { get; set; }

        /// <summary>The <see cref="Exists"/> flag as the column shows it.</summary>
        public string Presence { get; set; } = string.Empty;
    }

    /// <summary>
    /// The curation locks, listed and removable.
    ///
    /// Locks were previously write-only: editing a User ID added one, and nothing in the interface could
    /// show or remove it, so the only way back was hand-editing the .fcf JSON. This window is the way
    /// back.
    ///
    /// It does NOT write to disk. Removing a lock changes the open <see cref="FamilyClusterFile"/> in
    /// memory and nothing else; the file is written by the main window's Save, exactly as before. The
    /// caption says so, because a management window that looked like it saved would be worse than none.
    ///
    /// Built in code rather than XAML for the same reason as <see cref="TextReportWindow"/> and
    /// <see cref="IdentityScanWindow"/>: it cannot fail to parse in a harness that never opens a window.
    /// </summary>
    public class LockManagerWindow : Window
    {
        static readonly Color Backdrop = Color.FromRgb(0x33, 0x39, 0x49);

        private readonly FamilyClusterFile file;
        private readonly Action? onLocksChanged;
        private readonly Action? onBeforeChange;
        private readonly DataGrid lockGrid;
        private readonly Button removeButton;

        private List<LockedFamilyRow> rows = new List<LockedFamilyRow>();

        /// <summary>The rows exactly as listed, rebuilt after every removal.</summary>
        public IReadOnlyList<LockedFamilyRow> Rows => rows;

        /// <param name="familyClusterFile">The OPEN file. Removals act on it directly, and are saved by
        /// whoever owns it, never here.</param>
        /// <param name="onLocksChanged">Invoked after a removal, so the caller's own lock summary can
        /// follow. Optional: the window works without one.</param>
        /// <param name="onBeforeChange">Invoked once, immediately BEFORE a removal actually mutates the
        /// lock set, so the caller can record an undo snapshot of the state about to change. Not called
        /// when nothing is selected. Optional.</param>
        public LockManagerWindow(FamilyClusterFile familyClusterFile, Action? onLocksChanged = null,
                                 Action? onBeforeChange = null)
        {
            file = familyClusterFile ?? throw new ArgumentNullException(nameof(familyClusterFile));
            this.onLocksChanged = onLocksChanged;
            this.onBeforeChange = onBeforeChange;

            Title = "Curation locks";
            Width = 900;
            Height = 560;
            // 1280x800 is the smallest screen this has to work on; keep the window inside it.
            MinWidth = 520;
            MinHeight = 320;
            Background = new SolidColorBrush(Backdrop);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var layout = new Grid { Margin = new Thickness(14) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Everything a removal means is said HERE, once, rather than in a confirmation box nobody
            // reads: what removal does to the next reclustering, that nothing was written to disk, and
            // why a family that no longer exists is still on the list.
            var caption = new TextBlock
            {
                Text =
                    "These are the families protected from automatic reclustering. A family is locked by "
                    + "typing its number into the User ID column, and by nothing else.\n\n"
                    + "Removing a lock takes effect at once in the file you have open, and it is NOT "
                    + "written to disk here: the .fcf is saved from the main window, exactly as before. "
                    + "Close this window and save when you are ready.\n\n"
                    + "What removal means: an unlocked family can be merged again, so the next \"Recluster "
                    + "by Words\" or \"Create Super Cluster\" may join it to another family, which is "
                    + "precisely what the lock was preventing.\n\n"
                    + "A row marked \"no, merged away\" is a lock that outlived its family: a later merge "
                    + "took that number out of use. It is listed rather than hidden, because the number "
                    + "becomes live again the moment a family adopts it.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA4, 0x9A)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(caption, 0);
            layout.Children.Add(caption);

            lockGrid = BuildGrid();
            Grid.SetRow(lockGrid, 1);
            layout.Children.Add(lockGrid);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            removeButton = new Button
            {
                Content = "Remove selected locks",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(0x61, 0xAE, 0xEE)),
                Foreground = Brushes.White,
                IsEnabled = false,
                ToolTip =
                    "Unlocks the selected families, so the next reclustering may merge them again. "
                    + "Nothing is saved until you save the Family Cluster File. Disabled until a row is selected."
            };
            removeButton.Click += (sender, e) => RemoveSelectedLocks();

            var closeButton = new Button
            {
                Content = "Close",
                Padding = new Thickness(14, 6, 14, 6),
                Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x68)),
                Foreground = Brushes.White,
                ToolTip =
                    "Closes this window; locks you removed are already gone from the open file. Save the "
                    + "Family Cluster File from the main window to keep the change."
            };
            closeButton.Click += (sender, e) => Close();

            buttons.Children.Add(removeButton);
            buttons.Children.Add(closeButton);
            Grid.SetRow(buttons, 2);
            layout.Children.Add(buttons);

            Content = layout;

            Refresh();
        }

        private DataGrid BuildGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                ToolTip =
                    "One row per locked family, read-only. Select rows (Ctrl or Shift for several), then "
                    + "press \"Remove selected locks\"."
            };

            var id = new DataGridTextColumn
            {
                Header = "Family number",
                Width = 130,
                Binding = new Binding("UserId"),
                HeaderStyle = HeaderTip(
                    "The locked family number, as in the User ID column. Automatic reclustering never "
                    + "merges it while the lock is here.")
            };

            var members = new DataGridTextColumn
            {
                Header = "Proteins",
                Width = 100,
                Binding = new Binding("Members"),
                HeaderStyle = HeaderTip(
                    "How many proteins carry this family number now. Zero means it is no longer in use "
                    + "(\"no, merged away\").")
            };

            var presence = new DataGridTextColumn
            {
                Header = "Still in use",
                Width = 140,
                Binding = new Binding("Presence"),
                HeaderStyle = HeaderTip(
                    "Whether this family number still exists. A merge can take a locked number out of use; "
                    + "the lock stays, and applies again if a family takes that number.")
            };

            var description = new DataGridTextColumn
            {
                Header = "Description",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 160,
                Binding = new Binding("Description"),
                HeaderStyle = HeaderTip(
                    "The family's name: its User Annotation, or else its most common protein description. Empty "
                    + "when no protein carries the number any more.")
            };

            grid.Columns.Add(id);
            grid.Columns.Add(members);
            grid.Columns.Add(presence);
            grid.Columns.Add(description);

            grid.SelectionChanged += (sender, e) => UpdateRemoveButton();

            return grid;
        }

        /// <summary>
        /// A per-column header tooltip. DataGridColumn is not a control, so the tooltip has to be attached
        /// through the header's style; a Style that sets two properties inherits its template and
        /// everything else from the theme, so the headers keep their normal appearance and sort arrows.
        /// </summary>
        private static Style HeaderTip(string tooltip)
        {
            var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));

            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, tooltip));
            style.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 60000));

            return style;
        }

        /// <summary>
        /// Rebuilds the listing from the live lock set, in ascending family order so the same file always
        /// lists the same way (a HashSet has no order of its own).
        /// </summary>
        private void Refresh()
        {
            rows = BuildRows();

            lockGrid.ItemsSource = null;
            lockGrid.ItemsSource = rows;

            UpdateRemoveButton();
        }

        private static bool IsDecoy(FastaItemExtended item) =>
            (item.SequenceIdentifier ?? string.Empty).StartsWith(FamilyFilter.DecoyPrefix, StringComparison.Ordinal);

        private List<LockedFamilyRow> BuildRows()
        {
            var byFamily = file.MyExtendedFastaItems
                .GroupBy(item => item.UserID)
                .ToDictionary(group => group.Key, group => group.ToList());

            var listed = new List<LockedFamilyRow>();

            foreach (int id in file.LockedUserIds.OrderBy(i => i))
            {
                if (byFamily.TryGetValue(id, out var familyMembers) && familyMembers.Count > 0)
                {
                    listed.Add(new LockedFamilyRow
                    {
                        UserId = id,
                        Members = familyMembers.Count,
                        // The name every report gives the family, over its target members when it has any.
                        Description = FamilyNaming.Name(familyMembers.Any(m => !IsDecoy(m))
                            ? familyMembers.Where(m => !IsDecoy(m)) : familyMembers),
                        Exists = true,
                        Presence = "yes"
                    });
                }
                else
                {
                    listed.Add(new LockedFamilyRow
                    {
                        UserId = id,
                        Members = 0,
                        Description = string.Empty,
                        Exists = false,
                        Presence = "no, merged away"
                    });
                }
            }

            return listed;
        }

        private void UpdateRemoveButton()
        {
            removeButton.IsEnabled = lockGrid.SelectedItems.Count > 0;
        }

        /// <summary>
        /// Selects the listed families by number. The grid is driven by mouse in the product; this exists
        /// so the same selection can be made without one.
        /// </summary>
        public void SelectFamilies(params int[] userIds)
        {
            lockGrid.SelectedItems.Clear();

            foreach (LockedFamilyRow row in rows.Where(r => userIds.Contains(r.UserId)))
            {
                lockGrid.SelectedItems.Add(row);
            }

            UpdateRemoveButton();
        }

        /// <summary>
        /// Unlocks every selected family and refreshes the listing. This is exactly what the button
        /// invokes; there is no second removal path.
        /// </summary>
        /// <returns>How many locks were removed.</returns>
        public int RemoveSelectedLocks()
        {
            var selected = lockGrid.SelectedItems.OfType<LockedFamilyRow>().ToList();

            if (selected.Count == 0) return 0;

            // Before the lock set changes, so an Undo can step back to exactly this listing.
            onBeforeChange?.Invoke();

            foreach (LockedFamilyRow row in selected)
            {
                file.LockedUserIds.Remove(row.UserId);
            }

            Refresh();

            // The caller's own summary must not be left describing the previous lock set.
            onLocksChanged?.Invoke();

            Console.WriteLine("Curation locks removed: " +
                string.Join(", ", selected.Select(r => r.UserId.ToString(CultureInfo.InvariantCulture))));

            return selected.Count;
        }

        public void ShowFor(Window? owner)
        {
            // Owned windows stay above the main window and close with it, instead of being lost behind it.
            if (owner != null && owner.IsLoaded) Owner = owner;

            ShowDialog();
        }
    }
}
