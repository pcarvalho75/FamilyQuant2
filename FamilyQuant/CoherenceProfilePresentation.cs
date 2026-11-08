using FamilyQuant.Evidence;
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FamilyQuant;

public static class CoherenceProfilePresentation
{
    private sealed record Summary(string Text);
    private static readonly ConditionalWeakTable<FamilyProfile, Summary> Summaries = new();
    public static string CompleteSources(FamilyProfile profile) => Summaries.GetValue(profile, p => new Summary(Describe(p))).Text;
    private static string Describe(FamilyProfile profile)
    {
        var complete = profile.ReviewedMembers.Where(m => m.IsComplete).ToArray();
        string count = profile.CompleteMembers.ToString("N0", CultureInfo.CurrentCulture);
        if (complete.Length != profile.CompleteMembers || profile.CompleteSources.Total != profile.CompleteMembers)
            return count + " complete reviewed members; source/species breakdown not recorded.";
        string Species(CoherenceMemberSource source) => string.Join(", ", complete.Where(m => m.Source == source)
            .GroupBy(m => string.IsNullOrWhiteSpace(m.Species) ? "Species not recorded" : m.Species, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " " + g.Count().ToString("N0", CultureInfo.CurrentCulture)));
        string Line(CoherenceMemberSource source, string label)
        {
            var members = complete.Where(m => m.Source == source).ToArray();
            return members.Length.ToString("N0", CultureInfo.CurrentCulture) + " " + label + ": " + (members.Length == 0 ? "none" : Species(source));
        }
        return count + " complete reviewed members by source and species\n"
            + Line(CoherenceMemberSource.File, "from your file") + "\n"
            + Line(CoherenceMemberSource.ReferencePanel, "from the reference panel (all species)")
            + (complete.Any(m => m.Source == CoherenceMemberSource.Unknown) ? "\n" + Line(CoherenceMemberSource.Unknown, "with source not recorded") : "");
    }
}
