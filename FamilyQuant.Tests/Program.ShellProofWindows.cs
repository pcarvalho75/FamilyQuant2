using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static T OffscreenProofWindow<T>(T window) where T : Window
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        return window;
    }

    private static bool HasProofWindowPlacement(Window window) => window.Left == -30000 && window.Top == -30000
        && !window.ShowActivated && !window.ShowInTaskbar && window.WindowStartupLocation == WindowStartupLocation.Manual;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProofWindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out ProofWindowRect rectangle);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static int ShellProofWindowChecks(string[] args, string workDir)
    {
        EnsureApplication();
        var window = OffscreenProofWindow(new Window { Width = 500, Height = 300 });
        try
        {
            Check("native proof placement is set before Show", HasProofWindowPlacement(window));
            window.Show(); PumpDispatcher();
            IntPtr handle = new WindowInteropHelper(window).Handle;
            Check("the actual proof HWND stays off screen", GetWindowRect(handle, out var rectangle)
                && rectangle.Right < -10000 && rectangle.Bottom < -10000);
            Check("the proof window neither activates nor enters the taskbar", HasProofWindowPlacement(window)
                && GetForegroundWindow() != handle);
        }
        finally { window.Close(); }
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
