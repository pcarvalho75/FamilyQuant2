using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int LookFocusTemplates(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Supply fresh output and date timestamp.");
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output); EnsureApplication();
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var results = new List<object>();
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        foreach (double scale in new[] { 1d, 1.25, 1.5 })
        foreach (string kind in new[] { "Button", "CheckBox", "ComboBox", "TextBox" })
        {
            Themes.ThemeManager.Apply(theme);
            Control target = kind switch
            {
                "Button" => new Button { Content = "Keyboard action" },
                "CheckBox" => new CheckBox { Content = "Example option" },
                "ComboBox" => new ComboBox { ItemsSource = new[] { "Example choice" }, SelectedIndex = 0 },
                _ => new TextBox { Text = "Editable value" }
            };
            target.Margin = new Thickness(12);
            var host = new Grid { Width = 280, Height = 72, Background = (Brush)Application.Current.FindResource("CanvasBrush") };
            host.Children.Add(target); LayoutAt(host, new Size(280, 72), -1);
            VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale)); LayoutAt(host, new Size(280, 72), -1);
            string name = theme + "-" + (scale * 100).ToString(CultureInfo.InvariantCulture) + "-" + kind;
            var baseline = FocusBitmap(host, scale, Path.Combine(output, name + "-normal.png"));
            bool state = VisualStateManager.GoToState(target, "Focused", false);
            SettleFocusPreview();
            LayoutAt(host, new Size(280, 72), -1);
            var focused = FocusBitmap(host, scale, Path.Combine(output, name + "-focus.png"));
            var pixels = FocusDifference(baseline, focused);
            string mode = "native Focused visual state";
            int nativeFocusProperties = 0;
            if (pixels.Changed == 0)
            {
                nativeFocusProperties = PaintNativeFocusConditions(target);
                LayoutAt(host, new Size(280, 72), -1); SettleFocusPreview();
                focused = FocusBitmap(host, scale, Path.Combine(output, name + "-focus.png"));
                pixels = FocusDifference(baseline, focused); mode = "native focus chrome/trigger values";
            }
            if (pixels.Changed == 0 && target.FocusVisualStyle != null)
            {
                var preview = new Control { Style = target.FocusVisualStyle, Focusable = false, IsHitTestVisible = false,
                    Margin = target.Margin };
                host.Children.Add(preview); LayoutAt(host, new Size(280, 72), -1);
                focused = FocusBitmap(host, scale, Path.Combine(output, name + "-focus.png"));
                pixels = FocusDifference(baseline, focused); mode = "native FocusVisualStyle template";
            }
            bool noFocus = !target.IsKeyboardFocused && !target.IsKeyboardFocusWithin;
            bool nativeDpi = VisualTreeHelper.GetDpi(target).DpiScaleX == scale;
            Check(name + " focus template paints a visible indicator without keyboard focus",
                pixels.Changed > 0 && pixels.Contrast >= 3 && noFocus && nativeDpi,
                mode + "; changed=" + pixels.Changed + "; contrast=" + pixels.Contrast.ToString("F2", CultureInfo.InvariantCulture));
            results.Add(new { theme = theme.ToString(), scale, kind, mode, state, nativeFocusProperties, pixels.Changed, pixels.Contrast, noFocus, nativeDpi,
                focusStyle = target.FocusVisualStyle?.TargetType.FullName, normal = name + "-normal.png", focused = name + "-focus.png" });
        }
        Themes.ThemeManager.Apply(Themes.AppTheme.Dark);
        string project = Path.Combine(output, "SYNTHETIC-UI-ONLY.fqproj");
        var file = new FamilyClusterFile { MyExtendedFastaItems = Enumerable.Range(1, 20).Select(index => new FastaItemExtended
        { SequenceIdentifier = "ui_fixture_" + index, Description = "Synthetic UI protein " + (index % 4), Sequence = "PEPTIDEK",
            UserID = index % 4, USearchID = index % 4 }).ToList() };
        ProjectStore.WriteFqproj(project, file, FinishQuantRows(), FinishStoredManifest());
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "focus-templates.json"), new { executedAt = args[2],
            fixture = project, actualKeyTraversal = false, keyboardFocusTaken = false, results });
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static void SettleFocusPreview()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(750) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); PumpDispatcher();
    }

    private static int PaintNativeFocusConditions(Control control)
    {
        int applied = 0;
        foreach (var child in WalkTree(control))
        {
            var property = child.GetType().GetProperty("RenderFocused");
            if (property is { CanWrite: true } && property.PropertyType == typeof(bool))
            { property.SetValue(child, true); applied++; }
        }
        bool FocusProperty(DependencyProperty property) => property.Name is "IsKeyboardFocused" or "IsKeyboardFocusWithin" or "IsFocused";
        DependencyObject? Named(string? name) => string.IsNullOrEmpty(name) ? control : control.Template?.FindName(name, control) as DependencyObject;
        void Apply(SetterBaseCollection setters)
        {
            foreach (var setter in setters.OfType<Setter>())
            {
                if (FocusProperty(setter.Property) || setter.Property.Name == "FocusedElement")
                    throw new InvalidOperationException("A focus proof cannot assign focus.");
                var target = Named(setter.TargetName);
                if (target == null) continue;
                if (setter.Value is DynamicResourceExtension resource && target is FrameworkElement element)
                    element.SetResourceReference(setter.Property, resource.ResourceKey);
                else target.SetValue(setter.Property, setter.Value);
                applied++;
            }
        }
        var triggers = new List<TriggerBase>();
        var styles = new Stack<Style>();
        for (Style? style = control.Style; style != null; style = style.BasedOn) styles.Push(style);
        foreach (var style in styles) triggers.AddRange(style.Triggers.Cast<TriggerBase>());
        if (control.Template != null) triggers.AddRange(control.Template.Triggers.Cast<TriggerBase>());
        foreach (var trigger in triggers)
        {
            if (trigger is Trigger single && FocusProperty(single.Property) && Equals(single.Value, true)) Apply(single.Setters);
            if (trigger is MultiTrigger multi && multi.Conditions.Cast<Condition>().Any(c => FocusProperty(c.Property) && Equals(c.Value, true))
                && multi.Conditions.Cast<Condition>().All(c => FocusProperty(c.Property) ? Equals(c.Value, true) : Equals(Named(c.SourceName)?.GetValue(c.Property), c.Value)))
                Apply(multi.Setters);
        }
        return applied;
    }

    private static RenderTargetBitmap FocusBitmap(FrameworkElement host, double scale, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(280 * scale), (int)Math.Ceiling(72 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(host); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        return bitmap;
    }

    private static (int Changed, double Contrast) FocusDifference(BitmapSource before, BitmapSource after)
    {
        int stride = before.PixelWidth * 4;
        var first = new byte[stride * before.PixelHeight]; var second = new byte[first.Length];
        before.CopyPixels(first, stride, 0); after.CopyPixels(second, stride, 0);
        double Lum(int offset)
        {
            double Channel(byte value) { double v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Channel(second[offset + 2]) + .7152 * Channel(second[offset + 1]) + .0722 * Channel(second[offset]);
        }
        int changed = 0; double contrast = 1;
        for (int index = 0; index < first.Length; index += 4)
        {
            if (first[index] == second[index] && first[index + 1] == second[index + 1] && first[index + 2] == second[index + 2]) continue;
            changed++; double here = Lum(index);
            foreach (int other in new[] { index - 4, index + 4, index - stride, index + stride })
            {
                if (other < 0 || other + 3 >= second.Length) continue;
                double beside = Lum(other); contrast = Math.Max(contrast, (Math.Max(here, beside) + .05) / (Math.Min(here, beside) + .05));
            }
        }
        return (changed, contrast);
    }
}
