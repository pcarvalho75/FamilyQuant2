using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LookFocusCommands() =>
    [
        new("--look-finish-focus", 10270, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--look-finish-focus", (args, _) => LookFocusTemplates(args)),
    ];
}
