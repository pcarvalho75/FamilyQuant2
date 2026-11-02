using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FamilyQuant.Tests;

// Observe only this test's own thread and private desktop. Optional dismissal is
// restricted to explicitly named completion dialogs, using their normal OK command.
internal sealed class NativeBehaviourMessages : IDisposable
{
    private readonly uint thread = GetCurrentThreadId();
    private readonly Timer timer;
    private readonly HashSet<string> messages = new(StringComparer.Ordinal);
    private readonly HashSet<string> dismiss;
    private int reading;
    internal NativeBehaviourMessages(params string[] completionTitles)
    { dismiss = new(completionTitles, StringComparer.Ordinal); timer = new Timer(_ => Read(), null, 5, 10); }
    internal string[] Texts { get { lock (messages) return new List<string>(messages).ToArray(); } }
    private void Read()
    {
        if (Interlocked.Exchange(ref reading, 1) != 0) return;
        try
        {
            EnumThreadWindows(thread, (window, _) =>
            {
                GetWindowThreadProcessId(window, out uint pid);
                if (pid != Environment.ProcessId || Class(window) != "#32770") return true;
                var parts = new List<string>();
                EnumChildWindows(window, (child, _) =>
                {
                    if (Class(child) == "Static")
                    { var value = new StringBuilder(16384); GetWindowText(child, value, value.Capacity); if (value.Length > 0) parts.Add(value.ToString()); }
                    return true;
                }, IntPtr.Zero);
                if (parts.Count > 0) lock (messages) messages.Add(string.Join("\n", parts));
                var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
                if (dismiss.Contains(title.ToString())) PostMessage(window, 0x111, new IntPtr(1), IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }
        finally { Volatile.Write(ref reading, 0); }
    }
    private static string Class(IntPtr window) { var value = new StringBuilder(128); GetClassName(window, value, value.Capacity); return value.ToString(); }
    public void Dispose() { using var done = new ManualResetEvent(false); if (timer.Dispose(done)) done.WaitOne(TimeSpan.FromSeconds(2)); }
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder value, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder value, int capacity);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
