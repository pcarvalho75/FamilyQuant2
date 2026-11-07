using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FamilyQuant;

public enum ImportSubclassRuleKind { RequiredPfam, AbsentPfam }
public sealed record ImportSubclassRuleOption(string Label, ImportSubclassRuleKind Value);
public sealed record ImportSubclassCell(string Header, string Text);
public sealed record ImportSubclassSource(string SheetName, int RowNumber, IReadOnlyList<ImportSubclassCell> Cells)
{
    public string Heading => SheetName + (RowNumber > 0 ? ", row " + RowNumber : ", row not supplied");
}

/// <summary>A detached presentation of a canonical proposal; it never interprets workbook cells.</summary>
public sealed class ImportSubclassChoice : ScreenViewModel
{
    private bool use, confirmed, reviewed, familyEdited, keyEdited, nameEdited;
    private string family = "", name = "", abbreviation = "";
    private PanelSubclassAcceptance? initial;
    public string ProposalId { get; init; } = "";
    public string ExpectedNameId { get; init; } = "";
    public bool HasMatchedTableRow { get; init; }
    public bool IsUncertain { get; init; }
    public string Review { get; init; } = "";
    public string Source { get; init; } = "";
    public string Notes { get; init; } = "";
    public string Required { get; init; } = "";
    public string Absent { get; init; } = "";
    public object? SourceProposal { get; init; }
    public IReadOnlyList<string> CandidateFamilies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ImportSubclassSource> SourceRows { get; init; } = Array.Empty<ImportSubclassSource>();
    public IReadOnlyList<FamilyPanelText> SourceNotes { get; init; } = Array.Empty<FamilyPanelText>();
    public ObservableCollection<ImportSubclassRuleChoice> Rules { get; } = new();
    public ObservableCollection<string> Problems { get; } = new();
    public bool HasDraftSelection => initial != null;
    public bool Reviewed => reviewed;
    public string SourceFamily => SourceProposal is PanelSubclassProposal p && p.FamilyKey.Length > 0
        ? "Proposed family: " + p.FamilyKey : "Originally unplaced";
    public string EffectiveName => Name.Length > 0 ? Name : (SourceProposal as PanelSubclassProposal)?.DisplayName ?? "";
    public string Title => EffectiveName.Length > 0 ? EffectiveName : ExpectedNameId.Length > 0 ? "Unnamed expected subclass"
        : SourceRows.Count > 0 ? "Source row without a subclass name" : "Unplaced source table";
    public string State => Confirmed ? "Confirmed for import" : reviewed ? Use ? "Included in draft" : "Left out of draft"
        : Use ? "Proposed in draft" : "Not selected";
    public string CandidateText => CandidateFamilies.Count == 0 ? "No candidate family was supplied."
        : "Source family candidates: " + string.Join(", ", CandidateFamilies);
    public string SourceStatus => SourceRows.Count > 0 ? "Original source cells"
        : ExpectedNameId.Length > 0 ? "No source row was supplied for this expected name."
        : "No class rows were supplied for this source table; its retained notes are below.";
    public bool HasProblems => Problems.Count > 0 || Rules.Any(rule => rule.HasProblems);
    public string ProblemText => string.Join("\n", Problems);
    public string Summary => Rules.Any(rule => rule.Use)
        ? string.Join("; ", Rules.Where(rule => rule.Use).Select(rule => rule.KindLabel + ": " + rule.Label))
        : "No rules selected in this draft.";
    public string Family { get => family; set { if (Set(ref family, value ?? "")) { familyEdited = true; Touch(); } } }
    public string Name { get => name; set { if (Set(ref name, value ?? "")) { nameEdited = true; Touch(); Changed(nameof(Title)); Changed(nameof(EffectiveName)); } } }
    public string Abbreviation { get => abbreviation; set { if (Set(ref abbreviation, value ?? "")) { keyEdited = true; Touch(); } } }
    public bool Use { get => use; set { if (Set(ref use, value)) Touch(); } }
    // Only the host's explicit AcceptAll action confirms the draft.
    public bool Confirmed { get => confirmed; set { if (Set(ref confirmed, value)) Changed(nameof(State)); } }

    public ImportSubclassChoice()
    {
        Problems.CollectionChanged += (_, _) => { Changed(nameof(ProblemText)); Changed(nameof(HasProblems)); };
        Rules.CollectionChanged += (_, args) =>
        {
            if (args.NewItems != null) foreach (ImportSubclassRuleChoice rule in args.NewItems)
                rule.PropertyChanged += (_, _) => { Changed(nameof(Summary)); Changed(nameof(HasProblems)); };
        };
    }
    private void Touch() { reviewed = true; Changed(nameof(Reviewed)); Changed(nameof(State)); }
    public void Reject() { Use = false; Touch(); }

    public static ImportSubclassChoice FromCore(PanelSubclassProposal proposal) => FromCore(proposal, null);
    public static ImportSubclassChoice FromCore(PanelSubclassProposal proposal, PanelSubclassAcceptance? draftSelection)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (draftSelection != null && draftSelection.ProposalId != proposal.ProposalId)
            throw new ArgumentException("The draft selection belongs to another subclass proposal.", nameof(draftSelection));
        var source = SubclassReviewCopies.Proposal(proposal);
        var draft = draftSelection == null ? null : SubclassReviewCopies.Acceptance(draftSelection);
        var row = new ImportSubclassChoice
        {
            ProposalId = source.ProposalId, ExpectedNameId = source.ExpectedNameId, SourceProposal = source,
            HasMatchedTableRow = source.HasMatchedTableRow, IsUncertain = source.IsUncertain, Review = source.Reason,
            CandidateFamilies = source.CandidateFamilyKeys.ToArray(), SourceRows = SubclassReviewCopies.Sources(source.SourceRows),
            SourceNotes = source.Notes, Source = string.Join("; ", source.SourceRows.Select(r => r.SheetName + ", row " + r.RowNumber)),
            Notes = string.Join("; ", source.Notes.Select(n => n.Name)),
            Required = string.Join(", ", source.Rules.Where(r => r.Kind == PanelSubclassRuleKind.RequiredPfam).Select(r => r.Accession)),
            Absent = string.Join(", ", source.Rules.Where(r => r.Kind == PanelSubclassRuleKind.AbsentPfam).Select(r => r.Accession)),
            initial = draft, use = draft?.Accepted ?? false,
            family = draft?.FamilyKey.Length > 0 ? draft.FamilyKey : source.FamilyKey,
            abbreviation = draft?.Key.Length > 0 ? draft.Key : source.Key,
            name = draft?.DisplayName.Length > 0 ? draft.DisplayName : source.DisplayName,
        };
        foreach (var rule in source.Rules)
            row.Rules.Add(ImportSubclassRuleChoice.FromCore(rule, draft?.Rules.FirstOrDefault(r => r.RuleId == rule.RuleId)));
        foreach (var missing in draft?.Rules.Where(r => source.Rules.All(rule => rule.RuleId != r.RuleId)) ?? Enumerable.Empty<PanelSubclassRuleAcceptance>())
            row.Rules.Add(ImportSubclassRuleChoice.Unavailable(missing));
        return row;
    }

    public PanelSubclassAcceptance Capture()
    {
        var captured = new PanelSubclassAcceptance
        {
            ProposalId = ProposalId, Accepted = Use,
            FamilyKey = !familyEdited && initial != null ? initial.FamilyKey : Family,
            Key = !keyEdited && initial != null ? initial.Key : Abbreviation,
            DisplayName = !nameEdited && initial != null ? initial.DisplayName : Name,
        };
        if (initial == null) captured.Rules = Rules.Select(rule => rule.Capture()).ToList();
        else
        {
            // Keep omitted selections and nullable polarity exactly until the user edits that rule.
            captured.Rules = initial.Rules.Select(seed => Rules.First(rule => rule.RuleId == seed.RuleId).Capture()).ToList();
            captured.Rules.AddRange(Rules.Where(rule => rule.Reviewed && initial.Rules.All(seed => seed.RuleId != rule.RuleId)).Select(rule => rule.Capture()));
        }
        return captured;
    }
    public static IReadOnlyList<PanelSubclassAcceptance> Capture(IEnumerable<ImportSubclassChoice> choices) =>
        (choices ?? throw new ArgumentNullException(nameof(choices))).Select(choice => choice.Capture()).ToArray();
}

public sealed class ImportSubclassRuleChoice : ScreenViewModel
{
    private bool use, confirmed, reviewed, kindEdited;
    private ImportSubclassRuleKind kind;
    private PanelSubclassRuleKind? initialKind;
    public static IReadOnlyList<ImportSubclassRuleOption> KindOptions { get; } = new[]
    {
        new ImportSubclassRuleOption("Required Pfam", ImportSubclassRuleKind.RequiredPfam),
        new ImportSubclassRuleOption("Absent Pfam", ImportSubclassRuleKind.AbsentPfam),
    };
    public string RuleId { get; init; } = "";
    public string Accession { get; init; } = "";
    public string Name { get; init; } = "";
    public string Source { get; init; } = "";
    public string Reason { get; init; } = "";
    public bool IsUncertain { get; init; }
    public object? SourceProposal { get; init; }
    public IReadOnlyList<string> CandidateAccessions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ImportSubclassSource> SourceRows { get; init; } = Array.Empty<ImportSubclassSource>();
    public ObservableCollection<string> Problems { get; } = new();
    public string Candidates => CandidateAccessions.Count == 0 ? ""
        : "Candidate accessions: " + string.Join("; ", CandidateAccessions);
    public string Label => Accession.Length > 0 ? Accession + (Name.Length > 0 ? " / " + Name : "")
        : Name.Length > 0 ? Name + " / accession unresolved" : "Accession unresolved";
    public string KindLabel => Kind == ImportSubclassRuleKind.RequiredPfam ? "Required Pfam"
        : Kind == ImportSubclassRuleKind.AbsentPfam ? "Absent Pfam" : "Rule kind unresolved";
    public string SourceStatus => SourceRows.Count > 0 ? "Original source cells" : "No source row was supplied for this rule.";
    public bool Reviewed => reviewed;
    public string State => Confirmed ? "Confirmed for import" : reviewed ? Use ? "Included in draft" : "Left out of draft"
        : Use ? "Proposed in draft" : "Not selected";
    public bool Use { get => use; set { if (Set(ref use, value)) Touch(); } }
    public bool Confirmed { get => confirmed; set { if (Set(ref confirmed, value)) Changed(nameof(State)); } }
    public ImportSubclassRuleKind Kind { get => kind; set { if (Set(ref kind, value)) { kindEdited = true; Touch(); Changed(nameof(KindLabel)); } } }
    public bool HasProblems => Problems.Count > 0;
    public string ProblemText => string.Join("\n", Problems);
    public ImportSubclassRuleChoice() => Problems.CollectionChanged += (_, _) => { Changed(nameof(ProblemText)); Changed(nameof(HasProblems)); };
    private void Touch() { reviewed = true; Changed(nameof(Reviewed)); Changed(nameof(State)); }
    public void Reject() { Use = false; Touch(); }
    public PanelSubclassRuleAcceptance Capture() => new()
    {
        RuleId = RuleId, Accepted = Use, Kind = kindEdited ? (PanelSubclassRuleKind)(int)Kind : initialKind,
    };
    public static ImportSubclassRuleChoice FromCore(PanelSubclassRuleProposal rule) => FromCore(rule, null);
    internal static ImportSubclassRuleChoice FromCore(PanelSubclassRuleProposal rule, PanelSubclassRuleAcceptance? draft)
    {
        var source = SubclassReviewCopies.Rule(rule);
        return new()
        {
            RuleId = source.RuleId, Accession = source.Accession, Name = source.Name, Reason = source.Reason,
            IsUncertain = source.IsUncertain, CandidateAccessions = source.CandidateAccessions.ToArray(), SourceProposal = source,
            SourceRows = SubclassReviewCopies.Sources(source.SourceRows),
            Source = string.Join("; ", source.SourceRows.Select(r => r.SheetName + ", row " + r.RowNumber)),
            use = draft?.Accepted ?? false, kind = (ImportSubclassRuleKind)(int)(draft?.Kind ?? source.Kind), initialKind = draft?.Kind,
        };
    }
    internal static ImportSubclassRuleChoice Unavailable(PanelSubclassRuleAcceptance draft) => new()
    {
        RuleId = draft.RuleId, Name = "Rule no longer in the proposal", IsUncertain = true,
        Reason = "Refresh the subclass proposals before using this previous draft rule.",
        use = draft.Accepted, kind = (ImportSubclassRuleKind)(int)(draft.Kind ?? PanelSubclassRuleKind.RequiredPfam), initialKind = draft.Kind,
    };
}

internal static class SubclassReviewCopies
{
    internal static PanelSubclassProposal Proposal(PanelSubclassProposal source) => new()
    {
        ProposalId = source.ProposalId, ExpectedNameId = source.ExpectedNameId, FamilyKey = source.FamilyKey,
        Key = source.Key, DisplayName = source.DisplayName, HasMatchedTableRow = source.HasMatchedTableRow,
        IsUncertain = source.IsUncertain, Reason = source.Reason, CandidateFamilyKeys = source.CandidateFamilyKeys.ToList(),
        Rules = source.Rules.Select(Rule).ToList(), SourceRows = source.SourceRows.Select(Row).ToList(),
        Notes = source.Notes.Select(note => new FamilyPanelText { Name = note.Name, Text = note.Text }).ToList(),
    };
    internal static PanelSubclassRuleProposal Rule(PanelSubclassRuleProposal source) => new()
    {
        RuleId = source.RuleId, Accession = source.Accession, Name = source.Name, Kind = source.Kind,
        CandidateAccessions = source.CandidateAccessions.ToList(), SourceRows = source.SourceRows.Select(Row).ToList(),
        IsUncertain = source.IsUncertain, Reason = source.Reason,
    };
    private static PanelSubclassSourceRow Row(PanelSubclassSourceRow source) => new()
        { SheetName = source.SheetName, RowNumber = source.RowNumber, Cells = new(source.Cells, source.Cells.Comparer) };
    internal static IReadOnlyList<ImportSubclassSource> Sources(IEnumerable<PanelSubclassSourceRow> rows) => rows.Select(row =>
        new ImportSubclassSource(row.SheetName, row.RowNumber, row.Cells.Select(cell => new ImportSubclassCell(cell.Key, cell.Value)).ToArray())).ToArray();
    internal static PanelSubclassAcceptance Acceptance(PanelSubclassAcceptance source) => new()
    {
        ProposalId = source.ProposalId, Accepted = source.Accepted, FamilyKey = source.FamilyKey, Key = source.Key, DisplayName = source.DisplayName,
        Rules = source.Rules.Select(rule => new PanelSubclassRuleAcceptance { RuleId = rule.RuleId, Accepted = rule.Accepted, Kind = rule.Kind }).ToList(),
    };
}
