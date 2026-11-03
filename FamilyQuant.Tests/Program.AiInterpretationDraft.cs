using FamilyQuant;
using FamilyQuant.Ai;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiInterpretationDraftCommands() =>
    [new("--test-ai-interpretation-draft", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-interpretation-draft", (_, _) =>
        {
            AiInterpretationDraftTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void AiInterpretationDraftTests()
    {
        const string input = "exact preview", output = "review this result";
        var original = new ReportDetails { Title = "User title", WrittenInterpretation = "Earlier text" };
        try
        {
            var draft = AiInterpretationDraft.Create(input, output, new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
            Check("J46 draft labels model and date and fingerprints the exact preview", draft.Text == "AI draft (model claude-opus-5-5, 2026-01-02); review before use.\n" + output
                && draft.Provenance.Model == AiTransport.Model && draft.Provenance.InputSha256 == hash);
            var saved = draft.Apply(original);
            Check("J46 accepting a draft preserves other details without changing the original", original.WrittenInterpretation == "Earlier text"
                && saved.Title == original.Title && saved.WrittenInterpretation == draft.Text && saved.AiInterpretation == draft.Provenance);
            var file = new FamilyClusterFile { ReportDetails = saved };
            var reopened = FamilyClusterFile.FromJsonText(file.ToJsonText());
            Check("J46 the model and input fingerprint survive project serialization", reopened.ReportDetails?.AiInterpretation == draft.Provenance
                && reopened.ReportDetails.WrittenInterpretation == draft.Text);
            ReportDetails? changedTitle = null;
            var titleEditor = new ReportDetailsViewModel(saved, value => changedTitle = value) { Title = "New title" };
            titleEditor.Save();
            Check("J46 changing another field retains unchanged draft provenance", changedTitle?.AiInterpretation == draft.Provenance);
            ReportDetails? edited = null;
            var editor = new ReportDetailsViewModel(saved, value => edited = value) { WrittenInterpretation = "Person edited the text" };
            editor.Save();
            Check("J46 editing the draft clears provenance rather than attributing changed text to it", edited?.AiInterpretation == null);
            Check("J46 empty model output cannot become an interpretation", Throws<ArgumentException>(() => AiInterpretationDraft.Create(input, " ", DateTimeOffset.UtcNow)));
        }
        catch
        {
            Check("J46 draft labels model and date and fingerprints the exact preview", false);
            Check("J46 accepting a draft preserves other details without changing the original", false);
            Check("J46 the model and input fingerprint survive project serialization", false);
            Check("J46 changing another field retains unchanged draft provenance", false);
            Check("J46 editing the draft clears provenance rather than attributing changed text to it", false);
            Check("J46 empty model output cannot become an interpretation", false);
        }
    }
}
