using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace FamilyQuant;

public enum ShellDestination { Start, Families, Quantify, Results, DeNovo, Build }

public sealed class ShellNavigationItem : INotifyPropertyChanged
{
    public ShellDestination Destination { get; }
    public string Label { get; }
    public bool IsEnabled { get; private set; }
    public bool IsSelected { get; private set; }
    public string StateText { get; private set; } = "";
    public string ToolTip => IsEnabled ? "Opens " + Label + " using the existing workflow. Keyboard shortcut: " + Shortcut + "."
        : StateText + ". Open a project or finish the current operation to continue.";
    public string Shortcut => Destination switch { ShellDestination.Families => "Alt+1", ShellDestination.Quantify => "Alt+2",
        ShellDestination.Results => "Alt+3", ShellDestination.DeNovo => "Alt+4", _ => "" };
    public event PropertyChangedEventHandler? PropertyChanged;
    public ShellNavigationItem(ShellDestination destination, string label) { Destination = destination; Label = label; }
    internal void Update(bool enabled, bool selected, string reason)
    {
        IsEnabled = enabled;
        IsSelected = selected;
        StateText = !enabled ? reason : selected ? "Current" : "Ready";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

/// <summary>Navigation and readiness only. Views and scientific operations live behind the request event.</summary>
public sealed class ShellViewModel : INotifyPropertyChanged
{
    public ShellDestination Current { get; private set; } = ShellDestination.Start;
    public bool HasProject { get; private set; }
    public bool IsBusy { get; private set; }
    public FamilyClusterFile? Document { get; private set; }
    public string? ProjectPath { get; private set; }
    public bool HasStoredResults { get; private set; }
    public IReadOnlyList<ShellNavigationItem> Items { get; } = new[]
    {
        new ShellNavigationItem(ShellDestination.Families, "Families"),
        new ShellNavigationItem(ShellDestination.Quantify, "Quantify"),
        new ShellNavigationItem(ShellDestination.Results, "Results"),
        new ShellNavigationItem(ShellDestination.DeNovo, "De novo")
    };
    public event Action<ShellDestination>? NavigationRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ShellViewModel() => Refresh();
    public bool CanNavigate(ShellDestination destination) => Enum.IsDefined(destination) && !IsBusy &&
        (HasProject || destination is not (ShellDestination.Quantify or ShellDestination.Results));
    public bool RequestNavigate(ShellDestination destination)
    {
        if (!CanNavigate(destination)) return false;
        NavigationRequested?.Invoke(destination);
        return true;
    }
    public void UpdateProjectState(bool hasProject, bool busy)
    {
        HasProject = hasProject;
        IsBusy = busy;
        Refresh();
    }
    public void SetCurrent(ShellDestination destination)
    {
        if (!Enum.IsDefined(destination)) throw new ArgumentOutOfRangeException(nameof(destination));
        Current = destination;
        Refresh();
    }
    public void SetDocument(FamilyClusterFile? document, string? projectPath, bool hasStoredResults)
    {
        Document = document;
        ProjectPath = projectPath;
        HasStoredResults = hasStoredResults;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    private void Refresh()
    {
        foreach (var item in Items) item.Update(CanNavigate(item.Destination), item.Destination == Current,
            IsBusy ? "Operation in progress" : "Open a project first");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
