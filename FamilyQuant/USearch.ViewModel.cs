using System;
using System.ComponentModel;
using System.IO;

namespace FamilyQuant;

/// <summary>Build-screen parameters and readiness. Numerical work remains in the existing Core services.</summary>
public sealed class BuildFamiliesViewModel : INotifyPropertyChanged
{
    private string databasePath = "";
    private int identity = 85, redundancyIdentity = 100;
    private decimal maximumMixing;
    private bool exhaustive = true, busy, hasResult;
    public string DatabasePath
    {
        get => databasePath;
        set
        {
            string next = value ?? "";
            if (!string.Equals(next, databasePath, StringComparison.OrdinalIgnoreCase))
            {
                hasResult = false;
                ResultSummary = Details = "";
                databasePath = next;
                Status = HasDatabase ? "Ready to build." : "Choose a database to begin.";
            }
            Changed();
        }
    }
    public int Identity { get => identity; set { identity = value; Changed(); } }
    public int RedundancyIdentity { get => redundancyIdentity; set { redundancyIdentity = value; Changed(); } }
    public decimal MaximumMixing { get => maximumMixing; set { maximumMixing = value; Changed(); } }
    public bool Exhaustive { get => exhaustive; set { exhaustive = value; Changed(); } }
    public bool IsBusy => busy;
    public bool HasDatabase => File.Exists(databasePath);
    public bool CanRun => !busy && HasDatabase;
    public bool CanScore => !busy && hasResult;
    public string Status { get; private set; } = "Choose a database to begin.";
    public string ResultSummary { get; private set; } = "";
    public string Details { get; private set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public bool TryBegin(string message)
    {
        if (busy) return false;
        busy = true;
        Status = message;
        Changed();
        return true;
    }
    public void End(bool generated)
    {
        busy = false;
        hasResult = generated;
        Status = generated ? "Families written." : HasDatabase ? "Ready." : "Choose a database to begin.";
        Changed();
    }
    public void Report(string summary, string details = "")
    {
        Status = summary.Replace('\r', ' ').Replace('\n', ' ');
        ResultSummary = Status;
        Details = details;
        Changed();
    }
}
