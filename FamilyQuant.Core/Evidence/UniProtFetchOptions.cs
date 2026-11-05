using System;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence;

public sealed class UniProtFetchOptions
{
    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;
}
