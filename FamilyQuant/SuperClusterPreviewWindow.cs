using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// Shows a <see cref="SuperClusterPreview"/>: everything Create Super Cluster is about to do, editable
    /// and live, with nothing changed until Create is pressed. It replaces the message that used to report
    /// the outcome afterwards.
    ///
    /// Built in code rather than XAML for the same reason as <see cref="LockManagerWindow"/>: it cannot
    /// fail to parse in a harness that never opens a window. All the reasoning is in the preview.
    /// </summary>
    public class SuperClusterPreviewWindow : Window
    {
        static readonly Color Backdrop = Color.FromRgb(0x33, 0x39, 0x49);
        static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0xD8, 0xD4, 0xCA));

        /// <summary>Ana, 2026-10-31: what carries none of her words must stand out, so she can decide on it.</summary>
        static readonly Brush RedFlag = new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x20));

        private readonly SuperClusterPreview preview;
        private readonly TextBlock headline;
        private readonly TextBlock notes;
        private readonly CheckBox hiddenBox;
        private readonly DataGrid grid;
        private readonly TextBox domainsBox;
        private readonly ComboBox domainsList;
        private readonly Panel domainsPanel;
        private readonly TextBlock noDomains;
        private readonly Button createButton;

        /// <summary>True once Create was pressed; Cancel, Escape or closing leave it false.</summary>
        public bool Confirmed { get; private set; }

        public SuperClusterPreviewWindow(SuperClusterPreview superClusterPreview)
        {
            preview = superClusterPreview ?? throw new ArgumentNullException(nameof(superClusterPreview));

            Title = "Super cluster preview";
            Width = 1120;
            Height = 700;
            // 1280x800 is the smallest screen this has to work on; keep the window inside it.
            MinWidth = 760;
            MinHeight = 480;
            Background = new SolidColorBrush(Backdrop);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var layout = new Grid { Margin = new Thickness(14) };
            for (int row = 0; row < 7; row++)
                layout.RowDefinitions.Add(new RowDefinition { Height = row == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            layout.RowDefinitions[5].MinHeight = 120;

            headline = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
            };
            Place(layout, headline, 0);

            var keywordsBox = NewBox(preview.KeywordsText,
                "Keywords separated by commas or spaces; the list below follows as you type.");
            var matchBox = new ComboBox
            {
                Width = 150,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Whether a protein must carry every keyword, or any one of them.",
            };
            matchBox.Items.Add("match all words");
            matchBox.Items.Add("match any word");
            matchBox.SelectedIndex = preview.Match == KeywordMatch.Any ? 1 : 0;
            var excludeBox = NewBox(preview.ExcludeText,
                "Whole words that keep a protein out, separated by commas or spaces; the rest of its family still joins. A hyphenated word or a \"quoted phrase\" also works.");

            var boxes = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            boxes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            boxes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            boxes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            boxes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            boxes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddBoxRow(boxes, 0, "Keywords:", keywordsBox);
            AddBoxRow(boxes, 1, "Exclude words:", excludeBox);
            Grid.SetRow(matchBox, 0);
            Grid.SetColumn(matchBox, 2);
            boxes.Children.Add(matchBox);
            Place(layout, boxes, 1);

            notes = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                Margin = new Thickness(0, 0, 0, 6),
            };
            Place(layout, notes, 2);

            hiddenBox = new CheckBox
            {
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "Gathers the matching families the Search box does not show, leaving your Search as it is.",
            };
            Place(layout, hiddenBox, 3);

            var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            actions.Children.Add(NewAction("Untick the red ones", "Unticks every protein with no keyword, leaving only the ones that carry your words.",
                () => preview.UntickWithoutKeyword()));
            actions.Children.Add(NewAction("Tick all", "Ticks every protein on the list again, undoing your unticks here.", () => preview.TickAll()));
            actions.Children.Add(NewAction("Untick selected", "Unticks the rows selected in the table. Click a heading to sort, then Shift+click to select many.",
                () => preview.SetTicks(Selected(), false)));
            actions.Children.Add(NewAction("Tick selected", "Ticks the rows selected in the table, without changing any other row.",
                () => preview.SetTicks(Selected(), true)));

            domainsBox = new TextBox
            {
                Width = 230,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                Padding = new Thickness(3),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Domains to keep, separated by commas or spaces, as the Domains column writes them, for example: Reprolysin, ADAM_CR.",
            };
            domainsList = new ComboBox
            {
                Width = 190,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "The domains on this list, most proteins first. Choosing one adds it to the box on the left.",
            };
            domainsList.SelectionChanged += (_, _) => AddChosenDomain();

            domainsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0) };
            domainsPanel.Children.Add(new TextBlock
            {
                Text = "Keep domains:",
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            domainsPanel.Children.Add(domainsBox);
            domainsPanel.Children.Add(domainsList);
            domainsPanel.Children.Add(NewAction("Apply", "Unticks proteins with none of these domains. A protein with no domains at all stays ticked if it carries a keyword.",
                () => preview.KeepDomains(domainsBox.Text)));
            actions.Children.Add(domainsPanel);

            noDomains = new TextBlock
            {
                Text = "No domains in this file yet: press Fetch evidence in the main window.",
                Foreground = Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0),
            };
            actions.Children.Add(noDomains);
            Place(layout, actions, 4);

            grid = BuildGrid();
            Place(layout, grid, 5);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            createButton = new Button
            {
                Content = "Create super cluster",
                Style = Application.Current.TryFindResource("PrimaryButton") as Style,
                Padding = new Thickness(12, 5, 12, 5),
                ToolTip = "Gathers the ticked proteins into one family, and Undo steps it back. Disabled while there is nothing to change.",
            };
            createButton.Click += (_, _) => { Confirmed = true; Close(); };
            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(10, 0, 0, 0),
                IsCancel = true,
                ToolTip = "Closes this window and changes nothing in the file you have open.",
            };
            buttons.Children.Add(createButton);
            buttons.Children.Add(cancelButton);
            Place(layout, buttons, 6);

            Content = layout;

            keywordsBox.TextChanged += (_, _) => { preview.KeywordsText = keywordsBox.Text; Recompute(); };
            excludeBox.TextChanged += (_, _) => { preview.ExcludeText = excludeBox.Text; Recompute(); };
            matchBox.SelectionChanged += (_, _) =>
            {
                preview.Match = matchBox.SelectedIndex == 1 ? KeywordMatch.Any : KeywordMatch.All;
                Recompute();
            };
            hiddenBox.Click += (_, _) => { preview.IncludeHidden = hiddenBox.IsChecked == true; Recompute(); };

            Recompute();
        }

        private static void Place(Grid layout, UIElement element, int row)
        {
            Grid.SetRow(element, row);
            layout.Children.Add(element);
        }

        private static TextBox NewBox(string text, string tooltip) => new TextBox
        {
            Text = text,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            Padding = new Thickness(3),
            Margin = new Thickness(0, 2, 0, 2),
            ToolTip = tooltip,
        };

        private static void AddBoxRow(Grid boxes, int row, string label, TextBox box)
        {
            var text = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetRow(text, row);
            boxes.Children.Add(text);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            boxes.Children.Add(box);
        }

        private Button NewAction(string text, string tooltip, Action run)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tooltip,
            };
            button.Click += (_, _) => { run(); ShowState(); };
            return button;
        }

        private DataGrid BuildGrid()
        {
            var table = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Extended,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                ToolTip = "One row per protein, the ones with no keyword first. Click a heading to sort. Right-click to copy " +
                          "a UniProt code or a description, or to open the UniProt page; Ctrl+C copies the column you clicked.",
            };
            GridCopy.Attach(table, row => (row as SuperClusterPreviewProtein)?.Locus,
                            row => (row as SuperClusterPreviewProtein)?.Description);

            var noKeyword = new DataTrigger { Binding = new Binding(nameof(SuperClusterPreviewProtein.HasKeyword)), Value = false };
            noKeyword.Setters.Add(new Setter(Control.FontStyleProperty, FontStyles.Italic));
            noKeyword.Setters.Add(new Setter(Control.ForegroundProperty, RedFlag));
            noKeyword.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
                "This protein carries no keyword: it is here because another protein of its family does. " +
                "Untick it and it stays in its family, without affecting the others."));
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Triggers.Add(noKeyword);
            table.RowStyle = rowStyle;

            // A template column, because a DataGridCheckBoxColumn needs one click to select and another to tick.
            var tick = new FrameworkElementFactory(typeof(CheckBox));
            tick.SetBinding(ToggleButton.IsCheckedProperty,
                new Binding(nameof(SuperClusterPreviewProtein.Join)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            tick.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            tick.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => FollowTicks()));
            table.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Joins",
                Width = 56,
                // Read-only keeps the cell out of edit mode, which would rebuild the box under a second click.
                IsReadOnly = true,
                CellTemplate = new DataTemplate { VisualTree = tick },
                // Ctrl+C copies the clicked column, and a tick has no text of its own to copy.
                ClipboardContentBinding = new Binding(nameof(SuperClusterPreviewProtein.Locus)),
                SortMemberPath = nameof(SuperClusterPreviewProtein.Join),
                HeaderStyle = HeaderTip("Untick to keep this protein in its family; the rest of the family still joins."),
            });
            AddColumn(table, "Locus", 170, nameof(SuperClusterPreviewProtein.Locus), "The protein identifier as in the database.");
            AddColumn(table, "Description", 0, nameof(SuperClusterPreviewProtein.Description), "The description exactly as in the database, OS= and all.");
            AddColumn(table, "Domains", 240, nameof(SuperClusterPreviewProtein.Domains),
                "The Pfam domains of this protein, from UniProt. Empty when the file has none yet, or when the protein is too short for Pfam.");
            AddColumn(table, "Keyword", 66, nameof(SuperClusterPreviewProtein.Keyword), "Whether this protein carries a keyword itself, in the description before OS=.");
            AddColumn(table, "Family", 66, nameof(SuperClusterPreviewProtein.Family), "The number of the family this protein belongs to now.");
            DataGridTextColumn inFamily = AddColumn(table, "In family", 76, nameof(SuperClusterPreviewProtein.FamilyKeywords),
                "How many proteins of its family carry a keyword: 2 of 11 means the family is here because of those 2.");
            inFamily.SortMemberPath = nameof(SuperClusterPreviewProtein.FamilyCarriers);
            Themes.TableAppearance.SetNumeric(inFamily, true);
            return table;
        }

        /// <param name="width">In pixels, or 0 for the column that takes the room left.</param>
        private static DataGridTextColumn AddColumn(DataGrid table, string header, int width, string path, string tooltip)
        {
            var column = new DataGridTextColumn
            {
                Header = header,
                Width = width == 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
                MinWidth = width == 0 ? 200 : 40,
                IsReadOnly = true,
                Binding = new Binding(path),
                HeaderStyle = HeaderTip(tooltip),
            };
            table.Columns.Add(column);
            return column;
        }

        private static Style HeaderTip(string tooltip)
        {
            var style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, tooltip));
            style.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 60000));
            return style;
        }

        private List<SuperClusterPreviewProtein> Selected() => grid.SelectedItems.Cast<SuperClusterPreviewProtein>().ToList();

        /// <summary>A new list every keystroke, which would throw away the sort a curator chose.</summary>
        private void Recompute()
        {
            List<SortDescription> sorts = grid.Items.SortDescriptions.ToList();
            preview.Refresh();
            grid.ItemsSource = preview.Proteins;
            foreach (SortDescription sort in sorts)
            {
                grid.Items.SortDescriptions.Add(sort);
                DataGridColumn? column = grid.Columns.FirstOrDefault(c => c.SortMemberPath == sort.PropertyName);
                if (column != null) column.SortDirection = sort.Direction;
            }

            domainsList.ItemsSource = preview.DomainsOnOffer
                .Select(d => d.Name + " (" + d.Proteins.ToString("N0", CultureInfo.CurrentCulture) + ")").ToList();
            ShowState();
        }

        /// <summary>Adds the chosen domain to the box, its name without the count, and resets the list, so
        /// the same name can be chosen again after the box was cleared.</summary>
        private void AddChosenDomain()
        {
            if (domainsList.SelectedItem is not string chosen) return;
            string name = chosen.Substring(0, chosen.LastIndexOf(" (", StringComparison.Ordinal));
            if (!WordCluster.ParseKeywords(domainsBox.Text).Contains(name.ToLowerInvariant()))
                domainsBox.Text = domainsBox.Text.Length == 0 ? name : domainsBox.Text + ", " + name;
            domainsList.SelectedItem = null;
        }

        private void FollowTicks()
        {
            preview.ApplyTicks();
            ShowState();
        }

        private void ShowState()
        {
            headline.Text = preview.Headline;
            notes.Text = preview.Notes;
            notes.Visibility = notes.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            hiddenBox.Content = preview.HiddenLabel;
            hiddenBox.Visibility = preview.HiddenFamilies > 0 ? Visibility.Visible : Visibility.Collapsed;
            domainsPanel.Visibility = preview.HasDomains ? Visibility.Visible : Visibility.Collapsed;
            noDomains.Visibility = preview.HasDomains ? Visibility.Collapsed : Visibility.Visible;
            createButton.IsEnabled = preview.CanCreate;
        }
    }
}
