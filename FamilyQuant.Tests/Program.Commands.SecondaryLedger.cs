using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SecondaryLedgerCommandTable() =>
    [
        new("--ledger-secondary-compare", 10400, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-secondary-compare", (args, _) => SecondaryLedgerCommand(args)),
        new("--ledger-secondary-runtime", 10410, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-secondary-runtime", (args, _) => SecondaryRuntimeCommand(args)),
        new("--ledger-secondary-interactions", 10420, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-secondary-interactions", (args, _) => SecondaryRuntimeCommand(args, interactions: true)),
        new("--test-quantify-close", 10430, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-quantify-close", (_, _) => QuantifyCloseCommand()),
        new("--secondary-wording-append", 10440, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--secondary-wording-append", (args, _) => SecondaryAppendWording(args)),
        new("--secondary-box-audit", 10450, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--secondary-box-audit", (args, _) => SecondaryBoxAudit(args)),
    ];
}
