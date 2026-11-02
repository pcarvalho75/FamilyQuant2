using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const int BusyLogoFrameCount = 48;
    private static int BusyLogoProof(string output)
    {
        EnsureApplication();
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        var foreground = GetForegroundWindow();
        Section("Y7 shared vector logo - motion lifetime and native Cancel");
        var logoType = typeof(App).Assembly.GetType("FamilyQuant.BusyLogo");
        Check("shared busy overlay supplies a native vector logo", logoType != null);
        if (logoType == null) return 1;
        var originalTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var backdrop = Y7Backdrop();
                var window = new Window { Content = backdrop, Width = 800, Height = 560, Left = -30000, Top = -30000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = WindowStyle.None };
                try
                {
                    window.Show(); PumpDispatcher();
                    var host = AppOperationHost.For(backdrop); PumpDispatcher();
                    var overlay = Y7Visual<BusyOverlay>(window).Single();
                    var layered = (FrameworkElement)window.Content;
                    var logo = (FrameworkElement)overlay.FindName("AnimatedLogo");
                    var preference = logoType.GetProperty("AnimationPreferenceOverride", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    bool Running() => (bool)logoType.GetProperty("IsAnimating", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(logo)!;
                    bool Clocks() => (bool)logoType.GetProperty("HasLogoClocks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(logo)!;
                    preference.SetValue(logo, true); PumpDispatcher();
                    Check(theme + " idle overlay has no logo clocks", !Running() && !Clocks());
                    var lease = host.Begin("Preparing family results");
                    lease.Context.Report("prepare", "Reading the stored evidence and preparing the selected result.", 2, 5);
                    PumpDispatcher(); window.UpdateLayout();
                    Check(theme + " busy starts the logo and preserves exact progress/message", Running() && host.Percent == 40
                        && host.Message == "Reading the stored evidence and preparing the selected result.");
                    var scrim = (SolidColorBrush)((Grid)overlay.FindName("Scrim")).Background;
                    var cornerHit = layered.InputHitTest(new Point(35, 35)) as DependencyObject;
                    Check(theme + " theme scrim is translucent and intercepts input over the disabled background",
                        scrim.Color.A / 255d is >= .6 and <= .7 && cornerHit != null && !backdrop.IsEnabled
                        && (ReferenceEquals(cornerHit, overlay) || overlay.IsAncestorOf(cornerHit)));
                    var cancel = Y7Visual<Button>(overlay).Single(button => (string?)button.Content == "Cancel");
                    Check(theme + " original Cancel command stays enabled", ReferenceEquals(cancel.Command, host.CancelCommand) && cancel.IsEnabled);
                    window.Width = 520; window.Height = 320; PumpDispatcher(); window.UpdateLayout();
                    var bounds = cancel.TransformToAncestor(overlay).TransformBounds(new Rect(cancel.RenderSize));
                    Check(theme + " compact wait screen keeps Cancel in its viewport", bounds.Top >= 0 && bounds.Bottom <= overlay.ActualHeight && bounds.Right <= overlay.ActualWidth);
                    window.Width = 800; window.Height = 560; PumpDispatcher();
                    foreach (double scale in new[] { 1d, 1.25, 1.5 })
                    {
                        VisualTreeHelper.SetRootDpi(window, new DpiScale(scale, scale));
                        string label = theme + "-" + (scale * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                        Check(label + " uses the requested native WPF DPI", VisualTreeHelper.GetDpi(logo).DpiScaleX == scale);
                        for (int frame = 0; frame < (scale == 1 ? BusyLogoFrameCount : 1); frame++)
                        {
                            logoType.GetMethod("SetCaptureTime", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(logo, new object[] { TimeSpan.FromSeconds(frame * 6d / BusyLogoFrameCount) });
                            PumpDispatcher();
                            Y7Save(layered, Path.Combine(output, label + "-" + frame.ToString("00") + ".png"), scale);
                        }
                        preference.SetValue(logo, false); PumpDispatcher();
                        Check(label + " Windows reduced-animation branch removes every logo clock", !Running() && !Clocks());
                        Y7Save(layered, Path.Combine(output, label + "-still.png"), scale);
                        preference.SetValue(logo, true); PumpDispatcher();
                    }
                    Check(theme + " native phases contain visible motion", !File.ReadAllBytes(Path.Combine(output, theme + "-100-00.png"))
                        .SequenceEqual(File.ReadAllBytes(Path.Combine(output, theme + "-100-24.png"))));
                    preference.SetValue(logo, null); PumpDispatcher();
                    Check(theme + " production default follows the actual Windows animation setting", Running() == SystemParameters.ClientAreaAnimation);
                    preference.SetValue(logo, true); PumpDispatcher();
                    var peer = new ButtonAutomationPeer(cancel);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke(); PumpDispatcher();
                    Check(theme + " Cancel reaches the real lease and retains busy until work ends", lease.Context.IsCancellationRequested
                        && host.IsBusy && Running() && !host.CancelCommand.CanExecute(null));
                    lease.Dispose(); PumpDispatcher();
                    Check(theme + " completion removes all animation clocks", !host.IsBusy && !Running() && !Clocks());
                    using (var escape = host.Begin("Keyboard cancellation fixture"))
                    {
                        PumpDispatcher();
                        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                        window.RaiseEvent(key); PumpDispatcher();
                        Check(theme + " Escape cancels the active operation without closing the window", key.Handled && escape.Context.IsCancellationRequested && window.IsVisible);
                    }
                    using var second = host.Begin("Next operation"); PumpDispatcher();
                    Check(theme + " the next operation starts a fresh animation", Running());
                    window.Content = null; PumpDispatcher();
                    Check(theme + " unloading during work removes every logo clock", !Running() && !Clocks());
                    Check(theme + " unloading detaches the Windows animation-preference event", !(bool)logoType.GetField("subscribed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(logo)!);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(originalTheme); }
        BusyLogoRouteTests();
        GroupingReviewInteractionTests();
        BusyLogoPerformance(output);
        WriteBusyLogoPreview(output);
        Check("native Y7 proof never takes foreground focus", foreground == GetForegroundWindow());
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static Grid Y7Backdrop()
    {
        var root = new Grid(); root.SetResourceReference(Panel.BackgroundProperty, "CanvasBrush");
        var panel = new StackPanel { Margin = new Thickness(30), HorizontalAlignment = HorizontalAlignment.Left, Width = 210 };
        panel.Children.Add(new TextBlock { Text = "Families", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Protein database", Margin = new Thickness(0, 26, 0, 8) });
        panel.Children.Add(new TextBox { Text = "Selected database.fasta" });
        panel.Children.Add(new Button { Content = "Search proteins", Margin = new Thickness(0, 20, 0, 0) });
        panel.Children.Add(new CheckBox { Content = "Keep current grouping", Margin = new Thickness(0, 20, 0, 0) });
        panel.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); root.Children.Add(panel); return root;
    }

    private static System.Collections.Generic.IEnumerable<T> Y7Visual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Y7Visual<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void Y7Save(FrameworkElement content, string path, double scale)
    {
        content.Measure(new Size(800, 560)); content.Arrange(new Rect(0, 0, 800, 560)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(800 * scale), (int)(560 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
