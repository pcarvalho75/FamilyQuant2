using System.Security.Cryptography;
using System.Text.Json;

namespace FamilyQuant.Setup.Engine;

public static class SetupProduct
{
    public const string Id = "FamilyQuant2";
    public const string AppExe = "FamilyQuant2.exe";
    public const string Uninstaller = "FamilyQuant2-Uninstall.exe";
    public const string Manifest = ".familyquant-install.json";
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}

public sealed record PayloadFile(string Path, long Length, string Sha256);
public sealed record PayloadManifest
{
    public int Schema { get; init; } = 1;
    public string ProductId { get; init; } = SetupProduct.Id;
    public string Version { get; init; } = "";
    public string ZipSha256 { get; init; } = "";
    public IReadOnlyList<PayloadFile> Files { get; init; } = Array.Empty<PayloadFile>();
}
public sealed record InstalledManifest
{
    public int Schema { get; init; } = 1;
    public string ProductId { get; init; } = SetupProduct.Id;
    public string Directory { get; init; } = "";
    public string Version { get; init; } = "";
    public IReadOnlyList<PayloadFile> Files { get; init; } = Array.Empty<PayloadFile>();
    public IReadOnlyList<string> Directories { get; init; } = Array.Empty<string>();
}
public sealed record InstallRegistration(string Directory, string Version, long SizeBytes)
{
    public bool DesktopShortcut { get; init; }
}

/// <summary>Only the production adapter touches HKCU, shell shortcuts, other processes or Launch.
/// Test mode uses a persistent file-backed fake. The engine itself changes only validated files.</summary>
public interface ISetupPlatform
{
    string UserDataDirectory { get; }
    InstallRegistration? GetRegistration();
    object CaptureIntegration();
    void RestoreIntegration(object snapshot);
    void Register(InstallRegistration registration, bool desktopShortcut);
    void Unregister(string directory);
    bool IsAppRunning(string directory);
    bool WaitForExit(int processId, TimeSpan timeout, CancellationToken cancellationToken);
    Version? ReadPortableVersion(string appPath);
    void Launch(string directory);
}

public sealed class SetupBusyException(string message) : IOException(message);
public sealed class SetupRecoveryException(string directory, Exception cause)
    : IOException("Setup could not fully restore the previous files. Recovery files remain in " + directory + ".", cause);
