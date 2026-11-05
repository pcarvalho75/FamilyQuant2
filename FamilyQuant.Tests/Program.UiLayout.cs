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
        private static void TypographyRulesTests()
        {
            foreach (var mode in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
            {
                FamilyQuant.Themes.ThemeManager.Apply(mode);
                TextBlock Role(string key) => new TextBlock { Text = "Readable text", Style = (Style)Application.Current.FindResource(key) };
                var title = Role("PageTitle"); var section = Role("SectionTitle");
                var body = Role("BodyText"); var caption = Role("CaptionText");
                Check(mode + " shared text roles have a readable hierarchy and line spacing",
                    title.FontSize > section.FontSize && section.FontSize > body.FontSize && body.FontSize > caption.FontSize
                    && new[] { title, section, body, caption }.All(t => t.LineHeight >= t.FontSize));
                var primary = new Button { Style = (Style)Application.Current.FindResource("PrimaryButton") };
                var quiet = new Button { Style = (Style)Application.Current.FindResource(typeof(Button)) };
                Check(mode + " primary and quiet actions share the token spacing",
                    primary.Margin == (Thickness)Application.Current.FindResource("ControlMargin")
                    && quiet.Margin == primary.Margin && primary.Padding == quiet.Padding);
            }
            var file = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "type_fixture", Sequence = "PEPTIDEK", Description = "Example protein", UserID = 1, USearchID = 1 }
            } };
            var control = new ClusterGenerationControl(file);
            Check("curation command labels use sentence case while retaining their commands",
                UiField<Button>(control, "ButtonAlignSelected")!.Content as string == "Align selected"
                && UiField<Button>(control, "ButtonWordCluster")!.Content as string == "Recluster by words"
                && UiField<Button>(control, "ButtonFetchDomains")!.Content as string == "Fetch evidence");
            var preview = new SuperClusterPreviewWindow(new SuperClusterPreview(file.MyExtendedFastaItems,
                null, "example", "", KeywordMatch.Any, null, null));
            Check("preview instructions name the visible fetch command",
                UiField<TextBlock>(preview, "noDomains")!.Text.Contains("Fetch evidence", StringComparison.Ordinal));
            string help = Flatten(HelpContent.Build(null, NormalizationMode.TotalChromatogram, SharedPeptidePolicy.FamilyUnique));
            Check("Help names the visible fetch command and retains the Pfam explanation",
                help.Contains("Fetch evidence asks UniProt", StringComparison.Ordinal)
                && help.Contains("Pfam domains", StringComparison.Ordinal));
            string fetchSource = File.ReadAllText(Path.Combine(GoldenNumbers.FindRepository(), "FamilyQuant", "ClusterGenerationControl.xaml.cs"))
                + File.ReadAllText(Path.Combine(GoldenNumbers.FindRepository(), "FamilyQuant", "CurationViewModel.Evidence.cs"));
            var messages = new List<ShellMessage>();
            void CaptureMessage(ShellMessage value) => messages.Add(value);
            var retry = new ClusterGenerationControl(RetryPanelFile());
            using var normalHandler = new EvidenceAndPanelHandler();
            using var retryHandler = new EvidenceAndPanelHandler(failPanel: true);
            control.FetchDomainsHandlerOverride = normalHandler;
            retry.FetchDomainsHandlerOverride = retryHandler;
            ShellMessages.Reported += CaptureMessage;
            try
            {
                var normalFetch = control.FetchProteinEvidenceAsync(); FinishBusyTask(normalFetch); normalFetch.GetAwaiter().GetResult();
                var retryFetch = retry.RetryPanelAsync(); FinishBusyTask(retryFetch); retryFetch.GetAwaiter().GetResult();
            }
            finally { ShellMessages.Reported -= CaptureMessage; }
            string fetchLabel = (string)UiField<Button>(control, "ButtonFetchDomains")!.Content;
            string retryLabel = (string)UiField<Button>(retry, "ButtonRetryPanel")!.Content;
            Check("fetch retry and dialog title name the visible fetch command",
                messages.Any(message => message.Title == fetchLabel)
                && messages.Any(message => message.Title == retryLabel && message.Text.Contains(retryLabel, StringComparison.Ordinal))
                && !fetchSource.Contains("Fetch Domains", StringComparison.Ordinal));
            control.Measure(new Size(1366, 820)); control.Arrange(new Rect(0, 0, 1366, 820));
            var primaryStyle = (Style)Application.Current.FindResource("PrimaryButton");
            var groups = WalkTree(control).OfType<GroupBox>().ToList();
            Check("each curation panel has at most one primary action",
                groups.Count > 0 && groups.All(g => WalkTree(g).OfType<Button>().Count(b => ReferenceEquals(b.Style, primaryStyle)) <= 1));
            FamilyQuant.Themes.ThemeManager.Apply(FamilyQuant.Themes.AppTheme.Dark);
        }

        private sealed class TableStyleFixture
        {
            public int Number { get; set; }
            public string Description { get; set; } = "";
            public bool Flag { get; set; }
        }

        private static void TableAppearanceTests()
        {
            var rows = Enumerable.Range(1, 5000).Select(i => new TableStyleFixture
                { Number = i, Description = new string('M', 100) + i, Flag = i == 1 }).ToList();
            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false };
            grid.Columns.Add(new DataGridTextColumn { Header = "Number", Binding = new System.Windows.Data.Binding("Number"), Width = 100 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new System.Windows.Data.Binding("Description"), Width = 240 });
            var flags = new Style(typeof(DataGridRow));
            var flag = new DataTrigger { Binding = new System.Windows.Data.Binding("Flag"), Value = true };
            flag.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ErrorBrush")));
            flags.Triggers.Add(flag); grid.RowStyle = flags;
            var window = new Window { Content = grid, Width = 640, Height = 360, Left = -30000, Top = -30000,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                // Imports fill already-visible grids, so numeric inference must also follow a later source.
                grid.ItemsSource = rows;
                PumpDispatcher(); window.UpdateLayout();
                foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
                {
                    FamilyQuant.Themes.ThemeManager.Apply(theme); PumpDispatcher(); window.UpdateLayout();
                    var number = grid.Columns[0].GetCellContent(rows[0]) as TextBlock;
                    var description = grid.Columns[1].GetCellContent(rows[0]) as TextBlock;
                    Check(theme + " table numbers align right and truncated text remains available in a tooltip",
                        number?.TextAlignment == TextAlignment.Right && description?.TextTrimming == TextTrimming.CharacterEllipsis
                        && description.ToolTip as string == rows[0].Description);
                    grid.SelectedItem = rows[0]; PumpDispatcher(); window.UpdateLayout();
                    var row = grid.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow;
                    Check(theme + " selection uses tokens and retains the existing status flag",
                        (row?.Background as SolidColorBrush)?.Color == ((SolidColorBrush)Application.Current.FindResource("SelectionBrush")).Color
                        && (row?.Foreground as SolidColorBrush)?.Color == ((SolidColorBrush)Application.Current.FindResource("ErrorBrush")).Color,
                        "selected=" + row?.IsSelected + "; background=" + row?.Background + "; foreground=" + row?.Foreground);
                }
                var replacement = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
                replacement.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "Updated column fact remains reachable after import."));
                grid.Columns[0].HeaderStyle = replacement; PumpDispatcher(); window.UpdateLayout();
                var header = WalkTree(grid).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>()
                    .FirstOrDefault(h => h.Column == grid.Columns[0]);
                Check("headers remain themed and keep replaced tooltip facts",
                    header?.ToolTip as string == "Updated column fact remains reachable after import."
                    && (header.Background as SolidColorBrush)?.Color == ((SolidColorBrush)Application.Current.FindResource("SubtleBrush")).Color);
                int realized = WalkTree(grid).OfType<DataGridRow>().Count();
                Check("large tables keep recycling virtualization and do not alter source values",
                    grid.EnableRowVirtualization && grid.EnableColumnVirtualization
                    && VirtualizingPanel.GetVirtualizationMode(grid) == VirtualizationMode.Recycling
                    && realized < 100 && rows.Select(r => r.Number).SequenceEqual(Enumerable.Range(1, 5000)),
                    realized.ToString(CultureInfo.InvariantCulture) + " realized rows");
            }
            finally { window.Close(); FamilyQuant.Themes.ThemeManager.Apply(FamilyQuant.Themes.AppTheme.Dark); }
        }

        /// <summary>
        /// Runs everything already queued on this thread's dispatcher, then returns.
        ///
        /// The suite has no message loop of its own, so an <c>await</c> inside a handler would never
        /// resume. The frame is ended from a Background-priority item, so every continuation already
        /// posted at Normal priority has run by the time this returns.
        /// </summary>
        private static void PumpDispatcher()
        {
            var frame = new DispatcherFrame();

            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => frame.Continue = false));

            Dispatcher.PushFrame(frame);
        }

        /// <summary>The tooltip a column's header style installs, which is where a DataGridColumn has to
        /// carry one because it is not a control itself.</summary>
        private static string? HeaderTooltipOf(DataGridColumn column)
        {
            if (column.HeaderStyle == null) return null;

            foreach (SetterBase setterBase in column.HeaderStyle.Setters)
            {
                if (setterBase is Setter setter && setter.Property == FrameworkElement.ToolTipProperty)
                    return setter.Value as string;
            }

            return null;
        }

        /// <summary>
        /// A Family Cluster File that looks like a real one: several families, decoys, a contaminant and
        /// descriptions long enough to stress the table, built without USEARCH so this section always runs.
        /// </summary>
        /// <summary>The exact share the real project reports for its largest family, used as a fixture.</summary>
        private const double SampleShare = 0.121109;

        private static FamilyClusterFile BuildSyntheticFcf()
        {
            var items = new List<FastaItemExtended>();

            string[] descriptions =
            {
                "Snake venom metalloproteinase BJ-PI OS=Bothrops jararaca OX=8724 GN=svmp PE=1 SV=2",
                "Zinc metalloproteinase-disintegrin-like jararhagin OS=Bothrops jararaca OX=8724",
                "Basic phospholipase A2 homolog OS=Bothrops jararaca OX=8724 GN=pla2 PE=1 SV=1",
                "L-amino-acid oxidase OS=Bothrops jararaca OX=8724 GN=laao PE=1 SV=1",
                "Snake venom serine protease bothrombin OS=Bothrops jararaca OX=8724",
            };

            for (int family = 0; family < descriptions.Length; family++)
            {
                for (int member = 0; member < 3; member++)
                {
                    items.Add(new FastaItemExtended
                    {
                        SequenceIdentifier = $"sp|P{family}{member}0000|VM{family}_BOTJA",
                        Description = descriptions[family],
                        Sequence = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFR",
                        USearchID = family,
                        UserID = family,
                        XIC = family == 0 ? SampleShare : 0,
                        XICUser = family == 0 ? SampleShare : 0
                    });

                    items.Add(new FastaItemExtended
                    {
                        SequenceIdentifier = $"Reverse_sp|P{family}{member}0000|VM{family}_BOTJA",
                        Description = "",
                        Sequence = "RFVIQLSDGYFDWTMKSTAVIHPSRHEEQ",
                        USearchID = descriptions.Length + family,
                        UserID = descriptions.Length + family
                    });
                }
            }

            items.Add(new FastaItemExtended
            {
                SequenceIdentifier = "contaminant_KRT1_HUMAN",
                Description = "Keratin, type II cytoskeletal 1 OS=Homo sapiens OX=9606 GN=KRT1",
                Sequence = "MSRQFSSRSGYRSGGGFSSGSAGIINYQRRT",
                USearchID = 2 * descriptions.Length,
                UserID = 2 * descriptions.Length
            });

            return new FamilyClusterFile { ClusteringIdentity = 85, MyExtendedFastaItems = items };
        }

        private static void EnsureApplication()
        {
            // The controls reference the shared theme brushes declared in App.xaml, so a real Application
            // must exist first. InitializeComponent only loads resources; StartupUri fires on Run(),
            // which is never called here. Only one Application may ever exist in a process.
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                return;
            }

            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.GetType().GetMethod("InitializeComponent")?.Invoke(app, null);
        }

        private static bool HasAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            for (DependencyObject? current = node; current != null; current = VisualParentOf(current))
            {
                if (current is T && !ReferenceEquals(current, node)) return true;
            }

            return false;
        }

        private static T? UiField<T>(object control, string name) where T : class
        {
            var value = control.GetType()
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?
                .GetValue(control) as T;
            if (value != null) return value;
            if (control is ClusterGenerationControl curation && curation.FindName("QuantitationPanel") is CurationQuantitationPanel panel)
                return UiField<T>(panel, name);
            return null;
        }
    }
}
