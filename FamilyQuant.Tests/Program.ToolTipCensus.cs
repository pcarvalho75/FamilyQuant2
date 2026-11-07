using FamilyQuant.DatabaseUpdate;
using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Alignment;
using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ToolTipCensusCommandTable() =>
    [
        new("--tooltip-census", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--tooltip-census", (args, _) =>
        {
            if (args.Length != 2) throw new ArgumentException("Use --tooltip-census <new-output-directory>.");
            return ToolTipCensusRun(args[1]);
        }),
    ];

    internal sealed record ToolTipCensusRow(string Kind, string Label, string Name, string Host, bool Enabled, string ToolTip, string Status, string Wording);
    internal sealed record ToolTipCensusTable(string Window, string Owner, List<ToolTipCensusRow> Rows, string Note);

    /// <summary>Kinds listed for reading but not required to carry a tooltip by rule 7.</summary>
    private static readonly HashSet<string> CensusInformationalKinds = new(StringComparer.Ordinal) { "read-only text", "menu", "table" };
    private static readonly Regex CensusColourWord = new(@"\b(red|green|blue|yellow|orange|purple|pink|grey|gray|black|white|gold|golden|violet|brown|teal|lilac|rose|mint)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CensusD21Word = new(@"\b(venom|venoms|toxin|toxins|snake|snakes|viper|vipers|serpent)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private const BindingFlags CensusAny = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static int ToolTipCensusRun(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        EnsureApplication();
        var previous = Themes.ThemeManager.Current;
        List<ToolTipCensusTable> tables;
        try { Themes.ThemeManager.Apply(Themes.AppTheme.Dark); tables = ToolTipCensus(); }
        finally { Themes.ThemeManager.Apply(previous); }
        WriteToolTipCensus(output, tables);
        foreach (var table in tables)
            Console.WriteLine(table.Window + " (" + table.Owner + "): " + CensusCounts(table) + (table.Note.Length > 0 ? "; " + table.Note : ""));
        Console.WriteLine("Census written to " + output);
        return tables.Any(t => t.Note.StartsWith("OPEN FAILED", StringComparison.Ordinal)) ? 1 : 0;
    }

    private static string CensusCounts(ToolTipCensusTable table) =>
        table.Rows.Count.ToString(CultureInfo.InvariantCulture) + " controls, "
        + table.Rows.Count(r => r.Status == "OK").ToString(CultureInfo.InvariantCulture) + " with a tooltip, "
        + table.Rows.Count(r => r.Status == "NONE").ToString(CultureInfo.InvariantCulture) + " NONE, "
        + table.Rows.Count(r => r.Status == "REPEATS-LABEL").ToString(CultureInfo.InvariantCulture) + " REPEATS-LABEL, "
        + table.Rows.Count(r => r.Wording.Length > 0).ToString(CultureInfo.InvariantCulture) + " wording";

    /// <summary>Opens every window as the screens harness and each window's own test do, reveals every tab and expander,
    /// and lists each interactive control with its tooltip. Every opener is guarded, so a failure is a row, not a stop.</summary>
    internal static List<ToolTipCensusTable> ToolTipCensus()
    {
        var tables = new List<ToolTipCensusTable>();
        string scratch = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "tooltip-census-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        void Guard(string window, string owner, Func<CensusCollector, string> open)
        {
            var collector = new CensusCollector();
            string note;
            try { note = open(collector); }
            catch (Exception ex) { note = "OPEN FAILED: " + ex.GetType().Name + ": " + (ex.InnerException ?? ex).Message; }
            tables.Add(new ToolTipCensusTable(window, owner, collector.Rows, note));
        }
        void Show(Window window)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -30000; window.Top = -30000;
            window.Width = Math.Max(window.Width is double.NaN ? 0 : window.Width, 1366); window.Height = Math.Max(window.Height is double.NaN ? 0 : window.Height, 820);
            window.ShowActivated = false; window.ShowInTaskbar = false; window.Show(); PumpDispatcher();
        }
        string Visit(Window window, CensusCollector collector, string note = "")
        {
            Show(window);
            try { collector.Collect(window); return note; }
            finally { window.Close(); PumpDispatcher(); }
        }

        var source = CensusFixture();
        var manifest = FinishStoredManifest(); var quant = FinishQuantRows();
        var main = new MainWindow();
        Show(main);
        try
        {
            var appGrid = (Grid)typeof(MainWindow).GetField("GridApp", CensusAny)!.GetValue(main)!;
            void Present(UserControl control, string active)
            {
                foreach (string field in new[] { "home", "usearch", "dnc", "analytics" }) typeof(MainWindow).GetField(field, CensusAny)?.SetValue(main, null);
                typeof(MainWindow).GetField(active, CensusAny)?.SetValue(main, control);
                appGrid.Children.Clear(); appGrid.Children.Add(control);
                typeof(MainWindow).GetMethod("UpdateGuide", CensusAny)?.Invoke(main, null);
                PumpDispatcher();
            }
            Guard("Main window and menus", "Z12q3 FQFetch", c => { c.Collect(main, skip: appGrid); return ""; });
            Guard("Home", "Z12q3 FQFetch", c => { c.Collect(appGrid.Children.OfType<UIElement>().Single()); return ""; });
            Guard("Build families (USearch)", "Z12q8 FQTips", c =>
            {
                typeof(MainWindow).GetMethod("MenuRunUSearch_Click", CensusAny)!.Invoke(main, new object[] { main, new RoutedEventArgs() }); PumpDispatcher();
                c.Collect(appGrid.Children.OfType<UIElement>().Single()); return "";
            });
            Guard("De novo and de novo filter", "Z12q4 FQTips", c =>
            {
                typeof(MainWindow).GetMethod("MenuItemDeNovo_Click", CensusAny)!.Invoke(main, new object[] { main, new RoutedEventArgs() }); PumpDispatcher();
                c.Collect(appGrid.Children.OfType<UIElement>().Single()); return "";
            });
            var curation = new ClusterGenerationControl(source);
            typeof(MainWindow).GetField("cgc", CensusAny)!.SetValue(main, curation);
            typeof(MainWindow).GetField("loadedCuration", CensusAny)?.SetValue(main, source);
            Guard("Curate families (Group by evidence setup page and curation quantitation)", "Z12q3 FQFetch, Z12q8 FQTips", c =>
            {
                Present(curation, "cgc"); c.Collect(curation); return "";
            });
            Guard("Quantify", "Z12q8 FQTips", c =>
            {
                typeof(MainWindow).GetMethod("PresentQuantifyScreen", CensusAny)!.Invoke(main, null); PumpDispatcher();
                c.Collect(appGrid.Children.OfType<UIElement>().Single()); return "";
            });
            Guard("Analytics and Family sensitivity", "Z12q8 FQTips, Z12q4 FQTips", c =>
            {
                Present(new AnalyticsControl(quant, manifest, source), "analytics"); c.Collect(appGrid.Children.OfType<UIElement>().Single()); return "";
            });
        }
        finally { main.Close(); PumpDispatcher(); }

        Guard("Busy overlay", "Z12q3 FQFetch", c => Visit(new Window { Content = new BusyOverlay() }, c));
        Guard("Compute XIC", "Z12q4 FQTips", c => Visit(new ComputeXicWindow(AppVersionInfo.Current(), manifest, source), c));
        Guard("Database gaps", "Z12q4 FQTips", c => Visit(new Window { Content = new DatabaseGapControl(new DatabaseGapViewModel(manifest.FamilyWorldResult, source)) }, c));
        Guard("Group by words preview", "Z12q9 FQTips", c => Visit(new SuperClusterPreviewWindow(new SuperClusterPreview(source.MyExtendedFastaItems, null,
            "\"Example protein\"", "", KeywordMatch.Any, null, source.LockedUserIds)), c));
        GroupingReviewViewModel? reviewModel = null;
        Guard("Group by evidence review, Architecture review and details pane", "Z12q9 FQTips, Z12q4 FQTips", c =>
        {
            var review = ReviewLayoutWindow();
            reviewModel = (GroupingReviewViewModel)review.DataContext;
            return Visit(review, c);
        });
        Guard("Add to family by hand", "Z12q9 FQTips", c =>
        {
            var evidence = reviewModel?.Evidence ?? throw new InvalidOperationException("The review fixture did not open.");
            var editor = new ManualFamilyViewModel(evidence, new[] { evidence.Proteins.First() });
            return Visit(new ManualFamilyWindow(editor), c);
        });
        Guard("Coherence members", "Z12q9 FQTips", c =>
        {
            ProteinEvidence Evidence(string accession, string species) => new()
            { Accession = accession, Reviewed = true, Family = "Invented family", OrganismName = species, TaxonId = 100, Keywords = new List<string> { "scope" } };
            var file = new FamilyClusterFile();
            for (int i = 0; i < 3; i++) file.MyExtendedFastaItems.Add(new FastaItemExtended { SequenceIdentifier = "sp|P1000" + i.ToString(CultureInfo.InvariantCulture) + "|invented",
                Sequence = new string('A', 90) + new string('C', 8 + i), Evidence = Evidence("P1000" + i.ToString(CultureInfo.InvariantCulture), "Invented species") });
            file.EvidenceSource = new EvidenceProvenance { ReferencePanel = new ReferencePanel { Entries = new List<PanelEntry>
            { new() { Accession = "P20000", Sequence = new string('A', 90) + new string('C', 10), Evidence = Evidence("P20000", "Invented panel species") } } } };
            return Visit(new CoherenceMembersWindow(CoherenceCheck.Learn(file)["Invented family"]), c);
        });
        Guard("Family list import wizard", "Z12q2 FQHome", c => CensusWizard(c, scratch, Show));
        Guard("Import family list guide", "unassigned", c => Visit(new FamilyListImportGuideWindow(), c));
        Guard("Family panel editor", "Z12q3 FQFetch", c => Visit(new PanelEditorWindow(new FamilyPanel { Name = "Panel",
            Families = new() { new FamilyPanelFamily { Key = "a", DisplayName = "Family a" } } }, evidenceFile: source), c));
        Guard("Match stored names", "Z12q3 FQFetch", c =>
        {
            var file = EvidenceMatchingFile();
            var evidence = new PanelEvidenceMatching(file);
            var context = new PanelEvidenceMatchContext(evidence, file.FamilyPanel!.Families[0], null, () => evidence.IsCurrent(file));
            return Visit(new PanelEvidenceMatchWindow(new PanelEvidenceMatchViewModel(context)), c);
        });
        Guard("Database update", "Z12q5 FQFloat", c =>
        {
            var data = DatabaseUpdateShellData();
            var update = DatabaseUpdateSource.FromEntries("new-database.fasta", "NEWSHA", data.Entries);
            var comparison = DatabaseUpdateComparer.Compare(data.Project, update);
            var preview = DatabaseUpdatePlanner.Preview(data.Project, comparison, DatabaseUpdatePlacer.Place(data.Project, comparison));
            var model = new DatabaseUpdateViewModel(preview, data.Project.MyExtendedFastaItems.Select(p => p.SequenceIdentifier).ToArray(), "new-database.fasta", "project.fqproj");
            return Visit(new DatabaseUpdateWindow(model), c);
        });
        Guard("Align selected", "Z12q5 FQFloat", c =>
        {
            var fixture = AlignFixture();
            var session = new EvidenceGroupingSession(fixture.File, "census fixture", (_, _) => fixture.Search);
            session.Propose("hydrolase");
            var set = AlignmentCandidates.Find(fixture.File, session.Proposal, new[] { fixture.QueryIndex });
            return Visit(new AlignmentWindow(new AlignmentViewModel(set)), c);
        });
        Guard("Installer", "Z12q5 FQFloat", c =>
        {
            foreach (var controller in new[] { new CensusSetupController(null, false), new CensusSetupController(new Setup.ExistingInstallation(CensusSetupController.Folder, "2.1.2", true, true), false),
                new CensusSetupController(new Setup.ExistingInstallation(CensusSetupController.Folder, "2.1.2", true, true), true) })
                Visit(new Setup.InstallerWindow(controller), c);
            return "fresh install, upgrade and uninstall start pages; later pages need a real install";
        });
        Guard("Lock manager", "unassigned", c => Visit(new LockManagerWindow(source), c));
        Guard("Spectrum comparison", "unassigned", c => Visit(SpectrumComparisonWindow.Create(manifest.SpectrumComparisonRows), c));
        Guard("Identity scan", "unassigned", c => Visit(IdentityScanWindow.Create("Identity scan", manifest.PresentationIdentitySweep ?? new List<IdentityScanRow>(), "Identity\tFamilies"), c));
        Guard("Text report and Help", "unassigned", c => Visit(TextReportWindow.Create("FamilyQuant help",
            HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique), true), c));
        try { Directory.Delete(scratch, true); } catch (IOException) { }
        return tables;
    }

    private static string CensusWizard(CensusCollector collector, string scratch, Action<Window> show)
    {
        var file = ManualChoiceFile();
        var curation = new CurationViewModel(file);
        var model = new FamilyListImportViewModel(new FamilyListImportTransaction(curation), matcher: new FamilyListStoredEvidenceMatches(file));
        var window = new FamilyListImportWindow(model, path => ExampleFamilyList.Write(path), PanelImportTemplate.Write);
        string workbook = Path.Combine(scratch, ExampleFamilyList.DefaultFileName);
        ExampleFamilyList.Write(workbook);
        var pages = new List<string>();
        show(window);
        try
        {
            void Page() { PumpDispatcher(); collector.Collect(window); pages.Add((model.Page + 1).ToString(CultureInfo.InvariantCulture)); }
            Page();
            FinishReviewWork(() => model.LoadAsync(workbook)); Page();
            ((Button)window.FindName("ForwardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Page();
            var decision = model.Columns.Single(c => c.Role == PanelImportFields.Decision);
            decision.Header = decision.Headers.First(h => h.Contains("DECIS", StringComparison.OrdinalIgnoreCase));
            ((Button)window.FindName("ForwardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Page();
            model.ConfirmDecisions();
            FinishReviewWork(async () => { await model.NextAsync(); }); Page();
            while (model.SpecialRows.FirstOrDefault(r => r.Blocking) is { } blocking)
            { model.SelectedSpecialRow = blocking; blocking.Action = FamilyPanelRowAction.Import; model.UseRowChoice(); }
            Page();
            FinishReviewWork(async () => { await model.NextAsync(); }); Page();
            return "pages walked: " + string.Join(", ", pages.Distinct(StringComparer.Ordinal)) + "; later pages need the matching answers";
        }
        finally { window.Close(); PumpDispatcher(); }
    }

    private static FamilyClusterFile CensusFixture() => new()
    {
        ClusteringIdentity = 85,
        MyExtendedFastaItems = Enumerable.Range(1, 100).Select(i => new FastaItemExtended
        {
            SequenceIdentifier = "fixture_" + i.ToString(CultureInfo.InvariantCulture),
            Description = "Example protein " + (i % 5).ToString(CultureInfo.InvariantCulture) + " OS=Synthetic fixture",
            Sequence = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFR", UserID = i % 5, USearchID = i % 5,
        }).ToList(),
        LockedUserIds = new HashSet<int> { 1 },
    };

    private sealed class CensusSetupController : Setup.ISetupController
    {
        internal const string Folder = @"C:\Users\Example\AppData\Local\Programs\FamilyQuant2";
        private readonly Setup.ExistingInstallation? existing;
        public CensusSetupController(Setup.ExistingInstallation? existing, bool uninstall) { this.existing = existing; Options = new Setup.SetupOptions { Uninstall = uninstall }; }
        public Setup.SetupOptions Options { get; }
        public string DefaultDirectory => Folder;
        public string PayloadVersion => "2.1.11.0";
        public Setup.ExistingInstallation? DetectExisting() => existing;
        public bool IsAppRunning(string directory) => false;
        public System.Threading.Tasks.Task<Setup.SetupResult> InstallAsync(string directory, bool desktopShortcut, IProgress<Setup.SetupProgress>? progress = null,
            System.Threading.CancellationToken cancellationToken = default, bool allowOlderVersion = false) => throw new InvalidOperationException("The census never installs.");
        public System.Threading.Tasks.Task<Setup.SetupResult> UninstallAsync(string directory, IProgress<Setup.SetupProgress>? progress = null,
            System.Threading.CancellationToken cancellationToken = default) => throw new InvalidOperationException("The census never uninstalls.");
        public void Launch(string directory) => throw new InvalidOperationException("The census never launches.");
    }

    /// <summary>Collects controls from a window or a subtree; selects every tab and expands every expander so their
    /// content exists; reads context menus and DataGrid column headers; one row per distinct control.</summary>
    private sealed class CensusCollector
    {
        private readonly HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> revealedPanels = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> keys = new(StringComparer.Ordinal);
        public List<ToolTipCensusRow> Rows { get; } = new();

        public void Collect(DependencyObject root, DependencyObject? skip = null)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                bool revealed = false;
                foreach (var node in Nodes(root, skip).ToArray())
                {
                    Add(node);
                    if (node is Expander { IsExpanded: false } expander && revealedPanels.Add(expander)) { expander.IsExpanded = true; revealed = true; }
                    if (node is TabControl tabs && revealedPanels.Add(tabs))
                    {
                        foreach (var item in tabs.Items.OfType<TabItem>().ToArray()) { tabs.SelectedItem = item; PumpDispatcher(); foreach (var inner in Nodes(tabs, skip).ToArray()) Add(inner); }
                        revealed = true;
                    }
                }
                PumpDispatcher();
                if (!revealed) break;
            }
        }

        private static IEnumerable<DependencyObject> Nodes(DependencyObject root, DependencyObject? skip)
        {
            var seen = new HashSet<DependencyObject>(); var pending = new Stack<DependencyObject>(); pending.Push(root);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (ReferenceEquals(node, skip) || !seen.Add(node)) continue;
                yield return node;
                foreach (object child in LogicalTreeHelper.GetChildren(node)) if (child is DependencyObject d) pending.Push(d);
                if (node is Visual or System.Windows.Media.Media3D.Visual3D)
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) pending.Push(VisualTreeHelper.GetChild(node, i));
                // Context menus written by the application; a theme's default Cut, Copy and Paste menu is not a local value.
                if (node is FrameworkElement { ContextMenu: { } menu } owner && owner.ReadLocalValue(FrameworkElement.ContextMenuProperty) != DependencyProperty.UnsetValue)
                    pending.Push(menu);
            }
        }

        private void Add(DependencyObject node)
        {
            if (node is DataGrid grid && !InsideCell(grid))
                foreach (var column in grid.Columns)
                {
                    if (!seen.Add(column)) continue;
                    string header = Text(column.Header);
                    string tip = Themes.TableAppearance.GetHeaderToolTip(column) ?? StyleToolTip(column.HeaderStyle)
                        ?? Text((column.Header as FrameworkElement)?.ToolTip) ?? Realized(grid, column) ?? string.Empty;
                    Row("column header", header.Length == 0 ? "(no header text)" : header, string.Empty, Host(grid), column.Visibility == Visibility.Visible, tip);
                }
            string? kind = Kind(node);
            if (kind == null || !seen.Add(node) || TemplatePart(node) || InsideCell(node)) return;
            bool enabled = node is UIElement element ? element.IsEnabled : node is ContentElement content ? content.IsEnabled : true;
            object? toolTip = node is FrameworkElement fe ? fe.ToolTip : (node as FrameworkContentElement)?.ToolTip;
            Row(kind, Label(node), (node as FrameworkElement)?.Name ?? string.Empty, Host(node), enabled, Text(toolTip));
        }

        private void Row(string kind, string label, string name, string host, bool enabled, string tip)
        {
            tip = Regex.Replace(tip, @"\s+", " ").Trim();
            if (!keys.Add(kind + "\u0001" + label + "\u0001" + name + "\u0001" + host + "\u0001" + tip)) return;
            string status = tip.Length == 0 ? "NONE" : RepeatsLabel(label, tip) ? "REPEATS-LABEL" : "OK";
            var wording = new List<string>();
            if (tip.Contains('\u2013') || tip.Contains('\u2014')) wording.Add("DASH");
            foreach (Match m in CensusColourWord.Matches(tip)) wording.Add("COLOUR:" + m.Value.ToLowerInvariant());
            foreach (Match m in CensusD21Word.Matches(tip)) wording.Add("D21:" + m.Value.ToLowerInvariant());
            Rows.Add(new ToolTipCensusRow(kind, label, name, host, enabled, tip, status, string.Join(" ", wording.Distinct(StringComparer.Ordinal))));
        }

        private static string? Kind(DependencyObject node) => node switch
        {
            MenuItem { Role: MenuItemRole.TopLevelHeader } => "menu",
            MenuItem item when Text(item.Header).Length > 0 => "menu item",
            RadioButton => "option",
            CheckBox => "check box",
            TabItem => "tab",
            Expander => "expander",
            ComboBox => "combo box",
            TextBox { IsReadOnly: true } => "read-only text",
            TextBox or PasswordBox or Slider or DatePicker => "input box",
            Control c when c.GetType().Name.EndsWith("UpDown", StringComparison.Ordinal) => "input box",
            DataGrid => "table",
            ListBox => "list",
            ButtonBase => "button",
            Hyperlink => "link",
            _ => null,
        };

        /// <summary>A part of a control template (a combo box's toggle, a scroll bar button); content from a data template is kept.</summary>
        private static bool TemplatePart(DependencyObject node) => node is FrameworkElement { TemplatedParent: Control parent }
            && !(parent is ContentControl owner && ReferenceEquals(owner.Content, node));

        private static bool InsideCell(DependencyObject node)
        {
            if (node is not Visual) return false;
            for (var current = VisualTreeHelper.GetParent(node); current != null; current = VisualTreeHelper.GetParent(current))
                if (current is DataGridCell or DataGridColumnHeader or ScrollBar) return true;
            return false;
        }

        private static string Host(DependencyObject node)
        {
            for (DependencyObject? current = node; current != null;
                current = (current is Visual ? VisualTreeHelper.GetParent(current) : null) ?? LogicalTreeHelper.GetParent(current))
                if (current is UserControl or Window && !ReferenceEquals(current, node)) return current.GetType().Name;
            return string.Empty;
        }

        private static string Label(DependencyObject node)
        {
            string text = node switch
            {
                HeaderedItemsControl h => Text(h.Header),
                HeaderedContentControl h => Text(h.Header),
                ContentControl { Content: string or DependencyObject or null } c => Text(c.Content),
                ContentControl c => Text((FrameworkElement)c),
                Hyperlink link => new TextRange(link.ContentStart, link.ContentEnd).Text,
                _ => string.Empty,
            };
            if (text.Length == 0) text = AutomationProperties.GetName(node);
            if (text.Length == 0 && AutomationProperties.GetLabeledBy(node) is { } by) text = Label(by) is { Length: > 0 } l ? l : by is TextBlock t ? t.Text : string.Empty;
            if (text.Length == 0 && node is ComboBox combo) text = Text(combo.SelectedItem);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        internal static string Text(object? value) => value switch
        {
            null => string.Empty,
            string s => s.Replace("__", "\u0001", StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).Replace("\u0001", "_", StringComparison.Ordinal),
            ToolTip t => Text(t.Content),
            TextBlock t => t.Text.Length > 0 ? t.Text : new TextRange(t.ContentStart, t.ContentEnd).Text,
            AccessText a => a.Text.Replace("_", string.Empty, StringComparison.Ordinal),
            FrameworkElement fe => string.Join(" ", Nodes(fe, null).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0)),
            DependencyObject => string.Empty,
            _ => value.ToString() ?? string.Empty,
        };

        private static string? StyleToolTip(Style? style)
        {
            for (var current = style; current != null; current = current.BasedOn)
                if (current.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.ToolTipProperty)?.Value is { } value) return Text(value);
            return null;
        }

        private string? Realized(DataGrid grid, DataGridColumn column) => Nodes(grid, null).OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => ReferenceEquals(h.Column, column))?.ToolTip is { } tip ? Text(tip) : null;

        internal static bool RepeatsLabel(string label, string tip)
        {
            var labelWords = Words(label); var tipWords = Words(tip);
            return labelWords.Count > 0 && tipWords.Count > 0 && tipWords.IsSubsetOf(labelWords);
        }

        private static HashSet<string> Words(string text) => Regex.Matches(string.Concat(text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The census status of one control on its own, for the check's negative controls.</summary>
    private static string ToolTipCensusProbe(Control control)
    {
        var window = new Window { Content = control, Width = 300, Height = 200, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false, ShowActivated = false };
        try { window.Show(); PumpDispatcher(); var collector = new CensusCollector(); collector.Collect(window); return collector.Rows.Single().Status; }
        finally { window.Close(); PumpDispatcher(); }
    }

    private static void WriteToolTipCensus(string output, List<ToolTipCensusTable> tables)
    {
        static string Cell(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        static string Md(string value) => Cell(value).Replace("|", "\\|", StringComparison.Ordinal);
        var summary = new StringBuilder("# Tooltip census (Z12q1)\n\nOne table per window: control kind, label, x:Name, host view, enabled, tooltip text, status "
            + "(OK, NONE, REPEATS-LABEL) and wording flags (DASH, COLOUR, D21). Kinds menu, table and read-only text are listed for reading; "
            + "rule 7 requires the others.\n\n| Window | Owner | Counts | Note |\n|---|---|---|---|\n");
        var details = new StringBuilder();
        for (int i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            string file = (i + 1).ToString("D2", CultureInfo.InvariantCulture) + "-" + Regex.Replace(table.Window.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-') + ".tsv";
            File.WriteAllLines(Path.Combine(output, file), new[] { "kind\tlabel\tname\thost\tenabled\ttooltip\tstatus\twording" }
                .Concat(table.Rows.Select(r => string.Join("\t", Cell(r.Kind), Cell(r.Label), Cell(r.Name), Cell(r.Host), r.Enabled ? "yes" : "no", Cell(r.ToolTip), r.Status, r.Wording))),
                new UTF8Encoding(false));
            summary.Append("| ").Append(Md(table.Window)).Append(" | ").Append(Md(table.Owner)).Append(" | ").Append(CensusCounts(table)).Append(" | ").Append(Md(table.Note)).Append(" |\n");
            details.Append("\n## ").Append(Md(table.Window)).Append("\n\nOwner: ").Append(Md(table.Owner)).Append(". File: ").Append(file).Append(table.Note.Length > 0 ? ". " + Md(table.Note) : string.Empty)
                .Append("\n\n| Kind | Label | Name | Host | Enabled | Tooltip | Status | Wording |\n|---|---|---|---|---|---|---|---|\n");
            foreach (var r in table.Rows)
                details.Append("| ").Append(string.Join(" | ", Md(r.Kind), Md(r.Label), Md(r.Name), Md(r.Host), r.Enabled ? "yes" : "no", Md(r.ToolTip), r.Status, r.Wording)).Append(" |\n");
        }
        File.WriteAllText(Path.Combine(output, "census.md"), summary.ToString() + details, new UTF8Encoding(false));
    }
}
