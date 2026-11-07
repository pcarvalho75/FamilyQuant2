using FamilyQuant.Evidence;
using System.Globalization;

namespace FamilyQuant;

/// <summary>A worked navigation guide; its example labels never act as classification rules.</summary>
public static class EvidenceOutsideScopeHelp
{
    public static string Build()
    {
        var options = new EvidenceClassificationOptions();
        string guide = "QUANTIFY A FAMILY OUTSIDE THE SUGGESTED SCOPE\n"
            + "Fictitious example: you want metalloprotease inhibitors in a secreted-protein mixture alongside the families already suggested. The suggested scope does not yet contain this function. A, B and C are invented protein labels, not database accessions. A is reviewed and carries the stored keyword \"Metalloprotease inhibitor\"; unreviewed B and C carry none of the scope words in their keyword or tissue annotations. Stored evidence has already been fetched for this example.\n\n"
            + "1. Find the function. Open Group by evidence and expand Database taxa and keyword counts. Read the keyword table's Category column and choose Molecular function only to find the inhibitor keyword; All categories shows the other available annotations. The keyword table counts reviewed accessions; a missing category is unavailable, rather than a guessed function.\n"
            + "2. Add the words. Double-click the \"Metalloprotease inhibitor\" keyword row, or select it and press Add to scope. Its quoted phrase is appended to your scope while existing words are kept. You can also edit the scope words yourself.\n"
            + "3. Read the preview. The added-words preview reads stored keyword and tissue matches in your file, groups them by the recorded UniProt family, and shows reviewed/unreviewed counts and example names. A can appear as a direct word match; B and C do not appear as word matches under this example's annotations. Contaminants are excluded because a contaminant never joins a family; this preview leaves the project unchanged.\n"
            + "4. Build the family proposal. Press Update proposal to use the edited scope and stored evidence, without downloading. Review the proposed families, Reason and any panel-link choices. A relative without scope words can join through a qualifying in-scope reviewed relative in the file or reference panel only at "
            + options.UnannotatedIdentityPercent.ToString("0.##", CultureInfo.InvariantCulture) + "% identity over at least "
            + options.MinQueryAlignedPercent.ToString("0.##", CultureInfo.InvariantCulture)
            + "% of the protein's length, with overlapping, non-conflicting observed domains. B may join this way; the word preview alone does not decide its family.\n"
            + "5. Inspect what remains. Open Unassigned and read each Reason and flag; C remains there in this example when it has no qualifying relative/domain support. All proteins and Find proteins in the Proteins tab can help locate an entry by its identifier or description. An unassigned protein keeps its current family and remains available for quantitation.\n"
            + "6. Make an explicit hand choice when justified. Highlight C in Unassigned or Proteins and press Add to family. Choose a proposed destination, or New family and type a name such as \"Fictitious inhibitor family\"; enter an optional note explaining your review. The row is ticked, labelled Added by hand, and its Reason and details retain the note separately from the automatic evidence. Update proposal keeps the hand choice; Remove from family restores the automatic proposal before Apply. Locked proteins are refused until their current family is unlocked in Curate families.\n"
            + "7. Commit the reviewed selection. Check the ticks, names and warnings, then press Apply grouping. Ticked proteins join their chosen groups; unticked proteins keep their current families and are still quantified. Use File > Save to keep the grouping recipe and notes; Undo restores the preceding grouping.\n"
            + "8. Third route: description words. In Curate families, part b Group by words offers Recluster by words for description similarity and Group by keywords for words you type in descriptions. A description match can offer other members of its current family; red rows without the words can be unticked. Review every offered member before Apply grouping. This description search is separate from UniProt keyword/tissue scope and does not require the family to be in that scope. These routes respect family locks; unlock a family before including it.\n";
        for (int step = 2; step <= 8; step++) guide = guide.Replace("\n" + step + ".", "\n\n" + step + ".");
        return guide;
    }
}
