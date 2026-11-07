using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

public sealed class ImportColumnChoice : ScreenViewModel
{
    private string header = "";
    public string Role { get; }
    public string Label { get; }
    public bool Required { get; }
    public IReadOnlyList<string> Headers { get; }
    private readonly Func<string, string> example;
    public string Header { get => header; set { if (Set(ref header, value ?? "")) { Changed(nameof(Example)); Changed(nameof(HasChoice)); } } }
    public string Example => Header.Length == 0 ? Role == PanelImportFields.Decision ? "Quantify every family" : "Not used" : example(Header);
    public bool HasChoice => Header.Length > 0;
    public ImportColumnChoice(string role, string label, bool required, IReadOnlyList<string> headers, Func<string, string> example)
    { Role = role; Label = label; Required = required; Headers = required ? headers : new[] { "" }.Concat(headers).ToArray(); this.example = example; }
}

public sealed class ImportDecisionChoice : ScreenViewModel
{
    private FamilyPanelDecision? decision;
    private string target = "";
    private bool confirmed;
    public string Source { get; init; } = "";
    public string Rows { get; init; } = "";
    public string Reason { get; init; } = "";
    public FamilyPanelDecision? Decision { get => decision; set { if (Set(ref decision, value)) { Confirmed = false; Changed(nameof(IsMerge)); } } }
    public string MergeTarget { get => target; set { if (Set(ref target, value ?? "")) Confirmed = false; } }
    public bool IsMerge => Decision == FamilyPanelDecision.MergeIntoAnotherFamily;
    public bool Confirmed { get => confirmed; set => Set(ref confirmed, value); }
    public FamilyPanelDecisionMapping Capture() => new() { SourceValue = Source, Decision = Decision!.Value, MergeTargetFamilyKey = IsMerge ? MergeTarget.Trim() : "" };
}

public sealed class ImportColorChoice : ScreenViewModel
{
    private string label = "";
    public string Color { get; init; } = "";
    public string ColorName { get; init; } = "";
    public string Rows { get; init; } = "";
    public string Label { get => label; set => Set(ref label, value ?? ""); }
}

public sealed class ImportRowPart : ScreenViewModel
{
    private string key = "", name = "";
    public string Key { get => key; set => Set(ref key, value ?? ""); }
    public string Name { get => name; set => Set(ref name, value ?? ""); }
}

public sealed class ImportSpecialRow : ScreenViewModel
{
    private FamilyPanelRowAction? action;
    private string key = "", name = "", mergeTarget = "";
    public int RowNumber { get; init; }
    public string Abbreviation { get; init; } = "";
    public string Original { get; init; } = "";
    public string Message { get; init; } = "";
    public string Kind { get; init; } = "";
    public bool Blocking { get; init; }
    public bool Chosen { get; init; }
    public string Status => Blocking ? "Needs a choice" : Chosen ? "Ready" : "Skipped";
    public bool SeveralNames => Kind == nameof(PanelImportProblemKind.SeveralFamilies);
    public bool Duplicate => Kind == nameof(PanelImportProblemKind.DuplicateKey);
    public FamilyPanelRowAction? Action { get => action; set { if (Set(ref action, value)) { Changed(nameof(Splitting)); Changed(nameof(Merging)); } } }
    public bool Splitting => Action == FamilyPanelRowAction.Split;
    public bool Merging => Action == FamilyPanelRowAction.MergeInto;
    public string Key { get => key; set => Set(ref key, value ?? ""); }
    public string Name { get => name; set => Set(ref name, value ?? ""); }
    public string MergeTarget { get => mergeTarget; set => Set(ref mergeTarget, value ?? ""); }
    public ObservableCollection<ImportRowPart> Parts { get; } = new();
    public FamilyPanelRowChoice Capture() => new() { RowNumber = RowNumber, Action = Action ?? FamilyPanelRowAction.Import,
        Key = Key.Trim(), Name = Name.Trim(), MergeTargetFamilyKey = MergeTarget.Trim(), Parts = Parts.Select(p => new FamilyPanelSplitPart { Key = p.Key.Trim(), Name = p.Name.Trim() }).ToList() };
}

public sealed class ImportEvidenceMatch : ScreenViewModel
{
    private bool use;
    private string text = "", details = "";
    private int proteins, reviewed;
    public string Key { get; init; } = "";
    public string Text { get => text; set { if (Set(ref text, value ?? "") && Coverage != null) { var result = Coverage(text); Proteins = result.Proteins; Reviewed = result.Reviewed; Details = result.Details; } } }
    public string Type { get; init; } = "";
    public double NameCoverage { get; init; }
    public bool IsAdditional { get; init; }
    public bool IsFamilyLevelMatch { get; init; }
    public int Proteins { get => proteins; set => Set(ref proteins, value); }
    public int Reviewed { get => reviewed; set => Set(ref reviewed, value); }
    public string Details { get => details; set => Set(ref details, value ?? ""); }
    public bool Use { get => use; set => Set(ref use, value); }
    public Func<string, ImportMatchCoverage>? Coverage { get; init; }
    public string? PreviousValue { get; init; }
}
public sealed record ImportMatchCoverage(int Proteins, int Reviewed, string Details);

public sealed class ImportMatchFamily : ScreenViewModel
{
    private string choice = "Needs a choice";
    private bool showAdditional;
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string NamesSignature { get; init; } = "";
    public bool PreviouslyConfirmed { get; init; }
    public IReadOnlyList<ImportEvidenceMatch> Suggestions { get; init; } = Array.Empty<ImportEvidenceMatch>();
    public IReadOnlyList<ImportEvidenceMatch> PrimarySuggestions => Suggestions.Where(s => !s.IsAdditional).ToArray();
    public IReadOnlyList<ImportEvidenceMatch> AdditionalSuggestions => Suggestions.Where(s => s.IsAdditional).ToArray();
    public bool HasAdditionalSuggestions => Suggestions.Any(s => s.IsAdditional);
    public bool HasNoFamilyLevelMatch => !Suggestions.Any(s => s.IsFamilyLevelMatch);
    public bool ShowAdditional { get => showAdditional; set => Set(ref showAdditional, value); }
    public string Choice { get => choice; set => Set(ref choice, value); }
    public bool Decided => Choice != "Needs a choice";
}

/// <summary>Presentation adapter for the independently owned stored-evidence matching operation.</summary>
public interface IFamilyListImportMatches
{
    string SourceStatus { get; }
    string ChangeNotice { get; }
    IReadOnlyList<ImportMatchFamily> Suggest(FamilyClusterFile project, FamilyPanel panel);
    FamilyPanel Accept(FamilyPanel panel, IReadOnlyList<ImportMatchFamily> choices);
}

public sealed record ImportDecisionOption(string Label, FamilyPanelDecision Value);
public sealed record ImportRowOption(string Label, FamilyPanelRowAction Value);

public sealed record ImportSummaryFamily(string Key, string Name, string OtherName, string Decision, string Matches, string Notes, string Color);
public sealed record ImportWizardStep(int Index, string Label, bool Done, bool Current, bool Enabled);
