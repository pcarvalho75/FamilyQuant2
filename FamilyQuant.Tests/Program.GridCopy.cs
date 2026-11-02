using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        /// <summary>DataGrid's own Copy, observed instead of run, so a test can see it without the clipboard.</summary>
        private sealed class CountingGrid : DataGrid
        {
            public int OwnCopies { get; private set; }

            protected override void OnExecutedCopy(System.Windows.Input.ExecutedRoutedEventArgs args)
            {
                OwnCopies++;
                args.Handled = true;
            }
        }

        /// <summary>
        /// What a selection means, on a real grid in a shown window: a click on a row number means the row,
        /// a click on a cell means its column, and gestures that move nothing must not change that.
        /// </summary>
        private static void GridCopyRowNumberTests()
        {
            var preview = new SuperClusterPreview(Disintegrins(), null, ViperKeywords, "", KeywordMatch.Any, null, null);
            List<SuperClusterPreviewProtein> shown = preview.Proteins.ToList();
            var grid = new CountingGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.All,
                SelectionMode = DataGridSelectionMode.Extended,
            };
            var locus = new DataGridTextColumn { Header = "Locus", Binding = new System.Windows.Data.Binding(nameof(SuperClusterPreviewProtein.Locus)) };
            var description = new DataGridTextColumn { Header = "Description", Binding = new System.Windows.Data.Binding(nameof(SuperClusterPreviewProtein.Description)) };
            grid.Columns.Add(locus);
            grid.Columns.Add(description);
            GridCopy.Attach(grid, row => (row as SuperClusterPreviewProtein)?.Locus, row => (row as SuperClusterPreviewProtein)?.Description);
            grid.ItemsSource = shown;

            var host = new Window
            {
                Content = grid, Width = 900, Height = 400, Left = -3000, Top = 20,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
            };
            var copied = new List<string>();
            GridCopy.ClipboardOverride = copied.Add;
            try
            {
                host.Show();
                PumpDispatcher();
                grid.UpdateLayout();

                static IEnumerable<DependencyObject> Visuals(DependencyObject root)
                {
                    yield return root;
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                        foreach (DependencyObject child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
                }
                DataGridRow Row(int i) => (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(i);
                UIElement Cell(int i, DataGridColumn column) => Visuals(Row(i)).OfType<DataGridCell>().First(c => c.Column == column);
                UIElement RowNumber(int i) => Visuals(Row(i)).OfType<System.Windows.Controls.Primitives.DataGridRowHeader>().First();
                static void Press(UIElement element, System.Windows.Input.MouseButton button) =>
                    element.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, button)
                    {
                        RoutedEvent = UIElement.PreviewMouseDownEvent,
                    });
                void Key(System.Windows.Input.Key key) =>
                    grid.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid), 0, key)
                    {
                        RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent,
                    });
                string Copy()
                {
                    int ours = copied.Count, own = grid.OwnCopies;
                    System.Windows.Input.ApplicationCommands.Copy.Execute(null, grid);
                    return grid.OwnCopies > own ? "the row" : copied.Count > ours ? copied.Last() : "nothing";
                }
                const System.Windows.Input.MouseButton Left = System.Windows.Input.MouseButton.Left;
                const System.Windows.Input.MouseButton Right = System.Windows.Input.MouseButton.Right;

                grid.SelectedItem = shown[1];
                grid.CurrentCell = new DataGridCellInfo(shown[1], description);
                Press(Cell(1, description), Left);
                Check("a click on a cell copies its column", Copy() == shown[1].Description, copied.LastOrDefault());

                Press(RowNumber(1), Left);
                grid.CurrentCell = new DataGridCellInfo(shown[1], locus);      // what WPF does on that click
                Check("a click on a row number hands the row to DataGrid's own Copy, its table formats included, not the first column",
                    Copy() == "the row", copied.LastOrDefault());
                Press(Cell(1, description), Right);
                Check("a right-click inside that selection moves nothing, so it still means the row", Copy() == "the row");
                Thumb? grip = Visuals(Row(1)).OfType<Thumb>().FirstOrDefault();
                Check("the row has a grip to test with", grip != null);
                Press(Cell(1, description), Left);
                Press(grip!, Left);
                grid.CurrentCell = new DataGridCellInfo(shown[1], description);
                Check("and the grip that resizes a row selects nothing, so a column stays a column", Copy() == shown[1].Description, copied.LastOrDefault());

                Press(RowNumber(1), Right);
                Check("the same the other way round: a right-click on a selected row number keeps the column", Copy() == shown[1].Description, copied.LastOrDefault());

                Press(RowNumber(1), Left);
                Key(System.Windows.Input.Key.Right);
                Check("after a row number, moving along the row goes back to the column under the cursor", Copy() == shown[1].Description, copied.LastOrDefault());

                Press(RowNumber(1), Left);
                Press(Cell(2, locus), Right);
                grid.SelectedItem = shown[2];                                  // what WPF does on that right-click
                grid.CurrentCell = new DataGridCellInfo(shown[2], locus);
                Check("a right-click on a row outside the selection means that cell's column", Copy() == shown[2].Locus, copied.LastOrDefault());

                grid.UnselectAll();
                Check("with nothing selected there is nothing to copy", !System.Windows.Input.ApplicationCommands.Copy.CanExecute(null, grid));
            }
            finally
            {
                GridCopy.ClipboardOverride = null;
                host.Close();
            }
        }

        /// <summary>
        /// Ana goes to UniProt all the time and was typing the codes by hand: a grid selecting whole rows
        /// copies every column, and had no right-click. No test here touches the real clipboard or a browser.
        /// </summary>
        private static void GridCopyTests()
        {
            Check("the code is read from sp, tr and contaminant identifiers",
                FamilyNaming.Accession("sp|P31982|VM2I_CROCC") == "P31982"
                && FamilyNaming.Accession("tr|A0A8T1N447|A0A8T1N447_BOTJA") == "A0A8T1N447"
                && FamilyNaming.Accession("contaminant_P05784|K1C18_MOUSE") == "P05784"
                && FamilyNaming.Accession("P31982") == "P31982" && FamilyNaming.Accession(" sp|P31982-2|X ") == "P31982-2");
            Check("a decoy has no code", FamilyNaming.Accession("Reverse_sp|P31982|VM2I_CROCC") == ""
                && FamilyNaming.Accession("Reverse_contaminant_Q10735|PEPB_PIG") == "");
            Check("an identifier from another database comes back whole, and nothing crashes on an empty one",
                FamilyNaming.Accession("gi|12345|ref") == "gi|12345|ref" && FamilyNaming.Accession("Venom_contig_12") == "Venom_contig_12"
                && FamilyNaming.Accession(null) == "" && FamilyNaming.Accession("  ") == "");
            Check("only a UniProt accession has a UniProt page, and an isoform opens its protein",
                FamilyNaming.UniProtUrl("P31982") == "https://www.uniprot.org/uniprotkb/P31982/entry"
                && FamilyNaming.UniProtUrl("A0A8T1N447") == "https://www.uniprot.org/uniprotkb/A0A8T1N447/entry"
                && FamilyNaming.UniProtUrl("P31982-2") == "https://www.uniprot.org/uniprotkb/P31982/entry"
                && FamilyNaming.UniProtUrl("gi|12345|ref") == "" && FamilyNaming.UniProtUrl("VM2I_CROCC") == ""
                && FamilyNaming.UniProtUrl("") == "" && FamilyNaming.UniProtUrl(null) == "");
            string nl = Environment.NewLine;
            Check("several rows copy one line each, and a decoy keeps its empty line so a paste beside another column stays aligned",
                GridCopy.Codes(new[] { "sp|P31982|A_B", "Reverse_sp|Q90221|C_D", "tr|Q90220|E_F" }) == "P31982" + nl + nl + "Q90220");
            Check("nothing to copy is nothing, not a column of empty lines", GridCopy.Lines(new[] { "", null, "  " }) == "");

            List<FastaItemExtended> items = Disintegrins();
            items[1].Domains = "Reprolysin";
            items[2].Domains = "Disintegrin";
            var preview = new SuperClusterPreview(items, null, ViperKeywords, "", KeywordMatch.Any, null, null);
            var window = new SuperClusterPreviewWindow(preview);
            var content = (FrameworkElement)window.Content;
            LayoutAt(content, new Size(window.Width, window.Height), -1);
            DataGrid table = UiField<DataGrid>(window, "grid")!;
            SuperClusterPreviewProtein Protein(string locus) => preview.Proteins.First(x => x.Locus == locus);
            Button Action(string text) => WalkTree(content).OfType<Button>().First(b => (b.Content as string) == text);
            static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bool Red(string locus)
            {
                table.ScrollIntoView(Protein(locus));
                LayoutAt(content, new Size(window.Width, window.Height), -1);
                var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromItem(Protein(locus));
                return row.Foreground is SolidColorBrush brush
                    && brush.Color == ((SolidColorBrush)Application.Current.FindResource("ErrorBrush")).Color;
            }

            Check("the table lists every protein", table.Items.Count == 6, table.Items.Count.ToString());
            Check("a protein with no keyword is in red, as Ana asked", Red("sp|P31987|VM2I_CROVV"));
            Check("and one that carries a word is not", !Red("sp|O93517|VM3S4_GLOBR"));
            DataGridColumn familyCount = table.Columns.First(c => (c.Header as string) == "In family");
            Check("'In family' sorts on its number, or 10 of 20 would come before 2 of 11",
                familyCount.SortMemberPath == nameof(SuperClusterPreviewProtein.FamilyCarriers), familyCount.SortMemberPath);

            TextBlock Headline() => UiField<TextBlock>(window, "headline")!;
            Click(Action("Untick the red ones"));
            Check("Untick the red ones reaches the model and the headline at once",
                preview.JoiningProteins == 3 && Headline().Text.StartsWith("3 proteins from 2 families", StringComparison.Ordinal), Headline().Text);
            Click(Action("Tick all"));
            Check("and Tick all brings them back", preview.JoiningProteins == 6, Headline().Text);
            table.SelectedItems.Clear();
            table.SelectedItems.Add(Protein("sp|P83044|DID8B_CERCE"));
            table.SelectedItems.Add(Protein("sp|P31987|VM2I_CROVV"));
            Click(Action("Untick selected"));
            Check("Untick selected takes only the selected rows", preview.JoiningProteins == 4 && !Protein("sp|P83044|DID8B_CERCE").Join, Headline().Text);
            Click(Action("Tick selected"));
            Check("and Tick selected gives them back", preview.JoiningProteins == 6, Headline().Text);

            var tickRow = WalkTree(content).OfType<CheckBox>().First(x => HasAncestor<DataGridCell>(x));
            var tickProtein = (SuperClusterPreviewProtein)tickRow.DataContext;
            tickRow.IsChecked = false;
            tickRow.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("a row's tick is one click, and the headline follows", !tickProtein.Join && preview.JoiningProteins == 5, Headline().Text);
            tickRow.IsChecked = true;
            tickRow.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            TextBox domainBox = WalkTree(content).OfType<TextBox>().First(x => x.TemplatedParent == null && (x.ToolTip as string)?.StartsWith("Domains to keep", StringComparison.Ordinal) == true);
            ComboBox domainList = WalkTree(content).OfType<ComboBox>().First(x => (x.ToolTip as string)?.StartsWith("The domains on this list", StringComparison.Ordinal) == true);
            Check("the domains found are offered with their counts",
                domainList.Items.Cast<string>().SequenceEqual(new[] { "Disintegrin (1)", "Reprolysin (1)" }), string.Join(" | ", domainList.Items.Cast<string>()));
            domainList.SelectedIndex = 1;
            Check("choosing one writes its name in the box, without the count", domainBox.Text == "Reprolysin", domainBox.Text);
            domainList.SelectedIndex = 0;
            Check("a second goes after it, and the list is ready to choose again", domainBox.Text == "Reprolysin, Disintegrin" && domainList.SelectedItem == null, domainBox.Text);
            domainList.SelectedIndex = 0;
            Check("and the same one is not written twice", domainBox.Text == "Reprolysin, Disintegrin", domainBox.Text);
            domainBox.Text = "Disintegrin";
            Click(Action("Apply"));
            Check("Apply keeps the domains named", Protein("sp|P31987|VM2I_CROVV").Join && !Protein("sp|O93517|VM3S4_GLOBR").Join, Headline().Text);
            Click(Action("Tick all"));

            TextBox excludeBox = WalkTree(content).OfType<TextBox>().First(x => x.TemplatedParent == null && (x.ToolTip as string)?.StartsWith("Whole words", StringComparison.Ordinal) == true);
            table.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(SuperClusterPreviewProtein.Locus), System.ComponentModel.ListSortDirection.Descending));
            excludeBox.Text = "cc8b";
            PumpDispatcher();
            Check("typing rebuilds the list and keeps the sort a curator chose", table.Items.Count == 5 && table.Items.SortDescriptions.Count == 1, table.Items.Count.ToString());
            excludeBox.Text = "";
            table.Items.SortDescriptions.Clear();
            LayoutAt(content, new Size(window.Width, window.Height), -1);

            var noDomainWindow = new SuperClusterPreviewWindow(new SuperClusterPreview(Disintegrins(), null, ViperKeywords, "", KeywordMatch.Any, null, null));
            var noDomainContent = (FrameworkElement)noDomainWindow.Content;
            LayoutAt(noDomainContent, new Size(noDomainWindow.Width, noDomainWindow.Height), -1);
            Check("a file with no domains says how to get them, instead of showing an empty control",
                WalkTree(noDomainContent).OfType<TextBlock>().Any(t => t.Text.StartsWith("No domains in this file yet", StringComparison.Ordinal) && t.Visibility == Visibility.Visible)
                && ((FrameworkElement)WalkTree(noDomainContent).OfType<Button>().First(b => (b.Content as string) == "Apply").Parent).Visibility == Visibility.Collapsed);
            Check("the tick column stays out of edit mode, which would rebuild the box under a second click",
                table.Columns.First(c => (c.Header as string) == "Joins").IsReadOnly);

            // The real menu of the real tables, through the two seams.
            var copied = new List<string>();
            var opened = new List<string>();
            GridCopy.ClipboardOverride = copied.Add;
            GridCopy.OpenUrlOverride = opened.Add;
            try
            {
                static MenuItem Item(DataGrid grid, string header) =>
                    grid.ContextMenu.Items.OfType<MenuItem>().First(m => (m.Header as string) == header);
                static void Press(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                // The events WPF raises for a right-click. Selection and focus are DataGrid's own answer to them.
                static void RightClick(DataGridRow row)
                {
                    FrameworkElement cell = WalkTree(row).OfType<DataGridCell>().First();
                    cell.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                        System.Windows.Input.MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseDownEvent });
                    var opening = (ContextMenuEventArgs)typeof(ContextMenuEventArgs).GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic, null,
                        new[] { typeof(object), typeof(bool), typeof(double), typeof(double) }, null)!
                        .Invoke(new object[] { cell, true, 0d, 0d });
                    opening.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent;
                    cell.RaiseEvent(opening);
                }
                static DataGridRow RowOf(DataGrid grid, object item) => (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(item);
                string? CtrlC(DataGrid grid, DataGridColumn column, params object[] rows)
                {
                    grid.SelectedItem = rows[0];
                    foreach (object row in rows.Skip(1)) grid.SelectedItems.Add(row);
                    grid.CurrentCell = new DataGridCellInfo(rows[0], column);
                    System.Windows.Input.ApplicationCommands.Copy.Execute(null, grid);
                    return copied.LastOrDefault();
                }

                Check("a right-click offers the code, the description and the UniProt page",
                    table.ContextMenu.Items.OfType<MenuItem>().Select(m => m.Header as string)
                        .SequenceEqual(new[] { "Copy UniProt code", "Copy description", "Open in UniProt" }));
                Check("every entry of the menu says what it does",
                    table.ContextMenu.Items.OfType<MenuItem>().All(m => (m.ToolTip as string)?.Split(' ').Length >= 6));

                LayoutAt(content, new Size(window.Width, window.Height), -1);
                List<SuperClusterPreviewProtein> shown = preview.Proteins.ToList();

                table.SelectedItems.Clear();
                table.SelectedItem = shown[0];
                RightClick(RowOf(table, shown[2]));
                Check("a right-click moves to the row under the mouse", table.SelectedItems.Count == 1 && table.SelectedItem == shown[2]);
                Check("and the current cell goes with it, so the next keystroke cannot land on the old row",
                    table.CurrentCell.Item == shown[2], (table.CurrentCell.Item as SuperClusterPreviewProtein)?.Locus);
                Press(Item(table, "Copy UniProt code"));
                Check("Copy UniProt code gives the code alone", copied.LastOrDefault() == FamilyNaming.Accession(shown[2].Locus), copied.LastOrDefault());
                Press(Item(table, "Copy description"));
                Check("Copy description gives the database's text", copied.LastOrDefault() == shown[2].Description, copied.LastOrDefault());
                Press(Item(table, "Open in UniProt"));
                Check("Open in UniProt opens that protein's page",
                    opened.LastOrDefault() == "https://www.uniprot.org/uniprotkb/" + FamilyNaming.Accession(shown[2].Locus) + "/entry", opened.LastOrDefault());

                table.SelectedItems.Clear();
                table.SelectedItems.Add(shown[3]);
                table.SelectedItems.Add(shown[0]);
                RightClick(RowOf(table, shown[3]));
                Check("inside a selection a right-click leaves it alone", table.SelectedItems.Count == 2);
                Press(Item(table, "Copy UniProt code"));
                Check("and the codes come in the order on screen, not the order clicked",
                    copied.LastOrDefault() == FamilyNaming.Accession(shown[0].Locus) + Environment.NewLine + FamilyNaming.Accession(shown[3].Locus), copied.LastOrDefault());
                int pages = opened.Count;
                Press(Item(table, "Open in UniProt"));
                Check("while the page opens for the one row that was clicked, not one tab per selected row",
                    opened.Count == pages + 1 && opened.Last().Contains(FamilyNaming.Accession(shown[3].Locus), StringComparison.Ordinal), opened.Last());

                DataGridColumn locusColumn = table.Columns.First(c => (c.Header as string) == "Locus");
                DataGridColumn descriptionColumn = table.Columns.First(c => (c.Header as string) == "Description");
                Check("Ctrl+C on the real table copies the clicked column alone", CtrlC(table, locusColumn, shown[2]) == shown[2].Locus, copied.LastOrDefault());
                Check("whichever column was clicked", CtrlC(table, descriptionColumn, shown[2]) == shown[2].Description, copied.LastOrDefault());
                Check("for every selected row, in the order on screen",
                    CtrlC(table, locusColumn, shown[3], shown[0]) == shown[0].Locus + Environment.NewLine + shown[3].Locus, copied.LastOrDefault());
                Check("the tick column copies the Locus, having no text of its own",
                    CtrlC(table, table.Columns.First(c => (c.Header as string) == "Joins"), shown[1]) == shown[1].Locus, copied.LastOrDefault());

                string fcfPath = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "grid_copy.fcf");
                Directory.CreateDirectory(Path.GetDirectoryName(fcfPath)!);
                var file = new List<FastaItemExtended>(Disintegrins())
                {
                    new FastaItemExtended { SequenceIdentifier = "Reverse_sp|P31987|VM2I_CROVV", Description = "", UserID = 1, USearchID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "Venom_contig_12", Description = "Venom transcript", UserID = 2, USearchID = 2 },
                };
                new FamilyClusterFile { ClusteringIdentity = 70, MyExtendedFastaItems = file }.Save(fcfPath);
                var control = new ClusterGenerationControl(fcfPath);
                DataGrid browser = UiField<DataGrid>(control, "DataGridResultGrid")!;
                var host = new Window { Content = control, Width = 1400, Height = 900, Left = -3000, ShowActivated = false, ShowInTaskbar = false };
                host.Show();
                TabItem browserTab = WalkTree(control).OfType<TabItem>().First(t => (t.Header as string)?.Contains("Data Browser", StringComparison.Ordinal) == true);
                browserTab.IsSelected = true;
                PumpDispatcher();
                browser.UpdateLayout();
                var rows = browser.Items.Cast<FastaItemExtended>().ToList();
                FastaItemExtended Named(string id) => rows.First(r => r.SequenceIdentifier == id);
                DataGridRow BrowserRow(string id)
                {
                    browser.ScrollIntoView(Named(id));
                    browser.UpdateLayout();
                    return RowOf(browser, Named(id));
                }
                DataGridColumn Browser(string header) => browser.Columns.First(c => (c.Header as string) == header);

                FastaItemExtended edited = Named("sp|P83044|DID8B_CERCE");
                int familyBefore = edited.UserID;
                browser.CurrentCell = new DataGridCellInfo(edited, Browser("User ID"));
                browser.SelectedItem = edited;
                browser.BeginEdit();
                PumpDispatcher();
                RightClick(BrowserRow("sp|P0DM77|VM2_DEIAC"));
                PumpDispatcher();
                Check("a right-click leaves no edit open on the old row, which typing would then renumber",
                    browser.CurrentCell.Item == Named("sp|P0DM77|VM2_DEIAC") && !RowOf(browser, edited).IsEditing && edited.UserID == familyBefore,
                    (browser.CurrentCell.Item as FastaItemExtended)?.SequenceIdentifier);
                browser.UnselectAll();
                Press(Item(browser, "Copy UniProt code"));
                Check("and the menu acts on what was selected when it opened, even if a refresh clears the selection under it",
                    copied.LastOrDefault() == "P0DM77", copied.LastOrDefault());

                // Fetch Domains with a cell of the table still open: a refresh would throw and take the app down.
                var fetchButton = typeof(ClusterGenerationControl).GetMethod("ButtonFetchDomains_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var uiContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                browser.CurrentCell = new DataGridCellInfo(Named("sp|P83044|DID8B_CERCE"), Browser("User Annotation"));
                browser.SelectedItem = Named("sp|P83044|DID8B_CERCE");
                browser.BeginEdit();
                PumpDispatcher();
                control.FetchDomainsHandlerOverride = new CannedUniProt((System.Net.HttpStatusCode.OK, UniProtReply));
                fetchButton.Invoke(control, new object?[] { null, null });
                var fetchWatch = System.Diagnostics.Stopwatch.StartNew();
                while (control.fcf.DomainSource.Length == 0 && fetchWatch.Elapsed < TimeSpan.FromSeconds(10)) { PumpDispatcher(); Thread.Sleep(5); }
                PumpDispatcher();
                control.FetchDomainsHandlerOverride = null;
                SynchronizationContext.SetSynchronizationContext(uiContext);
                Check("Fetch Domains with a cell left open commits it and refreshes the table, and the edit is gone",
                    control.fcf.DomainSource == "UniProt 2026_03" && !((System.ComponentModel.IEditableCollectionView)browser.Items).IsEditingItem && control.IsEnabled, control.fcf.DomainSource);
                rows = browser.Items.Cast<FastaItemExtended>().ToList();
                browser.UpdateLayout();

                Check("the Data Browser has the same menu", browser.ContextMenu != null
                    && browser.ContextMenu.Items.OfType<MenuItem>().Select(m => m.Header as string)
                        .SequenceEqual(new[] { "Copy UniProt code", "Copy description", "Open in UniProt" }));
                Check("and a Domains column", browser.Columns.Any(c => (c.Header as string) == "Domains"));
                RightClick(BrowserRow("sp|P83044|DID8B_CERCE"));
                Press(Item(browser, "Copy UniProt code"));
                Check("and its right-click copies the code of the row under the mouse", copied.LastOrDefault() == "P83044", copied.LastOrDefault());
                Check("with the page to open", Item(browser, "Open in UniProt").IsEnabled);
                RightClick(BrowserRow("Venom_contig_12"));
                Check("an identifier from another database has no UniProt page to offer", !Item(browser, "Open in UniProt").IsEnabled);
                Press(Item(browser, "Copy UniProt code"));
                Check("but still copies, whole", copied.LastOrDefault() == "Venom_contig_12", copied.LastOrDefault());
                RightClick(BrowserRow("Reverse_sp|P31987|VM2I_CROVV"));
                Check("a decoy has nothing to copy, and the menu greys that out instead of leaving the last code to be pasted",
                    !Item(browser, "Copy UniProt code").IsEnabled && !Item(browser, "Copy description").IsEnabled
                    && !Item(browser, "Open in UniProt").IsEnabled);
                Check("and Ctrl+C on its empty cell empties the clipboard the same way",
                    CtrlC(browser, Browser("Description"), Named("Reverse_sp|P31987|VM2I_CROVV")) == "", copied.LastOrDefault());
                host.Close();
            }
            finally
            {
                GridCopy.ClipboardOverride = null;
                GridCopy.OpenUrlOverride = null;
            }
        }
    }
}
