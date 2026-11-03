using FamilyQuant.Ai;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiInterpretationWorkflowCommands() =>
    [new("--test-ai-interpretation-workflow", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-interpretation-workflow", (_, _) =>
        {
            AiInterpretationWorkflowTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private sealed class AiFixtureTransport : IAiTransport
    {
        public int Calls { get; private set; }
        public string Prompt { get; private set; } = "";
        public AiResult Result { get; set; } = new(AiOutcome.Success, "Observed 3; unsupported 4.");
        public Task<AiResult> SendAsync(string key, string prompt, int maxTokens, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++; Prompt = prompt; return Task.FromResult(Result);
        }
    }

    private static void AiInterpretationWorkflowTests()
    {
        var transport = new AiFixtureTransport();
        var workflow = new AiInterpretationWorkflow(transport);
        var input = new AiInterpretationInput("Exact information sent", new[] { ("count", "3") });
        int applied = 0, reviewed = 0;
        bool exact = false, flagged = false;
        bool Confirm(AiInterpretationInput shown, string model) { exact = shown.Text == input.Text && model == AiTransport.Model; return true; }
        bool Review(AiInterpretationDraft draft, IReadOnlyList<string> missing) { reviewed++; flagged = missing.Count == 1 && missing[0] == "4"; return true; }
        AiWorkflowOutcome Run(string? key, Func<AiInterpretationInput, string, bool> confirm, Func<AiInterpretationDraft, IReadOnlyList<string>, bool> review, Func<bool> current) =>
            workflow.RunAsync(key, input, confirm, review, current, _ => applied++, CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            Check("J46 no key sends nothing", Run(null, Confirm, Review, () => true) == AiWorkflowOutcome.MissingKey && transport.Calls == 0);
            Check("J46 declining the exact preview sends nothing", Run("fixture", (_, _) => false, Review, () => true) == AiWorkflowOutcome.Cancelled && transport.Calls == 0);
            Check("J46 declining the draft leaves the project untouched", Run("fixture", Confirm, (_, _) => false, () => true) == AiWorkflowOutcome.Cancelled && transport.Calls == 1 && applied == 0);
            int currentChecks = 0;
            Check("J46 changed project prevents accepting an old draft", Run("fixture", Confirm, Review, () => ++currentChecks == 1) == AiWorkflowOutcome.Stale && applied == 0);
            Check("J46 accepted draft uses the exact preview and lists unsupported numbers", Run("fixture", Confirm, Review, () => true) == AiWorkflowOutcome.Accepted
                && exact && flagged && transport.Prompt == input.Text && reviewed == 2 && applied == 1);
            transport.Result = new(AiOutcome.Refusal);
            Check("J46 a refusal never reaches review or apply", Run("fixture", Confirm, Review, () => true) == AiWorkflowOutcome.Refusal && applied == 1 && reviewed == 2);
        }
        catch
        {
            Check("J46 no key sends nothing", false);
            Check("J46 declining the exact preview sends nothing", false);
            Check("J46 declining the draft leaves the project untouched", false);
            Check("J46 changed project prevents accepting an old draft", false);
            Check("J46 accepted draft uses the exact preview and lists unsupported numbers", false);
            Check("J46 a refusal never reaches review or apply", false);
        }
    }
}
