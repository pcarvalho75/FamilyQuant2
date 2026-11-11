using System.Globalization;

namespace FamilyQuant;

public sealed partial class EvidenceReviewViewModel
{
    public int ProteinsInList { get; }
    public int InScopeProteinsOutsideList { get; }
    public int ProteinsOutsideScope { get; }
    public string ListDestinationText => ProteinsInList.ToString("N0", CultureInfo.CurrentCulture)
        + (ProteinsInList == 1 ? " protein is proposed in a family of your list." : " proteins are proposed in a family of your list.");
    public string SequenceDestinationText => InScopeProteinsOutsideList.ToString("N0", CultureInfo.CurrentCulture)
        + (InScopeProteinsOutsideList == 1
            ? " in-scope protein in no family of your list keeps its sequence family, named after its proteins, and is measured in it."
            : " in-scope proteins in no family of your list keep their sequence family, named after its proteins, and are measured together in it.");
    public string OutsideScopeDestinationText => ProteinsOutsideScope.ToString("N0", CultureInfo.CurrentCulture)
        + (ProteinsOutsideScope == 1 ? " protein is outside the scope and keeps its current family."
            : " proteins are outside the scope and keep their current family.");
    public const string DestinationHelp = "These counts describe the current proposal and exclude decoys and contaminants. Locked or unticked proteins keep their current family; only Apply grouping changes the project, with Undo available.";
}
