using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DeckScreens2120Commands() =>
    [
        new("--deck-screens-2120", 451, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--deck-screens-2120", (a, _) => DeckScreens2120Command(a)),
    ];

    private delegate IntPtr DeckHookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int hook, DeckHookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out ProofWindowRect value, int size);

    /// <summary>AQ6: the 2.1.20 deck's screens on copies. The computed project gives the File menu, the message after
    /// Save families as (.fcf), the Results bars and the TIC panel; the exported report gives the HTML screens in headless
    /// Edge. The family ID names the family that must sit in the contaminants row instead of the ranking.</summary>
    private static int DeckScreens2120Command(string[] args)
    {
        if (args.Length != 5 || !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int contaminantFamily))
        {
            Console.WriteLine("Use --deck-screens-2120 <computed.fqproj> <report.html> <family ID expected among contaminants> <new-output-directory>.");
            return 1;
        }
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output) || File.Exists(output)) { Console.WriteLine("Use a fresh screen output directory."); return 1; }
        Directory.CreateDirectory(output);
        string work = Path.Combine(output, "work"); Directory.CreateDirectory(work);
        var inputs = args.Skip(1).Take(2).ToArray();
        var hashes = inputs.Select(FileFingerprint.Sha256).ToArray();
        var copies = inputs.Select((input, index) => Path.Combine(work, index.ToString(CultureInfo.InvariantCulture) + "-" + Path.GetFileName(input))).ToArray();
        for (int index = 0; index < inputs.Length; index++) File.Copy(inputs[index], copies[index]);
        var screens = new List<object>();
        var oldTheme = Themes.ThemeManager.Current; var oldMessages = ShellMessages.Presenter; var oldFiles = ShellDialogs.FilePresenter;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride; var oldCulture = CultureInfo.CurrentCulture;
        MainWindow? main = null; BitmapSource? message = null; bool saved = false;
        void Print(string line) { Console.WriteLine(line); File.AppendAllText(Path.Combine(output, "screens.log"), line + Environment.NewLine); }
        // The window drawn at 1366 x 820 as ScreenHarness.Save does, with what floats over it (a menu, a message) on top.
        void Save(string name, string shows, Action<FrameworkElement, DrawingContext>? over = null)
        {
            PumpDispatcher(); main!.UpdateLayout(); PumpDispatcher();
            var content = (FrameworkElement)main.Content;
            content.Measure(new Size(1366, 820)); content.Arrange(new Rect(0, 0, 1366, 820)); content.UpdateLayout();
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(main.Background, null, new Rect(0, 0, 1366, 820));
                dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect(0, 0, 1366, 820));
                over?.Invoke(content, dc);
            }
            var bitmap = new RenderTargetBitmap(1366, 820, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, name))) encoder.Save(stream);
            screens.Add(new { File = name, Shows = shows, Width = 1366, Height = 820 });
            Print("SCREEN " + name + ": " + shows);
        }
        try
        {
            EnsureApplication(); Themes.ThemeManager.Apply(Themes.AppTheme.Light);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            ShellMessages.Presenter = (owner, shown) =>
            {
                Print("MESSAGE " + shown.Title + ": " + shown.Text.Replace("\n", " ", StringComparison.Ordinal));
                if (shown.Title == "Saved" && owner != null) { message = DeckNativeMessage(owner, shown); saved = true; }
                return MessageBoxResult.OK;
            };
            ShellDialogs.FilePresenter = (_, id, dialog) =>
            {
                if (id != "families.export") throw new InvalidOperationException("Unexpected file dialog " + id);
                dialog.FileName = Path.Combine(output, "families.fcf"); return true;
            };
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            main = OffscreenProofWindow(new MainWindow { Width = 1366, Height = 820 }); main.Show(); PumpDispatcher();
            Themes.ThemeManager.Apply(Themes.AppTheme.Light);
            Task screensTask = AppOperationHost.OnUi(main, async () =>
            {
                if (!await main.OpenProjectSafelyAsync(copies[0])) throw new InvalidOperationException("The computed project could not be opened.");
                var host = (Grid)main.FindName("GridApp");
                await host.Children.OfType<ClusterGenerationControl>().Single().FullXicReady;
                while (main.Operations.IsBusy) await Task.Delay(200);

                var menu = (MenuItem)main.FindName("MenuFile"); menu.IsSubmenuOpen = true; PumpDispatcher();
                var popup = (FrameworkElement)((Popup)menu.Template.FindName("PART_Popup", menu)).Child;
                var shownItems = new[] { "MenuItemSaveProject", "MenuItemSaveAs", "MenuItemSaveFamiliesAs" }.Select(name => (MenuItem)main.FindName(name)).ToArray();
                Print("CHECK 01 " + string.Join("; ", shownItems.Select(item => item.Header + " visible " + item.IsVisible + ", enabled " + item.IsEnabled)));
                Save("01-file-menu.png", "File menu open on the computed project with Save, Save as and Save families as (.fcf)", (content, dc) =>
                    dc.DrawRectangle(new VisualBrush(popup) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null,
                        new Rect(menu.TranslatePoint(new Point(0, menu.ActualHeight), content), new Size(popup.ActualWidth, popup.ActualHeight))));
                menu.IsSubmenuOpen = false; PumpDispatcher();

                shownItems[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var waited = Stopwatch.StartNew();
                while (!saved)
                {
                    if (waited.Elapsed > TimeSpan.FromMinutes(5)) throw new InvalidOperationException("Save families as (.fcf) showed no completion message.");
                    await Task.Delay(200);
                }
                var picture = message ?? throw new InvalidOperationException("The completion message could not be pictured.");
                var families = FamilyClusterFile.Load(Path.Combine(output, "families.fcf"));
                Print("CHECK 02 families file proteins " + families.MyExtendedFastaItems.Count.ToString(CultureInfo.InvariantCulture)
                    + ", families " + families.MyExtendedFastaItems.Select(protein => protein.UserID).Distinct().Count().ToString(CultureInfo.InvariantCulture)
                    + ", no quantitation " + families.MyExtendedFastaItems.All(protein => protein.XICUser == 0) + "; project still modified " + main.DocumentState.IsModified);
                var dpi = VisualTreeHelper.GetDpi(main);
                Save("02-save-families-done.png", "The completion message after File > Save families as (.fcf)", (_, dc) =>
                {
                    double width = picture.PixelWidth / dpi.DpiScaleX, height = picture.PixelHeight / dpi.DpiScaleY;
                    dc.DrawImage(picture, new Rect((1366 - width) / 2, (820 - height) / 2, width, height));
                });

                AnalyticsControl? analytics = null;
                waited.Restart();
                while ((analytics = host.Children.OfType<AnalyticsControl>().FirstOrDefault(control => control.IsVisible)) == null
                    || !WalkTree(analytics).OfType<FamilyContributionChart>().Any())
                {
                    if (waited.Elapsed > TimeSpan.FromMinutes(8)) throw new InvalidOperationException("Results did not show the family chart: " + main.StatusModel.Text);
                    if (!main.Operations.IsBusy && analytics == null) main.NavigationModel.RequestNavigate(ShellDestination.Results);
                    await Task.Delay(500);
                }
                var model = (AnalyticsViewModel)analytics.DataContext;
                var choice = (ComboBox)analytics.FindName("ComboBoxFamilyTopN");
                var scroll = (ScrollViewer)analytics.FindName("ScrollAnalytics");
                void Top(FrameworkElement element)
                {
                    analytics.UpdateLayout(); PumpDispatcher();
                    double top = element.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + top); analytics.UpdateLayout(); PumpDispatcher();
                }
                choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == "15");
                var options = (FrameworkElement)analytics.FindName("FamilyChartOptions"); Top(options);
                var plot = WalkTree(analytics).OfType<FamilyContributionChart>().First();
                if (plot.Parent is Border frame) frame.Height = Math.Max(plot.MinHeight, scroll.ViewportHeight - 65);
                Top(options);
                double measured = FamilyContributionPlot.MeasuredTotal(model.Families);
                var contaminants = plot.Bars.SingleOrDefault(bar => bar.Kind == FamilyContributionBarKind.Contaminants);
                Print("CHECK 03 bars " + plot.Bars.Count(bar => bar.Kind == FamilyContributionBarKind.Family).ToString(CultureInfo.InvariantCulture) + " families; contaminants row "
                    + (contaminants == null ? "absent" : (100 * contaminants.Total / measured).ToString("F2", CultureInfo.InvariantCulture) + "% of PSM and de novo, "
                        + contaminants.FamilyCount.ToString(CultureInfo.InvariantCulture) + " families")
                    + "; family " + contaminantFamily.ToString(CultureInfo.InvariantCulture) + " ranked " + plot.Bars.Any(bar => bar.Kind == FamilyContributionBarKind.Family && bar.FamilyUserId == contaminantFamily)
                    + ", in the contaminants row " + (contaminants != null && Regex.IsMatch(contaminants.Description, @"(^|[ ;])F" + contaminantFamily.ToString(CultureInfo.InvariantCulture) + @"\b")));
                Save("03-results-bars.png", "Results: Family chart, Top 15 families, with the contaminants row");

                var cascade = (FrameworkElement)analytics.FindName("BorderCascade"); Top(cascade);
                foreach (var line in model.Cascade) Print("CHECK 04 " + line.Label + ": " + line.Value);
                Save("04-results-tic-panel.png", "Results: From TIC to families with every line");
            });
            while (!screensTask.IsCompleted) { PumpDispatcher(); Thread.Sleep(5); }
            screensTask.GetAwaiter().GetResult();

            string? edge = new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
                .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Microsoft", "Edge", "Application", "msedge.exe")).FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException("The report screens need installed Microsoft Edge.");
            foreach (var (name, shows, setup, anchor) in new[]
            {
                ("05-html-ring-tic.png", "Report: the family ring under Normalize to TIC, its legend and caption splitting the remainder in two",
                    "document.getElementById('tab-families').click();document.getElementById('basis-tic').click();", "composition-title"),
                ("06-html-normalize-names.png", "Report: the Normalize to buttons and the Signal recovery cards with the five base names",
                    "document.getElementById('tab-recovery').click();", "panel-recovery"),
                // The recovery chart's card has no id of its own; the page marks it so the view can start there.
                ("07-html-recovery.png", "Report: the recovery chart naming Isotope envelopes (measured and identified) beside the isotope-series diagnostic",
                    "document.getElementById('tab-recovery').click();[...document.querySelectorAll('#recovery-figures > section')]"
                    + ".find(card=>card.querySelector('h3').textContent.includes('M+0')).id='deck-recovery-chart';", "deck-recovery-chart"),
                // The view starts five bars above the contaminants row, so the row and the bars around it fit.
                ("09-html-family-bars.png", "Report: the family bars with the contaminants row",
                    "document.getElementById('tab-families').click();const rows=[...document.querySelectorAll('#family-contribution-chart .contribution-row')];"
                    + "const at=rows.findIndex(row=>row.querySelector('.contribution-label').textContent.trim().startsWith('contaminants'));"
                    + "if(at<0)throw new Error('The report has no contaminants row.');rows[Math.max(0,at-5)].id='deck-contaminants';", "deck-contaminants"),
            })
            {
                string profile = Path.Combine(work, "browser-" + Guid.NewGuid().ToString("N"));
                var start = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
                    "--disable-component-update", "--force-device-scale-factor=1", "--hide-scrollbars", "--window-size=1366,900",
                    "--user-data-dir=" + profile, "--remote-debugging-port=0", new Uri(copies[1]).AbsoluteUri })
                    start.ArgumentList.Add(argument);
                using var browser = Process.Start(start) ?? throw new IOException("Could not start the isolated screenshot browser.");
                var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
                try
                {
                    if (!CaptureExactViewport(profile, Path.Combine(output, name), 1366, "document.documentElement.dataset.theme='light';" + setup
                        + "document.documentElement.style.scrollBehavior='auto';", anchor).GetAwaiter().GetResult())
                        throw new InvalidOperationException("The report screen " + name + " was not captured at 1366 wide.");
                }
                finally
                {
                    if (!browser.HasExited) browser.Kill(entireProcessTree: true);
                    browser.WaitForExit();
                    File.WriteAllText(Path.Combine(work, name + "-browser.txt"), stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
                }
                screens.Add(new { File = name, Shows = shows, Width = 1366, Height = 900 });
                Print("SCREEN " + name + ": " + shows);
            }

            if (screens.Count != 8) throw new InvalidOperationException("Only " + screens.Count.ToString(CultureInfo.InvariantCulture) + " of the eight screens were made.");
            if (inputs.Select(FileFingerprint.Sha256).Where((hash, index) => hash != hashes[index]).Any()) throw new InvalidDataException("An input changed.");
            File.WriteAllText(Path.Combine(output, "screens.json"), JsonSerializer.Serialize(new
            { Theme = "Light", Version = AppVersionInfo.Current(), InputsUnchanged = true, Screens = screens },
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { Print("RESULT " + error.Message); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); return 1; }
        finally
        {
            main?.Close(); PumpDispatcher(); Themes.ThemeManager.Apply(oldTheme); CultureInfo.CurrentCulture = oldCulture;
            ShellMessages.Presenter = oldMessages; ShellDialogs.FilePresenter = oldFiles; MainWindow.UnsavedProjectChoiceOverride = oldChoice;
        }
    }

    /// <summary>The real message box, moved off screen as it activates, as the proof windows are, and pictured before it is closed.</summary>
    private static BitmapSource? DeckNativeMessage(Window owner, ShellMessage shown)
    {
        IntPtr box = IntPtr.Zero, hook = IntPtr.Zero; BitmapSource? picture = null;
        DeckHookProc activate = (code, wParam, lParam) =>
        {
            // HCBT_ACTIVATE: SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE.
            if (code == 5 && box == IntPtr.Zero) { box = wParam; SetWindowPos(box, IntPtr.Zero, -30000, -30000, 0, 0, 0x0001 | 0x0004 | 0x0010); }
            return CallNextHookEx(hook, code, wParam, lParam);
        };
        hook = SetWindowsHookEx(5, activate, IntPtr.Zero, GetCurrentThreadId());
        // A second for the box to finish painting; earlier pictures miss its text.
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000), DispatcherPriority.Normal, (sender, _) =>
        {
            if (box == IntPtr.Zero) return;
            ((DispatcherTimer)sender!).Stop();
            GetWindowRect(box, out var rectangle);
            int width = rectangle.Right - rectangle.Left, height = rectangle.Bottom - rectangle.Top;
            IntPtr screen = GetDC(IntPtr.Zero), dc = CreateCompatibleDC(screen), bitmap = CreateCompatibleBitmap(screen, width, height), old = SelectObject(dc, bitmap);
            // A plain print first makes the box paint its frame; PW_RENDERFULLCONTENT then draws it as composed, off screen
            // too, without unpainted black edges.
            // DWMWA_EXTENDED_FRAME_BOUNDS: the window rectangle also holds invisible borders, which print black.
            if (PrintWindow(box, dc, 0) && PrintWindow(box, dc, 2) && DwmGetWindowAttribute(box, 9, out var frame, Marshal.SizeOf<ProofWindowRect>()) == 0)
                picture = new CroppedBitmap(Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()),
                    new Int32Rect(frame.Left - rectangle.Left, frame.Top - rectangle.Top, frame.Right - frame.Left, frame.Bottom - frame.Top));
            SelectObject(dc, old); DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(IntPtr.Zero, screen);
            PostMessage(box, 0x0010, IntPtr.Zero, IntPtr.Zero);
        }, owner.Dispatcher);
        try { MessageBox.Show(owner, shown.Text, shown.Title, shown.Buttons, shown.Image, shown.DefaultResult); }
        finally { timer.Stop(); UnhookWindowsHookEx(hook); GC.KeepAlive(activate); }
        return picture;
    }
}
