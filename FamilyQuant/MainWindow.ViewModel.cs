using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace FamilyQuant;

public enum ShellDestination { Start, Families, Quantify, Results, DeNovo, Build }

/// <summary>One sidebar entry: a numbered step of the names table and its readiness.</summary>
public sealed class ShellNavigationItem : INotifyPropertyChanged
{
    public ShellDestination Destination { get; }
    public int Number { get; }
    public string Label { get; }
    public bool IsEnabled { get; private set; }
    public bool IsSelected { get; private set; }
    public string StateText { get; private set; } = "";
    public string ToolTip => !IsEnabled
        ? StateText == "Operation in progress"
            ? "Unavailable while an operation is running. Wait for it to finish or cancel it before changing screens."
            : "Unavailable until a project is open" + (Destination == ShellDestination.Results ? " or a homology search has completed" : "")
                + ". Open a project with File > Open to continue."
        : Destination switch
        {
            ShellDestination.Build => "Opens Build families to choose a database and sequence identity. Save, Discard or Cancel protects edits before replacing the open project.",
            ShellDestination.Families => "Shows the open families for curation, or offers Open project and Build families if none is open. Opening this screen makes no curation edit.",
            ShellDestination.Quantify => "Opens Quantify to choose identification packages and RAW files for Full XIC. Opening this screen computes nothing.",
            ShellDestination.Results => "Shows figures and tables from stored quantitation or completed homology results. Opening Results does not regroup families or read RAW files.",
            ShellDestination.DeNovo => "Opens optional filtering and comparison of de novo calls with identification evidence. Opening this screen changes no families or stored quantitation.",
            _ => "Returns to Home while keeping the open project and unsaved edits. Opening Home changes no project data."
        };
    /// <summary>Alt and the step number, as Home numbers the steps.</summary>
    public string Shortcut => "Alt+" + Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public event PropertyChangedEventHandler? PropertyChanged;
    public ShellNavigationItem(ShellDestination destination, int number, string label) { Destination = destination; Number = number; Label = label; }
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
    public bool HasHomologyResult { get; private set; }
    /// <summary>The five steps in Home's order; Build families asks before replacing an open project.</summary>
    public IReadOnlyList<ShellNavigationItem> Items { get; } = new[]
    {
        new ShellNavigationItem(ShellDestination.Build, 1, WorkflowNames.BuildFamilies),
        new ShellNavigationItem(ShellDestination.Families, 2, WorkflowNames.CurateFamilies),
        new ShellNavigationItem(ShellDestination.Quantify, 3, WorkflowNames.Quantify),
        new ShellNavigationItem(ShellDestination.Results, 4, WorkflowNames.Results),
        new ShellNavigationItem(ShellDestination.DeNovo, 5, WorkflowNames.DeNovoReview)
    };
    public event Action<ShellDestination>? NavigationRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ShellViewModel() => Refresh();
    public bool CanNavigate(ShellDestination destination) => Enum.IsDefined(destination) && !IsBusy &&
        (HasProject || destination == ShellDestination.Results && HasHomologyResult
            || destination is not (ShellDestination.Quantify or ShellDestination.Results));
    public void SetHomologyState(bool available) { HasHomologyResult = available; Refresh(); }
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
