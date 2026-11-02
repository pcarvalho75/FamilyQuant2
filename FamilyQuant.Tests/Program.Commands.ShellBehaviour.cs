using System.Collections.Generic;
namespace FamilyQuant.Tests;
internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellBehaviourCommands() =>
    [new("--prove-shell-behaviour", 10570, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--prove-shell-behaviour", (args, _) => ShellBehaviour(args))];
}
