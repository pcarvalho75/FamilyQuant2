using System.Collections.Generic;
namespace FamilyQuant.Tests;
internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellBehaviourCommands() =>
    [new("--prove-shell-behaviour", 10570, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--prove-shell-behaviour", (args, _) => ShellBehaviour(args)),
     new("--prove-shell-behaviour2", 10571, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--prove-shell-behaviour2", (args, _) => ShellBehaviour(args)),
     new("--prove-shell-behaviour3", 10572, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--prove-shell-behaviour3", (args, _) => ShellBehaviour(args))];
}
