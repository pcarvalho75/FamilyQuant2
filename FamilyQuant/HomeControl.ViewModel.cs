using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace FamilyQuant;

public sealed record HomeRecentProject(string Path, string Name, bool Exists)
{
    public string Availability => Exists ? "" : "File unavailable";
    public string ToolTip => Exists ? "Opens this recent project: " + Path : "This file is unavailable. Last path: " + Path;
}

/// <summary>Start-page navigation state. It reads no scientific file and runs no engine.</summary>
public sealed class HomeViewModel : INotifyPropertyChanged
{
    public bool HasProject { get; private set; }
    public string ProjectSummary { get; private set; } = "No project open.";
    public string ProjectReason => HasProject ? "Project open" : "Requires an open project";
    public ObservableCollection<HomeRecentProject> RecentProjects { get; } = new();
    public bool HasRecents => RecentProjects.Count > 0;
    public bool HasLastResult { get; private set; }
    public string LastResultText { get; private set; } = "No saved result yet.";
    public string? LastResultPath { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public void SetProjectState(bool loaded, string summary)
    {
        HasProject = loaded;
        ProjectSummary = summary.Replace('\r', ' ').Replace('\n', ' ').Trim();
        Changed();
    }

    public void SetRecentProjects(IEnumerable<string> paths, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        RecentProjects.Clear();
        foreach (string path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10))
            RecentProjects.Add(new HomeRecentProject(path, Path.GetFileName(path), exists(path)));
        Changed();
    }

    public void SetLastResult(string? path, bool currentHasResult, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        LastResultPath = path;
        HasLastResult = currentHasResult || path != null && exists(path);
        LastResultText = currentHasResult ? "Results in the open project" : path == null ? "No saved result yet."
            : exists(path) ? Path.GetFileName(path) : "Last result file is unavailable.";
        Changed();
    }
}
