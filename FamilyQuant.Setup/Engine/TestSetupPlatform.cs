using System.Text.Json;

namespace FamilyQuant.Setup.Engine;

/// <summary>Persisted test double. It never calls Registry, WScript, Process.Start or process APIs.</summary>
public sealed class TestSetupPlatform : ISetupPlatform
{
    public sealed record State(InstallRegistration? Registration = null, bool StartMenu = false, bool Desktop = false,
        bool Running = false, bool WaitExited = true, bool Launched = false);
    private readonly string statePath;
    public string Root { get; }
    public string UserDataDirectory => Path.Combine(Root, "user-data");
    public State Current { get { CheckState(); return File.Exists(statePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(statePath), SetupProduct.Json) ?? new() : new(); } }
    public TestSetupPlatform(string root)
    {
        Root = SetupPaths.FullDirectory(root);
        string temporary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
        if (!SetupPaths.Within(temporary, Root) || SetupPaths.Same(temporary, Root))
            throw new IOException("Test mode requires a child folder under the current user's LocalAppData Temp folder.");
        statePath = Path.Combine(Root, "setup-test-state.json");
    }
    public void SetState(State state) { CheckState(); Directory.CreateDirectory(Root); File.WriteAllText(statePath, JsonSerializer.Serialize(state, SetupProduct.Json)); }
    private void CheckState()
    {
        SetupPaths.CheckAncestors(Root);
        if (File.Exists(statePath) && (File.GetAttributes(statePath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Test state must not be a file link.");
    }
    public InstallRegistration? GetRegistration() => Current.Registration;
    public object CaptureIntegration() => (File.Exists(statePath), Current);
    public void RestoreIntegration(object snapshot)
    {
        var (existed, state) = ((bool, State))snapshot;
        if (existed) SetState(state); else if (File.Exists(statePath)) File.Delete(statePath);
    }
    public void Register(InstallRegistration registration, bool desktopShortcut)
        => SetState(Current with { Registration = registration with { DesktopShortcut = desktopShortcut }, StartMenu = true, Desktop = desktopShortcut });
    public void Unregister(string directory)
    {
        if (Current.Registration is { } registration && !SetupPaths.Same(registration.Directory, directory))
            throw new IOException("The fake registration belongs to another folder.");
        SetState(Current with { Registration = null, StartMenu = false, Desktop = false });
    }
    public bool IsAppRunning(string directory) => Current.Running;
    public bool WaitForExit(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Current.WaitExited; }
    public Version? ReadPortableVersion(string appPath)
    {
        if (!File.Exists(appPath)) return null;
        var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(appPath);
        return version.ProductName == SetupProduct.Id && Version.TryParse(version.FileVersion, out var value) ? value : null;
    }
    public void Launch(string directory) => SetState(Current with { Launched = true });
}
