using FamilyQuant.Setup;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SetupWindowCommands() =>
    [new(LogoAssets.SetupArgument, 10531, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == LogoAssets.SetupArgument,
        (args, _) => LogoAssets.Run(args)),
    new("--test-setup-window", 10497, TestCommandPhase.Raw, args => args.Length == 1 && args[0] == "--test-setup-window", (_, _) =>
    {
        SetupWindowTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];
    private static void SetupWindowTests()
    {
        EnsureApplication();
        try { foreach (string message in InstallerWindowPreview.CheckInteractions()) Check(message.StartsWith("PASS ", StringComparison.Ordinal) ? message[5..] : message, true); }
        catch (Exception error) { Check("installer window fake-controller interactions", false, error.ToString()); }
        string root = GoldenNumbers.FindRepository();
        string brand = Path.Combine(root, "FamilyQuant", "Assets", "Brand");
        Check("Z2d Setup icon differs from app icon and is its configured executable icon",
            !File.ReadAllBytes(Path.Combine(brand, "FamilyQuant-Setup.ico")).SequenceEqual(File.ReadAllBytes(Path.Combine(brand, "FamilyQuant.ico")))
            && File.ReadAllText(Path.Combine(root, "FamilyQuant.Setup", "FamilyQuant.Setup.csproj"))
                .Contains("<ApplicationIcon>../FamilyQuant/Assets/Brand/FamilyQuant-Setup.ico</ApplicationIcon>", StringComparison.Ordinal));
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/FamilyQuant2-Setup;component/FamilyQuant-Setup.ico"),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Check("Z2d Setup resource has all nine Windows icon sizes", decoder.Frames.Select(frame => frame.PixelWidth).OrderBy(size => size)
            .SequenceEqual(LogoAssets.IconSizes.OrderBy(size => size)));
    }
}
