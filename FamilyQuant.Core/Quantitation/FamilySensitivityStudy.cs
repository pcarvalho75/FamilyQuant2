using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant;

public static class FamilySensitivityStudy
{
    public const string Method = "Stored computed variants; primary calibrated family signal / measured peptide signal. "
        + "Shared policies use the same prepared peptides. Family-world-only credits are excluded; full counts overlap. "
        + "Recorded inputs, grouping and extraction settings are checked; unrecorded settings cannot be compared.";

    public static FamilySensitivityResult Build(IReadOnlyList<QuantRow> rows, QuantManifest source, FamilyClusterFile curation,
        PeptideFamilyAttribution? mapping = null, CancellationToken cancellationToken = default)
    {
        var result = new FamilySensitivityResult
        {
            Method = Method, CurationFingerprint = CurationFingerprint(curation),
            InputCompatibilityFingerprint = CompatibilityFingerprint(source),
        };
        result.Variants.Add(Compute(rows, source, curation, mapping, null, cancellationToken));
        FamilySensitivityTable.Validate(result);
        return result;
    }

    public static FamilySensitivityResult Compare(FamilySensitivityResult current, IReadOnlyList<QuantRow> rows,
        QuantManifest source, FamilyClusterFile curation, string projectSha256,
        PeptideFamilyAttribution? mapping = null, CancellationToken cancellationToken = default)
    {
        FamilySensitivityTable.Validate(current);
        if (current.CurationFingerprint != CurationFingerprint(curation))
            throw new InvalidDataException("The comparison has different database proteins, family grouping or labels.");
        string compatible = CompatibilityFingerprint(source);
        if (compatible.Length == 0 || current.InputCompatibilityFingerprint.Length == 0 || compatible != current.InputCompatibilityFingerprint)
            throw new InvalidDataException("The projects lack matching recorded source fingerprints, run names or extraction settings.");
        var variant = Compute(rows, source, curation, mapping, projectSha256, cancellationToken);
        if (variant.Key == current.Variants[0].Key)
            throw new InvalidDataException("Choose a computed project with a different recorded de novo cutoff.");
        var result = new FamilySensitivityResult
        {
            Method = current.Method, CurationFingerprint = current.CurationFingerprint,
            InputCompatibilityFingerprint = current.InputCompatibilityFingerprint,
            Variants = new() { current.Variants[0], variant },
        };
        FamilySensitivityTable.Validate(result);
        return result;
    }

    private static FamilySensitivityVariant Compute(IReadOnlyList<QuantRow> rows, QuantManifest source, FamilyClusterFile curation,
        PeptideFamilyAttribution? mapping, string? projectSha256, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(rows); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(curation);
        if (rows.Count == 0) throw new InvalidDataException("No extracted quantitation is stored for this variant.");
        if (source.DeNovoInputs.Any(i => !double.IsFinite(i.MinimumConfidence))) throw new InvalidDataException("A stored source cutoff is invalid.");
        if (source.DeNovoInputs.GroupBy(i => i.SourceId, StringComparer.Ordinal).Any(g => g.Select(i => (i.ConfidenceField, i.MinimumConfidence, i.SourcePriority)).Distinct().Count() != 1))
            throw new InvalidDataException("A source has inconsistent recorded filters between input files.");
        token.ThrowIfCancellationRequested();
        string grouping = CurationFingerprint(curation), rowsHash = CurrentFingerprint(rows, source, curation, token);
        var peptides = FamilyEnvelopeQuant.PreparePeptides(rows);
        double gate = EffectiveIdentity(source);
        if (!double.IsFinite(gate) || gate is < 0 or > 1) throw new InvalidDataException("The recorded family identity gate is invalid.");
        mapping ??= FamilyEnvelopeQuant.BuildAttributionFromCuration(rows, curation, Homology.HomologyDefaults.ForStoredMapping(source.FamilyMapping));
        if (mapping.MembershipSha256 != null && mapping.MembershipSha256 != FamilyStudyMembership.Fingerprint(curation))
            throw new InvalidDataException(FamilyStudyMembership.ChangedMessage);
        if (mapping.IdentityThreshold != gate) throw new InvalidDataException("The cached attribution differs from the recorded family identity gate.");
        token.ThrowIfCancellationRequested();
        var attributed = FamilyEnvelopeQuant.AttributePeptides(peptides, mapping);
        var names = FamilyNaming.Names(curation.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)));
        var pooled = FamilyEnvelopeQuant.BuildFromAttributedPeptides(attributed, id => names.GetValueOrDefault(id, string.Empty));
        var filters = Filters(source);
        var variant = new FamilySensitivityVariant
        {
            Key = Hash(JsonSerializer.Serialize(filters)),
            Label = filters.Count == 0 ? "No de novo cutoff recorded" : string.Join("; ", filters.Select(f => f.SourceId + ": " + f.Field + " >= " + f.Cutoff.ToString("0.###", CultureInfo.InvariantCulture))),
            SourceSha256 = projectSha256 ?? rowsHash,
            DeNovoInputs = JsonSerializer.Deserialize<List<DeNovoInputManifest>>(JsonSerializer.Serialize(source.DeNovoInputs))!,
            Mapping = FamilyEnvelopeQuant.ProvenanceForScience(curation, mapping.IdentityThreshold, source.FamilyMapping?.Science),
        };
        foreach (var policy in Enum.GetValues<SharedPeptidePolicy>())
        {
            token.ThrowIfCancellationRequested();
            double measured = pooled.MeasuredTotalEnvelopeReferenceCalibrated;
            var column = new FamilySensitivityPolicyResult { Policy = policy, MeasuredSignal = measured };
            foreach (var family in pooled.Families)
            {
                double signal = family.Signal(policy);
                column.Families.Add(new() { FamilyUserId = family.FamilyUserId, Description = family.Description, Signal = signal,
                    ShareOfMeasuredSignal = measured > 0 ? signal / measured : null });
            }
            // FL deliberately refuses normalized full-count compositions. Its exact route owns the
            // additive policies; the pooled full-count signal above is the existing shared-effect route.
            if (policy != SharedPeptidePolicy.CountInEveryFamily)
                column.PerRunShares = FamilyCompositionStudy.BuildRunShares(attributed, source.PerRunTic.Keys, policy, source.PerRunTic);
            variant.Policies.Add(column);
        }
        if (grouping != CurationFingerprint(curation) || rowsHash != CurrentFingerprint(rows, source, curation, token))
            throw new InvalidDataException("The stored input changed while the sensitivity study was being prepared.");
        return variant;
    }

    public static string CurationFingerprint(FamilyClusterFile curation)
    {
        if (FamilyEnvelopeQuant.HasRepeatedProteinIdentifiers(curation)) throw new InvalidDataException("Repeated protein identifiers prevent a reproducible sensitivity comparison.");
        return Hash(string.Join("\n", curation.MyExtendedFastaItems.Select(p => JsonSerializer.Serialize(new
            { p.SequenceIdentifier, p.Sequence, p.UserID, p.UserAnnotation, p.Description })).OrderBy(s => s, StringComparer.Ordinal)));
    }

    public static string CurrentFingerprint(IReadOnlyList<QuantRow> rows, QuantManifest source, FamilyClusterFile curation, CancellationToken token = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(CurationFingerprint(curation) + JsonSerializer.Serialize(new
        {
            source.IsotopeCount, source.MinPointsForQuant, source.Ppm, source.Ms1Tolerance,
            source.FamilyMapping,
            Runs = source.PerRunTic.OrderBy(p => p.Key, StringComparer.Ordinal),
            Inputs = source.ScientificInputFingerprints.OrderBy(p => p.Key, StringComparer.Ordinal),
            Sources = source.DeNovoInputs.OrderBy(i => i.SourceId, StringComparer.Ordinal).ThenBy(i => i.Sha256, StringComparer.Ordinal),
        })));
        foreach (var row in rows) { token.ThrowIfCancellationRequested(); hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(row)); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed record SourceFilter(string SourceId, string Field, double Cutoff, int Priority);
    private static List<SourceFilter> Filters(QuantManifest source) => source.DeNovoInputs
        .Select(i => new SourceFilter(i.SourceId, i.ConfidenceField, i.MinimumConfidence, i.SourcePriority)).Distinct()
        .OrderBy(i => i.Priority).ThenBy(i => i.SourceId, StringComparer.Ordinal).ThenBy(i => i.Field, StringComparer.Ordinal).ThenBy(i => i.Cutoff).ToList();

    private static string CompatibilityFingerprint(QuantManifest source)
    {
        if (source.ScientificInputFingerprints.Count == 0 || source.ScientificInputFingerprints.Values.Any(v => !Sha(v))
            || source.DeNovoInputs.Any(i => !Sha(i.Sha256))) return string.Empty;
        return Hash(JsonSerializer.Serialize(new
        {
            source.IsotopeCount, source.MinPointsForQuant, source.Ppm, source.Ms1Tolerance,
            Identity = EffectiveIdentity(source), Attribution = source.FamilyMapping?.Science?.AnyEnabled == true
                ? FamilyEnvelopeQuant.AttributionMethodFor(source.FamilyMapping.Science) + JsonSerializer.Serialize(source.FamilyMapping.Science)
                : FamilyEnvelopeQuant.AttributionMethod,
            Inputs = source.ScientificInputFingerprints.Select(p => JsonSerializer.Serialize(new
                { Kind = Path.GetExtension(p.Key).ToUpperInvariant(), Run = Path.GetExtension(p.Key).Equals(".raw", StringComparison.OrdinalIgnoreCase)
                    ? DeNovoReconciler.NormalizeRunName(p.Key).ToUpperInvariant() : string.Empty, Sha256 = p.Value.ToUpperInvariant() }))
                .OrderBy(v => v, StringComparer.Ordinal),
            Runs = source.PerRunTic.OrderBy(p => DeNovoReconciler.NormalizeRunName(p.Key), StringComparer.OrdinalIgnoreCase)
                .Select(p => new { Run = DeNovoReconciler.NormalizeRunName(p.Key).ToUpperInvariant(), Tic = p.Value }),
            Sources = source.DeNovoInputs.Select(i => JsonSerializer.Serialize(new { i.SourceId, i.ConfidenceField, i.SourcePriority, Sha256 = i.Sha256.ToUpperInvariant() }))
                .OrderBy(s => s, StringComparer.Ordinal),
        }));
    }

    private static bool Sha(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static double EffectiveIdentity(QuantManifest source) => source.FamilyMapping?.MinIdentity ?? new Homology.HomologyOptions().MinIdentity;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
