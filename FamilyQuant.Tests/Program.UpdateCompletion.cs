using FamilyQuant.Setup;
using FamilyQuant.Setup.Engine;
using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> UpdateCompletionCommands() =>
    [new("--test-update-completion", 10530, TestCommandPhase.Raw,
        args => args.Length == 1 && args[0] == "--test-update-completion", (_, _) =>
        {
            EnsureApplication();
            UpdateCompletionTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void UpdateCompletionTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "update-completion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "setup.bin"); File.WriteAllText(source, "synthetic setup");
        string target = Path.Combine(root, "app");
        var platform = new TestSetupPlatform(root);
        var options = new SetupOptions { Silent = true, WaitPid = 42, TestMode = true, TestRoot = root, Directory = target };
        var controller = new SetupController(options, SetupFixturePayload("2.1.7.0"), source, platform);
        var result = controller.InstallAsync(target, false).GetAwaiter().GetResult();
        string marker = Path.Combine(target, ".familyquant-update-complete");
        Check("Z2b test-mode update writes its version and records one fake launch", result.Success
            && File.ReadAllText(marker) == "2.1.7.0" && platform.Current.LaunchCount == 1);
        var main = new MainWindow();
        Check("Z2b first reopened app shows the installed version and clears the marker", main.ShowUpdateCompletionNotice(target, "2.1.7.0")
            && main.StatusModel.Text.Contains("FamilyQuant was updated to 2.1.7.", StringComparison.Ordinal) && !File.Exists(marker));
        main.Close(); main = new MainWindow();
        Check("Z2b next start has no repeated notice", !main.ShowUpdateCompletionNotice(target, "2.1.7.0"));
        File.WriteAllText(marker, "2.1.6.0");
        Check("Z2b stale marker is cleared without claiming a different update", !main.ShowUpdateCompletionNotice(target, "2.1.7.0") && !File.Exists(marker));
        File.WriteAllText(marker, "invalid");
        Check("Z2b malformed marker is cleared without a notice", !main.ShowUpdateCompletionNotice(target, "2.1.7.0") && !File.Exists(marker));
        platform.SetState(platform.Current with { Running = true });
        result = controller.InstallAsync(target, false).GetAwaiter().GetResult();
        Check("Z2b refused update creates no success marker", !result.Success && !File.Exists(marker) && platform.Current.LaunchCount == 1);
        platform.SetState(platform.Current with { Running = false });
        controller = new(options with { WaitPid = null }, SetupFixturePayload("2.1.7.0"), source, platform);
        result = controller.InstallAsync(target, false).GetAwaiter().GetResult();
        Check("Z2b plain silent installation creates no update notice", result.Success && !File.Exists(marker) && platform.Current.LaunchCount == 1);
        File.WriteAllText(marker, "2.1.7.0");
        result = controller.UninstallAsync(target).GetAwaiter().GetResult();
        Check("Z2b uninstall removes an unconsumed completion marker", result.Success && !File.Exists(marker));
        result = controller.InstallAsync(target, false).GetAwaiter().GetResult();
        Check("Z2b reinstall succeeds in the same folder after marker cleanup", result.Success && platform.Current.LaunchCount == 1);
        Directory.CreateDirectory(marker); // An inert path collision forces the marker write to fail.
        controller = new(options, SetupFixturePayload("2.1.7.0"), source, platform);
        result = controller.InstallAsync(target, false).GetAwaiter().GetResult();
        Check("Z2b notice write failure never prevents reopening", result.Success && platform.Current.LaunchCount == 2 && Directory.Exists(marker));
        Directory.Delete(marker);
        main.Close();
    }
}
