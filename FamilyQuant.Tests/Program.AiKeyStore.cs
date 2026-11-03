using FamilyQuant.Ai;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiKeyStoreCommands() =>
    [new("--test-ai-key-store", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-key-store", (_, _) =>
        {
            AiKeyStoreTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void AiKeyStoreTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.AiKeyStore", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "secret.bin"), import = Path.Combine(root, "input.txt");
        const string first = "fixture-key-one", second = "fixture-key-two";
        bool Safe(Func<bool> action) { try { return action(); } catch { return false; } }
        var store = new AiKeyStore(path);
        Check("J46 a missing key is reported without creating one", Safe(() => store.Read() == null && !File.Exists(path)));
        Check("J46 a key survives reopening encrypted for the current user", Safe(() =>
        {
            store.Save(first);
            byte[] saved = File.ReadAllBytes(path);
            return new AiKeyStore(path).Read() == first && !Encoding.UTF8.GetString(saved).Contains(first, StringComparison.Ordinal)
                && Encoding.UTF8.GetString(ProtectedData.Unprotect(saved, null, DataProtectionScope.CurrentUser)) == first;
        }));
        Check("J46 replacing a key uses the replacement", Safe(() => { store.Save(second); return store.Read() == second; }));
        Check("J46 importing a key file trims surrounding whitespace", Safe(() =>
        {
            File.WriteAllText(import, "  " + first + "\r\n"); store.Import(import); return store.Read() == first;
        }));
        Check("J46 blank, multiline and oversized input cannot replace the saved key", Safe(() =>
        {
            bool blank = Throws<ArgumentException>(() => store.Save(" "));
            bool multiline = Throws<ArgumentException>(() => store.Save("one\ntwo"));
            File.WriteAllText(import, new string('a', 9000));
            bool large = Throws<ArgumentException>(() => store.Import(import));
            return blank && multiline && large && store.Read() == first;
        }));
        Check("J46 removal is repeatable and clears the saved key", Safe(() =>
        {
            store.Remove(); store.Remove(); return store.Read() == null && !File.Exists(path);
        }));
    }
}
