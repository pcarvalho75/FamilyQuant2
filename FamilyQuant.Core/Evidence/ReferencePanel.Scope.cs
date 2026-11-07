using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Evidence;

public sealed class ReferencePanelCoverage
{
    public List<string> ScopeTerms { get; set; } = new();
    public List<string> WholeFamilies { get; set; } = new();
}

public sealed record ReferencePanelRequest(IReadOnlyList<string> ScopeTerms, bool FullPanel = false);

public static partial class ReferencePanelFetcher
{
    internal static List<string> NormalizeScope(IEnumerable<string> terms) => terms
        .Select(term => string.Join(" ", Regex.Matches(term ?? "", @"[\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)
            .Select(match => match.Value.ToUpperInvariant())))
        .Where(term => term.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(term => term, StringComparer.Ordinal).ToList();

    public static void RequireScope(ReferencePanel? panel, IReadOnlyList<string> terms)
    {
        RequireComplete(panel);
        if (panel?.Coverage is not { } coverage) return;
        if (NormalizeScope(terms).Except(coverage.ScopeTerms, StringComparer.Ordinal).Any())
            throw new InvalidDataException("The reference panel does not cover this scope. Use Fetch missing panel, then Update proposal.");
    }

    public static void RequireFull(ReferencePanel? panel)
    {
        RequireComplete(panel);
        if (panel?.Coverage != null)
            throw new InvalidDataException("This step needs the full reference panel. Fetch evidence and choose Full panel.");
    }

    public static void RequireCoverage(ReferencePanel? panel, IReadOnlyList<string>? terms)
    {
        if (terms == null) RequireFull(panel); else RequireScope(panel, terms);
    }

    /// <summary>Fetches missing scope slices, then whole families needed by scope assignments and coherence checks.</summary>
    public static async Task<ReferencePanel> FetchForScopeAsync(FamilyClusterFile file, ReferencePanelRequest request,
        HttpMessageHandler? handler = null, IProgress<FetchProgress>? progress = null,
        CancellationToken cancellationToken = default, string? expectedRelease = null, UniProtFetchOptions? options = null)
    {
        var terms = NormalizeScope(request.ScopeTerms);
        if (!request.FullPanel && terms.Count == 0) throw new ArgumentException("Enter scope words or choose Full panel.", nameof(request));
        // Keep long queries and unsupported token forms on the existing full route.
        bool full = request.FullPanel || terms.Count >= MaximumFamiliesPerStream
            || terms.Any(term => term.Any(character => character > 127))
            || Uri.EscapeDataString(ScopeClause(terms)).Length > 2500;
        var universe = FamiliesFound(file);
        var universeSet = universe.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previous = file.EvidenceSource?.ReferencePanel;
        string? release = expectedRelease ?? file.EvidenceSource?.Release;
        if (string.IsNullOrEmpty(release)) release = null;
        if (previous?.Release != release || previous?.Provider != "UniProt") previous = null;
        bool storedFull = previous is { IsComplete: true, Coverage: null };
        var covered = storedFull && !full ? terms : previous?.IsComplete == true ? previous.Coverage?.ScopeTerms ?? new List<string>() : new List<string>();
        var accumulated = NormalizeScope(covered.Concat(terms));
        full |= accumulated.Count >= MaximumFamiliesPerStream || Uri.EscapeDataString(ScopeClause(accumulated)).Length > 2500;
        bool InScope(ProteinEvidence evidence) => EvidenceClassifier.IsInScope(evidence, accumulated);
        var whole = new HashSet<string>(previous?.Coverage?.WholeFamilies
            ?? (storedFull && full ? previous!.Families : new List<string>()), StringComparer.OrdinalIgnoreCase);
        whole.IntersectWith(universeSet);
        if (storedFull && !full)
        {
            // Only an explicit scoped fetch converts a legacy full panel. Its known complete entries
            // can supply both scope slices and whole profiles locally without another download.
            var priorFamilies = previous!.Families.ToHashSet(StringComparer.OrdinalIgnoreCase);
            whole.UnionWith(FamiliesFound(file, InScope).Concat(previous.Entries.Where(entry => InScope(entry.Evidence)).SelectMany(entry => FamiliesOf(entry.Evidence)))
                .Where(family => priorFamilies.Contains(family) && universeSet.Contains(family)));
            progress?.Report(new FetchProgress { Message = "Reusing stored full reference entries for the chosen scope" });
        }
        var entries = new SortedDictionary<string, PanelEntry>(StringComparer.Ordinal);
        foreach (var entry in previous?.Entries ?? new List<PanelEntry>())
            if (FamiliesOf(entry.Evidence).Any(universeSet.Contains) && (!storedFull || full || InScope(entry.Evidence) || FamiliesOf(entry.Evidence).Any(whole.Contains)))
                entries.TryAdd(entry.Accession, entry);
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new SortedSet<string>(StringComparer.Ordinal);
        DateTimeOffset? retrieved = previous?.RetrievedAtUtc;
        var required = full ? universe : FamiliesFound(file, InScope);
        await Fetch(required.Where(family => !whole.Contains(family)).ToList(), null).ConfigureAwait(false);
        if (!full)
        {
            var remaining = universe.Where(family => !whole.Contains(family)).ToList();
            var added = terms.Except(covered, StringComparer.Ordinal).ToList();
            await Fetch(remaining, added).ConfigureAwait(false);
            var newFamilies = remaining.Except(previous?.Families ?? new List<string>(), StringComparer.OrdinalIgnoreCase).ToList();
            await Fetch(newFamilies, covered).ConfigureAwait(false);
            // A scope reference can introduce another family. Complete that family's profile too;
            // ranges, source counts and domain carriers must match the full-panel proposal exactly.
            while (true)
            {
                var closure = entries.Values.Where(entry => InScope(entry.Evidence)).SelectMany(entry => FamiliesOf(entry.Evidence))
                    .Where(family => universeSet.Contains(family) && !whole.Contains(family) && !missing.Contains(family))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (closure.Count == 0) break;
                await Fetch(closure, null).ConfigureAwait(false);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new ReferencePanel
        {
            Provider = "UniProt", Release = release ?? "", RetrievedAtUtc = retrieved ?? options?.UtcNow() ?? DateTimeOffset.UtcNow,
            Families = universe, Entries = entries.Values.ToList(), MissingFamilies = missing.ToList(), FailureReason = string.Join("; ", failures),
            // Describe the final coverage plan, not transient retries, so resumed results serialize identically.
            TotalBatches = full ? PlannedStreamBatchCount(universe) : PlannedStreamBatchCount(whole.OrderBy(family => family, StringComparer.OrdinalIgnoreCase).ThenBy(family => family, StringComparer.Ordinal).ToList())
                + StreamQueryBatches(universe.Where(family => !whole.Contains(family)).ToList(), ScopeClause(accumulated), accumulated.Count).Count,
            FailedBatches = missing.Count,
            Coverage = full && missing.Count == 0 ? null : new() { ScopeTerms = accumulated,
                WholeFamilies = whole.OrderBy(family => family, StringComparer.OrdinalIgnoreCase).ThenBy(family => family, StringComparer.Ordinal).ToList() },
        };

        async Task Fetch(List<string> families, IReadOnlyList<string>? slice)
        {
            if (families.Count == 0 || slice?.Count == 0) return;
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new FetchProgress { Message = full ? "Fetching missing full reference panel families" : "Fetching missing reference panel entries for the chosen scope" });
            var fetched = await FetchStreamsAsync(families, handler, progress, cancellationToken, release, options,
                slice == null ? "" : ScopeClause(slice), slice?.Count ?? 0).ConfigureAwait(false);
            release = fetched.Release; retrieved ??= fetched.RetrievedAtUtc;
            missing.UnionWith(fetched.MissingFamilies);
            if (fetched.FailureReason.Length > 0) failures.Add(fetched.FailureReason);
            if (slice == null) whole.UnionWith(families.Except(fetched.MissingFamilies, StringComparer.OrdinalIgnoreCase));
            foreach (var entry in fetched.Entries)
                if (FamiliesOf(entry.Evidence).Any(universeSet.Contains) && (slice == null || EvidenceClassifier.IsInScope(entry.Evidence, slice)))
                    entries.TryAdd(entry.Accession, entry);
        }
    }

    private static string ScopeClause(IEnumerable<string> terms) => " AND (" + string.Join(" OR ", terms.Select(term => "\"" + term + "\"")) + ")";
}
