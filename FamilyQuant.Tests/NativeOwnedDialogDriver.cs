using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace FamilyQuant.Tests;

// Drives only common-dialog windows created on the invoking test thread in this process.
// No foreground keyboard/mouse input and no handles from another process are used.
internal sealed class NativeOwnedDialogDriver : IDisposable
{
    private readonly uint thread = GetCurrentThreadId();
    private readonly string? selected;
    private readonly Timer timer;
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private int entered;
    private long lastSubmit;
    private IntPtr submittedWindow;
    internal bool SawDialog { get; private set; }
    internal bool EnteredPath { get; private set; }
    internal bool TimedOut { get; private set; }
    internal List<string> Controls { get; } = new();

    internal NativeOwnedDialogDriver(string? selectedPath)
    {
        selected = selectedPath;
        timer = new Timer(_ => Tick(), null, 50, 100);
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref entered, 1) != 0) return;
        try
        {
            EnumThreadWindows(thread, (window, _) =>
            {
                GetWindowThreadProcessId(window, out uint process);
                if (process != Environment.ProcessId || Class(window) != "#32770") return true;
                SawDialog = true;
                SetWindowPos(window, IntPtr.Zero, -28000, -28000, 0, 0, 1 | 4 | 16);
                if (selected == null) { PostMessage(window, 0x111, new IntPtr(2), IntPtr.Zero); return true; }
                if (watch.Elapsed > TimeSpan.FromSeconds(12))
                {
                    TimedOut = true; PostMessage(window, 0x111, new IntPtr(2), IntPtr.Zero); return true;
                }
                IntPtr edit = GetDlgItem(window, 1152);
                if (edit == IntPtr.Zero || Class(edit) != "Edit")
                {
                    IntPtr combo = GetDlgItem(window, 1148);
                    if (combo != IntPtr.Zero) EnumChildWindows(combo, (child, _) => { if (Class(child) == "Edit") edit = child; return true; }, IntPtr.Zero);
                }
                if (Controls.Count == 0)
                    EnumChildWindows(window, (child, _) => { Controls.Add(GetDlgCtrlID(child) + ":" + Class(child)); return true; }, IntPtr.Zero);
                if (watch.ElapsedMilliseconds - lastSubmit > 500)
                {
                    if (submittedWindow != window)
                    {
                        if (edit != IntPtr.Zero)
                        {
                            SendMessageTimeout(edit, 0x000C, IntPtr.Zero, selected, 2, 500, out _);
                            EnteredPath = true; submittedWindow = window;
                        }
                        else
                        {
                            var root = AutomationElement.FromHandle(window);
                            var edits = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                            foreach (AutomationElement candidate in edits)
                            {
                                string name = candidate.Current.Name, id = candidate.Current.AutomationId;
                                Controls.Add("uia-edit:" + id + ":" + name);
                                if ((id == "1001" || name.Contains("File name", StringComparison.OrdinalIgnoreCase)
                                    || name.Contains("Folder", StringComparison.OrdinalIgnoreCase))
                                    && candidate.TryGetCurrentPattern(ValuePattern.Pattern, out object value))
                                {
                                    ((ValuePattern)value).SetValue(selected); EnteredPath = true; submittedWindow = window; break;
                                }
                            }
                        }
                    }
                    lastSubmit = watch.ElapsedMilliseconds;
                    if (EnteredPath)
                    {
                        IntPtr accept = GetDlgItem(window, 1);
                        if (accept != IntPtr.Zero) PostMessage(accept, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                        else PostMessage(window, 0x111, new IntPtr(1), IntPtr.Zero);
                    }
                }
                return true;
            }, IntPtr.Zero);
        }
        finally { Volatile.Write(ref entered, 0); }
    }

    private static string Class(IntPtr window) { var text = new StringBuilder(128); GetClassName(window, text, text.Capacity); return text.ToString(); }
    public void Dispose()
    {
        using var done = new ManualResetEvent(false);
        if (timer.Dispose(done)) done.WaitOne(TimeSpan.FromSeconds(2));
    }
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message,
        IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
