using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FamilyQuant;

public static partial class CurationHomologyCache
{
    /// <summary>Build a native compact J28 entry from complete, externally hash-verified replay
    /// results. Validates database/query/offset coverage, writes and reads the ordinary cache codec.
    /// This method never starts another homology search or performs RAW extraction.</summary>
    public static CurationHomologyHits FromReplay(IReadOnlyList<QuantRow> rows, FamilyClusterFile curation,
        HomologyDatabase database, IReadOnlyList<HomologyResult> identified, IReadOnlyList<HomologyResult> deNovo,
        string cacheDirectory, HomologyOptions? options = null,
        bool identifiedBestScoreOnly = FamilyEnvelopeQuant.IdentifiedBestScoreOnlyDefault,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(rows); ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(database); ArgumentNullException.ThrowIfNull(identified); ArgumentNullException.ThrowIfNull(deNovo);
        var expected = HomologyDatabase.Build(curation.MyExtendedFastaItems, database.SeedLength, false);
        if (expected.Proteins.Count != database.Proteins.Count || expected.Proteins.Where((p, i) =>
            p.Id != database.Proteins[i].Id || p.Sequence != database.Proteins[i].Sequence).Any())
            throw new ArgumentException("Replayed database differs from current curation.", nameof(database));
        var requested = (options ?? new HomologyOptions()).Clone();
        var effective = requested.Clone(); effective.TopHitsPerPeptide = int.MaxValue;
        if (effective.Science != null) effective.Science.QueryRole = HomologyQueryRole.DeNovo;
        var idOptions = effective.Clone(); idOptions.BestScoreOnly = identifiedBestScoreOnly;
        if (idOptions.Science != null) idOptions.Science.QueryRole = HomologyQueryRole.Identified;
        var hits = new CurationHomologyHits { IdentityThreshold = effective.MinIdentity,
            EffectiveOptions = effective, IdentifiedOptions = idOptions, Database = database };
        var ids = new List<string>(); var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var proteins = database.Proteins.GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        hits.Identified = Side(NativeQuantFractions.SideIdentified, identified);
        hits.DeNovo = Side(NativeQuantFractions.SideDeNovo, deNovo);
        hits.ProteinIds = ids.ToArray();
        string key = Fingerprint(rows, curation, requested, identifiedBestScoreOnly, token);
        string path = Path.Combine(cacheDirectory, key + ".fqhits");
        Write(path, key, hits, token);
        if (!TryRead(path, key, rows, curation, requested, identifiedBestScoreOnly, token, out var verified) || verified == null)
            throw new InvalidDataException("Replayed compact attribution did not pass native cache readback.");
        return verified;

        List<(string Key, int[] Proteins)> Side(string side, IReadOnlyList<HomologyResult> results)
        {
            string[] required = rows.Where(r => r != null && string.Equals(r.Side, side, StringComparison.Ordinal))
                .Select(r => DeNovoReconciler.NormalizeSequence(r.Sequence ?? "")).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            var byQuery = results.ToDictionary(r => r.IsRejected ? r.Peptide : r.NormalizedPeptide, StringComparer.Ordinal);
            if (!byQuery.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(required))
                throw new ArgumentException("Replay query coverage differs for " + side + ".");
            var compact = new List<(string, int[])>();
            foreach (string sequence in required)
            {
                token.ThrowIfCancellationRequested(); var result = byQuery[sequence];
                foreach (var hit in result.Hits)
                    if (!proteins.TryGetValue(hit.ProteinId, out var choices) || !choices.Any(p => hit.Offset >= 0
                        && hit.Offset <= p.Sequence.Length - sequence.Length
                        && p.Sequence.Substring(hit.Offset, sequence.Length) == hit.MatchedSubsequence))
                        throw new ArgumentException("Replay contains an invalid protein/offset/span.");
                string[] unique = result.Hits.Select(h => h.ProteinId).Distinct(StringComparer.Ordinal).ToArray();
                int[] mapped = unique.Select(id =>
                {
                    if (!indices.TryGetValue(id, out int index)) { indices[id] = index = ids.Count; ids.Add(id); }
                    return index;
                }).ToArray();
                compact.Add((sequence, mapped));
                var offsets = result.Hits.GroupBy(h => h.ProteinId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Offset, StringComparer.Ordinal);
                hits.RecordLocalizationOffsets(side, sequence, mapped, unique.Select(id => offsets[id]).ToArray());
            }
            return compact;
        }
    }
}
