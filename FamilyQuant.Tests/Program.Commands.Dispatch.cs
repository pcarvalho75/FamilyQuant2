using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DispatchCommandTable() =>
    [
        new("--list-test-commands", -100, TestCommandPhase.Raw, args => args.Length == 1 && args[0] == "--list-test-commands", ListTestCommands),
        new("--test-dispatch", 0, TestCommandPhase.Raw, MatchesCommand000, RunCommand000),
    ];

    private static int ListTestCommands(string[] args, string workDir)
    {
        Console.WriteLine("phase\tpriority\tflag\thandler");
        foreach (TestCommand command in TestCommandRegistry.Commands)
            Console.WriteLine(command.Phase + "\t" + command.Order.ToString(CultureInfo.InvariantCulture) +
                "\t" + command.Flag + "\t" + command.Run.Method.Name);
        return 0;
    }

    private static bool MatchesCommand000(string[] args) => args.Contains("--test-dispatch", StringComparer.Ordinal);
    private static int RunCommand000(string[] args, string workDir)
    {
        CommandRegistryTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }


}
