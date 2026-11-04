using FamilyQuant.Setup;
using FamilyQuant.Setup.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SetupCommands() =>
    [new("--test-setup", 10496, TestCommandPhase.Raw, args => args.Length is 1 or 2 && args[0] == "--test-setup", (args, _) =>
    {
        SetupTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "setup-tests"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private sealed class SetupFake(string root) : ISetupPlatform
    {
        internal InstallRegistration? Entry;
        internal bool StartMenu, Desktop, Running, WaitExited = true, ThrowRegister, ThrowUnregister;
        internal int RegisterCount, WaitCount, WaitSeconds;
        internal Version? PortableVersion;
        public string UserDataDirectory => Path.Combine(root, "user-data");
        public InstallRegistration? GetRegistration() => Entry;
        public object CaptureIntegration() => (Entry, StartMenu, Desktop);
        public void RestoreIntegration(object value) => (Entry, StartMenu, Desktop) = ((InstallRegistration?, bool, bool))value;
        public void Register(InstallRegistration value, bool desktop)
        { RegisterCount++; Entry = value with { DesktopShortcut = desktop }; StartMenu = true; Desktop = desktop; if (ThrowRegister) throw new IOException("Synthetic integration failure"); }
        public void Unregister(string directory)
        { Entry = null; StartMenu = false; Desktop = false; if (ThrowUnregister) throw new IOException("Synthetic removal failure"); }
        public bool IsAppRunning(string directory) => Running;
        public bool WaitForExit(int processId, TimeSpan timeout, CancellationToken token)
        { token.ThrowIfCancellationRequested(); WaitCount++; WaitSeconds = (int)timeout.TotalSeconds; return WaitExited; }
        public Version? ReadPortableVersion(string path) => File.Exists(path) ? PortableVersion : null;
        public void Launch(string directory) => throw new InvalidOperationException("A setup test must not launch an application.");
    }

    private static SetupPayload SetupFixturePayload(string version = "2.1.3.0", Dictionary<string, string>? contents = null)
    {
        contents ??= new(StringComparer.Ordinal) { [SetupProduct.AppExe] = "new program " + version, ["lib/runtime.dll"] = "new library" };
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var item in contents)
            { using var writer = new StreamWriter(zip.CreateEntry(item.Key).Open(), new UTF8Encoding(false)); writer.Write(item.Value); }
        byte[] bytes = memory.ToArray();
        var files = contents.Select(item => new PayloadFile(item.Key, Encoding.UTF8.GetByteCount(item.Value),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Value))))).ToArray();
        return new(new() { Version = version, ZipSha256 = Convert.ToHexString(SHA256.HashData(bytes)), Files = files }, () => new MemoryStream(bytes, false));
    }

    private static void SetupTests(string output)
    {
        Section("SDK-only setup engine, temporary files and fake machine integration");
        Directory.CreateDirectory(output);
        string source = Path.Combine(output, "setup-source.bin"); File.WriteAllText(source, "Synthetic self-contained setup bytes");
        (SetupEngine Engine, SetupFake Platform, string Target, string Data) Case(string name)
        {
            string root = Path.Combine(output, name), target = Path.Combine(root, "app");
            Directory.CreateDirectory(root);
            var platform = new SetupFake(root);
            Directory.CreateDirectory(platform.UserDataDirectory);
            string data = Path.Combine(platform.UserDataDirectory, "kept.fqproj"); File.WriteAllText(data, "user data");
            return (new(platform, source, root), platform, target, data);
        }
        var item = Case("install");
        item.Engine.Install(SetupFixturePayload(), item.Target, true);
        Check("setup extracts exact payload and records fake Start menu desktop and uninstall entry", File.ReadAllText(Path.Combine(item.Target, SetupProduct.AppExe)) == "new program 2.1.3.0"
            && item.Platform is { StartMenu: true, Desktop: true, Entry.Version: "2.1.3.0" }
            && File.Exists(Path.Combine(item.Target, SetupProduct.Uninstaller)) && File.Exists(Path.Combine(item.Target, SetupProduct.Manifest)));
        Directory.CreateDirectory(Path.Combine(item.Target, "personal-empty"));
        File.WriteAllText(Path.Combine(item.Target, "personal.fqproj"), "personal project");
        item.Engine.Uninstall(item.Target);
        Check("setup uninstall removes exactly installed files and fake integration while keeping user data and unlisted files", !File.Exists(Path.Combine(item.Target, SetupProduct.AppExe))
            && !File.Exists(Path.Combine(item.Target, SetupProduct.Uninstaller)) && !Directory.Exists(Path.Combine(item.Target, "lib"))
            && File.ReadAllText(item.Data) == "user data" && File.ReadAllText(Path.Combine(item.Target, "personal.fqproj")) == "personal project"
            && Directory.Exists(Path.Combine(item.Target, "personal-empty")) && item.Platform.Entry == null && !item.Platform.StartMenu && !item.Platform.Desktop);

        item = Case("upgrade");
        item.Engine.Install(SetupFixturePayload("2.1.2.0"), item.Target, false);
        File.WriteAllText(Path.Combine(item.Target, "notes.txt"), "keep notes");
        item.Engine.Install(SetupFixturePayload(), item.Target, true);
        Check("setup upgrades older files in place without changing user data settings or unlisted files",
            File.ReadAllText(Path.Combine(item.Target, SetupProduct.AppExe)) == "new program 2.1.3.0" && File.ReadAllText(item.Data) == "user data"
            && File.ReadAllText(Path.Combine(item.Target, "notes.txt")) == "keep notes" && item.Platform.Entry?.Version == "2.1.3.0");
        Check("setup refuses a downgrade and preserves the current installation", Throws<IOException>(() => item.Engine.Install(SetupFixturePayload("2.1.1.0"), item.Target, false))
            && item.Platform.Entry?.Version == "2.1.3.0" && File.ReadAllText(Path.Combine(item.Target, SetupProduct.AppExe)) == "new program 2.1.3.0");

        item = Case("portable"); Directory.CreateDirectory(item.Target);
        File.WriteAllText(Path.Combine(item.Target, SetupProduct.AppExe), "older portable executable"); item.Platform.PortableVersion = new(2, 1, 2, 0);
        File.WriteAllText(Path.Combine(item.Target, "saved-project.fqproj"), "portable user project");
        item.Engine.Install(SetupFixturePayload(), item.Target, false);
        Check("setup adopts a recognized older portable folder and preserves its personal files", item.Engine.DetectExisting(item.Target)?.HasManifest == true
            && File.ReadAllText(Path.Combine(item.Target, "saved-project.fqproj")) == "portable user project");

        item = Case("running"); item.Platform.Running = true;
        Check("setup running-app guard changes no program files or entries", Throws<SetupBusyException>(() => item.Engine.Install(SetupFixturePayload(), item.Target, false))
            && !Directory.Exists(item.Target) && item.Platform.RegisterCount == 0 && File.ReadAllText(item.Data) == "user data");
        item = Case("foreign"); Directory.CreateDirectory(item.Target); File.WriteAllText(Path.Combine(item.Target, "foreign.txt"), "foreign");
        Check("setup refuses an unrelated nonempty folder", Throws<IOException>(() => item.Engine.Install(SetupFixturePayload(), item.Target, false))
            && File.ReadAllText(Path.Combine(item.Target, "foreign.txt")) == "foreign" && item.Platform.RegisterCount == 0);
        Check("setup refuses user-data folders and targets outside test root before writing", Throws<IOException>(() => item.Engine.Install(SetupFixturePayload(), Path.GetDirectoryName(item.Data)!, false))
            && Throws<IOException>(() => item.Engine.Install(SetupFixturePayload(), Path.Combine(output, "outside"), false)) && !Directory.Exists(Path.Combine(output, "outside")));

        item = Case("rollback"); item.Engine.Install(SetupFixturePayload("2.1.2.0"), item.Target, false); item.Platform.ThrowRegister = true;
        Check("setup restores old files and fake integration if registration fails", Throws<IOException>(() => item.Engine.Install(SetupFixturePayload(), item.Target, true))
            && File.ReadAllText(Path.Combine(item.Target, SetupProduct.AppExe)) == "new program 2.1.2.0" && item.Platform.Entry?.Version == "2.1.2.0"
            && item.Platform.StartMenu && !item.Platform.Desktop && File.ReadAllText(item.Data) == "user data");
        item.Platform.ThrowRegister = false; item.Platform.ThrowUnregister = true;
        Check("setup restores files and fake entry if uninstall integration fails", Throws<IOException>(() => item.Engine.Uninstall(item.Target))
            && File.ReadAllText(Path.Combine(item.Target, SetupProduct.AppExe)) == "new program 2.1.2.0" && item.Platform.Entry?.Version == "2.1.2.0");
        item.Platform.ThrowUnregister = false; File.WriteAllText(Path.Combine(item.Target, "lib", "runtime.dll"), "locally changed"); item.Engine.Uninstall(item.Target);
        Check("setup uninstall keeps locally changed owned files", File.ReadAllText(Path.Combine(item.Target, "lib", "runtime.dll")) == "locally changed" && !File.Exists(Path.Combine(item.Target, SetupProduct.AppExe)));

        item = Case("unsafe");
        var bad = SetupFixturePayload(contents: new() { [SetupProduct.AppExe] = "program", ["../escaped.txt"] = "bad" });
        Check("setup refuses ZIP traversal before publication", Throws<IOException>(() => item.Engine.Install(bad, item.Target, false)) && !File.Exists(Path.Combine(output, "escaped.txt")) && !Directory.Exists(item.Target));
        bad = SetupFixturePayload(contents: new() { [SetupProduct.AppExe] = "program", ["LIB/a.dll"] = "one", ["lib/A.dll"] = "two" });
        Check("setup rejects case-colliding payload files", Throws<IOException>(() => item.Engine.Install(bad, item.Target, false)) && !Directory.Exists(item.Target));
        bad = SetupFixturePayload(); bad = new(bad.Manifest with { ZipSha256 = new string('0', 64) }, () => new MemoryStream(new byte[] { 1, 2, 3 }));
        Check("setup refuses a checksum mismatch without publishing", Throws<IOException>(() => item.Engine.Install(bad, item.Target, false)) && !Directory.Exists(item.Target));

        item = Case("cancel"); using var cancel = new CancellationTokenSource();
        Check("setup cancellation before commit keeps previous state", Throws<OperationCanceledException>(() => item.Engine.Install(SetupFixturePayload(), item.Target, false,
            new SetupProgressAction(_ => cancel.Cancel()), cancel.Token)) && !Directory.Exists(item.Target) && item.Platform.Entry == null);

        item = Case("quiet-wait"); item.Platform.WaitExited = false;
        var options = new SetupOptions { Silent = true, WaitPid = 42, TestMode = true, TestRoot = Path.GetDirectoryName(item.Target), Directory = item.Target };
        var controller = new SetupController(options, SetupFixturePayload(), source, item.Platform);
        var result = controller.InstallAsync(item.Target, false).GetAwaiter().GetResult();
        Check("quiet waitpid timeout uses60seconds and changes nothing", !result.Success && result.ExitCode == 3 && item.Platform.WaitCount == 1
            && item.Platform.WaitSeconds == 60 && !Directory.Exists(item.Target) && item.Platform.Entry == null && File.ReadAllText(item.Data) == "user data");
        item.Platform.WaitExited = true; result = controller.InstallAsync(item.Target, false).GetAwaiter().GetResult();
        Check("quiet waitpid success proceeds through the running-app guard and installs", result.Success && result.ExitCode == 0 && File.Exists(Path.Combine(item.Target, SetupProduct.AppExe)));
        item.Platform.Running = true; result = controller.InstallAsync(item.Target, false).GetAwaiter().GetResult();
        Check("quiet mode still refuses a running app after the waited process exits", result.ExitCode == 3 && item.Platform.Entry?.Version == "2.1.3.0");
        Check("setup command parser is available for the updater and safe test-mode CLI", typeof(SetupOptions).Assembly.GetType("FamilyQuant.Setup.SetupCommandLine") != null);
        item = Case("quiet-desktop"); item.Engine.Install(SetupFixturePayload("2.1.2.0"), item.Target, true);
        options = new() { Silent = true, TestMode = true, TestRoot = Path.GetDirectoryName(item.Target), Directory = item.Target };
        controller = new(options, SetupFixturePayload(), source, item.Platform);
        result = controller.InstallAsync(item.Target, false).GetAwaiter().GetResult();
        Check("quiet upgrade keeps the existing desktop shortcut preference", result.Success && item.Platform.Desktop);
        var parsed = SetupCommandLine.Parse(new[] { "/silent", "/test-mode", "/test-root=" + options.TestRoot, "/dir=" + item.Target, "/waitpid=42" });
        Check("setup CLI parses the closed silent directory waitpid contract", parsed.Silent && parsed.TestMode && parsed.Directory == item.Target && parsed.WaitPid == 42);
        Check("setup CLI refuses unknown duplicate and malformed process options", Throws<ArgumentException>(() => SetupCommandLine.Parse(new[] { "/silent", "/silent" }))
            && Throws<ArgumentException>(() => SetupCommandLine.Parse(new[] { "/silent", "/waitpid=0" }))
            && Throws<ArgumentException>(() => SetupCommandLine.Parse(new[] { "/waitpid=42" }))
            && Throws<ArgumentException>(() => SetupCommandLine.Parse(new[] { "/payload=sidecar.zip" })));
        Check("setup test-mode CLI requires explicit temp root and confined directory", Throws<ArgumentException>(() => SetupCommandLine.Parse(new[] { "/test-mode" }))
            && Throws<ArgumentException>(() => SetupCommandLine.Parse(new[] { "/test-mode", "/test-root=" + options.TestRoot, "/dir=" + output })));
    }
    private sealed class SetupProgressAction(Action<SetupProgress> report) : IProgress<SetupProgress> { public void Report(SetupProgress value) => report(value); }
}
