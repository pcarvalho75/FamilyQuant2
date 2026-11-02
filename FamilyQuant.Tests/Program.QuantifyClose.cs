using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record QuantifyCloseReading(bool HasButton, string? Caption, bool Displayed, bool HasStyle,
        int Clicks, bool Closed, bool? ModalResult, bool Offscreen, bool TookFocus);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static QuantifyCloseReading ReadQuantifyClose(Window window, bool modal)
    {
        window.Left = -30000; window.Top = -30000; window.ShowActivated = false; window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        Button? button = window.FindName("ButtonClose") as Button;
        bool displayed = false, styled = false, closed = false, focused = false;
        int clicks = 0;
        window.Closed += (_, _) => closed = true;
        if (button != null) button.Click += (_, _) => clicks++;
        void CloseFromDisplayedButton()
        {
            focused |= window.IsActive || GetForegroundWindow() == new WindowInteropHelper(window).Handle;
            displayed = button?.IsVisible == true && PresentationSource.FromVisual(button) != null;
            styled = button?.Style != null;
            if (button != null) SecondaryClick(button);
            else window.Close(); // A red missing-control check must never leave a modal window waiting.
        }
        bool? result = null;
        try
        {
            window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(new Action(CloseFromDisplayedButton), DispatcherPriority.Background);
            if (modal) result = window.ShowDialog();
            else
            {
                window.Show();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!closed && watch.Elapsed < TimeSpan.FromSeconds(5)) PumpDispatcher();
                if (!closed) throw new InvalidOperationException("The nonmodal Close proof did not finish.");
            }
            return new(button != null, button?.Content as string, displayed, styled, clicks, closed, result,
                window.Left == -30000 && window.Top == -30000 && !window.ShowActivated && !window.ShowInTaskbar, focused);
        }
        finally { if (!closed) window.Close(); }
    }

    private static int QuantifyCloseCommand()
    {
        QuantifyCloseTests();
        Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        return failed == 0 ? 0 : 1;
    }

    private static void QuantifyCloseTests()
    {
        EnsureApplication();
        var savedTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (bool modal in new[] { false, true })
            {
                Themes.ThemeManager.Apply(theme);
                var result = new FullXicComputeResult { Rows = new() { new() { Side = "identified", Run = "close-fixture", Scan = 1 } }, Manifest = new() };
                var window = new ComputeXicWindow("native close contract");
                // Seed only a completed native result. Closing must retain the exact objects for its caller.
                typeof(ComputeXicViewModel).GetProperty(nameof(ComputeXicViewModel.Result))!.SetValue(window.ViewModel, result);
                var observed = ReadQuantifyClose(window, modal);
                string prefix = theme + (modal ? " modal" : " nonmodal");
                Check(prefix + " Quantify offers the displayed styled Close control and caption", observed.HasButton && observed.Displayed
                    && observed.HasStyle && observed.Caption == "Close");
                Check(prefix + " actual Close click closes once and preserves the frozen modal result", observed.Clicks == 1 && observed.Closed
                    && (!modal || observed.ModalResult == true), System.Text.Json.JsonSerializer.Serialize(observed));
                Check(prefix + " Close retains the completed rows and manifest for the caller", window.Computed
                    && ReferenceEquals(window.ResultRows, result.Rows) && ReferenceEquals(window.ResultManifest, result.Manifest));
                Check(prefix + " proof window stays offscreen without activation or a taskbar entry", observed.Offscreen && !observed.TookFocus);
            }
        }
        finally { Themes.ThemeManager.Apply(savedTheme); }
    }
}
