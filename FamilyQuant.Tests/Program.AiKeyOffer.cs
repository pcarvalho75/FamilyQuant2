using FamilyQuant.Ai;
using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiKeyOfferCommands() =>
    [new("--test-ai-key-offer", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-key-offer", (_, _) =>
        {
            AiKeyOfferTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void AiKeyOfferTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.AiKeyOffer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string project = Path.Combine(root, "project.fqproj"), file = Path.Combine(root, "FamilyQuant AI key.txt");
        var store = new AiKeyStore(Path.Combine(root, "protected.bin"));
        var offer = new AiKeyOffer(store); int asked = 0;
        try
        {
            Check("J46 no adjacent file does not offer or create a key", offer.EnsureKey(project, () => { asked++; return true; }) == null && asked == 0);
            File.WriteAllText(file, "fixture-adjacent-key");
            Check("J46 declining an adjacent key leaves it unimported", offer.EnsureKey(project, () => { asked++; return false; }) == null && asked == 1 && store.Read() == null);
            Check("J46 declining is not repeatedly prompted in the same session", offer.EnsureKey(project, () => { asked++; return true; }) == null && asked == 1);
            var nextSession = new AiKeyOffer(store);
            Check("J46 accepting imports the adjacent key without changing the file", nextSession.EnsureKey(project, () => { asked++; return true; }) == "fixture-adjacent-key"
                && store.Read() == "fixture-adjacent-key" && File.Exists(file) && asked == 2);
            Check("J46 an already saved key needs no import prompt", nextSession.EnsureKey(project, () => { asked++; return true; }) == "fixture-adjacent-key" && asked == 2);
        }
        catch
        {
            Check("J46 no adjacent file does not offer or create a key", false);
            Check("J46 declining an adjacent key leaves it unimported", false);
            Check("J46 declining is not repeatedly prompted in the same session", false);
            Check("J46 accepting imports the adjacent key without changing the file", false);
            Check("J46 an already saved key needs no import prompt", false);
        }
    }
}
