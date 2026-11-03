using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiAssistantHelpCommands() =>
    [new("--test-ai-assistant-help", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-assistant-help", (_, _) =>
        {
            AiAssistantHelpTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void AiAssistantHelpTests()
    {
        EnsureApplication();
        var shell = new MainWindow();
        try
        {
            string text = (string)typeof(MainWindow).GetMethod("WorkflowText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null)!;
            string flat = Regex.Replace(text, @"\s+", " ");
            string PathFor(string name)
            {
                var item = (MenuItem)shell.FindName(name);
                var parent = (MenuItem)item.Parent;
                return parent.Header + " > " + item.Header;
            }
            string[] paths = { PathFor("MenuItemAiKey"), PathFor("MenuItemAiDraft") };
            Check("J46d AI Help names both commands from the live menu", paths.All(p => flat.Contains(p, StringComparison.Ordinal)));
            string[] details = { "exact", "confirm", "number", "not found", "program", "without AI", "cancel" };
            Check("J46d AI Help explains the preview, confirmation, number check and program narrative", details.All(p => flat.Contains(p, StringComparison.OrdinalIgnoreCase)));
        }
        finally { shell.Close(); }
    }
}
