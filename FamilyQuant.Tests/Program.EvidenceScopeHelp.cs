using FamilyQuant.Evidence;
using System;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void EvidenceScopeHelpTests()
    {
        Section("Suggested scope and family proposal - four review steps");
        string shared = EvidenceScopeHelp.Build();
        string[] steps = shared.Split('\n').Where(line => line.Length > 2 && line[1] == '.' && char.IsDigit(line[0])).ToArray();
        Check("scope workflow is four steps in order", steps.Length == 4 && steps.Select(s => s[0]).SequenceEqual("1234"));
        if (steps.Length != 4) return;
        Check("opening fills an empty box from reviewed proteins and the read action refreshes counts", steps[0].Contains("empty scope box", StringComparison.Ordinal)
            && steps[0].Contains("suggested scope from the file's reviewed proteins", StringComparison.Ordinal)
            && steps[0].Contains("Read database and scope reads the counts again", StringComparison.Ordinal));
        Check("the person keeps or changes scope words", steps[1].Contains("Keep or change the scope words", StringComparison.Ordinal));
        Check("Update proposal builds the family proposal from stored evidence without downloading", steps[2].Contains("Update proposal builds the family proposal", StringComparison.Ordinal)
            && steps[2].Contains("stored evidence", StringComparison.Ordinal) && steps[2].Contains("never downloads", StringComparison.Ordinal));
        Check("review and untick precede Apply and changed scope needs another update", steps[3].Contains("untick", StringComparison.Ordinal)
            && steps[3].Contains("After changing scope words, use Update proposal again before Apply grouping", StringComparison.Ordinal));
        var options = new EvidenceClassificationOptions();
        Check("out-of-scope exception keeps relative source, scope, identity and query domain conditions", shared.Contains("in the file or the reference panel", StringComparison.Ordinal)
            && shared.Contains("relative must carry the scope words", StringComparison.Ordinal)
            && shared.Contains(options.UnannotatedIdentityPercent.ToString("0.##", CultureInfo.InvariantCulture) + "% identity or more", StringComparison.Ordinal)
            && shared.Contains("query must carry compatible observed domains without a conflicting learned marker", StringComparison.Ordinal));
        foreach (var page in new[] { EvidenceHelp.Build(), EvidenceHelp.Build(panelMode: true), HelpContent.Build(null,
            NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique) })
            Check("each Help entry point includes the same complete workflow", page.Contains(shared, StringComparison.Ordinal)
                && !page.Contains("proposes a scope", StringComparison.OrdinalIgnoreCase));
    }
}
