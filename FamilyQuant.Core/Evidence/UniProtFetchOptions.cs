using System;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence;

public sealed class UniProtFetchOptions
{
    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;
    public string? ResumeDirectory { get; init; }
    public Func<DateTimeOffset> UtcNow { get; init; } = () => DateTimeOffset.UtcNow;
}
