using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Setup.Engine;

/// <summary>Files are staged beside the target and swapped only after full checksum validation.
/// Integration failure restores the old directory and the captured registry/shortcut state.</summary>
public sealed class SetupEngine(ISetupPlatform platform, string setupExecutable, string? testRoot = null)
{
    public ExistingInstallation? DetectExisting(string directory)
    {
        string target = ValidateTarget(directory);
        var stored = ReadManifest(target);
        if (stored != null) return new(target, stored.Version, platform.GetRegistration()?.Directory is string registered
            && SetupPaths.Same(registered, target), true);
        var portable = platform.ReadPortableVersion(Path.Combine(target, SetupProduct.AppExe));
        return portable == null ? null : new(target, portable.ToString(), false, false);
    }

    public string ValidateTarget(string directory)
    {
        string target = SetupPaths.FullDirectory(directory);
        string data = Path.GetFullPath(platform.UserDataDirectory);
        if (SetupPaths.Within(data, target) || SetupPaths.Within(target, data))
            throw new IOException("Choose a program folder separate from your user data.");
        if (testRoot != null && (!SetupPaths.Within(testRoot, target) || SetupPaths.Same(testRoot, target)))
            throw new IOException("Test mode writes only inside its selected temporary root.");
        return target;
    }

    public void Install(SetupPayload payload, string directory, bool desktop, IProgress<SetupProgress>? progress = null,
        CancellationToken token = default)
    {
        string target = ValidateTarget(directory);
        if (SetupPaths.Within(target, setupExecutable)) throw new IOException("Run Setup from outside the program folder.");
        if (payload.Manifest.ProductId != SetupProduct.Id || payload.Manifest.Schema != 1 || !Version.TryParse(payload.Manifest.Version, out var version))
            throw new IOException("The package version or identity is invalid.");
        using var gate = Acquire(target);
        RequireStopped(target);
        var registered = platform.GetRegistration();
        if (registered != null && !SetupPaths.Same(registered.Directory, target))
            throw new IOException("Use the existing program folder, or uninstall the previous installation first.");
        var previous = ReadManifest(target);
        var existing = DetectExisting(target);
        bool hadTarget = Directory.Exists(target);
        if (hadTarget)
        {
            _ = SetupPaths.Files(target).Count();
            if (existing == null && Directory.EnumerateFileSystemEntries(target).Any())
                throw new IOException("The selected folder contains files from an unrecognized installation. Choose an empty folder.");
            if (existing != null && Version.Parse(existing.Version) > version)
                throw new IOException("A newer version is already installed. Keep it or uninstall it first.");
        }
        string parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, ".FamilyQuant2-stage-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(parent, ".FamilyQuant2-backup-" + Guid.NewGuid().ToString("N"));
        bool recovery = false, oldMoved = false, newMoved = false;
        Directory.CreateDirectory(stage);
        try
        {
            payload.Extract(stage, progress, token);
            if (hadTarget) PreserveUnknown(target, stage, previous, payload.Manifest.Files);
            File.Copy(setupExecutable, Path.Combine(stage, SetupProduct.Uninstaller));
            var files = payload.Manifest.Files.Append(new PayloadFile(SetupProduct.Uninstaller, new FileInfo(setupExecutable).Length,
                SetupProduct.Hash(setupExecutable))).ToArray();
            var installed = new InstalledManifest { Directory = target, Version = payload.Manifest.Version, Files = files,
                Directories = payload.Manifest.Files.SelectMany(file => ParentDirectories(file.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
            File.WriteAllText(Path.Combine(stage, SetupProduct.Manifest), JsonSerializer.Serialize(installed, SetupProduct.Json), new UTF8Encoding(false));
            token.ThrowIfCancellationRequested();
            RequireStopped(target);
            object snapshot = platform.CaptureIntegration();
            progress?.Report(new("Installing program files", 0, 1));
            try
            {
                if (hadTarget) { Directory.Move(target, backup); oldMoved = true; }
                Directory.Move(stage, target); newMoved = true;
                platform.Register(new(target, payload.Manifest.Version, files.Sum(f => f.Length)), desktop);
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                try { platform.RestoreIntegration(snapshot); } catch (Exception rollback) { errors.Add(rollback); }
                try
                {
                    if (newMoved) { Directory.Move(target, stage); newMoved = false; }
                    if (oldMoved) { Directory.Move(backup, target); oldMoved = false; }
                }
                catch (Exception rollback) { errors.Add(rollback); }
                if (errors.Count > 1) { recovery = true; throw new SetupRecoveryException(parent, new AggregateException(errors)); }
                throw;
            }
            // A completed commit wins a late Cancel. Old files are now only a recoverable backup.
            try { progress?.Report(new("Installation complete", 1, 1)); } catch { /* The completed commit is authoritative. */ }
        }
        finally
        {
            if (!recovery)
            {
                Cleanup(parent, stage, ".FamilyQuant2-stage-");
                if (oldMoved) Cleanup(parent, backup, ".FamilyQuant2-backup-");
            }
        }
    }

    public void Uninstall(string directory, IProgress<SetupProgress>? progress = null, CancellationToken token = default)
    {
        string target = ValidateTarget(directory);
        using var gate = Acquire(target);
        RequireStopped(target);
        var manifest = ReadManifest(target) ?? throw new IOException("No installation file list was found in this folder.");
        _ = SetupPaths.Files(target).Count();
        string parent = Path.GetDirectoryName(target)!;
        string removed = Path.Combine(parent, ".FamilyQuant2-remove-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(removed);
        var moved = new List<string>(); bool recovery = false;
        object snapshot;
        try { snapshot = platform.CaptureIntegration(); } catch { Cleanup(parent, removed, ".FamilyQuant2-remove-"); throw; }
        try
        {
            token.ThrowIfCancellationRequested();
            RequireStopped(target);
            foreach (var file in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                string source = SetupPaths.FileIn(target, file.Path);
                // Locally changed files and unlisted files belong to the person. Leave them alone.
                if (!File.Exists(source) || !SetupProduct.Hash(source).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                MoveToRemoved(file.Path);
                progress?.Report(new("Removing " + file.Path, moved.Count, manifest.Files.Count + 1));
            }
            token.ThrowIfCancellationRequested();
            MoveToRemoved(SetupProduct.Manifest);
            platform.Unregister(target);
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            try { platform.RestoreIntegration(snapshot); } catch (Exception rollback) { errors.Add(rollback); }
            foreach (string relative in moved.AsEnumerable().Reverse())
                try
                {
                    string destination = SetupPaths.FileIn(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(SetupPaths.FileIn(removed, relative), destination);
                }
                catch (Exception rollback) { errors.Add(rollback); }
            if (errors.Count > 1) { recovery = true; throw new SetupRecoveryException(removed, new AggregateException(errors)); }
            throw;
        }
        finally { if (!recovery) Cleanup(parent, removed, ".FamilyQuant2-remove-"); }
        try { SetupPaths.RemoveEmptyDirectories(target, manifest.Directories); }
        catch (IOException) { } catch (UnauthorizedAccessException) { } // Files/integration already committed; keep busy empty folders.
        try { progress?.Report(new("Program removed. Your user data and changed files were kept.", 1, 1)); } catch { }

        void MoveToRemoved(string relative)
        {
            string path = SetupPaths.FileIn(removed, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Move(SetupPaths.FileIn(target, relative), path);
            moved.Add(relative);
        }
    }

    private InstalledManifest? ReadManifest(string target)
    {
        string path = Path.Combine(target, SetupProduct.Manifest);
        if (!File.Exists(path)) return null;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("The installation file list is a link.");
        var manifest = JsonSerializer.Deserialize<InstalledManifest>(File.ReadAllText(path), SetupProduct.Json)
            ?? throw new IOException("The installation file list is invalid.");
        if (manifest.Schema != 1 || manifest.ProductId != SetupProduct.Id || !SetupPaths.Same(manifest.Directory, target)
            || !Version.TryParse(manifest.Version, out _) || manifest.Files.Count == 0)
            throw new IOException("The installation identity or folder does not match.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
            if (!names.Add(file.Path) || file.Path.Equals(SetupProduct.Manifest, StringComparison.OrdinalIgnoreCase)
                || file.Length < 0 || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new IOException("The installation file list is unsafe.");
            else _ = SetupPaths.FileIn(target, file.Path);
        if (!names.Contains(SetupProduct.AppExe) || !names.Contains(SetupProduct.Uninstaller)) throw new IOException("The installation file list is incomplete.");
        foreach (string directory in manifest.Directories) _ = SetupPaths.FileIn(target, directory);
        return manifest;
    }
    private static void PreserveUnknown(string target, string stage, InstalledManifest? previous, IReadOnlyList<PayloadFile> next)
    {
        var owned = new HashSet<string>(previous?.Files.Select(f => f.Path) ?? next.Select(f => f.Path), StringComparer.OrdinalIgnoreCase)
            { SetupProduct.Manifest, SetupProduct.Uninstaller };
        foreach (string path in SetupPaths.Files(target))
        {
            string relative = Path.GetRelativePath(target, path).Replace('\\', '/');
            if (owned.Contains(relative)) continue;
            string destination = SetupPaths.FileIn(stage, relative);
            if (File.Exists(destination)) throw new IOException("A personal file conflicts with the new program files: " + relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination);
        }
        var oldOwnedDirectories = new HashSet<string>(previous?.Directories ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(target, path).Replace('\\', '/');
            if (!oldOwnedDirectories.Contains(relative)) Directory.CreateDirectory(SetupPaths.FileIn(stage, relative));
        }
    }
    private static IEnumerable<string> ParentDirectories(string path)
    {
        int slash = path.LastIndexOf('/');
        while (slash >= 0) { path = path[..slash]; yield return path; slash = path.LastIndexOf('/'); }
    }
    private void RequireStopped(string target)
    {
        if (platform.IsAppRunning(target)) throw new SetupBusyException("FamilyQuant is running. Close it and try again.");
    }
    private static IDisposable Acquire(string target)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.ToUpperInvariant())));
        var mutex = new Mutex(false, "Local\\FamilyQuant2-Setup-" + hash);
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { mutex.Dispose(); throw new SetupBusyException("Another setup is already using this folder."); }
        return new MutexLease(mutex);
    }
    private sealed class MutexLease(Mutex mutex) : IDisposable { public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); } }
    private static void Cleanup(string parent, string path, string prefix)
    {
        try { SetupPaths.DeleteWork(parent, path, prefix); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
