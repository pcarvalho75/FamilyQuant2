using System;
using System.Runtime.InteropServices;

namespace FamilyQuant;

/// <summary>Native crash UI works without the WPF dispatcher or application resources.</summary>
internal static class CrashNoticeDialog
{
    private const int OpenFolder = 100, Close = 8;
    private const uint ButtonClicked = 2;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Callback(IntPtr window, uint notification, UIntPtr first, IntPtr second, IntPtr data);
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Button { public int Id; public IntPtr Text; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct Config
    {
        public uint Size; public IntPtr Parent, Instance; public uint Flags, CommonButtons;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Title;
        public IntPtr MainIcon;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Instruction;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Content;
        public uint ButtonCount; public IntPtr Buttons; public int DefaultButton;
        public uint RadioCount; public IntPtr Radios; public int DefaultRadio;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Verification;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Expanded;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ExpandedControl;
        [MarshalAs(UnmanagedType.LPWStr)] public string? CollapsedControl;
        public IntPtr FooterIcon;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Footer;
        [MarshalAs(UnmanagedType.FunctionPtr)] public Callback? Handler;
        public IntPtr Data; public uint Width;
    }
    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int TaskDialogIndirect(ref Config config, out int button, IntPtr radio, IntPtr verification);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr owner, string text, string title, uint style);

    public static void Show(CrashNotice notice)
    {
        if (TryShowNative(notice, null)) return;
        // Last-resort UI if the native task dialog itself cannot be constructed.
        try { MessageBox(IntPtr.Zero, notice.Message, "FamilyQuant", 0x10); } catch { }
    }

    internal static bool TryShowNative(CrashNotice notice, Action<IntPtr>? created)
    {
        IntPtr text = IntPtr.Zero, button = IntPtr.Zero;
        try
        {
            text = Marshal.StringToHGlobalUni("Open folder");
            button = Marshal.AllocHGlobal(Marshal.SizeOf<Button>());
            Marshal.StructureToPtr(new Button { Id = OpenFolder, Text = text }, button, false);
            Callback callback = (window, notification, first, _, _) =>
            {
                try
                {
                    if (notification == 0) created?.Invoke(window);
                    if (notification == ButtonClicked && first.ToUInt64() == OpenFolder)
                    { CrashReportStore.TryOpenFolder(notice.Directory); return 1; } // S_FALSE keeps the notice open.
                }
                catch { }
                return 0;
            };
            var config = new Config { Size = (uint)Marshal.SizeOf<Config>(), Flags = 8u | (created != null ? 0x10000u : 0u), CommonButtons = 0x20,
                Title = "FamilyQuant", Content = notice.Message, ButtonCount = 1, Buttons = button, DefaultButton = Close, Handler = callback };
            int result = TaskDialogIndirect(ref config, out _, IntPtr.Zero, IntPtr.Zero);
            GC.KeepAlive(callback);
            return result >= 0;
        }
        catch { }
        finally
        {
            if (button != IntPtr.Zero) Marshal.FreeHGlobal(button);
            if (text != IntPtr.Zero) Marshal.FreeHGlobal(text);
        }
        return false;
    }
}
