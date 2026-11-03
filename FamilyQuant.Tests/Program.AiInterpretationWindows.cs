using FamilyQuant.Ai;
using System;
using System.Collections.Generic;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiInterpretationWindowsCommands() =>
    [new("--test-ai-interpretation-windows", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-interpretation-windows", (_, _) =>
        {
            AiInterpretationWindowsTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void AiInterpretationWindowsTests()
    {
        EnsureApplication();
        string input = "Exact preview\nwith a second line.";
        var draft = AiInterpretationDraft.Create(input, "Output with 77 and 88.", DateTimeOffset.UtcNow);
        var preview = new AiPreviewWindow(input, AiTransport.Model);
        var review = new AiDraftReviewWindow(draft, new[] { "77", "88" });
        try
        {
            Check("J46 the preview shows the entire immutable input and model", preview.InputText.Text == input
                && preview.InputText.IsReadOnly && preview.ModelText.Text.Contains(AiTransport.Model, StringComparison.Ordinal));
            Check("J46 sending requires an explicit visible action", preview.SendButton.Content != null && !preview.SendButton.IsDefault);
            Check("J46 review shows the labelled draft and all unmatched values", review.DraftText.Text == draft.Text
                && review.DraftText.IsReadOnly && review.Warnings.Text.Contains("77", StringComparison.Ordinal)
                && review.Warnings.Text.Contains("88", StringComparison.Ordinal));
            Check("J46 acceptance is a separate visible action", review.AcceptButton.Content != null && !review.AcceptButton.IsDefault);
        }
        finally { preview.Close(); review.Close(); }
    }
}
