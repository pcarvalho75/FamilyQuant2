using FamilyQuant.Setup.Engine;

namespace FamilyQuant.Setup;

public sealed class SetupController : ISetupController
{
    private readonly ISetupPlatform platform;
    private readonly SetupEngine engine;
    private readonly SetupPayload payload;
    public SetupOptions Options { get; }
    public string DefaultDirectory => Options.Directory ?? platform.GetRegistration()?.Directory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", SetupProduct.Id);
    public string PayloadVersion => payload.Manifest.Version;
    public SetupController(SetupOptions options) : this(options, SetupPayload.Embedded(), Environment.ProcessPath
        ?? throw new IOException("Setup cannot find its own executable."), options.TestMode
        ? new TestSetupPlatform(options.TestRoot ?? throw new IOException("Test mode needs /test-root=<temporary folder>.")) : new WindowsSetupPlatform()) { }
    public SetupController(SetupOptions options, SetupPayload payload, string setupExecutable, ISetupPlatform platform)
    {
        Options = options; this.platform = platform; this.payload = payload;
        engine = new(platform, setupExecutable, options.TestMode ? options.TestRoot : null);
        if (options.TestMode && (string.IsNullOrWhiteSpace(options.Directory) || string.IsNullOrWhiteSpace(options.TestRoot)))
            throw new IOException("Test mode needs explicit /dir and /test-root paths.");
        if (options.TestMode) _ = engine.ValidateTarget(options.Directory!);
    }
    public ExistingInstallation? DetectExisting() => engine.DetectExisting(DefaultDirectory);
    public bool IsAppRunning(string directory) => platform.IsAppRunning(directory);
    public Task<SetupResult> InstallAsync(string directory, bool desktopShortcut, IProgress<SetupProgress>? progress = null, CancellationToken token = default,
        bool allowOlderVersion = false)
        => Task.Run(() => Run(directory, false, () =>
        {
            if (allowOlderVersion && Options.Silent) throw new IOException("Silent setup cannot install an older version.");
            engine.Install(payload, directory, desktopShortcut || (Options.Silent && platform.GetRegistration()?.DesktopShortcut == true),
                progress, token, allowOlderVersion);
        }, token), CancellationToken.None);
    public Task<SetupResult> UninstallAsync(string directory, IProgress<SetupProgress>? progress = null, CancellationToken token = default)
        => Task.Run(() => Run(directory, true, () => engine.Uninstall(directory, progress, token), token), CancellationToken.None);
    public void Launch(string directory) => platform.Launch(directory);
    private SetupResult Run(string directory, bool uninstall, Action operation, CancellationToken token)
    {
        try
        {
            if (Options.Silent && Options.WaitPid is int processId && !platform.WaitForExit(processId, TimeSpan.FromSeconds(60), token))
                throw new SetupBusyException("FamilyQuant did not close within 60 seconds. Close it and try again.");
            operation();
            if (!uninstall && Options.Silent && Options.WaitPid != null)
            {
                try { FamilyQuant.Deployment.UpdateCompletionMarker.Write(directory, PayloadVersion); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Trace.WriteLine("Update completion notice could not be saved: " + error.Message);
                }
                try
                {
                    platform.Launch(directory);
                }
                catch (Exception error)
                {
                    return new(false, 4, "FamilyQuant was installed but could not reopen: " + error.Message + " Open FamilyQuant from its shortcut.", directory);
                }
            }
            return new(true, 0, uninstall ? "Program removed. Your user data and projects were kept." : "FamilyQuant is ready to use.", directory);
        }
        catch (OperationCanceledException) { return new(false, 1, "Setup cancelled. Previous program files were kept.", directory); }
        catch (SetupBusyException error) { return new(false, 3, error.Message, directory); }
        catch (Exception error) { return new(false, 1, error.Message, directory); }
    }
}
