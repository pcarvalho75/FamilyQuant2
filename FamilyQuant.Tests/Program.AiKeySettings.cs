using FamilyQuant.Ai;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiKeySettingsCommands() =>
    [new("--test-ai-key-settings", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-key-settings", (_, _) =>
        {
            AiKeySettingsTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void AiKeySettingsTests()
    {
        EnsureApplication();
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.AiKeySettings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new AiKeyStore(Path.Combine(root, "saved.bin"));
        string input = Path.Combine(root, "input.txt");
        File.WriteAllText(input, "fixture-imported-key");
        var shell = new MainWindow();
        var window = new AiKeySettingsWindow(store, () => input);
        try
        {
            Check("J46 the live Help menu exposes AI key settings", shell.FindName("MenuItemAiKey") is MenuItem);
            Check("J46 the settings window exposes masked entry, save, import, remove and test",
                window.SaveKey.Content != null && window.ImportKey.Content != null && window.RemoveKey.Content != null && window.TestKey.Content != null);
            window.KeyEditor.Password = "fixture-entered-key";
            window.SaveKey.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("J46 saving the masked entry protects it and clears the editor", store.Read() == "fixture-entered-key" && window.KeyEditor.Password.Length == 0);
            window.ImportKey.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("J46 import uses the chosen file without deleting it", store.Read() == "fixture-imported-key" && File.Exists(input));
            window.RemoveKey.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("J46 removing the key leaves no saved key", store.Read() == null);
        }
        finally { window.Close(); shell.Close(); }
    }
}
