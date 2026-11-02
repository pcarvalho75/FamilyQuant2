using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// Captures the real application resources and controls, using the off-screen VisualBrush probe.
    /// No Run(), file dialogs, computation, data exports or writes to the supplied projects occur.
    /// Omitted input paths select a labelled synthetic fixture, never a scientific deliverable.
    /// </summary>
    internal static class ScreenHarness
    {
        internal const string Argument = "--screens";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        internal static readonly string[] Screens = {
            "01-home", "02-build", "03-curation", "04-curation-browser", "05-denovo",
            "06-denovo-advanced", "07-analytics", "08-compute", "09-super-cluster-preview",
            "10-lock-manager", "11-spectrum-table", "12-identity-scan", "13-text-report", "14-help"
        };
        private static readonly (int Width, int Height)[] Sizes = { (1366, 820), (1024, 768) };
        private static string[] ThemeNames(string[] args) => args.Contains("--screen-system", StringComparer.OrdinalIgnoreCase)
            ? new[] { "dark", "light", "system" } : new[] { "dark", "light" };

        internal static bool IsRequested(string[] args) =>
            args.Any(a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase));

        private static string? Option(string[] args, string name)
        {
            int index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return null;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException(name + " requires a value.");
            return args[index + 1];
        }

        internal static int Run(string[] args)
        {
            App? app = null;
            try
            {
                string output = Path.GetFullPath(Option(args, Argument)!);
                string? fcfPath = Option(args, "--screen-fcf");
                string? projectPath = Option(args, "--screen-project");
                bool shellStatus = args.Contains("--screen-shell-status", StringComparer.OrdinalIgnoreCase);
                var inputHashes = new Dictionary<string, string>();
                foreach (string path in new[] { fcfPath, projectPath }.OfType<string>())
                {
                    string fullPath = Path.GetFullPath(path);
                    // An output directory must never contain one of the read-only input files.
                    if (fullPath.StartsWith(output.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("Keep screen output separate from input files.");
                    inputHashes[fullPath] = FileHash(fullPath);
                }
                Directory.CreateDirectory(output);
                string manifestPath = Path.Combine(output, "screens.json");
                // A failed rerun must not leave a prior success marker looking current.
                if (File.Exists(manifestPath)) File.Delete(manifestPath);
                var source = fcfPath == null ? Fixture() : FamilyClusterFile.Load(fcfPath);
                var project = projectPath == null
                    ? (fcf: Fixture(), quant: new List<QuantRow>(), manifest: new QuantManifest())
                    : ProjectStore.ReadFqproj(projectPath);
                Console.WriteLine("Screens: curation " + (fcfPath ?? "synthetic fixture") +
                    "; analytics " + (projectPath ?? "empty synthetic fixture"));

                app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                var captures = new List<CaptureInfo>();
                string[] themeNames = ThemeNames(args);
                foreach (string theme in themeNames)
                {
                    // Exercise the actual Fluent base without removing or replacing product styles.
                    // V2 will remove the legacy templates from the application itself.
                    Themes.ThemeManager.Apply(theme == "system" ? Themes.AppTheme.System
                        : theme == "dark" ? Themes.AppTheme.Dark : Themes.AppTheme.Light);
                    CaptureTheme(theme, source, project.fcf, project.quant, project.manifest, output, captures,
                        fcfPath ?? "synthetic fixture.fcf", projectPath ?? "synthetic fixture.fqproj", shellStatus);
                }
                foreach (var input in inputHashes)
                    if (FileHash(input.Key) != input.Value)
                        throw new InvalidOperationException("An input changed during capture: " + input.Key);
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
                {
                    CurationSource = fcfPath == null ? "synthetic fixture" : Path.GetFullPath(fcfPath),
                    AnalyticsSource = projectPath == null ? "empty synthetic fixture" : Path.GetFullPath(projectPath),
                    InputSha256 = inputHashes,
                    InputsUnchanged = true,
                    UseShellStatusBar = shellStatus,
                    OperatingSystem = Environment.OSVersion.VersionString,
                    Runtime = Environment.Version.ToString(),
                    Capture = "Client area only, 96 DPI, off-screen real WPF windows; no native title bar.",
                    Captures = captures
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Screens: " + captures.Count + " PNGs; input hashes unchanged; " + output);
                return captures.Count == Screens.Length * Sizes.Length * themeNames.Length ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Screen capture failed: " + ex);
                return 1;
            }
            finally
            {
                if (app != null)
                {
                    foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
                    app.Shutdown();
                }
            }
        }

        private static void CaptureTheme(string theme, FamilyClusterFile source, FamilyClusterFile projectFile,
            List<QuantRow> quant, QuantManifest manifest, string output, List<CaptureInfo> captures,
            string curationPath, string projectPath, bool shellStatus)
        {
            var main = new MainWindow();
            ShowOffScreen(main);
            try
            {
                void Capture(string name, Window window)
                {
                    foreach (var size in Sizes)
                    {
                        string relative = Path.Combine(theme, size.Width + "x" + size.Height, name + ".png");
                        string path = Path.Combine(output, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        window.Width = size.Width;
                        window.Height = size.Height;
                        Pump();
                        if (!string.Equals(Themes.ThemeManager.Current.ToString(), theme, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("The selected theme changed during capture: " + theme);
                        Save(window, path, size.Width, size.Height);
                        captures.Add(new CaptureInfo(relative.Replace('\\', '/'), theme, size.Width, size.Height, name,
                            Themes.ThemeManager.Effective.ToString()));
                        Console.WriteLine("  " + relative);
                    }
                }

                Capture(Screens[0], main);
                Call(main, "MenuRunUSearch_Click", main, new RoutedEventArgs());
                Capture(Screens[1], main);
                Call(main, "MenuItemDeNovo_Click", main, new RoutedEventArgs());
                Capture(Screens[4], main);
                var appGrid = Field<Grid>(main, "GridApp");
                var deNovo = appGrid.Children.OfType<DeNovoControl>().Single();
                var advanced = Walk(deNovo).OfType<Expander>().Single();
                advanced.IsExpanded = true;
                // Capture the advanced content at the bottom, rather than the same first two panels.
                Pump();
                Walk(deNovo).OfType<ScrollViewer>().First().ScrollToBottom();
                Capture(Screens[5], main);

                var curation = new ClusterGenerationControl(source) { UseShellStatusBar = shellStatus };
                Set(main, "cgc", curation);
                Set(main, "loadedCuration", source);
                Set(main, "lastFcfPath", curationPath);
                foreach (string command in new[] { "MenuItemSaveFCF", "MenuItemSaveProject",
                    "MenuItemComputeXic", "MenuItemReanotateSEPro" })
                    Field<MenuItem>(main, command).IsEnabled = true;
                Present(main, curation, "cgc");
                Capture(Screens[2], main);
                // The tab is temporary. C1 removes it; capture still works once the table is always visible.
                var tabs = Walk(curation).OfType<TabControl>().FirstOrDefault();
                if (tabs != null) tabs.SelectedIndex = 1;
                Capture(Screens[3], main);

                var analytics = new AnalyticsControl(quant, manifest, projectFile);
                Set(main, "loadedManifest", manifest);
                Set(main, "loadedQuant", quant);
                Set(main, "loadedCuration", projectFile);
                Set(main, "lastFcfPath", projectPath);
                Present(main, analytics, "analytics");
                Capture(Screens[6], main);

                void Secondary(string name, Window window)
                {
                    ShowOffScreen(window);
                    try { Capture(name, window); }
                    finally { window.Close(); }
                }
                Secondary(Screens[7], new ComputeXicWindow(AppVersionInfo.Current(), manifest, projectFile));
                var preview = new SuperClusterPreview(source.MyExtendedFastaItems, null,
                    PreviewWord(source), "", KeywordMatch.Any, null, source.LockedUserIds);
                Secondary(Screens[8], new SuperClusterPreviewWindow(preview));
                Secondary(Screens[9], new LockManagerWindow(projectFile));
                Secondary(Screens[10], SpectrumComparisonWindow.Create(manifest.SpectrumComparisonRows));
                List<IdentityScanRow> sweep = manifest.PresentationIdentitySweep ?? new List<IdentityScanRow>();
                string report = sweep.Count == 0 ? "No stored identity scan in this project."
                    : "Identity\tFamilies\tMixed\tFDR\n" + string.Join("\n", sweep.Select(r =>
                        string.Join("\t", r.Identity, r.ReportedTargetGroups, r.MixedGroups,
                            r.Score.ToString("G", CultureInfo.InvariantCulture))));
                Secondary(Screens[11], IdentityScanWindow.Create("Identity scan", sweep, report));
                Secondary(Screens[12], TextReportWindow.Create("Identity scan report", report));
                Secondary(Screens[13], TextReportWindow.Create("FamilyQuant help",
                    HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique), true));
            }
            finally { main.Close(); }
        }

        private static string PreviewWord(FamilyClusterFile source) => source.MyExtendedFastaItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Description))
            .Select(i => i.Description.Split(" OS=", StringSplitOptions.None)[0].Replace("\"", "").Trim())
            .Where(name => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => "\"" + g.Key + "\"")
            .FirstOrDefault() ?? "example";

        private static void Present(MainWindow main, UserControl control, string active)
        {
            foreach (string field in new[] { "home", "usearch", "dnc", "analytics" }) Set(main, field, null);
            Set(main, active, control);
            var grid = Field<Grid>(main, "GridApp");
            grid.Children.Clear();
            grid.Children.Add(control);
            Call(main, "UpdateGuide");
            Pump();
        }

        private static void ShowOffScreen(Window window)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            // Captures contain client content only, so native frame metrics must not shrink it.
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.Left = -30000;
            window.Top = -30000;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
            Pump();
        }

        internal static void Save(Window window, string path, int width, int height)
        {
            var content = (FrameworkElement)window.Content;
            // Explicit client viewport makes the requested dimensions independent of native frame/DPI.
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, height));
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void Pump()
        {
            for (int i = 0; i < 4; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }
        }

        private static IEnumerable<DependencyObject> Walk(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
        }

        private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Any)!.GetValue(instance)!;
        private static void Set(object instance, string name, object? value) => instance.GetType().GetField(name, Any)!.SetValue(instance, value);
        private static void Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Any)!.Invoke(instance, args);
        private static string FileHash(string path) => TestFileHash.Upper(path);
        private sealed record CaptureInfo(string File, string Theme, int PixelWidth, int PixelHeight, string Screen,
            string EffectiveTheme);

        private static FamilyClusterFile Fixture() => new FamilyClusterFile
        {
            ClusteringIdentity = 85,
            MyExtendedFastaItems = Enumerable.Range(1, 100).Select(i => new FastaItemExtended
            {
                SequenceIdentifier = "fixture_" + i,
                Description = "Example protein " + (i % 5) + " OS=Synthetic fixture",
                Sequence = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFR",
                UserID = i % 5, USearchID = i % 5
            }).ToList(),
            LockedUserIds = new HashSet<int> { 1 }
        };

        internal static void Verify(Action<string, bool, string?> check, string workDir)
        {
            check("screen lane is opt-in and accepts a flag after positional arguments",
                !IsRequested(Array.Empty<string>()) && IsRequested(new[] { ".", ".", Argument, "out" }), null);
            check("screen matrix contains 14 unique views in both themes and viewport sizes",
                Screens.Length == 14 && Screens.Distinct().Count() == 14 && Sizes.Length == 2
                && ThemeNames(Array.Empty<string>()).Length == 2, null);
            check("System capture is opt-in and adds the Windows appearance without dropping either direction",
                ThemeNames(new[] { "--screen-system" }).SequenceEqual(new[] { "dark", "light", "system" }), null);
            bool refused = false;
            try { Option(new[] { Argument, "--screen-fcf" }, Argument); }
            catch (ArgumentException) { refused = true; }
            check("missing output argument is refused instead of shifting input paths", refused, null);
            var fixture = Fixture();
            var preview = new SuperClusterPreview(fixture.MyExtendedFastaItems, null,
                PreviewWord(fixture), "", KeywordMatch.Any, null, fixture.LockedUserIds);
            check("capture preview selects a matchable phrase from the input", preview.Proteins.Count > 0, null);
            var content = new Grid { Background = SystemColors.WindowBrush,
                Children = { new TextBlock { Text = "Screen capture check" } } };
            var window = new Window { Width = 320, Height = 180, Content = content };
            ShowOffScreen(window);
            string path = Path.Combine(workDir, "screen-harness-check.png");
            Save(window, path, 320, 180);
            using var stream = File.OpenRead(path);
            var decoded = BitmapFrame.Create(stream);
            var corner = new byte[4];
            decoded.CopyPixels(new Int32Rect(319, 179, 1, 1), corner, 4, 0);
            check("screen PNG fills the exact requested client dimensions",
                decoded.PixelWidth == 320 && decoded.PixelHeight == 180 && corner[3] == 255
                && Math.Abs(content.ActualWidth - 320) < 0.1 && Math.Abs(content.ActualHeight - 180) < 0.1, null);
            window.Close();
        }
    }
}
