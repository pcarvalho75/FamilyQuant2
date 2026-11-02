using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DeNovoLedgerCommandTable() =>
    [
        new("--prove-denovo-ledger", 10300, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-denovo-ledger", (args, _) => DeNovoLedgerDeclarationsCommand(args)),
        new("--prove-denovo-runtime", 10310, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-denovo-runtime", (args, _) => DeNovoLedgerRuntimeCommand(args)),
        new("--prove-denovo-workflows", 10320, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-denovo-workflows", (args, _) => DeNovoLedgerWorkflowCommand(args)),
        new("--prove-denovo-refusals", 10330, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-denovo-refusals", (args, _) => DeNovoLedgerRefusalsCommand(args)),
        new("--denovo-command-probe", 10340, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--denovo-command-probe", (args, _) => DeNovoCommandProbe(args)),
        new("--native-dialog-probe", 10350, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--native-dialog-probe", (args, _) => NativeDialogProbe(args)),
        new("--prove-denovo-commands", 10360, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-denovo-commands", (args, _) => DeNovoCommandEvidence(args)),
    ];
}
