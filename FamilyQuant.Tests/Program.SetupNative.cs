using FamilyQuant.Setup;
using FamilyQuant.Setup.Engine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SetupNativeCommands() =>
    [new("--test-setup-native", 10498, TestCommandPhase.Raw,
        args => args.Length == 4 && args[0] == "--test-setup-native", (args, _) =>
        {
            SetupNativeTests(args[1], args[2], args[3]);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void SetupNativeTests(string setupExe, string root, string expectedVersion)
    {
        root = SetupPaths.FullDirectory(root); setupExe = Path.GetFullPath(setupExe);
        var platform = new TestSetupPlatform(root); // Validates trusted LocalAppData Temp, without writes.
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Use a fresh temporary proof folder.");
        Directory.CreateDirectory(root);
        string target = Path.Combine(root, "app folder with spaces"), personal = Path.Combine(platform.UserDataDirectory, "keep.fqproj");
        Directory.CreateDirectory(platform.UserDataDirectory); File.WriteAllText(personal, "Planted user data and settings");
        string dataHash = SetupProduct.Hash(personal);
        int sequence = 0;
        void AssertMetadata(string label)
        {
            var info = FileVersionInfo.GetVersionInfo(Path.Combine(target, SetupProduct.AppExe));
            Check(label + " installed EXE file and product version", info.FileVersion == expectedVersion
                && info.ProductVersion?.StartsWith(expectedVersion, StringComparison.Ordinal) == true);
            Check(label + " installed managed assembly version without launching the app",
                AssemblyName.GetAssemblyName(Path.Combine(target, "FamilyQuant2.dll")).Version?.ToString() == expectedVersion);
        }
        (int Exit, SetupResult Result) Run(string executable, params string[] extra)
        {
            string resultPath = Path.Combine(root, "result-" + (++sequence) + ".json");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
            string temporary = Path.Combine(root, "temp"); Directory.CreateDirectory(temporary);
            start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(root, "bundle-cache");
            start.Environment["TEMP"] = temporary; start.Environment["TMP"] = temporary;
            foreach (string arg in new[] { "/silent", "/test-mode", "/test-root=" + root, "/dir=" + target, "/result=" + resultPath }.Concat(extra))
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("The owned test-mode Setup process did not start.");
            try
            {
                if (!process.WaitForExit(180000)) throw new TimeoutException("Owned test-mode Setup exceeded its timeout.");
                var wait = Stopwatch.StartNew();
                while (!File.Exists(resultPath) && wait.Elapsed < TimeSpan.FromSeconds(90)) Thread.Sleep(50);
                if (!File.Exists(resultPath)) throw new IOException("Test-mode Setup returned no result file, exit " + process.ExitCode);
                using var resultJson = JsonDocument.Parse(File.ReadAllText(resultPath));
                int workerId = resultJson.RootElement.GetProperty("processId").GetInt32();
                if (workerId != process.Id)
                    try
                    {
                        using var worker = Process.GetProcessById(workerId);
                        if (worker.MainModule?.FileName is string path && SetupPaths.Within(root, path) && !worker.WaitForExit(10000))
                            throw new TimeoutException("The owned uninstall helper did not exit.");
                    }
                    catch (ArgumentException) { }
                var result = JsonSerializer.Deserialize<SetupResult>(resultJson.RootElement.GetRawText(), SetupProduct.Json)!;
                return (process.ExitCode, result);
            }
            catch
            {
                if (!process.HasExited) process.Kill(true);
                // Only helpers executing from THIS fresh proof root are eligible for cleanup.
                foreach (var child in Process.GetProcesses().Where(p => p.ProcessName.StartsWith("FamilyQuant2", StringComparison.OrdinalIgnoreCase)))
                    using (child)
                        try { if (child.MainModule?.FileName is string path && SetupPaths.Within(root, path)) { child.Kill(true); child.WaitForExit(); } }
                        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                throw;
            }
        }
        var installed = Run(setupExe, "/desktop");
        Check("native one-file Setup test-mode install succeeds without Windows integration", installed.Exit == 0 && installed.Result.Success
            && platform.Current is { StartMenu: true, Desktop: true, Registration: not null });
        AssertMetadata("native install");
        string extraFile = Path.Combine(target, "personal-project.fqproj"); File.WriteAllText(extraFile, "unlisted personal project");
        string manifestPath = Path.Combine(target, SetupProduct.Manifest);
        var manifest = JsonSerializer.Deserialize<InstalledManifest>(File.ReadAllText(manifestPath), SetupProduct.Json)!;
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { Version = "2.1.2.0" }, SetupProduct.Json));
        platform.SetState(platform.Current with { Registration = platform.Current.Registration! with { Version = "2.1.2.0" } });
        File.WriteAllText(Path.Combine(target, SetupProduct.AppExe), "Older fake program files");
        var upgraded = Run(setupExe);
        Check("native silent upgrade over older fake install preserves data settings and desktop preference", upgraded.Exit == 0 && upgraded.Result.Success
            && platform.Current.Desktop && SetupProduct.Hash(personal) == dataHash && File.ReadAllText(extraFile) == "unlisted personal project");
        AssertMetadata("native upgrade");
        string currentManifest = File.ReadAllText(manifestPath);
        var nextVersion = Version.Parse(expectedVersion);
        string newerVersion = new Version(nextVersion.Major, nextVersion.Minor, nextVersion.Build + 1, 0).ToString();
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { Version = newerVersion }, SetupProduct.Json));
        platform.SetState(platform.Current with { Registration = platform.Current.Registration! with { Version = newerVersion } });
        string beforeDowngradeHash = SetupProduct.Hash(Path.Combine(target, SetupProduct.AppExe));
        var downgrade = Run(setupExe, "/waitpid=12345");
        Check("native silent downgrade refuses with both versions, no files changed and no launch", downgrade.Exit != 0 && !downgrade.Result.Success
            && downgrade.Result.Message == SetupVersions.AlreadyInstalled(newerVersion, expectedVersion)
            && SetupProduct.Hash(Path.Combine(target, SetupProduct.AppExe)) == beforeDowngradeHash && platform.Current.LaunchCount == 0);
        File.WriteAllText(manifestPath, currentManifest);
        platform.SetState(platform.Current with { Registration = platform.Current.Registration! with { Version = expectedVersion } });
        platform.SetState(platform.Current with { Running = true });
        string appHash = SetupProduct.Hash(Path.Combine(target, SetupProduct.AppExe));
        var running = Run(setupExe);
        Check("native quiet test-mode running guard refuses before file or entry changes", running.Exit == 3 && running.Result.ExitCode == 3
            && SetupProduct.Hash(Path.Combine(target, SetupProduct.AppExe)) == appHash && platform.Current.Running && SetupProduct.Hash(personal) == dataHash);
        platform.SetState(platform.Current with { Running = false, WaitExited = false });
        var timeout = Run(setupExe, "/waitpid=12345");
        Check("native quiet waitpid uses fake timeout and preserves installation", timeout.Exit == 3 && timeout.Result.ExitCode == 3
            && SetupProduct.Hash(Path.Combine(target, SetupProduct.AppExe)) == appHash && SetupProduct.Hash(personal) == dataHash);
        platform.SetState(platform.Current with { WaitExited = true });
        var waited = Run(setupExe, "/waitpid=12345");
        Check("native quiet waitpid update records exactly one fake launch", waited.Exit == 0 && waited.Result.Success && platform.Current.LaunchCount == 1);
        var removed = Run(Path.Combine(target, SetupProduct.Uninstaller), "/uninstall");
        Check("native installed uninstaller relocates and removes all recorded program files", removed.Exit == 0 && removed.Result.Success
            && manifest.Files.All(file => !File.Exists(SetupPaths.FileIn(target, file.Path))) && !File.Exists(manifestPath));
        Check("native uninstall retains planted data and unlisted project, removes fake shortcut and uninstall entries", SetupProduct.Hash(personal) == dataHash
            && File.ReadAllText(extraFile) == "unlisted personal project" && platform.Current is { Registration: null, StartMenu: false, Desktop: false, LaunchCount: 1 });
        File.WriteAllText(Path.Combine(root, "native-setup-proof.json"), JsonSerializer.Serialize(new
        {
            Setup = setupExe, SetupSha256 = SetupProduct.Hash(setupExe), ExpectedVersion = expectedVersion, TestMode = true,
            WindowsIntegration = "file-backed fake only", AppStarted = false, UserDataSha256 = dataHash, passed, failed,
        }, SetupProduct.Json));
        Console.WriteLine("Native Setup proof root (test mode only): " + root);
    }
}
