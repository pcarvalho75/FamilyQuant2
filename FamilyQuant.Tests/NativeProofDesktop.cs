using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FamilyQuant.Tests;

// A separate, never-selected desktop keeps native common dialogs away from the user's focus.
// This helper never calls SwitchDesktop, SetForegroundWindow or a clipboard API.
internal static class NativeProofDesktop
{
    private const string Prefix = "FQEvidenceProof_";
    internal static bool IsPrivate
    {
        get
        {
            var name = new StringBuilder(256);
            return GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, name.Capacity * 2, out _)
                && name.ToString().StartsWith(Prefix, StringComparison.Ordinal);
        }
    }

    internal static int Run(string[] workerArguments, string log)
    {
        string name = Prefix + Environment.ProcessId + "_" + Guid.NewGuid().ToString("N");
        IntPtr desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x01ff, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create the private proof desktop.");
        var process = new ProcessInformation();
        try
        {
            var startup = new StartupInformation { Size = Marshal.SizeOf<StartupInformation>(), Desktop = name };
            string executable = Path.ChangeExtension(typeof(NativeProofDesktop).Assembly.Location, ".exe");
            var command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(workerArguments).Select(Quote)));
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000000, IntPtr.Zero,
                Environment.CurrentDirectory, ref startup, out process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot start the isolated native proof.");
            Console.WriteLine("Native proof running on a private desktop; child PID " + process.ProcessId + ".");
            uint wait = WaitForSingleObject(process.Process, 300_000);
            if (wait != 0)
            {
                // This exact process handle belongs to the child created immediately above.
                TerminateProcess(process.Process, 124);
                WaitForSingleObject(process.Process, 10_000);
                throw new TimeoutException("The owned native proof exceeded five minutes.");
            }
            if (!GetExitCodeProcess(process.Process, out uint code)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (File.Exists(log)) Console.Write(File.ReadAllText(log));
            return checked((int)code);
        }
        finally
        {
            if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
            if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
            CloseDesktop(desktop);
        }
    }

    private static string Quote(string argument)
    {
        var text = new StringBuilder("\""); int slashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            text.Append('\\', character == '"' ? slashes * 2 + 1 : slashes).Append(character); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInformation
    {
        internal int Size;
        internal string? Reserved, Desktop, Title;
        internal uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        internal ushort ShowWindow, Reserved2Size;
        internal IntPtr Reserved2, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { internal IntPtr Process, Thread; internal uint ProcessId, ThreadId; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder information, int bytes, out uint needed);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string executable, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inherit, uint flags, IntPtr environment, string directory, ref StartupInformation startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
