using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AssayRowIsolationCommandTable() =>
    [
        new("--test-assay-row-isolation", 10000, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-assay-row-isolation", AssaySpectrumRowIsolationTests),
    ];
}
