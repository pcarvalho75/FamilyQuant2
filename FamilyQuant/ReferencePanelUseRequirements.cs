using FamilyQuant.Panels;
using System.Linq;

namespace FamilyQuant;

public static class ReferencePanelUseRequirements
{
    public const string SubclassChoice = "Choose Full panel for this project's subclass assignment, validation reports and Fetch UniProt families.";
    public const string SubclassApply = "Choose Full panel in Fetch evidence before Apply grouping to keep subclass assignment available.";
    public static bool HasSubclasses(FamilyPanel? panel) => panel?.Families.Any(SubclassAssignment.HasExecutableDefinitions) == true;
}
