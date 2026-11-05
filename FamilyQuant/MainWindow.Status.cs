using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Controls;

namespace FamilyQuant;

/// <summary>Formats supplied presentation state. It calculates no scientific value.</summary>
public sealed class ShellStatusViewModel : INotifyPropertyChanged
{
    private string stage = WorkflowNames.Home, project = "No project open", activity = "";
    private string contextBase = "";
    public string Text => string.Join(" | ", new[] { stage, project, activity }).Trim(' ', '|');
    public string ContextHelp { get; private set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    public void Update(string nextStage, string nextProject, string nextActivity, string context)
    {
        stage = OneLine(nextStage);
        project = OneLine(nextProject);
        activity = OneLine(nextActivity);
        contextBase = context;
        ContextHelp = context;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContextHelp)));
    }

    public void ShowMessage(string message)
    {
        activity = OneLine(message.Split('\n')[0]);
        ContextHelp = contextBase + "\n\n" + message;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContextHelp)));
    }
}

public partial class MainWindow
{
    public ShellStatusViewModel StatusModel { get; } = new();
    private object? statusSource;
    private EventInfo? statusEvent;
    private EventHandler? statusChanged;
    private bool updatingStatus;

    // LK's additive contract: string ShellStatusText, EventHandler ShellStatusChanged,
    // bool UseShellStatusBar. Reflection permits the two lanes to merge in either order.
    // The source computes its own status; the shell copies it verbatim.
    private void AttachStatusSource(object? source)
    {
        if (ReferenceEquals(source, statusSource)) return;
        if (statusSource != null && statusChanged != null) statusEvent?.RemoveEventHandler(statusSource, statusChanged);
        statusSource = source;
        statusEvent = source?.GetType().GetEvent("ShellStatusChanged");
        statusChanged = (_, _) => { if (!updatingStatus) UpdateGuide(); };
        statusEvent?.AddEventHandler(source, statusChanged);
        source?.GetType().GetProperty("UseShellStatusBar")?.SetValue(source, true);
    }

    private string SourceStatus(string property) => statusSource?.GetType().GetProperty(property)?.GetValue(statusSource) as string ?? "";

    private void UpdateGuide()
    {
        if (updatingStatus) return;
        updatingStatus = true;
        try
        {
            RefreshShellMenuState();
            bool families = cgc != null && GridApp.Children.Contains(cgc);
            RefreshNavigationState();
            NavigationModel.SetCurrent(IsQuantifyVisible ? ShellDestination.Quantify : home != null ? ShellDestination.Start : analytics != null ? ShellDestination.Results
                : dnc != null ? ShellDestination.DeNovo : families ? ShellDestination.Families
                : usearch != null ? ShellDestination.Build : ShellDestination.Start);
            AttachStatusSource(usearch != null && GridApp.Children.Contains(usearch) ? usearch : families ? cgc : null);
            string stage = IsQuantifyVisible ? "Quantify" : home != null ? WorkflowNames.Home : analytics != null ? "Results" : dnc != null ? "De novo"
                : families ? "Families" : usearch != null ? "Build families" : WorkflowNames.Home;
            string project = cgc != null ? Path.GetFileName(lastFcfPath) ?? "Open project" : "No project open";
            if (cgc != null && DocumentState.IsModified) project += " *";
            string activity = IsQuantifyVisible ? quantify!.ViewModel.Status : SourceStatus("ShellStatusText");
            if (activity.Length == 0 && families && cgc?.FindName("TextBlockStatusLine") is TextBlock legacy) activity = legacy.Text;
            if (activity.Length == 0 && home != null)
                activity = cgc == null ? "Use Open project or " + WorkflowNames.BuildFamilies + " to begin." : "Use Families to curate the open project, Quantify to compute, or Results to inspect stored results.";
            if (activity.StartsWith("showing: ", StringComparison.OrdinalIgnoreCase)) activity = activity[9..];
            string context = CurrentContextHelp() + "\n\n" + ProjectStatusText();
            string sourceHelp = SourceStatus("ShellHelpText");
            if (sourceHelp.Length > 0) context += "\n\n" + sourceHelp;
            if (activity.Length > 0) context += "\n\nCurrent screen status: " + activity;
            StatusModel.Update(stage, project, activity, context);
        }
        finally { updatingStatus = false; }
    }
}
