using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyQuant.Evidence;

public sealed record EvidenceFamilyIdentity
{
    public int FamilyId { get; init; }
    public string Family { get; init; } = "";
    public string GroupKey { get; init; } = "";
    public bool PanelFamily { get; init; }
    /// <summary>Explicit proposal-family unlock retains eligibility until those families are applied again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReuseAfterExplicitUnlock { get; init; }
    public IReadOnlyList<string> Identifiers { get; init; } = Array.Empty<string>();
}

public sealed partial class EvidenceRecipe
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<EvidenceFamilyIdentity>? FamilyIdentities { get; set; }
}

public static partial class EvidenceApply
{
    /// <summary>Preserve only already anchored identities of the locked families the user explicitly unlocks.
    /// The caller records the lock/identity Undo together. Ordinary manual unlocking does not set this flag.</summary>
    public static void PreserveIdentitiesForUnlock(FamilyClusterFile file, IReadOnlyCollection<int> familyIds)
    {
        ArgumentNullException.ThrowIfNull(file); ArgumentNullException.ThrowIfNull(familyIds);
        if (file.EvidenceRecipe == null) return;
        var selected = familyIds.ToHashSet();
        var retained = CurrentFamilyIdentities(file).Where(identity => selected.Contains(identity.FamilyId)
            && file.LockedUserIds.Contains(identity.FamilyId)).Select(identity => identity.FamilyId).ToHashSet();
        if (retained.Count == 0) return;
        var identities = file.EvidenceRecipe.FamilyIdentities
            ?? AssignmentIdentities(file.EvidenceRecipe.Assignments, file.EvidenceRecipe.PanelContext != null).ToList();
        file.EvidenceRecipe.FamilyIdentities = identities.Select(identity => identity with
        {
            Identifiers = identity.Identifiers.ToArray(),
            ReuseAfterExplicitUnlock = identity.ReuseAfterExplicitUnlock || retained.Contains(identity.FamilyId),
        }).ToList();
    }

    private static List<EvidenceFamilyIdentity> CurrentFamilyIdentities(FamilyClusterFile file)
    {
        var stored = file.EvidenceRecipe;
        IEnumerable<EvidenceFamilyIdentity> identities = stored?.FamilyIdentities
            ?? AssignmentIdentities(stored?.Assignments ?? new List<EvidenceAssignment>(), stored?.PanelContext != null);
        var members = file.MyExtendedFastaItems.Where(protein => !NearestReviewed.IsDecoy(protein)
            && !SequenceLabels.IsContaminant(protein.SequenceIdentifier)).Select(protein => (protein.UserID, protein.SequenceIdentifier)).ToHashSet();
        return NormalizeIdentities(identities.Where(identity => file.LockedUserIds.Contains(identity.FamilyId) || identity.ReuseAfterExplicitUnlock), members);
    }

    private static IEnumerable<EvidenceFamilyIdentity> AssignmentIdentities(IEnumerable<EvidenceAssignment> assignments, bool panelFamily) =>
        assignments.Select(assignment => new EvidenceFamilyIdentity
        {
            FamilyId = assignment.FamilyId, Family = assignment.Family, GroupKey = assignment.GroupKey,
            PanelFamily = panelFamily, Identifiers = new[] { assignment.Identifier },
        });

    private static List<EvidenceFamilyIdentity> NormalizeIdentities(IEnumerable<EvidenceFamilyIdentity> identities,
        HashSet<(int FamilyId, string Identifier)> members) => identities
        .Where(identity => identity.FamilyId > 0 && HasStableFamilyKey(identity))
        .GroupBy(identity => (identity.FamilyId, identity.Family, identity.GroupKey, identity.PanelFamily))
        .Select(group => new EvidenceFamilyIdentity
        {
            FamilyId = group.Key.FamilyId, Family = group.Key.Family, GroupKey = group.Key.GroupKey, PanelFamily = group.Key.PanelFamily,
            ReuseAfterExplicitUnlock = group.Any(identity => identity.ReuseAfterExplicitUnlock),
            Identifiers = group.SelectMany(identity => identity.Identifiers).Where(identifier => members.Contains((group.Key.FamilyId, identifier)))
                .Distinct(StringComparer.Ordinal).OrderBy(identifier => identifier, StringComparer.Ordinal).ToArray(),
        })
        .Where(identity => identity.Identifiers.Count > 0)
        .OrderBy(identity => identity.FamilyId).ThenBy(identity => identity.Family, StringComparer.Ordinal)
        .ThenBy(identity => identity.GroupKey, StringComparer.Ordinal).ThenBy(identity => identity.PanelFamily).ToList();

    private static bool HasStableFamilyKey(EvidenceFamilyIdentity identity)
    {
        // Named protein overrides have a three-part key containing an output name.
        // Only family/architecture keys identify an evidence group independently of its display name.
        try
        {
            var parts = JsonSerializer.Deserialize<string[]>(identity.GroupKey);
            return parts is { Length: 2 } && parts[0] == identity.Family;
        }
        catch (JsonException) { return false; }
    }

    private static int ReusableFamilyId(IReadOnlyList<EvidenceFamilyIdentity> identities, string family, string groupKey, bool panelFamily)
    {
        var matches = identities.Where(identity => identity.Family == family && identity.GroupKey == groupKey && identity.PanelFamily == panelFamily).ToArray();
        if (matches.Length != 1 || identities.Count(identity => identity.FamilyId == matches[0].FamilyId) != 1) return 0;
        return matches[0].FamilyId;
    }

    private static List<EvidenceFamilyIdentity> ProjectFamilyIdentities(FamilyClusterFile file, EvidenceRecipe recipe)
    {
        var assigned = recipe.Assignments.ToDictionary(assignment => assignment.ProteinIndex);
        var members = file.MyExtendedFastaItems.Select((protein, index) =>
            (assigned.TryGetValue(index, out var assignment) ? assignment.FamilyId : protein.UserID, protein.SequenceIdentifier)).ToHashSet();
        var appliedFamilies = recipe.Assignments.Select(assignment => assignment.FamilyId).ToHashSet();
        return NormalizeIdentities(CurrentFamilyIdentities(file).Concat(AssignmentIdentities(recipe.Assignments, recipe.PanelContext != null)), members)
            .Select(identity => appliedFamilies.Contains(identity.FamilyId) ? identity with { ReuseAfterExplicitUnlock = false } : identity).ToList();
    }

    private static void ValidateFamilyIdentities(FamilyClusterFile file, EvidenceRecipe recipe)
    {
        if (recipe.FamilyIdentities == null) return;
        if (Hash(ProjectFamilyIdentities(file, recipe)) != Hash(recipe.FamilyIdentities))
            throw new ArgumentException("The saved family identities do not match the retained families and reviewed assignments.", nameof(recipe));
        int maximum = MaximumId(file);
        if (recipe.Assignments.All(assignment => assignment.FamilyId > maximum)) return;
        var current = CurrentFamilyIdentities(file);
        if (recipe.Assignments.Any(assignment => assignment.FamilyId <= maximum
            && ReusableFamilyId(current, assignment.Family, assignment.GroupKey, recipe.PanelContext != null) != assignment.FamilyId))
            throw new ArgumentException("The saved family identities no longer permit this family number. Reopen Group by evidence and update the proposal.");
    }
}
