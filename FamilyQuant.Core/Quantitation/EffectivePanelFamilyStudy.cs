using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace FamilyQuant;

/// <summary>Reuses the native study methods after mapping complete candidate memberships to
/// effective panel keys. It never sums the existing UserID summaries or changes source proteins.</summary>
public static partial class EffectivePanelFamilyStudy
{
    public const string Method = "Complete prepared peptide candidates collapse from source UserIDs to effective panel keys, " +
        "with duplicate keys removed before shared-peptide credit. Outside, excluded and conflicting source groups remain " +
        "separate competitors. Native run statistics, database-protein explanation and pooled part allocation are reused. " +
        "Assigned-signal denominators use this collapsed candidate universe; original UserID studies are separate.";
    public const string ChangedMessage = "The saved effective panel-family studies do not match the current source membership or panel mapping. Compute the effective panel-family study again.";

    public static EffectivePanelFamilyResult Build(FamilyPeptideAttribution attribution, CurationHomologyHits hits,
        FamilyClusterFile curation, IEnumerable<string>? runs = null, IReadOnlyDictionary<string, double>? perRunTic = null,
        SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique)
    {
        ArgumentNullException.ThrowIfNull(attribution); ArgumentNullException.ThrowIfNull(hits); ArgumentNullException.ThrowIfNull(curation);
        string membership = FamilyStudyMembership.Fingerprint(curation);
        if (!string.Equals(attribution.MembershipSha256, membership, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(FamilyStudyMembership.ChangedMessage, nameof(attribution));
        var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(curation);
        if (!map.IsCurrent) throw new InvalidOperationException(map.Message);
        var slots = Slots(curation, map);
        var remap = slots.SelectMany(s => s.SourceUserIds.Select(id => (Id: id, Slot: s.StudyFamilyId))).ToDictionary(p => p.Id, p => p.Slot);
        var sourceMap = FamilyEnvelopeQuant.AttributeHits(hits, curation);
        foreach (var peptide in attribution.Peptides)
            if (!peptide.FamilyUserIds.Order().SequenceEqual(sourceMap.Resolve(peptide.Peptide.Side, peptide.Peptide.Sequence).Order())
                || peptide.FamilyUserIds.Any(id => !remap.ContainsKey(id)))
                throw new ArgumentException("The peptide attribution and retained protein hits do not agree.", nameof(hits));
        var collapsed = new FamilyPeptideAttribution(attribution.Peptides.Select(p => new FamilyAttributedPeptide(p.Peptide,
            p.FamilyUserIds.Select(id => remap[id]).Distinct().Order().ToArray())).ToList()) { MembershipSha256 = membership };
        var grouped = new FamilyClusterFile { MyExtendedFastaItems = curation.MyExtendedFastaItems.Select(p => new FastaItemExtended
        {
            SequenceIdentifier = p.SequenceIdentifier, Sequence = p.Sequence, UserID = remap.GetValueOrDefault(p.UserID),
            Evidence = p.Evidence,
        }).ToList() };
        var result = new EffectivePanelFamilyResult
        {
            MembershipSha256 = membership, PanelMapSha256 = MapFingerprint(curation, map), Method = Method, Policy = policy, Families = slots,
            RunShares = FamilyCompositionStudy.BuildRunShares(collapsed, runs, policy, perRunTic),
            ProteinCounts = FamilyCompositionStudy.BuildProteinCounts(collapsed, grouped.MyExtendedFastaItems),
        };
        foreach (var slot in slots)
        {
            if (result.RunShares.Families.All(f => f.FamilyUserId != slot.StudyFamilyId))
                result.RunShares.Families.Add(FamilyUnobservedStudies.ZeroShare(slot.StudyFamilyId, result.RunShares.Runs));
            if (result.ProteinCounts.Families.All(f => f.FamilyUserId != slot.StudyFamilyId))
                result.ProteinCounts.Families.Add(new() { FamilyUserId = slot.StudyFamilyId });
        }
        const string unit = " Candidate unit: effective panel-family study slot, not source UserID.";
        result.RunShares.Method += unit; result.ProteinCounts.Method += unit;
        BuildParts(result, collapsed, hits, curation, grouped);
        return result;
    }

    public static void Store(QuantManifest manifest, FamilyPeptideAttribution attribution, CurationHomologyHits hits,
        FamilyClusterFile curation, SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var result = Build(attribution, hits, curation, manifest.PerRunTic.Keys, manifest.PerRunTic, policy);
        manifest.EffectivePanelFamilies = result;
    }

    /// <summary>Automatic computation is additive only where collapsing several UserIDs is needed.
    /// A caller may explicitly Store a bundle for a one-UserID panel as well.</summary>
    public static bool StoreIfNeeded(QuantManifest manifest, FamilyPeptideAttribution attribution, CurationHomologyHits hits,
        FamilyClusterFile curation, SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique)
    {
        if (!HasMultipleUserIds(curation)) { manifest.EffectivePanelFamilies = null; return false; }
        Store(manifest, attribution, hits, curation, policy);
        return true;
    }

    public static bool HasMultipleUserIds(FamilyClusterFile curation)
    {
        var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(curation);
        return map.IsCurrent && map.UserFamilies.Values.Where(IsPanelFamily).GroupBy(f => f.PanelFamilyKey, StringComparer.Ordinal).Any(g => g.Count() > 1);
    }

    private static bool IsPanelFamily(PanelFamilyUserMap family) => !family.HasConflict && family.PanelFamilyKey != null
        && family.Decision is FamilyPanelDecision.Quantify or FamilyPanelDecision.ReportUnderOthers;

    internal static List<EffectivePanelFamilySlot> Slots(FamilyClusterFile curation, PanelFamilyMap map)
    {
        var targets = curation.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)).ToList();
        var names = FamilyNaming.Names(targets);
        return targets.Select(p => p.UserID).Distinct().Select(id =>
        {
            map.UserFamilies.TryGetValue(id, out var entry);
            bool panel = entry != null && IsPanelFamily(entry);
            var kind = panel ? EffectivePanelFamilyKind.PanelFamily : entry?.HasConflict == true ? EffectivePanelFamilyKind.AssignmentConflict
                : entry?.Decision == FamilyPanelDecision.NotAFamily ? EffectivePanelFamilyKind.Excluded : EffectivePanelFamilyKind.OutsidePanel;
            return new { Id = id, Kind = kind, Key = panel ? entry!.PanelFamilyKey : null };
        }).GroupBy(p => new { p.Kind, p.Key, PrivateId = p.Kind == EffectivePanelFamilyKind.PanelFamily ? 0 : p.Id })
            .OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.Key, StringComparer.Ordinal).ThenBy(g => g.Key.PrivateId)
            .Select((g, index) => new EffectivePanelFamilySlot
            {
                StudyFamilyId = index + 1, PanelFamilyKey = g.Key.Key, Kind = g.Key.Kind, SourceUserIds = g.Select(p => p.Id).Order().ToList(),
                DisplayName = g.Key.Key == null ? names.GetValueOrDefault(g.Key.PrivateId, "Family " + g.Key.PrivateId.ToString(CultureInfo.InvariantCulture))
                    : curation.FamilyPanel!.Families.Single(f => f.Key == g.Key.Key).DisplayName,
            }).ToList();
    }

    private static string MapFingerprint(FamilyClusterFile curation, PanelFamilyMap map) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        Version = 1,
        Memberships = map.UserFamilies.Values.OrderBy(f => f.FamilyUserId).Select(f => new
        { f.FamilyUserId, f.PanelFamilyKey, f.Decision, f.HasConflict, f.HasOutsideMembers, Keys = f.PanelKeys.Order(StringComparer.Ordinal).ToArray() }).ToArray(),
        Rules = curation.FamilyPanel!.Families.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new
        {
            f.Key, f.Decision, f.MergeTargetFamilyKey, Domains = f.Pfam.Select(p => p.Accession).Order(StringComparer.Ordinal).ToArray(),
            Classes = f.Subclasses.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => new
            { s.Key, s.ReportAsSubclassKey, Required = s.RequiredPfam.Order(StringComparer.Ordinal).ToArray(), Absent = s.AbsentPfam.Order(StringComparer.Ordinal).ToArray(),
                Source = s.UniProtSubfamilies.Order(StringComparer.Ordinal).ToArray() }).ToArray(),
        }).ToArray(),
    })));

    public static string? Problem(FamilyClusterFile curation, EffectivePanelFamilyResult result)
    {
        if (result.SchemaVersion != 1 || string.IsNullOrWhiteSpace(result.MembershipSha256)
            || !string.Equals(result.MembershipSha256, FamilyStudyMembership.Fingerprint(curation), StringComparison.OrdinalIgnoreCase)) return ChangedMessage;
        var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(curation);
        if (!map.IsCurrent || !string.Equals(result.PanelMapSha256, MapFingerprint(curation, map), StringComparison.OrdinalIgnoreCase)) return ChangedMessage;
        var expected = Slots(curation, map);
        string Identities(IEnumerable<EffectivePanelFamilySlot> slots) => JsonSerializer.Serialize(slots.Select(s => new { s.StudyFamilyId, s.PanelFamilyKey, s.Kind, s.SourceUserIds }));
        if (Identities(expected) != Identities(result.Families)) return ChangedMessage;
        var ids = expected.Select(s => s.StudyFamilyId).Order().ToArray();
        bool Complete(IEnumerable<int> actual) => actual.Order().SequenceEqual(ids);
        if (!Complete(result.RunShares.Families.Select(f => f.FamilyUserId)) || !Complete(result.ProteinCounts.Families.Select(f => f.FamilyUserId))
            || result.RunShares.Policy != result.Policy || (result.SubclassParts != null && (!Complete(result.SubclassParts.Families.Select(f => f.FamilyUserId)) || result.SubclassParts.Policy != result.Policy))
            || (result.DomainParts != null && (!Complete(result.DomainParts.Domains.Select(f => f.FamilyUserId)) || !Complete(result.DomainParts.Products.Select(f => f.FamilyUserId)) || result.DomainParts.Policy != result.Policy))) return ChangedMessage;
        var nested = new IFamilyMembershipStudy?[] { result.RunShares, result.ProteinCounts, result.SubclassParts, result.DomainParts, result.Localization };
        if (nested.Where(s => s != null).Any(s => !string.Equals(s!.MembershipSha256, result.MembershipSha256, StringComparison.OrdinalIgnoreCase))) return ChangedMessage;
        string[] keys = expected.Where(s => s.Kind == EffectivePanelFamilyKind.PanelFamily).Select(s => s.PanelFamilyKey!).ToArray();
        if (result.Parts.Any(p => !keys.Contains(p.PanelFamilyKey, StringComparer.Ordinal) || !Enum.IsDefined(p.Kind) || !Enum.IsDefined(p.State)
            || string.IsNullOrWhiteSpace(p.Reason)
            || (p.State is EffectivePanelPartState.NotComputed or EffectivePanelPartState.NotMeasured ? p.Signal.HasValue || p.ShareOfFamily.HasValue
                : !p.Signal.HasValue || !double.IsFinite(p.Signal.Value) || p.Signal.Value < 0))
            || keys.Any(key => Enum.GetValues<EffectivePanelPartKind>().Any(kind => !result.Parts.Any(p => p.PanelFamilyKey == key && p.Kind == kind))))
            return "The saved effective panel-family part availability is incomplete or invalid. Compute the study again.";
        return null;
    }
}
