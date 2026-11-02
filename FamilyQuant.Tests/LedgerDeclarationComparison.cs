using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

/// <summary>Exact named-property comparisons. These prove declarations, not control reachability.</summary>
internal static class LedgerDeclarationComparison
{
    internal sealed record Result(string Id, bool Matched, string Before, string Actual, string CurrentPath,
        string Symbol, string Reason);

    private static string? Name(XElement element) => element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value;

    internal static Result Compare(FeatureLedger.Observation observation, XDocument frozen,
        IReadOnlyDictionary<string, XDocument> current)
    {
        Result No(string reason, string actual = "") => new(observation.Id, false, observation.Detail, actual, "", "", reason);
        if (observation.Kind is not ("tooltip" or "option/copy" or "column"))
            return No("This kind needs another executed check or an inspected render.");
        int equals = observation.Detail.IndexOf(" = ", StringComparison.Ordinal);
        if (equals < 0) return No("Not a property observation.");
        string left = observation.Detail[..equals];
        int dot = left.LastIndexOf('.');
        if (dot < 1) return No("No named property anchor.");
        string name = left[..dot], property = left[(dot + 1)..], expected = observation.Detail[(equals + 3)..];
        var before = frozen.Root!.DescendantsAndSelf().Where(node => Name(node) == name).ToArray();
        if (before.Length != 1) return No("Unnamed or ambiguous frozen element; no guessed text match.");
        var oldAttribute = before[0].Attributes().SingleOrDefault(a => a.Name.LocalName == property);
        if (oldAttribute?.Value != expected) return No("Frozen XML property differs from its recorded observation.");
        if (expected.StartsWith("{", StringComparison.Ordinal)) return No("Markup requires a resolved-value or interaction check.");
        var candidates = current.SelectMany(file => file.Value.Root!.DescendantsAndSelf()
            .Where(node => Name(node) == name).Select(node => (file.Key, Node: node))).ToArray();
        if (candidates.Length != 1) return No("Current named element is missing or ambiguous.");
        var candidate = candidates[0];
        if (candidate.Node.Name.LocalName != before[0].Name.LocalName) return No("Named element type changed.");
        var attribute = candidate.Node.Attributes().SingleOrDefault(a => a.Name.LocalName == property);
        if (attribute == null) return No("The current declaration has no explicit matching property.");
        if (attribute.Value != expected) return No("The declared value changed and needs a specific preservation proof.", attribute.Value);
        string owner = candidate.Node.Document!.Root!.Attributes().FirstOrDefault(a => a.Name.LocalName == "Class")?.Value ?? "view";
        return new(observation.Id, true, expected, attribute.Value, candidate.Key,
            owner + "." + name + "." + property, "Exact named-element type and property value agree.");
    }
}
