using FamilyQuant.Themes;
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

        /// <summary>
        /// The title showed the csproj's assembly version (1.5.0.0) on an installed 1.5.0.2: Visual
        /// Studio's Publish increments only the publish profile's ApplicationVersion. The installed copy
        /// now reports the deployment version the ClickOnce launcher passes in ClickOnce_CurrentVersion.
        /// </summary>
        private static void AppVersionTests()
        {
            DeploymentIdentityTests();
            Check("2.0 has its own executable assembly identity",
                typeof(AppVersionInfo).Assembly.GetName().Name == "FamilyQuant2");
            Check("the resource namespace stays compatible after the project rename",
                typeof(AppVersionInfo).Assembly.GetManifestResourceNames()
                    .Contains("FamilyQuant.MyResources.resources"));
            Check("2.0 has a local data folder separate from 1.x",
                ApplicationPaths.LocalDataDirectory == Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamilyQuant2"));
            Check("2.0 isolates USEARCH scratch files from 1.x",
                ApplicationPaths.ScratchDirectory == Path.Combine(Path.GetTempPath(), "FamilyQuant2"));

            string? usearchOverride = Environment.GetEnvironmentVariable("FAMILYQUANT_USEARCH");
            try
            {
                Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", null);
                string bundled = (string)typeof(USearchTools)
                    .GetMethod("GetUSearchPath", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null)!;
                Check("the bundled USEARCH is extracted under the 2.0 data folder",
                    Path.GetDirectoryName(bundled) == ApplicationPaths.LocalDataDirectory && File.Exists(bundled), bundled);
            }
            finally
            {
                Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", usearchOverride);
            }

            var assembly = new Version(1, 5, 0, 0);
            Check("the ClickOnce deployment version wins, all four parts",
                AppVersionInfo.Resolve("1.5.0.2", assembly) == "1.5.0.2");
            Check("without ClickOnce the assembly version is reported",
                AppVersionInfo.Resolve(null, assembly) == "1.5.0.0" && AppVersionInfo.Resolve(" ", assembly) == "1.5.0.0");
            Check("an unparseable deployment version falls back to the assembly version",
                AppVersionInfo.Resolve("not-a-version", assembly) == "1.5.0.0");
            Check("no version at all reads unknown",
                AppVersionInfo.Resolve(null, null) == "unknown");

            // End to end through the window title, with the variable set the way the launcher sets it.
            string? saved = Environment.GetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable);
            try
            {
                Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, "9.8.7.6");
                var main = new MainWindow();
                Check("the window identifies FamilyQuant 2.0 beside the older application",
                    main.Title.StartsWith("FamilyQuant 2.0 ", StringComparison.Ordinal), main.Title);
                Check("an installed copy shows its deployment version in the title",
                    main.Title.EndsWith(" 9.8.7.6", StringComparison.Ordinal), main.Title);
                main.Close();

                Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, null);
                string own = typeof(AppVersionInfo).Assembly.GetName().Version!.ToString();
                var dev = new MainWindow();
                Check("a build run outside ClickOnce shows the assembly version in the title",
                    dev.Title.EndsWith(" " + own, StringComparison.Ordinal), dev.Title);
                dev.Close();
            }
            finally
            {
                Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, saved);
            }
        }

        private static void DeploymentIdentityTests()
        {
            Check("the 2.0 assembly starts at version 2.0.0.0",
                typeof(AppVersionInfo).Assembly.GetName().Version == new Version(2, 0, 0, 0));

            DirectoryInfo? root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "FamilyQuant2.sln")))
                root = root.Parent;
            Check("the deployment contract can find the checkout", root != null);
            if (root == null) return;

            var project = System.Xml.Linq.XDocument.Load(
                Path.Combine(root.FullName, "FamilyQuant", "FamilyQuant.App.csproj"));
            var profile = System.Xml.Linq.XDocument.Load(
                Path.Combine(root.FullName, "FamilyQuant", "Properties", "PublishProfiles", "ClickOnceProfile.pubxml"));
            string? Property(System.Xml.Linq.XDocument document, string name) =>
                document.Root!.Elements("PropertyGroup").SelectMany(g => g.Elements(name))
                    .Select(e => e.Value).SingleOrDefault();

            Check("assembly and file versions match the fixed 2.0 deployment version",
                new[] { "Version", "AssemblyVersion", "FileVersion" }
                    .All(name => Property(project, name) == "2.0.0.0") &&
                Property(profile, "ApplicationVersion") == "2.0.0.0" &&
                Property(profile, "ApplicationRevision") == "0");
            const string install = "http://patternlabforproteomics.org/familyquant2/";
            Check("2.0 installs and updates from its separate address",
                Property(profile, "InstallUrl") == install && Property(profile, "UpdateUrl") == install);
            Check("the installed product has its own Start menu identity",
                Property(profile, "ProductName") == "FamilyQuant 2.0");
            Check("automatic foreground updates stay enabled",
                string.Equals(Property(profile, "UpdateEnabled"), "true", StringComparison.OrdinalIgnoreCase) &&
                Property(profile, "UpdateMode") == "Foreground");
        }

        /// <summary>
        /// Ana (2026-10-25): long tooltips ran off the right edge, one line per paragraph, and could not
        /// be read. The app's ToolTip style caps the width and draws text tooltips wrapped. Builds a
        /// ToolTip the way WPF does for a string ToolTip property and lays it out.
        /// </summary>
        private static void TooltipWrapTests()
        {
            var style = System.Windows.Application.Current.TryFindResource(typeof(ToolTip)) as Style;
            Check("the application defines a ToolTip style", style != null);
            if (style == null) return;

            string longText = string.Join(" ", Enumerable.Repeat("a long tooltip sentence", 60)) +
                              "\n\nand a second paragraph";
            var tip = new ToolTip { Style = style, Content = longText };
            tip.ApplyTemplate();
            tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tip.Arrange(new Rect(tip.DesiredSize));
            tip.UpdateLayout();

            var text = WalkTree(tip).OfType<TextBlock>().FirstOrDefault(t => t.Text == longText);
            Check("a text tooltip is drawn by a wrapping TextBlock",
                text != null && text.TextWrapping == TextWrapping.Wrap,
                text == null ? "no TextBlock carries the text" : text.TextWrapping.ToString());
            Check("and the tooltip is no wider than its cap, however long the text",
                tip.MaxWidth <= 600 && tip.DesiredSize.Width <= tip.MaxWidth + 0.5,
                tip.DesiredSize.Width.ToString("F0", CultureInfo.InvariantCulture) + " of " +
                tip.MaxWidth.ToString("F0", CultureInfo.InvariantCulture));
            Check("so the long text takes several lines instead of one",
                text != null && text.ActualHeight > 3 * text.FontSize * text.FontFamily.LineSpacing,
                text == null ? "-" : text.ActualHeight.ToString("F0", CultureInfo.InvariantCulture));

            // A tooltip that holds its own element is not forced through the text template.
            var panel = new StackPanel();
            var custom = new ToolTip { Style = style, Content = panel };
            custom.ApplyTemplate();
            custom.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check("a tooltip holding its own element shows that element",
                WalkTree(custom).Contains(panel));
        }

        /// <summary>
        /// The reported case (Ana, 2026-10-25): Compute started from the Home card finished, and Save
        /// project was disabled there; the only way back to the curation screen reopened the file, which
        /// threw the computed quant away, and Analytics dropped the project outright. The open project now
        /// stays saveable on Home and Analytics, and the Curate card returns to it as it was left.
        /// </summary>
        private static void OpenProjectNavigationTests(string workDir)
        {
            string fcfPath = Path.Combine(workDir, "openproject.fcf");
            new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|A|X", Description = "Toxin", USearchID = 1, UserID = 1 },
                }
            }.Save(fcfPath);

            MainWindow main;
            ClusterGenerationControl project;
            try
            {
                main = new MainWindow();
                project = new ClusterGenerationControl(fcfPath);
            }
            catch (Exception ex)
            {
                Check("the main window and a project build for the navigation check", false, ex.ToString());
                return;
            }

            void Set(string field, object? value) => typeof(MainWindow)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, value);
            object? Get(string field) => typeof(MainWindow)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main);
            void Call(string method) => typeof(MainWindow)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance, null,
                    method.EndsWith("_Click", StringComparison.Ordinal)
                        ? new[] { typeof(object), typeof(RoutedEventArgs) } : Type.EmptyTypes, null)!
                .Invoke(main, method.EndsWith("_Click", StringComparison.Ordinal) ? new object?[] { null, null } : null);

            var app = UiField<Grid>(main, "GridApp")!;
            var saveProject = UiField<MenuItem>(main, "MenuItemSaveProject")!;
            var saveFcf = UiField<MenuItem>(main, "MenuItemSaveFCF")!;
            var compute = UiField<MenuItem>(main, "MenuItemComputeXic")!;

            Call("ShowHome");
            Check("with no project open, the Home leaves both Save commands off",
                !saveProject.IsEnabled && !saveFcf.IsEnabled);

            // An open project with a quantitation computed in this session, as Compute leaves them.
            var computed = new List<QuantRow> { new QuantRow { Side = "identified", Run = "run_01", Sequence = "PEPTIDEK" } };
            Set("cgc", project);
            Set("lastFcfPath", fcfPath);
            Set("loadedCuration", project.fcf);
            Set("loadedQuant", computed);

            Call("ShowHome");
            var homeCompute = UiField<Button>((HomeControl)app.Children[0], "ButtonHomeCompute")!;
            Check("on the Home, an open project can be saved, project and Family Cluster File alike",
                saveProject.IsEnabled && saveFcf.IsEnabled && compute.IsEnabled && homeCompute.IsEnabled);

            Call("ShowCurationOrOpen");
            Check("Curate returns to the SAME open project instead of reopening the file",
                app.Children.Count == 1 && ReferenceEquals(app.Children[0], project) && ReferenceEquals(Get("cgc"), project));
            Check("and the quantitation computed in the session is still held, and saveable",
                ReferenceEquals(Get("loadedQuant"), computed) && saveProject.IsEnabled);

            Set("loadedQuant", new List<QuantRow>());
            Call("MenuItemAnalytics_Click");
            Check("Analytics keeps the open project, so Save project stays live there and Compute does not",
                app.Children.Count == 1 && app.Children[0] is AnalyticsControl
                && ReferenceEquals(Get("cgc"), project) && saveProject.IsEnabled && !compute.IsEnabled);

            Call("ShowHome");
            var homeAfter = UiField<Button>((HomeControl)app.Children[0], "ButtonHomeCompute")!;
            Check("back on the Home after Analytics, the project is still open and its cards are lit",
                homeAfter.IsEnabled && saveProject.IsEnabled);

            Set("loadedQuant", computed);
            Call("MenuItemDeNovo_Click");
            Check("the de novo review keeps the open project and its computed quant, and Save stays live",
                app.Children[0] is DeNovoControl && ReferenceEquals(Get("cgc"), project)
                && ReferenceEquals(Get("loadedQuant"), computed) && saveProject.IsEnabled && !compute.IsEnabled);
        }
        private static void AppearanceDirectionTests()
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var file = new FamilyClusterFile
            {
                MyExtendedFastaItems = Enumerable.Range(1, 100).Select(i => new FastaItemExtended
                {
                    SequenceIdentifier = "appearance_" + i, Description = "Example test protein " + i,
                    Sequence = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFR",
                    UserID = i % 4, USearchID = i % 4, UserAnnotation = i % 2 == 0 ? "a note" : ""
                }).ToList(),
                LockedUserIds = new HashSet<int> { 1 }
            };
            string Snapshot() => string.Join("|", file.MyExtendedFastaItems.OrderBy(i => i.SequenceIdentifier, StringComparer.Ordinal).Select(i =>
                i.SequenceIdentifier + ":" + i.UserID + ":" + i.USearchID + ":" + i.UserAnnotation))
                + string.Join(",", file.LockedUserIds.OrderBy(i => i));
            string before = Snapshot();
            var main = new MainWindow
            {
                Width = 1024, Height = 768, Left = -30000, Top = -30000,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize
            };
            var control = new ClusterGenerationControl(file);
            var host = (Grid)typeof(MainWindow).GetField("GridApp", any)!.GetValue(main)!;
            host.Children.Clear();
            host.Children.Add(control);
            main.Show();
            try
            {
                Check("File is the first menu and both appearance choices remain reachable",
                    ((MenuItem)AppearanceWalk(main).OfType<Menu>().First().Items[0]).Header as string == "File"
                    && main.FindName("MenuThemeLight") is MenuItem && main.FindName("MenuThemeDark") is MenuItem, null);
                foreach (AppTheme theme in new[] { AppTheme.Dark, AppTheme.Light })
                {
                    string handler = theme == AppTheme.Dark ? "MenuThemeDark_Click" : "MenuThemeLight_Click";
                    typeof(MainWindow).GetMethod(handler, any)!.Invoke(main, new object[] { main, new RoutedEventArgs() });
                    AppearancePump();
                    main.UpdateLayout();
                    var input = (TextBox)control.FindName("TextBoxSearch");
                    var token = (SolidColorBrush)Application.Current.FindResource("InputBrush");
                    var numeric = (DependencyObject)control.FindName("DecimalUpDownWordReCluster");
                    TextBox numericText = AppearanceWalk(numeric).OfType<TextBox>().First();
                    Check(theme + " inputs and the numeric editor use the same surface family",
                        (input.Background as SolidColorBrush)?.Color == token.Color
                        && (numericText.Background as SolidColorBrush)?.Color == token.Color, null);
                    Check(theme + " changes appearance without replacing or mutating the curation",
                        ReferenceEquals(control.fcf, file) && ReferenceEquals(host.Children[0], control)
                        && Snapshot() == before, null);
                    var light = (MenuItem)main.FindName("MenuThemeLight");
                    var dark = (MenuItem)main.FindName("MenuThemeDark");
                    Check(theme + " is the sole checked appearance choice",
                        light.IsChecked == (theme == AppTheme.Light) && dark.IsChecked == (theme == AppTheme.Dark), null);
                }
                var table = (DataGrid)control.FindName("DataGridResultGrid");
                Check("appearance keeps curation row and column virtualization enabled",
                    table.EnableRowVirtualization && table.EnableColumnVirtualization
                    && VirtualizingPanel.GetVirtualizationMode(table) == VirtualizationMode.Recycling, null);
                string help = HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
                Check("Help retains the explanation moved from the curation panel",
                    help.Contains("Shared peptides applies to both columns", StringComparison.Ordinal)
                    && help.Contains("Full XIC share of to the Full XIC column", StringComparison.Ordinal), null);
            }
            finally { main.Close(); ThemeManager.Apply(AppTheme.Light); }
        }

        private static void AppearancePump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static IEnumerable<DependencyObject> AppearanceWalk(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in AppearanceWalk(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
