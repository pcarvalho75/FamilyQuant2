using FamilyQuant.Operations;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace FamilyQuant;

public enum ReleaseUpdateStatus { Unavailable, Current, Newer }
public sealed record ReleaseUpdateResult(ReleaseUpdateStatus Status, string? Tag = null, Uri? ReleasePage = null);

public static class ReleaseUpdateChecker
{
    public static Task<ReleaseUpdateResult> CheckAsync(string repository, string currentVersion,
        OperationContext operation, HttpMessageHandler? handler = null) =>
        Task.FromResult(new ReleaseUpdateResult(ReleaseUpdateStatus.Unavailable));
}
