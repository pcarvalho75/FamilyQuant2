using System.Collections.Generic;

namespace FamilyQuant.Panels.Import;

public enum PanelSubclassRuleKind { RequiredPfam, AbsentPfam }

public sealed class PanelSubclassSourceRow
{
    public string SheetName { get; set; } = "";
    public int RowNumber { get; set; }
    public Dictionary<string, string> Cells { get; set; } = new();
}

public sealed class PanelSubclassExpectedName
{
    public string ExpectedNameId { get; set; } = "";
    public string FamilyKey { get; set; } = "";
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<PanelSubclassSourceRow> SourceRows { get; set; } = new();
}

public sealed class PanelSubclassProposalResult
{
    public List<PanelSubclassProposal> Proposals { get; set; } = new();
}

public sealed class PanelSubclassProposal
{
    public string ProposalId { get; set; } = "";
    public string ExpectedNameId { get; set; } = "";
    public string FamilyKey { get; set; } = "";
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> CandidateFamilyKeys { get; set; } = new();
    public List<PanelSubclassSourceRow> SourceRows { get; set; } = new();
    public List<PanelSubclassRuleProposal> Rules { get; set; } = new();
    public List<FamilyPanelText> Notes { get; set; } = new();
    public bool HasMatchedTableRow { get; set; }
    public bool IsUncertain { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class PanelSubclassRuleProposal
{
    public string RuleId { get; set; } = "";
    /// <summary>Canonical supplied accession. Empty for an unresolved name; Name is never an engine rule.</summary>
    public string Accession { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> CandidateAccessions { get; set; } = new();
    public PanelSubclassRuleKind Kind { get; set; }
    public List<PanelSubclassSourceRow> SourceRows { get; set; } = new();
    public bool IsUncertain { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class PanelSubclassRuleAcceptance
{
    public string RuleId { get; set; } = "";
    public bool Accepted { get; set; }
    public PanelSubclassRuleKind? Kind { get; set; }
}

public sealed class PanelSubclassAcceptance
{
    public string ProposalId { get; set; } = "";
    public bool Accepted { get; set; }
    public string FamilyKey { get; set; } = "";
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<PanelSubclassRuleAcceptance> Rules { get; set; } = new();
}

public sealed class PanelSubclassDefinition
{
    public string FamilyKey { get; set; } = "";
    public FamilyPanelSubclass Definition { get; set; } = new();
}

public sealed class PanelSubclassImportProblem
{
    public string ProposalId { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class PanelSubclassBuildResult
{
    public List<PanelSubclassDefinition> Definitions { get; set; } = new();
    public List<PanelSubclassImportProblem> Problems { get; set; } = new();
    public bool CanBuild => Problems.Count == 0;
}
