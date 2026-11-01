using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant;

/// <summary>Navigation preferences only. Project and scientific data never enter this file.</summary>
public sealed class ShellSessionState
{
    public List<string> RecentProjects { get; set; } = new();
    public string? LastResultProject { get; set; }

    public void Remember(string path, bool hasStoredResults = false)
    {
        string full = Path.GetFullPath(path);
        RecentProjects = RecentProjects.Where(p => !string.Equals(p, full, StringComparison.OrdinalIgnoreCase))
            .Prepend(full).Take(10).ToList();
        if (hasStoredResults) LastResultProject = full;
        else if (string.Equals(LastResultProject, full, StringComparison.OrdinalIgnoreCase)) LastResultProject = null;
    }

    public static ShellSessionState Load(string path)
    {
        try
        {
            var state = JsonSerializer.Deserialize<ShellSessionState>(File.ReadAllText(path)) ?? new();
            state.RecentProjects = (state.RecentProjects ?? new()).Where(p => !string.IsNullOrWhiteSpace(p))
                .Where(p => Path.IsPathFullyQualified(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            if (!Path.IsPathFullyQualified(state.LastResultProject ?? "")) state.LastResultProject = null;
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new();
        }
    }

    public string? Save(string path)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, path, true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "Recent projects could not be saved: " + ex.Message;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

public partial class MainWindow
{
    public static string? ShellSettingsPathOverride { get; set; }
    public static Func<string?>? SelectOpenPathOverride { get; set; }
    public static Func<string?>? SelectSaveProjectPathOverride { get; set; }
    private ShellSessionState shellState = new();
    private string shellSettingsPath = "";

    private void RememberProject(string path)
    {
        shellState.Remember(path, loadedQuant.Count > 0);
        string? error = shellState.Save(shellSettingsPath);
        if (error != null) StatusModel.ShowMessage(error);
        RefreshRecentMenu();
        home?.SetRecentProjects(shellState.RecentProjects);
        home?.SetLastResult(shellState.LastResultProject, loadedQuant.Count > 0);
    }

    private async void ShowLastResult()
    {
        if (cgc != null && loadedQuant.Count > 0) MenuItemAnalytics_Click(this, new System.Windows.RoutedEventArgs());
        else if (shellState.LastResultProject is string path && await OpenProjectSafelyAsync(path))
            MenuItemAnalytics_Click(this, new System.Windows.RoutedEventArgs());
    }
}
