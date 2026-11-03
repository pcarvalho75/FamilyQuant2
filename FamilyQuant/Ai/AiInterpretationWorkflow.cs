using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FamilyQuant.Report;

namespace FamilyQuant.Ai;

public sealed record AiInterpretationInput(string Text, IReadOnlyList<(string Column, string Value)> Cells);
public enum AiWorkflowOutcome { Accepted, Cancelled, MissingKey, Stale, WrongKey, Billing, RateLimited, Unavailable, NoConnection, Refusal, Incomplete }

public sealed class AiInterpretationWorkflow(IAiTransport transport)
{
    public async Task<AiWorkflowOutcome> RunAsync(string? key, AiInterpretationInput input,
        Func<AiInterpretationInput, string, bool> confirmSend,
        Func<AiInterpretationDraft, IReadOnlyList<string>, bool> review,
        Func<bool> isCurrent, Action<AiInterpretationDraft> apply, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(key)) return AiWorkflowOutcome.MissingKey;
        if (!confirmSend(input, AiTransport.Model)) return AiWorkflowOutcome.Cancelled;
        cancellationToken.ThrowIfCancellationRequested();
        if (!isCurrent()) return AiWorkflowOutcome.Stale;
        AiResult result = await transport.SendAsync(key, input.Text, 16000, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Outcome != AiOutcome.Success) return result.Outcome switch
        {
            AiOutcome.WrongKey => AiWorkflowOutcome.WrongKey,
            AiOutcome.Billing => AiWorkflowOutcome.Billing,
            AiOutcome.RateLimited => AiWorkflowOutcome.RateLimited,
            AiOutcome.NoConnection => AiWorkflowOutcome.NoConnection,
            AiOutcome.Refusal => AiWorkflowOutcome.Refusal,
            AiOutcome.Incomplete => AiWorkflowOutcome.Incomplete,
            _ => AiWorkflowOutcome.Unavailable,
        };
        var draft = AiInterpretationDraft.Create(input.Text, result.Text, DateTimeOffset.UtcNow);
        var missing = InterpretationNumberTrace.Unmatched(result.Text, input.Cells);
        if (!review(draft, missing)) return AiWorkflowOutcome.Cancelled;
        cancellationToken.ThrowIfCancellationRequested();
        if (!isCurrent()) return AiWorkflowOutcome.Stale;
        apply(draft);
        return AiWorkflowOutcome.Accepted;
    }
}
