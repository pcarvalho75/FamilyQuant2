using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Evidence;

public sealed partial class PanelEvidenceMatching
{
    public static FamilyPanel CarryConfirmedMatches(FamilyPanel previousPanel, FamilyPanel newPanel)
    {
        var result = FamilyPanelValidation.Clone(newPanel);
        var keys = result.Families.Select(family => family.Key).ToHashSet(StringComparer.Ordinal);
        var records = (result.ConfirmedEvidenceMatches ?? new Dictionary<string, PanelConfirmedEvidenceMatches>())
            .Where(pair => keys.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var dropped = new List<string>();
        foreach (var pair in previousPanel.ConfirmedEvidenceMatches ?? new Dictionary<string, PanelConfirmedEvidenceMatches>())
        {
            if (!keys.Contains(pair.Key)) { dropped.Add(pair.Key); continue; }
            if (!records.ContainsKey(pair.Key)) records.Add(pair.Key, EvidenceApply.Clone(pair.Value));
        }
        result.ConfirmedEvidenceMatches = records.Count == 0 ? null : records;
        result.ConfirmedMatchChangeNotice = MatchChangeNotice(Array.Empty<string>(), dropped);
        ValidateConfirmed(result);
        return result;
    }

    public static FamilyPanel RekeyConfirmedMatches(FamilyPanel previousPanel, FamilyPanel newPanel,
        IReadOnlyDictionary<string, string> retainedKeys)
    {
        var result = FamilyPanelValidation.Clone(newPanel);
        var keys = result.Families.Select(family => family.Key).ToHashSet(StringComparer.Ordinal);
        var records = new Dictionary<string, PanelConfirmedEvidenceMatches>(StringComparer.Ordinal);
        var moved = new List<string>();
        var dropped = new List<string>();
        foreach (var pair in previousPanel.ConfirmedEvidenceMatches ?? new Dictionary<string, PanelConfirmedEvidenceMatches>())
        {
            if (!retainedKeys.TryGetValue(pair.Key, out string? key) || !keys.Contains(key)) { dropped.Add(pair.Key); continue; }
            records[key] = EvidenceApply.Clone(pair.Value);
            if (key != pair.Key) moved.Add(pair.Key + " to " + key);
        }
        result.ConfirmedEvidenceMatches = records.Count == 0 ? null : records;
        result.ConfirmedMatchChangeNotice = MatchChangeNotice(moved, dropped);
        ValidateConfirmed(result);
        return result;
    }

    private static string MatchChangeNotice(IEnumerable<string> moved, IEnumerable<string> dropped)
    {
        string moves = string.Join(", ", moved.OrderBy(value => value, StringComparer.Ordinal));
        string removals = string.Join(", ", dropped.OrderBy(value => value, StringComparer.Ordinal));
        return string.Join(" ", new[] { moves.Length == 0 ? "" : "Moved confirmed matches from " + moves + ".",
            removals.Length == 0 ? "" : "Dropped confirmed matches for " + removals + "." }.Where(text => text.Length > 0));
    }
}
