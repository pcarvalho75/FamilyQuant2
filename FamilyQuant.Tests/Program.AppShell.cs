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
        [TestCommandTable]
        private static IEnumerable<TestCommand> AppVersionCommandTable() =>
        [
            new("--test-app-version", 432, TestCommandPhase.Raw,
                args => args.Length == 1 && args[0] == "--test-app-version", (_, _) =>
                {
                    EnsureApplication();
                    AppVersionTests();
                    Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                    return failed == 0 ? 0 : 1;
                }),
        ];

        /// <summary>
        /// The title showed the csproj's assembly version (1.5.0.0) on an installed 1.5.0.2: Visual
        /// Studio's Publish increments only the publish profile's ApplicationVersion. The installed copy
        /// now reports the deployment version the ClickOnce launcher passes in ClickOnce_CurrentVersion.
        /// </summary>
        private static void AppVersionTests()
        {
            DeploymentIdentityTests();
            Check("the current application keeps its executable assembly identity",
                typeof(AppVersionInfo).Assembly.GetName().Name == "FamilyQuant2");
            Check("the resource namespace stays compatible after the project rename",
                typeof(USearchTools).Assembly.GetManifestResourceNames()
                    .Contains("FamilyQuant.MyResources.resources"));
            Check("the product's 2.x local data folder stays separate from 1.x",
                WithProductDataPath(() => ApplicationPaths.LocalDataDirectory) == Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamilyQuant2"));
            Check("the active data folder respects the process test isolation override",
                ApplicationPaths.LocalDataDirectory == (ApplicationPaths.LocalDataDirectoryOverride ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamilyQuant2")));
            Check("2.x isolates USEARCH scratch files from 1.x",
                ApplicationPaths.ScratchDirectory == Path.Combine(Path.GetTempPath(), "FamilyQuant2"));

            string? usearchOverride = Environment.GetEnvironmentVariable("FAMILYQUANT_USEARCH");
            try
            {
                Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", null);
                string bundled = (string)typeof(USearchTools)
                    .GetMethod("GetUSearchPath", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null)!;
                Check("the bundled USEARCH is extracted under the 2.x data folder",
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
                Check("the window identifies FamilyQuant with its running deployment version",
                    main.Title == "FamilyQuant 9.8.7.6", main.Title);
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
            Check("the prepared application assembly is version 2.1.12.0",
                typeof(AppVersionInfo).Assembly.GetName().Version == new Version(2, 1, 12, 0));

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

            Check("assembly and file versions match the prepared deployment version",
                new[] { "Version", "AssemblyVersion", "FileVersion" }
                    .All(name => Property(project, name) == "2.1.12.0") &&
                Property(project, "Title") == "FamilyQuant 2.1.12" &&
                Property(profile, "ApplicationVersion") == "2.1.12.0" &&
                Property(profile, "ApplicationRevision") == "0");
            const string install = "http://patternlabforproteomics.org/familyquant2/";
            Check("2.x installs and updates from its separate address",
                Property(profile, "InstallUrl") == install && Property(profile, "UpdateUrl") == install);
            Check("the installed product has its own Start menu identity",
                Property(profile, "ProductName") == "FamilyQuant 2.1.12");
            string packaging = File.ReadAllText(Path.Combine(root.FullName, "tools", "packaging", "Test-ReleaseSetup.ps1"));
            var packagingVersion = System.Text.RegularExpressions.Regex.Match(packaging,
                @"\[Version\]\$ExpectedVersion\s*=\s*\[Version\]'([^']+)'");
            Check("the package verifier default matches the prepared application version", packagingVersion.Success
                && packagingVersion.Groups[1].Value == Property(project, "Version"));
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
            string ProfileMarker() => File.Exists(ThemeSettings.DefaultPath)
                ? File.GetLastWriteTimeUtc(ThemeSettings.DefaultPath).Ticks.ToString(CultureInfo.InvariantCulture)
                    + "|" + File.ReadAllText(ThemeSettings.DefaultPath) : "missing";
            string profileBefore = ProfileMarker();
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
            ((Expander)control.FindName("WordGroupingActions")).IsExpanded = true;
            var host = (Grid)typeof(MainWindow).GetField("GridApp", any)!.GetValue(main)!;
            host.Children.Clear();
            host.Children.Add(control);
            var accentCheck = new CheckBox { IsChecked = true, Content = "Accent check" };
            host.Children.Add(accentCheck);
            main.Show();
            try
            {
                Check("File is the first menu and all three appearance choices remain reachable",
                    ((MenuItem)AppearanceWalk(main).OfType<Menu>().First().Items[0]).Header as string == "File"
                    && main.FindName("MenuThemeLight") is MenuItem && main.FindName("MenuThemeDark") is MenuItem
                    && main.FindName("MenuThemeSystem") is MenuItem, null);
                foreach (AppTheme theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.System })
                {
                    string handler = theme == AppTheme.System ? "MenuThemeSystem_Click"
                        : theme == AppTheme.Dark ? "MenuThemeDark_Click" : "MenuThemeLight_Click";
                    typeof(MainWindow).GetMethod(handler, any)!.Invoke(main, new object[] { main, new RoutedEventArgs() });
                    AppearancePump();
                    main.UpdateLayout();
#pragma warning disable WPF0001 // Verify the experimental API under the same local suppression as the app.
                    Check(theme + " uses the matching built-in Fluent ThemeMode",
                        Application.Current.ThemeMode == (theme == AppTheme.System ? ThemeMode.System
                            : theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light));
#pragma warning restore WPF0001
                    var input = (TextBox)control.FindName("TextBoxSearch");
                    var token = (SolidColorBrush)Application.Current.FindResource("InputBrush");
                    var selectedMark = AppearanceWalk(accentCheck).OfType<Border>()
                        .FirstOrDefault(b => b.Name == "ControlBorderIconPresenter");
                    var accent = (SolidColorBrush)Application.Current.FindResource("AccentBrush");
                    Check(theme + " Fluent selection uses the same accent as the primary action",
                        (selectedMark?.Background as SolidColorBrush)?.Color == accent.Color,
                        selectedMark?.Background?.ToString() ?? "missing selection mark");
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
                    var system = (MenuItem)main.FindName("MenuThemeSystem");
                    Check(theme + " is the sole checked appearance choice",
                        light.IsChecked == (theme == AppTheme.Light) && dark.IsChecked == (theme == AppTheme.Dark)
                        && system.IsChecked == (theme == AppTheme.System), null);
                }
                var table = (DataGrid)control.FindName("DataGridResultGrid");
                Check("appearance keeps curation row and column virtualization enabled",
                    table.EnableRowVirtualization && table.EnableColumnVirtualization
                    && VirtualizingPanel.GetVirtualizationMode(table) == VirtualizationMode.Recycling, null);
                string help = HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
                Check("Help retains the explanation moved from the curation panel",
                    help.Contains("Shared peptides applies to both columns", StringComparison.Ordinal)
                    && help.Contains("Share of under Full XIC changes that column immediately", StringComparison.Ordinal), null);
                Check("hosted theme switching never writes the user's appearance preference",
                    ProfileMarker() == profileBefore);
            }
            finally { main.Close(); ThemeManager.Apply(AppTheme.Dark); }
        }

        private static void ThemePreferenceTests(string workDir)
        {
            string path = Path.Combine(workDir, "appearance-settings.json");
            File.Delete(path);
            Check("a new profile starts with the selected dark direction", ThemeSettings.Load(path) == AppTheme.Dark);
            foreach (AppTheme theme in Enum.GetValues<AppTheme>())
                Check(theme + " preference survives a new settings read",
                    ThemeSettings.Save(path, theme) && ThemeSettings.Load(path) == theme);
            File.WriteAllText(path, "{ broken");
            Check("a damaged preference file falls back to Dark", ThemeSettings.Load(path) == AppTheme.Dark);
            File.WriteAllText(path, "[]");
            Check("a valid JSON value with the wrong shape falls back to Dark", ThemeSettings.Load(path) == AppTheme.Dark);
            File.WriteAllText(path, "{\"Theme\":\"future-value\"}");
            Check("an unknown preference falls back to Dark", ThemeSettings.Load(path) == AppTheme.Dark);
            Check("System resolves both Windows app appearances; explicit choices stay fixed",
                ThemeManager.Resolve(AppTheme.System, true) == AppTheme.Light
                && ThemeManager.Resolve(AppTheme.System, false) == AppTheme.Dark
                && ThemeManager.Resolve(AppTheme.Light, false) == AppTheme.Light
                && ThemeManager.Resolve(AppTheme.Dark, true) == AppTheme.Dark);
            Check("product appearance settings belong to the separate 2.0 profile",
                Path.GetFileName(Path.GetDirectoryName(WithProductDataPath(() => ThemeSettings.DefaultPath))) == ApplicationPaths.DataFolderName);
            Check("active appearance settings stay in the isolated data folder",
                Path.GetDirectoryName(ThemeSettings.DefaultPath) == ApplicationPaths.LocalDataDirectory);
        }

        // Inspect pure path getters only: no file access, dispatcher pumping or asynchronous work.
        private static string WithProductDataPath(Func<string> read)
        {
            string? isolated = ApplicationPaths.LocalDataDirectoryOverride;
            try { ApplicationPaths.LocalDataDirectoryOverride = null; return read(); }
            finally { ApplicationPaths.LocalDataDirectoryOverride = isolated; }
        }

        private static void LegacySurfaceContrastTests()
        {
            AppTheme saved = ThemeManager.Current;
            try
            {
                foreach (AppTheme theme in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    ThemeManager.Apply(theme);
                    var screen = new USearchLib();
                    screen.Measure(new Size(1280, 800));
                    screen.Arrange(new Rect(0, 0, 1280, 800));
                    AppearancePump();
                    screen.UpdateLayout();
                    var identity = AppearanceWalk(screen).OfType<Label>().First(l => l.Content as string == "Identity (%)");
                    var surface = (Brush)screen.FindResource("SurfaceBrush");
                    Check(theme + " preserves readable labels on the rebuilt Build screen",
                        ChartPalette.ContrastRatio(identity.Foreground, surface) >= 4.5);
                    var heading = AppearanceWalk(screen).OfType<TextBlock>().FirstOrDefault(c => c.Text == "Database");
                    Check(theme + " preserves readable headings on the rebuilt Build screen",
                        heading != null && ChartPalette.ContrastRatio(heading.Foreground, surface) >= 4.5,
                        "heading=" + (heading?.Foreground?.ToString() ?? "missing") + "; panel=" + surface);
                }
            }
            finally { ThemeManager.Apply(saved); }
        }

        private static void ShellStatusBridgeTests()
        {
            var file = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "shell_status", Description = "Status fixture",
                    Sequence = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFR", UserID = 7, USearchID = 7 }
            } };
            var control = new ClusterGenerationControl(file);
            var banner = (Border)control.FindName("BorderCurationStatus");
            Check("standalone curation retains its inline status by default",
                !control.UseShellStatusBar && banner.Visibility == Visibility.Visible);
            control.UseShellStatusBar = true;
            Check("the shell can hide the inline status while reading a short nonempty summary",
                banner.Visibility == Visibility.Collapsed && control.ShellStatusText.Length > 0
                && control.ShellStatusText.Length < 180);
            int changed = 0;
            control.ShellStatusChanged += (_, _) => changed++;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ClusterGenerationControl).GetProperty("DisplayedMode", flags)!.SetValue(control, NormalizationMode.IdentifiedSignal);
            var refresh = typeof(ClusterGenerationControl).GetMethod("UpdateStatusLine", flags)!;
            string before = control.ShellStatusText;
            refresh.Invoke(control, null);
            int afterChange = changed;
            refresh.Invoke(control, null);
            Check("the shell status event fires once for changed text and stays quiet for identical text",
                changed == 1 && afterChange == 1 && before != control.ShellStatusText);
            control.UseShellStatusBar = false;
            Check("returning to standalone restores the banner without changing family numbers",
                banner.Visibility == Visibility.Visible && file.MyExtendedFastaItems[0].UserID == 7
                && file.MyExtendedFastaItems[0].USearchID == 7);
        }

        private static void LogoResourceTests(string workDir)
        {
            var mark = Application.Current.TryFindResource("StartPageLogo") as DrawingImage;
            Check("StartPageLogo is a square vector ImageSource", mark != null && mark.Width == 256 && mark.Height == 256);
            if (mark == null) return;
            Color markColour = ((SolidColorBrush)Application.Current.FindResource("LogoMarkBrush")).Color;
            Color tileColour = ((SolidColorBrush)Application.Current.FindResource("LogoTileBrush")).Color;
            foreach (int size in new[] { 16, 32, 256 })
            {
                using var stream = new MemoryStream(LogoAssets.Png(mark, size, size));
                var decoded = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var pixels = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
                byte[] corner = new byte[4], middle = new byte[4];
                pixels.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
                pixels.CopyPixels(new Int32Rect(size / 2, size / 2, 1, 1), middle, 4, 0);
                double Distance(Color colour) => Math.Pow(middle[2] - colour.R, 2)
                    + Math.Pow(middle[1] - colour.G, 2) + Math.Pow(middle[0] - colour.B, 2);
                Check("the " + size + " px mark has transparent corners and a visible centre mark",
                    corner[3] == 0 && middle[3] == 255 && Distance(markColour) < Distance(tileColour));
            }
            var icon = Application.Current.FindResource("AppWindowIcon") as BitmapFrame;
            Check("AppWindowIcon retains every native ICO size",
                icon?.Decoder != null && icon.Decoder.Frames.Select(f => f.PixelWidth).OrderBy(n => n)
                    .SequenceEqual(LogoAssets.IconSizes.OrderBy(n => n)));
            var window = new Window { Width = 180, Height = 120, Left = -30000, Top = -30000,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                AppearancePump();
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                IntPtr small = ReadLogoIcon(handle, 0x007F, IntPtr.Zero, IntPtr.Zero);
                IntPtr large = ReadLogoIcon(handle, 0x007F, new IntPtr(1), IntPtr.Zero);
                Check("a real off-screen window receives both native logo icons", small != IntPtr.Zero && large != IntPtr.Zero);
                foreach (var native in new[] { ("small", small), ("large", large) })
                {
                    if (native.Item2 == IntPtr.Zero) continue;
                    var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(native.Item2,
                        Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using var output = File.Create(Path.Combine(workDir, "logo-native-" + native.Item1 + ".png"));
                    encoder.Save(output);
                }
            }
            finally { window.Close(); }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr ReadLogoIcon(IntPtr window, uint message, IntPtr parameter, IntPtr data);

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
