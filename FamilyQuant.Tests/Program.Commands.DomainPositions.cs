using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DomainPositionCommandTable() =>
    [
        new("--domain-positions-live", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--domain-positions-live", (args, _) => DomainPositionsLive(args)),
        new("--test-domain-positions", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-domain-positions", (_, _) =>
        {
            DomainPositionTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "domain-positions"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];
}
