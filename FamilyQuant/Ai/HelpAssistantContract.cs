using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Ai;

// The seam between the AI Help window, which gathers the question and the program state, and the help assistant,
// which asks Claude and lets it read the FamilyQuant source on GitHub. Both sides build against this file; a change
// to it goes through the desk.

/// <summary>What the program shows when the user asks. Every field is plain text; an empty string means none.</summary>
public sealed record HelpState(
    string Version,
    string Screen,
    string ScreenHelp,
    string ProjectSummary,
    string LastError,
    string LogTail);

/// <summary>An earlier question and answer in the same Help window, oldest first.</summary>
public sealed record HelpTurn(string Question, string Answer);

/// <summary>The question; <paramref name="ScreenshotPng"/> is null unless the user ticked the picture box.</summary>
public sealed record HelpQuestion(string Text, HelpState State, byte[]? ScreenshotPng, IReadOnlyList<HelpTurn> Earlier);

/// <summary>A repository file the answer read, with its 1-based inclusive line range.</summary>
public sealed record HelpSource(string Path, int FromLine, int ToLine);

/// <summary>
/// Outcome is Claude's (the same cases as the AI draft). SourceReadable is false when this build cannot read
/// GitHub at all. GitHubLimitResetsAt is set when GitHub's limit stopped the reading early; the answer then says so.
/// </summary>
public sealed record HelpAnswer(
    AiOutcome Outcome,
    string Text,
    IReadOnlyList<HelpSource> Sources,
    bool SourceReadable,
    DateTimeOffset? GitHubLimitResetsAt);

public interface IHelpAssistant
{
    Task<HelpAnswer> AskAsync(string key, HelpQuestion question, CancellationToken cancellationToken);
}
