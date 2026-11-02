using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LookGalleryCommands() =>
    [
        new("--look-finish-gallery", 10280, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--look-finish-gallery", (args, _) =>
            {
                if (args.Length != 2) throw new ArgumentException("Supply the existing native capture directory.");
                return ScreenHarness.FinishGallery(args[1]);
            }),
    ];
}
