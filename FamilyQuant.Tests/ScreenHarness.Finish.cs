using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class ScreenHarness
{
    private sealed record FinishTabStop(string Type, string Name, string Text, int TabIndex, string Navigation);
    private sealed record FinishCapture(string File, string Theme, string Screen, double Scale,
        double NativeRootScale, double NativeContentScale, int DipWidth, int DipHeight, int PixelWidth,
        int PixelHeight, double MinimumWidth, double MinimumHeight, bool LayoutAccepted,
        List<FinishTabStop> TabStops, bool KeyboardTraversalExecuted = false);

    private static void CaptureFinish(string theme, string screen, Window window, string output, List<FinishCapture> captures)
    {
        foreach (double scale in new[] { 1.0, 1.25, 1.5 })
        foreach (var requested in new[] { (Width: 900, Height: 600), (Width: 1366, Height: 820) })
        {
            int width = (int)Math.Ceiling(Math.Max(requested.Width, window.MinWidth));
            int height = (int)Math.Ceiling(Math.Max(requested.Height, window.MinHeight));
            window.Width = width; window.Height = height;
            Pump();
            // Set actual WPF DPI metadata and propagate it to native child visuals. This does
            // not change Windows settings or pretend the display's monitor DPI changed.
            VisualTreeHelper.SetRootDpi(window, new DpiScale(scale, scale));
            SettleFinish();
            var content = (FrameworkElement)window.Content;
            bool layout = Program.VerifyFinishLayout(theme + "/" + screen + "/" + scale.ToString(CultureInfo.InvariantCulture)
                + "/" + width + "x" + height, content, new Size(width, height));
            double rootScale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            double contentScale = VisualTreeHelper.GetDpi(content).DpiScaleX;
            if (rootScale != scale || contentScale != scale)
                throw new InvalidOperationException("Native WPF DPI did not propagate to the captured content.");
            if (window.IsActive || window.IsKeyboardFocusWithin)
                throw new InvalidOperationException("A proof window took focus.");
            var tabStops = Walk(content).OfType<Control>()
                .Where(control => control.IsVisible && control.IsEnabled && control.Focusable && control.IsTabStop)
                .Select(control => new FinishTabStop(control.GetType().Name, control.Name,
                    control is ContentControl { Content: string text } ? text : control.ToolTip as string ?? "",
                    KeyboardNavigation.GetTabIndex(control), KeyboardNavigation.GetTabNavigation(control).ToString()))
                .ToList();
            string relative = Path.Combine(theme, ((int)(scale * 100)).ToString(CultureInfo.InvariantCulture),
                width + "x" + height, screen + ".png");
            string path = Path.Combine(output, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            int pixelWidth = (int)Math.Ceiling(width * scale), pixelHeight = (int)Math.Ceiling(height * scale);
            RenderFinish(content, window.Background, width, height, scale, path);
            captures.Add(new(relative.Replace('\\', '/'), theme, screen, scale, rootScale, contentScale,
                width, height, pixelWidth, pixelHeight, window.MinWidth, window.MinHeight, layout, tabStops));
            Console.WriteLine("  C14 " + relative + "; WPF DPI=" + (96 * scale).ToString(CultureInfo.InvariantCulture)
                + "; native tab candidates=" + tabStops.Count);
        }
        VisualTreeHelper.SetRootDpi(window, new DpiScale(1, 1));
    }

    private static void SettleFinish()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            { Interval = TimeSpan.FromMilliseconds(750) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); Pump();
    }

    private static RenderTargetBitmap RenderFinish(FrameworkElement content, Brush background,
        int width, int height, double scale, string path)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var visual = new DrawingVisual();
        VisualTreeHelper.SetRootDpi(visual, new DpiScale(scale, scale));
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        return bitmap;
    }
}
