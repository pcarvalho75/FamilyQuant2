using System;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant;

public enum UnsavedProjectChoice { Save, Discard, Cancel }

/// <summary>Saved-state bookkeeping only. The fingerprint observes persisted curation without changing it.</summary>
public sealed class ShellDocumentState : INotifyPropertyChanged
{
    private FamilyClusterFile? document;
    private string? savedFingerprint;
    private long contentRevision, savedRevision;
    public bool IsModified { get; private set; }
    public string? ProjectPath { get; private set; }
    public string SaveCheckText { get; private set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public static string Fingerprint(FamilyClusterFile value) => value.JsonContentSha256();

    public void Open(FamilyClusterFile value, string? path)
    {
        document = value;
        ProjectPath = path;
        savedFingerprint = Fingerprint(value);
        contentRevision = savedRevision = 0;
        IsModified = false;
        SaveCheckText = "";
        Changed();
    }
    public void Clear()
    {
        document = null;
        savedFingerprint = null;
        contentRevision = savedRevision = 0;
        ProjectPath = null;
        IsModified = false;
        SaveCheckText = "";
        Changed();
    }
    public void MarkModified()
    {
        contentRevision++;
        IsModified = true;
        Changed();
    }
    public void MarkCurationChanged()
    {
        IsModified = true;
        Changed();
    }
    public bool CheckForChanges(FamilyClusterFile value)
    {
        if (document == null) Open(value, null);
        else document = value;
        IsModified = contentRevision != savedRevision || Fingerprint(value) != savedFingerprint;
        Changed();
        return IsModified;
    }
    public void Saved(FamilyClusterFile value, string path, string checkText)
    {
        document = value;
        ProjectPath = path;
        savedFingerprint = Fingerprint(value);
        savedRevision = contentRevision;
        IsModified = false;
        SaveCheckText = checkText;
        Changed();
    }
}
