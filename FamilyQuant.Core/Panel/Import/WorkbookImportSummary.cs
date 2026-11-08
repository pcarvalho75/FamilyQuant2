using FamilyQuant.Evidence;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Panels.Import;

public enum WorkbookImportSectionKind { Sheets, Columns, Colors, Decisions, FamilyRows, Pfam, Subclasses, Notes }
public enum WorkbookImportItemState { Found, Proposed, Uncertain, CouldNotPlace }
public enum WorkbookImportPage { FileAndSheets, Columns, Colors, Decisions, FamilyRows, EvidenceMatches, Subclasses, Notes }

public sealed record WorkbookImportSource(string SheetName, IReadOnlyList<int> RowNumbers, IReadOnlyList<string> Columns);
public sealed record WorkbookImportItem(string Id, string Label, string Value, string Reason, string ConfirmationWarning,
    WorkbookImportItemState State, WorkbookImportPage Page, bool Required, IReadOnlyList<WorkbookImportSource> Sources);
public sealed record WorkbookImportSection(WorkbookImportSectionKind Kind, string Title, IReadOnlyList<WorkbookImportItem> Items);
public sealed record WorkbookImportFamilyRow(string Key, string DisplayName, FamilyPanelDecision? Decision, string MergeTargetFamilyKey,
    WorkbookImportItemState State, string Reason, WorkbookImportPage Page, IReadOnlyList<WorkbookImportSource> Sources,
    IReadOnlyDictionary<string, string> SourceCells);
public sealed record WorkbookImportProblem(string ItemId, string DetailId, string SheetName, string Message,
    IReadOnlyList<int> RowNumbers, bool Blocking, WorkbookImportPage Page);
public sealed record WorkbookImportItemResult(string ItemId, bool Accepted, IReadOnlyList<WorkbookImportProblem> Problems);

/// <summary>Immutable visible findings and their private detached acceptance plan. Rebuild after editing a choice.</summary>
public sealed class WorkbookImportSummary
{
    public string SourceName { get; }
    public string SourceSha256 { get; }
    public IReadOnlyList<WorkbookImportSection> Sections { get; }
    public IReadOnlyCollection<string> FamilyKeys { get; }
    public FamilyPanelImport FamilyChoices => WorkbookImportSummaries.AcceptedChoices(Plan);
    public IReadOnlyList<FamilyPanelDecisionMapping> ProposedDecisions => Detached(Plan.Actions
        .Where(action => action.Item.State == WorkbookImportItemState.Proposed).Select(action => action.Value).OfType<FamilyPanelDecisionMapping>());
    public IReadOnlyList<PanelSubclassExpectedName> ExpectedSubclassNames => Detached(Plan.ExpectedNames);
    public IReadOnlyList<FamilyPanelDomain> RecognizedPfamDomains => Detached(Plan.Vocabulary);
    public IReadOnlyList<PanelSubclassProposal> SubclassProposals => Detached(Plan.Subclasses);
    public IReadOnlyList<PanelSubclassAcceptance> ReviewedSubclassSelections => Detached(Plan.ReviewedSubclasses);
    public IReadOnlyList<PanelSubclassAcceptance> DraftSubclassSelections => Detached(Plan.ReviewedSubclasses);
    public IReadOnlyList<WorkbookImportItem> SubclassReviewFindings => System.Array.AsReadOnly(Plan.Actions
        .Where(action => action.Value is WorkbookImportSubclassReviewFinding).Select(action => action.Item).ToArray());
    public IReadOnlyList<WorkbookImportFamilyRow> FamilyRows => System.Array.AsReadOnly(Plan.FamilyRows.ToArray());
    public IReadOnlyDictionary<string, IReadOnlyList<PanelEvidenceChoice>> ProposedEvidenceSelections =>
        new ReadOnlyDictionary<string, IReadOnlyList<PanelEvidenceChoice>>(Plan.ReviewedEvidence.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<PanelEvidenceChoice>)System.Array.AsReadOnly(pair.Value.Select(choice => choice with { }).ToArray()), System.StringComparer.Ordinal));
    public bool HasBlockingItems => Sections.SelectMany(section => section.Items).Any(item => item.Required
        && item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace);
    internal WorkbookImportPlan Plan { get; }

    internal WorkbookImportSummary(WorkbookImportPlan plan, IReadOnlyList<WorkbookImportSection> sections, IReadOnlyCollection<string> keys)
    {
        Plan = plan; Sections = sections; FamilyKeys = keys;
        SourceName = plan.Proposal.Workbook.SourceName; SourceSha256 = plan.Proposal.Workbook.SourceSha256;
    }
    private static IReadOnlyList<T> Detached<T>(IEnumerable<T> values) => System.Array.AsReadOnly(JsonSerializer.Deserialize<T[]>(JsonSerializer.Serialize(values))!);
}

public sealed class WorkbookImportAcceptResult
{
    public FamilyPanel? Panel { get; }
    public bool CanImport => Panel != null;
    public IReadOnlyList<WorkbookImportItemResult> Items { get; }
    internal WorkbookImportAcceptResult(FamilyPanel? panel, IReadOnlyList<WorkbookImportItemResult> items) { Panel = panel; Items = items; }
}

internal sealed class WorkbookImportPlan
{
    internal required PanelWorkbookProposal Proposal { get; init; }
    internal required FamilyPanelImport Choices { get; init; }
    internal required PanelEvidenceMatching Matching { get; init; }
    internal required List<PanelSubclassProposal> Subclasses { get; init; }
    internal required List<PanelSubclassExpectedName> ExpectedNames { get; init; }
    internal required List<PanelSubclassExpectedName> SubclassNotes { get; init; }
    internal required List<FamilyPanelDomain> Vocabulary { get; init; }
    internal required List<PanelSubclassAcceptance> ReviewedSubclasses { get; init; }
    internal required Dictionary<string, IReadOnlyList<PanelEvidenceChoice>> ReviewedEvidence { get; init; }
    internal List<WorkbookImportAction> Actions { get; } = new();
    internal List<WorkbookImportFamilyRow> FamilyRows { get; } = new();
}

internal sealed record WorkbookImportAction(WorkbookImportSectionKind Section, WorkbookImportItem Item, object? Value);
internal sealed record WorkbookImportColumn(string Role, string Header);
internal sealed record WorkbookImportDomain(string FamilyKey, FamilyPanelDomain Domain);
internal sealed record WorkbookImportEvidence(string FamilyKey, PanelEvidenceChoice? Choice);
internal sealed record WorkbookImportSubclass(string ProposalId, string RuleId);
