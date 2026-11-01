using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    public static double FitWindowDimension(double saved, double fallback, double minimum, double maximum) =>
        Math.Clamp(double.IsFinite(saved) && saved > 0 ? saved : fallback, minimum, Math.Max(minimum, maximum));

    private void InitializeWindowPreferences()
    {
        if (persistShellSettings)
        {
            Width = FitWindowDimension(shellState.WindowWidth, 1024, MinWidth, SystemParameters.WorkArea.Width);
            Height = FitWindowDimension(shellState.WindowHeight, 768, MinHeight, SystemParameters.WorkArea.Height);
            if (shellState.WindowMaximized) WindowState = WindowState.Maximized;
        }
        Closed += (_, _) => PersistWindowPreferences();
        AllowDrop = true;
        PreviewDragOver += (sender, args) =>
        {
            if (!args.Data.GetDataPresent(DataFormats.FileDrop)) return;
            args.Effects = TryGetDroppedProject(args.Data.GetData(DataFormats.FileDrop) as string[], out _) ? DragDropEffects.Copy : DragDropEffects.None;
            args.Handled = true;
        };
        PreviewDrop += async (_, args) =>
        {
            if (!args.Data.GetDataPresent(DataFormats.FileDrop)) return;
            args.Handled = true;
            await OpenDroppedProjectAsync(args.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>());
        };
    }

    private void PersistWindowPreferences()
    {
        if (!persistShellSettings) return;
        Rect restore = RestoreBounds;
        shellState.WindowWidth = WindowState == WindowState.Normal || restore.IsEmpty ? Width : restore.Width;
        shellState.WindowHeight = WindowState == WindowState.Normal || restore.IsEmpty ? Height : restore.Height;
        shellState.WindowMaximized = WindowState == WindowState.Maximized;
        string? error = shellState.Save(shellSettingsPath);
        if (error != null) System.Diagnostics.Trace.WriteLine(error);
    }

    public void PrepareFileDialog(FileDialog dialog)
    {
        if (Directory.Exists(shellState.LastFolder)) dialog.InitialDirectory = shellState.LastFolder;
    }
    public void RememberFolder(string path)
    {
        string? folder = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path));
        if (!Directory.Exists(folder)) return;
        shellState.LastFolder = folder;
        string? error = persistShellSettings ? shellState.Save(shellSettingsPath) : null;
        if (error != null) StatusModel.ShowMessage(error);
    }

    public static bool TryGetDroppedProject(IEnumerable<string>? files, out string? path)
    {
        path = null;
        string[] candidates = files?.Take(2).ToArray() ?? Array.Empty<string>();
        if (candidates.Length != 1 || string.IsNullOrWhiteSpace(candidates[0])) return false;
        string extension = Path.GetExtension(candidates[0]);
        if (!(extension.Equals(".fqproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fcf", StringComparison.OrdinalIgnoreCase))
            || !File.Exists(candidates[0])) return false;
        path = Path.GetFullPath(candidates[0]);
        return true;
    }

    public async Task<bool> OpenDroppedProjectAsync(IEnumerable<string> files)
    {
        if (!TryGetDroppedProject(files, out string? path))
        {
            StatusModel.ShowMessage("Drop one existing .fqproj or .fcf file to open it.");
            return false;
        }
        return await OpenProjectSafelyAsync(path!);
    }
}
