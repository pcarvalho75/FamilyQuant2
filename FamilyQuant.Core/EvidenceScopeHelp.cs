using FamilyQuant.Evidence;
using System.Globalization;

namespace FamilyQuant;

/// <summary>The scope workflow shared by both evidence modes and the main Help.</summary>
public static class EvidenceScopeHelp
{
    public static string Build()
    {
        var options = new EvidenceClassificationOptions();
        return "SUGGESTED SCOPE AND FAMILY PROPOSAL\n"
            + "1. When Group by evidence opens, FamilyQuant fills an empty scope box with a suggested scope from the file's reviewed proteins. Read database and scope reads the counts again.\n"
            + "2. Keep or change the scope words.\n"
            + "3. Update proposal builds the family proposal from your scope and the stored evidence; it never downloads evidence.\n"
            + "4. Review the family proposal, untick proteins you want to leave unchanged, then Apply grouping. After changing scope words, use Update proposal again before Apply grouping.\n\n"
            + "An out-of-scope protein can still join through a qualifying reviewed relative in the file or the reference panel. The relative must carry the scope words and reach "
            + options.UnannotatedIdentityPercent.ToString("0.##", CultureInfo.InvariantCulture) + "% identity or more over at least "
            + options.MinQueryAlignedPercent.ToString("0.##", CultureInfo.InvariantCulture)
            + "% of the query; the query must carry compatible observed domains without a conflicting learned marker.\n";
    }
}
