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
        new("--prove-denovo-results-export", 10370, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-denovo-results-export", (args, _) => DeNovoResultsExportProof(args)),
        new("--isolated-native-proof", 10371, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--isolated-native-proof", (args, _) => IsolatedNativeProof(args)),
    ];

    private static int IsolatedNativeProof(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("Supply a native proof command and its arguments.");
        int outputIndex = args[1] switch { "--prove-denovo-results-export" => 4, "--prove-denovo-commands" => 5,
            "--prove-threeway" => 4, "--ledger-input-file-contracts" => 4,
            "--denovo-command-probe" => 4, "--native-dialog-probe" => 2, _ => throw new ArgumentException("Unsupported native proof command.") };
        if (args.Length <= outputIndex) throw new ArgumentException("Missing native proof output.");
        string logPath = System.IO.Path.GetFullPath(args[outputIndex]) + ".isolated-process.log";
        if (!NativeProofDesktop.IsPrivate) return NativeProofDesktop.Run(args, logPath);
        using var log = new System.IO.StreamWriter(logPath) { AutoFlush = true };
        var before = Console.Out; var beforeError = Console.Error;
        try
        {
            Console.SetOut(log); Console.SetError(log);
            System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window), System.Windows.FrameworkElement.LoadedEvent,
                new System.Windows.RoutedEventHandler((sender, _) =>
                {
                    if (sender is System.Windows.Window window)
                    { window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = window.Top = -30000; }
                }), true);
            var command = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(args, 1));
            return args[1] switch { "--prove-denovo-results-export" => DeNovoResultsExportProof(command),
                "--ledger-input-file-contracts" => InputFileCommand(command),
                "--prove-threeway" => ThreeWayLedgerProof(command),
                "--prove-denovo-commands" => DeNovoCommandEvidence(command), "--denovo-command-probe" => DeNovoCommandProbe(command),
                _ => NativeDialogProbe(command) };
        }
        catch (Exception exception) { Console.WriteLine(exception); return 1; }
        finally { Console.SetOut(before); Console.SetError(beforeError); }
    }
}
