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

/// <summary>One numbered step on Home: its names-table entry, its state and whether its button leads.</summary>
public sealed record HomeStep(WorkflowStep Step, string State, bool IsEnabled, bool IsNext);

/// <summary>Home navigation state. It reads no scientific file and runs no engine.</summary>
public sealed class HomeViewModel : INotifyPropertyChanged
{
    public const string Ready = "Ready", Done = "Done", NeedsProject = "Needs a project", NeedsQuantify = "Needs Quantify";
    public bool HasProject { get; private set; }
    public bool HasStoredResults { get; private set; }
    public string ProjectSummary { get; private set; } = "No project open.";

    /// <summary>With no project open, Open project leads; a step leads only once a project is open, so a
    /// returning user is not invited to rebuild. One primary button on the screen at a time.</summary>
    public bool OpenProjectLeads => !HasProject;

    /// <summary>The five steps in the names-table order. Quantify and Results need an open project, the
    /// condition their sidebar entries use; Curate families asks for one itself. With a project open,
    /// Curate families leads until stored results exist, then Results does.</summary>
    public IReadOnlyList<HomeStep> Steps
    {
        get
        {
            bool results = HasProject && HasStoredResults;
            int next = !HasProject ? 0 : results ? 4 : 2;
            string[] states =
            {
                HasProject ? Done : Ready,
                HasProject ? Ready : NeedsProject,
                !HasProject ? NeedsProject : results ? Done : Ready,
                !HasProject ? NeedsProject : results ? Ready : NeedsQuantify,
                Ready
            };
            return WorkflowNames.Steps.Select((step, index) => new HomeStep(step, states[index],
                step.Number is not (3 or 4) || HasProject, step.Number == next)).ToArray();
        }
    }
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
        foreach (string path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).Where(exists))
            RecentProjects.Add(new HomeRecentProject(path, Path.GetFileName(path), true));
        Changed();
    }

    public void SetLastResult(string? path, bool currentHasResult, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        LastResultPath = path;
        HasStoredResults = currentHasResult;
        HasLastResult = currentHasResult || path != null && exists(path);
        LastResultText = currentHasResult ? "Results in the open project" : path == null ? "No saved result yet."
            : exists(path) ? Path.GetFileName(path) : "Last result file is unavailable.";
        Changed();
    }
}
