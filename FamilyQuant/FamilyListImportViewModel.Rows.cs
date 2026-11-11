using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

public sealed partial class FamilyListImportViewModel
{
    private string decisionHeader = "";
    private ImportSpecialRow? selectedSpecial;
    public ImportSpecialRow? SelectedSpecialRow { get => selectedSpecial; set { if (Set(ref selectedSpecial, value)) { Changed(nameof(CanUseRowChoice)); Changed(nameof(AvailableRowOptions)); } } }
    public IReadOnlyList<ImportRowOption> AvailableRowOptions => SelectedSpecialRow?.SeveralNames == true
        ? new[] { new ImportRowOption("Keep as one family", FamilyPanelRowAction.Import) }.Concat(RowOptions.Where(o => o.Value is FamilyPanelRowAction.Split or FamilyPanelRowAction.ReportUnderOthers or FamilyPanelRowAction.Skip)).ToArray()
        : RowOptions.Where(o => o.Value is FamilyPanelRowAction.Import or FamilyPanelRowAction.Skip or FamilyPanelRowAction.MergeInto).ToArray();
    public bool CanUseRowChoice => !IsBusy && !Imported && SelectedSpecialRow?.Action != null;
    public bool HasDecisions => Decisions.Count > 0;
    public bool HasCustomDecisions => Decisions.Any(decision => !decision.IsProgramDecision);
    public bool HasColors => SourceColorChoices.Count > 0;
    public bool HasSourceFills => Preview?.Rows.Any(r => r.Cells.Any(c => c.Fill.Length > 0)) == true;
    public string DecisionSummary => HasDecisions ? Decisions.Count.ToString("N0", CultureInfo.CurrentCulture)
        + (Decisions.Count == 1 ? " value" : " values")
        + (Decisions.All(d => d.Confirmed) ? " confirmed" : " to review") : "Quantify every family";
    public string SpecialSummary => SpecialRows.Count(r => r.Blocking) is var count && count == 0 ? "Every special row has a choice"
        : count.ToString("N0", CultureInfo.CurrentCulture) + (count == 1 ? " row needs a choice" : " rows need a choice");
    private bool MappingValid => table != null && Columns.Where(c => c.Required).All(c => c.HasChoice && table.Headers.Contains(c.Header, StringComparer.Ordinal))
        && Columns.Where(c => c.HasChoice && c.Role != PanelImportFields.ColorCategory).GroupBy(c => c.Header, StringComparer.Ordinal).All(g => g.Count() == 1);

    private void BuildColumns(PanelSpreadsheetTable source)
    {
        var roles = new[] {
            (PanelImportFields.FamilyKey, "Abbreviation", true), (PanelImportFields.DisplayName, "Family name", true),
            (PanelImportFields.OtherName, "Other-language name (optional)", false), (PanelImportFields.Notes, "Notes (optional)", false),
            (PanelImportFields.Pfam, "Pfam codes (optional)", false), (WorkbookImportSummaryFields.Subclasses, "Subclass names (optional)", false), (PanelImportFields.Decision, "Decision (optional)", false),
            (PanelImportFields.UniProtFamilies, "UniProt family (optional)", false),
            (PanelImportFields.ColorCategory, "Colour category (optional)", false) };
        foreach (var (role, label, required) in roles.Where(role => WorkbookReviewEnabled || role.Item1 != WorkbookImportSummaryFields.Subclasses))
        {
            var column = new ImportColumnChoice(role, label, required, source.Headers, header => source.Rows.Select(r => r.Cells.GetValueOrDefault(header, ""))
                .FirstOrDefault(text => text.Length > 0) ?? "") { Header = choices.ColumnHeaders.GetValueOrDefault(role, "") };
            column.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName != nameof(ImportColumnChoice.Header) || updating || Imported) return;
                if (WorkbookReviewEnabled) reviewedWorkbookColumns.Add(role);
                if (role is PanelImportFields.FamilyKey or PanelImportFields.DisplayName or PanelImportFields.OtherName)
                { choices.RowChoices?.Clear(); matchesFingerprint = ""; }
                Recheck();
            };
            Columns.Add(column);
        }
        decisionHeader = "";
    }

    private string Raw(PanelSpreadsheetRow? row, string role) => row == null ? "" : row.Cells.GetValueOrDefault(choices.ColumnHeaders.GetValueOrDefault(role, ""), "");

    private void Recheck()
    {
        if (updating || table == null || Imported) { Refresh(); return; }
        updating = true;
        try
        {
            choices.ColumnHeaders = Columns.Where(c => c.HasChoice).ToDictionary(c => c.Role, c => c.Header, StringComparer.Ordinal);
            if (WorkbookReviewEnabled)
                foreach (var column in Columns.Where(c => reviewedWorkbookColumns.Contains(c.Role) && !c.HasChoice)) choices.ColumnHeaders[column.Role] = "";
            string nextDecision = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Decision, "");
            if (nextDecision != decisionHeader)
            {
                decisionHeader = nextDecision; Decisions.Clear();
                foreach (var proposed in importer.ProposeDecisions(table, choices))
                {
                    var legend = WorkbookDecision(proposed.SourceValue);
                    var row = new ImportDecisionChoice { Source = proposed.SourceValue,
                        Rows = string.Join(", ", proposed.RowNumbers), SourceRows = proposed.RowNumbers.ToArray(), Reason = legend?.Reason is { Length: > 0 } reason ? reason : proposed.Reason,
                        Meaning = legend?.LegendMeaning ?? "",
                        Decision = legend == null ? HasWorkbookMap && !proposed.Exact ? null : proposed.Mapping.Decision : legend.ProposedMapping?.Decision,
                        MergeTarget = legend?.ProposedMapping?.MergeTargetFamilyKey ?? proposed.Mapping.MergeTargetFamilyKey,
                        IsProgramDecision = proposed.Exact && (legend == null || legend.ExactProgramDecision),
                        Confirmed = proposed.Exact && (legend == null || legend.ExactProgramDecision) };
                    row.PropertyChanged += (_, _) => { if (!updating) { if (WorkbookReviewEnabled) Recheck(); else Refresh(); Changed(nameof(DecisionSummary)); } };
                    Decisions.Add(row);
                }
                ReviewingDecisions = Decisions.Any(d => !d.Confirmed);
            }
            foreach (var color in SourceColorChoices.Where(c => c.Label.Trim().Length == 0)) color.Label = color.ColorName;
            choices.ColorLabels = SourceColorChoices.Count == 0 ? null : SourceColorChoices.Select(c => new FamilyPanelColorLabel { Color = c.Color, Label = c.Label.Trim() }).ToList();
            check = importer.Check(table, choices, Decisions.Where(d => (WorkbookReviewEnabled || d.Confirmed) && d.Decision != null).Select(d => d.Capture()).ToArray(), PanelName.Trim());
            var sourceFills = check.Colors;
            var oldColors = SourceColorChoices.ToDictionary(c => c.Color, c => c.Label, StringComparer.OrdinalIgnoreCase);
            if (!SourceColorChoices.Select(c => c.Color).SequenceEqual(sourceFills.Select(c => c.Color), StringComparer.OrdinalIgnoreCase))
            {
                SourceColorChoices.Clear();
                foreach (var color in sourceFills)
                {
                    var row = new ImportColorChoice { Color = color.Color, ColorName = color.ColorName,
                        Meaning = WorkbookColor(color.Color)?.Meaning ?? "", Rows = string.Join(", ", color.RowNumbers), Label = oldColors.GetValueOrDefault(color.Color,
                            DefaultColorLabel(color)) };
                    row.PropertyChanged += (_, change) => { if (!updating && change.PropertyName == nameof(ImportColorChoice.Label)) Recheck(); };
                    SourceColorChoices.Add(row);
                }
                choices.ColorLabels = SourceColorChoices.Select(c => new FamilyPanelColorLabel { Color = c.Color, Label = c.Label.Trim() }).ToList();
                check = importer.Check(table, choices, Decisions.Where(d => (WorkbookReviewEnabled || d.Confirmed) && d.Decision != null).Select(d => d.Capture()).ToArray(), PanelName.Trim());
            }
            RebuildSpecialRows();
            Changed(nameof(HasDecisions)); Changed(nameof(HasCustomDecisions)); Changed(nameof(HasColors)); Changed(nameof(DecisionSummary)); Changed(nameof(SpecialSummary));
        }
        finally { updating = false; Refresh(); }
    }

    public void ConfirmDecisions()
    {
        if (Imported || IsBusy) return;
        if (Decisions.Any(d => d.Decision == null || d.IsMerge && d.MergeTarget.Trim().Length == 0))
        { Status = "Choose an action for every decision value and an abbreviation for each merge."; return; }
        updating = true; foreach (var decision in Decisions) decision.Confirmed = true; updating = false;
        Recheck();
        var invalid = check.Problems.FirstOrDefault(p => p.Kind is PanelImportProblemKind.DecisionMappingInvalid or PanelImportProblemKind.MergeTargetMissing or PanelImportProblemKind.MergeCycle);
        if (invalid != null)
        {
            updating = true; foreach (var decision in Decisions.Where(d => d.Source == invalid.Value || invalid.Value.Length == 0)) decision.Confirmed = false; updating = false;
            Status = invalid.Message;
        }
        else { Status = "Decision values confirmed."; ReviewingDecisions = false; }
        Changed(nameof(DecisionSummary));
        Refresh();
    }

    private void RebuildSpecialRows()
    {
        if (table == null) return;
        int oldSelection = SelectedSpecialRow?.RowNumber ?? 0;
        var rowNumbers = check.Problems.Where(p => p.RowNumber > 0).Select(p => p.RowNumber)
            .Concat(check.Rows.Where(r => r.ChosenByUser || r.ProposedParts.Count > 0).Select(r => r.RowNumber)).Distinct().OrderBy(n => n).ToArray();
        SpecialRows.Clear();
        foreach (int number in rowNumbers)
        {
            var source = table.Rows.FirstOrDefault(r => r.RowNumber == number);
            var problems = check.Problems.Where(p => p.RowNumber == number).OrderByDescending(p => p.Severity == PanelImportSeverity.Blocking).ToArray();
            var problem = problems.FirstOrDefault();
            var outcome = check.Rows.FirstOrDefault(r => r.RowNumber == number);
            var answer = choices.RowChoices?.FirstOrDefault(c => c.RowNumber == number);
            bool severalNeedsChoice = outcome?.ProposedParts.Count > 0 && answer == null;
            var row = new ImportSpecialRow { RowNumber = number, Abbreviation = Raw(source, PanelImportFields.FamilyKey),
                Original = source == null ? "Empty row" : string.Join("\n", source.Cells.Select(c => c.Key + ": " + c.Value)),
                Message = string.Join("\n", problems.Select(p => p.Message).DefaultIfEmpty("Your choice is ready.")),
                Kind = outcome?.ProposedParts.Count > 0 ? nameof(PanelImportProblemKind.SeveralFamilies) : problem?.Kind.ToString() ?? "Chosen",
                Blocking = severalNeedsChoice || problems.Any(p => p.Severity == PanelImportSeverity.Blocking),
                Chosen = answer != null, Key = !string.IsNullOrWhiteSpace(answer?.Key) ? answer.Key : !string.IsNullOrWhiteSpace(problem?.Suggestion) ? problem.Suggestion : Raw(source, PanelImportFields.FamilyKey),
                Name = !string.IsNullOrWhiteSpace(answer?.Name) ? answer.Name : Raw(source, PanelImportFields.DisplayName), MergeTarget = answer?.MergeTargetFamilyKey ?? "",
                Action = answer?.Action ?? (problem?.Severity == PanelImportSeverity.Note && !severalNeedsChoice
                    ? FamilyPanelRowAction.Skip : severalNeedsChoice ? FamilyPanelRowAction.Split : FamilyPanelRowAction.Import) };
            foreach (var part in answer?.Parts ?? outcome?.ProposedParts ?? new List<FamilyPanelSplitPart>())
                row.Parts.Add(new ImportRowPart { Key = part.Key, Name = part.Name });
            row.PropertyChanged += (_, _) => { Changed(nameof(CanUseRowChoice)); };
            SpecialRows.Add(row);
        }
        SelectedSpecialRow = SpecialRows.FirstOrDefault(r => r.RowNumber == oldSelection) ?? SpecialRows.FirstOrDefault(r => r.Blocking) ?? SpecialRows.FirstOrDefault();
    }

    public void UseRowChoice()
    {
        if (!CanUseRowChoice || SelectedSpecialRow == null) return;
        var answer = SelectedSpecialRow.Capture();
        choices.RowChoices ??= new(); choices.RowChoices.RemoveAll(c => c.RowNumber == answer.RowNumber); choices.RowChoices.Add(answer);
        Recheck();
        Status = check.Problems.FirstOrDefault(p => p.RowNumber == answer.RowNumber && p.Severity == PanelImportSeverity.Blocking)?.Message ?? "Row choice saved in the draft.";
    }

    public string Summary
    {
        get
        {
            if (check.Panel is not { } panel) return SpecialSummary;
            int split = check.Rows.Count(r => r.Action == FamilyPanelRowAction.Split);
            int skipped = check.Rows.Where(r => r.Action == FamilyPanelRowAction.Skip).Select(r => r.RowNumber)
                .Concat(SpecialRows.Where(r => r.Action == FamilyPanelRowAction.Skip && !r.Blocking).Select(r => r.RowNumber)).Distinct().Count();
            int renamed = (choices.RowChoices ?? new()).Count(c => c.Action == FamilyPanelRowAction.Import && c.Key.Length > 0
                && c.Key != Raw(table?.Rows.FirstOrDefault(r => r.RowNumber == c.RowNumber), PanelImportFields.FamilyKey).Trim());
            return FamilyBreakdown(panel) + "; " + Rows(split, "split") + "; "
                + Rows(skipped, "skipped") + "; " + renamed.ToString("N0", CultureInfo.CurrentCulture) + (renamed == 1 ? " abbreviation renamed" : " abbreviations renamed")
                + "; matching: " + Families(MatchFamilies.Count(f => f.Choice == "Confirmed")) + " confirmed, "
                + MatchFamilies.Count(f => f.Choice == "Kept from previous panel").ToString("N0", CultureInfo.CurrentCulture) + " kept, "
                + MatchFamilies.Count(f => f.Choice == "Skipped").ToString("N0", CultureInfo.CurrentCulture) + " skipped";
        }
    }
    public string ImportedFamilyCount
    {
        get
        {
            var panel = committedPanel ?? check.Panel;
            return panel == null ? "0 families imported" : FamilyBreakdown(panel) + " imported";
        }
    }

    private static string FamilyBreakdown(FamilyPanel panel)
    {
        int quantify = panel.Families.Count(f => f.Decision == FamilyPanelDecision.Quantify);
        int merged = panel.Families.Count(f => f.Decision == FamilyPanelDecision.MergeIntoAnotherFamily);
        int others = panel.Families.Count(f => f.Decision == FamilyPanelDecision.ReportUnderOthers);
        int excluded = panel.Families.Count(f => f.Decision == FamilyPanelDecision.NotAFamily);
        var extras = new List<string>();
        if (merged > 0) extras.Add(merged.ToString("N0", CultureInfo.CurrentCulture) + " merged");
        if (others > 0) extras.Add(others.ToString("N0", CultureInfo.CurrentCulture) + " reported under others");
        if (excluded > 0) extras.Add(excluded.ToString("N0", CultureInfo.CurrentCulture) + " not a family");
        string text = Families(quantify) + " to quantify";
        if (extras.Count > 0) text += " (plus " + string.Join(", ", extras) + ")";
        return text;
    }

    private static string SummaryDecision(FamilyPanelFamily family) =>
        family.Decision == FamilyPanelDecision.MergeIntoAnotherFamily && family.MergeTargetFamilyKey.Length > 0
            ? "Merge into " + family.MergeTargetFamilyKey
            : DecisionOptions.First(o => o.Value == family.Decision).Label;

    private string SummaryMatches(FamilyPanelFamily family) =>
        MatchFamilies.FirstOrDefault(m => m.Key == family.Key) is { MissingPfam: true } ? "No Pfam code; no proteins until it has one"
        : MatchFamilies.FirstOrDefault(m => m.Key == family.Key)?.Choice
        ?? (family.Decision is FamilyPanelDecision.MergeIntoAnotherFamily or FamilyPanelDecision.NotAFamily ? "Not needed" : "Not reviewed");

    private static string Rows(int count, string action) => count.ToString("N0", CultureInfo.CurrentCulture) + (count == 1 ? " row " : " rows ") + action;
    private static string Families(int count) => count.ToString("N0", CultureInfo.CurrentCulture) + (count == 1 ? " family" : " families");

    public IReadOnlyList<ImportSummaryFamily> SummaryFamilies => (committedPanel ?? check.Panel)?.Families.Select(f => new ImportSummaryFamily(f.Key, f.DisplayName,
        PanelImportFields.OtherNameText(committedPanel ?? check.Panel!, f), SummaryDecision(f),
        SummaryMatches(f), string.Join("\n", f.TextFields.Select(t => t.Name + ": " + t.Text)),
        SourceColorCategory(committedPanel ?? check.Panel!, f))).ToArray()
        ?? Array.Empty<ImportSummaryFamily>();

    private static string SourceColorCategory(FamilyPanel panel, FamilyPanelFamily family)
    {
        var import = panel.Import;
        string header = import?.ColumnHeaders.GetValueOrDefault(PanelImportFields.ColorCategory, "") ?? "";
        if (header.Length == 0) return "";
        string name = import?.ColorCategoryName is { Length: > 0 } category ? category : header + " colour";
        return string.Join("; ", family.TextFields.Where(f => f.Name == name).Select(f => f.Text).Distinct(StringComparer.Ordinal));
    }
}
