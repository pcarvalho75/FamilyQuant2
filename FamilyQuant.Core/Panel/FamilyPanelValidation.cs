using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Panels;

public static class FamilyPanelValidation
{
    public static FamilyPanel Clone(FamilyPanel panel) =>
        JsonStreamCopy.Clone(panel);

    /// <summary>Refuses ambiguous keys, broken/cyclic merges and contradictory subclass definitions.
    /// Does not change any user text or domain accession.</summary>
    public static void Validate(FamilyPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        Evidence.PanelEvidenceMatching.ValidateConfirmed(panel);
        var families = new Dictionary<string, FamilyPanelFamily>(StringComparer.Ordinal);
        foreach (FamilyPanelFamily family in panel.Families)
        {
            if (string.IsNullOrWhiteSpace(family.Key) || !families.TryAdd(family.Key, family))
                throw new ArgumentException("Every panel family needs a unique, nonempty key.", nameof(panel));
            if (!Enum.IsDefined(family.Decision)) throw new ArgumentException("Unknown panel decision.", nameof(panel));
            if (family.Pfam.Any(d => string.IsNullOrWhiteSpace(d.Accession)))
                throw new ArgumentException("A panel domain needs an accession.", nameof(panel));
            if (family.UniProtFamilies.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("A panel UniProt family name must not be empty.", nameof(panel));
            var subclasses = new Dictionary<string, FamilyPanelSubclass>(StringComparer.Ordinal);
            foreach (FamilyPanelSubclass part in family.Subclasses)
            {
                if (string.IsNullOrWhiteSpace(part.Key) || !subclasses.TryAdd(part.Key, part))
                    throw new ArgumentException("Subclass keys must be unique within a family.", nameof(panel));
                if (part.RequiredPfam.Concat(part.AbsentPfam).Any(string.IsNullOrWhiteSpace)
                    || part.RequiredPfam.Intersect(part.AbsentPfam, StringComparer.Ordinal).Any())
                    throw new ArgumentException("Subclass domains are empty or both required and absent.", nameof(panel));
            }
            foreach (FamilyPanelSubclass part in family.Subclasses)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal) { part.Key };
                string next = part.ReportAsSubclassKey;
                while (next.Length > 0)
                {
                    if (!visited.Add(next) || !subclasses.TryGetValue(next, out FamilyPanelSubclass? target))
                        throw new ArgumentException("A subclass report target is missing or cyclic.", nameof(panel));
                    next = target.ReportAsSubclassKey;
                }
            }
        }
        foreach (FamilyPanelFamily family in panel.Families) ResolveFamilyKey(panel, family.Key);
        if (panel.DecisionMappings.GroupBy(m => m.SourceValue, StringComparer.Ordinal).Any(g => g.Count() != 1))
            throw new ArgumentException("Each source decision value needs exactly one saved mapping.", nameof(panel));
        foreach (FamilyPanelDecisionMapping mapping in panel.DecisionMappings)
        {
            if (!Enum.IsDefined(mapping.Decision) || string.IsNullOrWhiteSpace(mapping.SourceValue))
                throw new ArgumentException("Invalid source decision mapping.", nameof(panel));
            if (mapping.Decision == FamilyPanelDecision.MergeIntoAnotherFamily
                && !families.ContainsKey(mapping.MergeTargetFamilyKey))
                throw new ArgumentException("A decision mapping's merge target is missing.", nameof(panel));
        }
    }

    /// <summary>Returns the final family key, or null for a row excluded by the user.</summary>
    public static string? ResolveFamilyKey(FamilyPanel panel, string key)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            FamilyPanelFamily? family = panel.Families.SingleOrDefault(f => f.Key == key);
            if (family == null || !visited.Add(key))
                throw new ArgumentException("A family merge target is missing or cyclic.", nameof(panel));
            if (family.Decision == FamilyPanelDecision.NotAFamily) return null;
            if (family.Decision != FamilyPanelDecision.MergeIntoAnotherFamily) return key;
            key = family.MergeTargetFamilyKey;
        }
    }
}
