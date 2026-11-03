using System;
using System.Collections.Generic;
using System.Linq;
using FamilyQuant.Evidence;

namespace FamilyQuant.Panels;

/// <summary>General subclass rules supplied by a project's panel and source evidence.</summary>
public static partial class SubclassAssignment
{
    private const string ConflictReason = "Source evidence supports conflicting panel subclass definitions.";
    /// <summary>Evaluate direct domain and source-subfamily facts. Conflicts and missing facts stay Unknown.</summary>
    public static ProteinSubclassLabel ClassifyDirect(FamilyPanelFamily family, ProteinEvidence? evidence)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (evidence == null) return Unknown("No source evidence is available for this protein.");
        var domains = (evidence.Pfam ?? new List<EvidenceDomain>()).Where(d => d != null && !string.IsNullOrWhiteSpace(d.Accession))
            .Select(d => d.Accession).ToHashSet(StringComparer.Ordinal);
        var texts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(evidence.Subfamily);
        Add(evidence.FamilyAnnotation);
        foreach (var membership in evidence.FamilyMemberships ?? new List<EvidenceFamilyMembership>())
        {
            Add(membership.Subfamily);
            Add(membership.Hierarchy);
        }
        var rankedTexts = texts.Select(WithoutRankSuffix).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitions = family.Subclasses.ToDictionary(s => s.Key, StringComparer.Ordinal);
        string Reported(FamilyPanelSubclass definition)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (definition.ReportAsSubclassKey.Length > 0)
            {
                if (!visited.Add(definition.Key) || !definitions.TryGetValue(definition.ReportAsSubclassKey, out var next))
                    throw new ArgumentException("A subclass report target is missing or cyclic.", nameof(family));
                definition = next;
            }
            return definition.Key;
        }
        var matches = family.Subclasses.Where(s =>
        {
            bool hasDomainRule = s.RequiredPfam.Count > 0 || s.AbsentPfam.Count > 0;
            bool architecture = hasDomainRule && domains.Count > 0
                && (s.AbsentPfam.Count == 0 || evidence.Fragment != true)
                && s.RequiredPfam.All(domains.Contains) && s.AbsentPfam.All(d => !domains.Contains(d));
            bool subfamily = s.UniProtSubfamilies.Any(t => !string.IsNullOrWhiteSpace(t)
                && rankedTexts.Contains(WithoutRankSuffix(t.Trim())));
            return architecture || subfamily;
        }).Select(s => (Definition: s.Key, Reported: Reported(s))).ToArray();
        var parts = matches.Select(m => m.Reported).Distinct(StringComparer.Ordinal).ToArray();
        if (parts.Length == 0) return Unknown("No panel subclass definition is supported by the available source evidence.");
        if (parts.Length > 1) return Unknown(ConflictReason);
        var matchedDefinitions = matches.Select(m => m.Definition).Distinct(StringComparer.Ordinal).ToArray();
        return new ProteinSubclassLabel { Status = ProteinSubclassStatus.Assigned, SubclassKey = parts[0],
            DefinitionKey = matchedDefinitions.Length == 1 ? matchedDefinitions[0] : null,
            Reason = "Source evidence matches the user-defined subclass rule." };

        void Add(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            texts.Add(text.Trim().TrimEnd('.', ';', ','));
            foreach (string component in text.Split(new[] { ',', ';', '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = component.Trim();
                if (value.Length > 0) texts.Add(value);
            }
        }
    }

    private static ProteinSubclassLabel Unknown(string reason) => new()
    {
        Status = ProteinSubclassStatus.Unknown, Reason = reason,
    };

    // Source ranks are structural suffixes. Match the complete remaining component, never a substring.
    private static string WithoutRankSuffix(string text)
    {
        foreach (string suffix in new[] { " sub-subfamily", " subfamily" })
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return text[..^suffix.Length].TrimEnd();
        return text;
    }
}
