using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SubclassUnchangedMembershipCommands() =>
    [new("--test-subclass-unchanged-membership", 449, TestCommandPhase.Raw, a => a is ["--test-subclass-unchanged-membership"], (_, _) =>
    {
        SubclassUnchangedMembershipTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void SubclassUnchangedMembershipTests() => SubclassReportFlowProof(true, keepUnchangedLocks: true);
}
