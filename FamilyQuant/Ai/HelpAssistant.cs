using System;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Ai;

// Owned by the help assistant builder. This stub only lets the AI Help window build until the real one lands.
public sealed class HelpAssistant : IHelpAssistant
{
    public Task<HelpAnswer> AskAsync(string key, HelpQuestion question, CancellationToken cancellationToken) =>
        Task.FromResult(new HelpAnswer(AiOutcome.Unavailable, "", Array.Empty<HelpSource>(), false, null));
}
