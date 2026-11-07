using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class FamilyListImportViewModel
{
    private ImportMatchFamily? selectedMatchFamily;
    public ImportMatchFamily? SelectedMatchFamily { get => selectedMatchFamily; set { if (Set(ref selectedMatchFamily, value)) { Changed(nameof(MatchStatus)); Changed(nameof(CanConfirmMatches)); Changed(nameof(SkipFamilyLabel)); } } }
    public bool CanConfirmMatches => !IsBusy && !Imported && SelectedMatchFamily?.Suggestions.Any(s => s.Use) == true;
    public string MatchingSourceStatus => matcher?.SourceStatus ?? "Stored matching evidence unavailable";
    public string MatchingChangeNotice => matcher?.ChangeNotice ?? "";
    public string SkipFamilyLabel => SelectedMatchFamily?.PreviouslyConfirmed == true ? "Keep previous matches" : "Skip this family";
    public string SkipAllLabel => MatchFamilies.Any(f => f.PreviouslyConfirmed) ? "Keep previous, skip new matches" : "Continue without matches";
    public string MatchStatus => SelectedMatchFamily == null ? "No families to review."
        : SelectedMatchFamily.Suggestions.Count == 0 ? "No matching suggestions are available."
        : "Counts are database entries for each suggestion; overlapping suggestions are not added together.";

    private void PrepareMatches()
    {
        if (check.Panel == null) return;
        string fingerprint = System.Text.Json.JsonSerializer.Serialize(check.Panel.Families.Select(f => new { f.Key, f.DisplayName, f.Names, f.Decision, f.MergeTargetFamilyKey }));
        if (fingerprint == matchesFingerprint) return;
        matchesFingerprint = fingerprint;
        var detached = FamilyPanelValidation.Clone(check.Panel);
        var suggestions = matcher?.Suggest(FamilyClusterFile.FromJsonText(transaction.Project.ToJsonText()), detached)
            ?? Array.Empty<ImportMatchFamily>();
        var previous = MatchFamilies.ToDictionary(f => f.Key, StringComparer.Ordinal);
        MatchFamilies.Clear();
        foreach (var family in check.Panel.Families.Where(f => FamilyPanelValidation.ResolveFamilyKey(check.Panel, f.Key) == f.Key))
        {
            var supplied = suggestions.FirstOrDefault(f => f.Key == family.Key);
            string signature = System.Text.Json.JsonSerializer.Serialize(new[] { family.DisplayName }.Concat(family.Names.Select(n => n.Text)));
            if (previous.TryGetValue(family.Key, out var retained) && retained.NamesSignature == signature)
            { MatchFamilies.Add(retained); continue; }
            var row = new ImportMatchFamily { Key = family.Key, Name = family.DisplayName, NamesSignature = signature,
                Suggestions = supplied?.Suggestions ?? Array.Empty<ImportEvidenceMatch>(), PreviouslyConfirmed = supplied?.PreviouslyConfirmed == true,
                Choice = supplied?.PreviouslyConfirmed == true ? "Kept from previous panel" : "Needs a choice" };
            if (!row.PreviouslyConfirmed) foreach (var suggestion in row.Suggestions) suggestion.Use = false;
            foreach (var suggestion in row.Suggestions) suggestion.PropertyChanged += (_, _) =>
            { row.Choice = "Needs a choice"; Changed(nameof(CanConfirmMatches)); Refresh(); };
            row.PropertyChanged += (_, _) => Refresh();
            MatchFamilies.Add(row);
        }
        SelectedMatchFamily = MatchFamilies.FirstOrDefault();
        Changed(nameof(MatchStatus)); Changed(nameof(MatchingChangeNotice)); Refresh();
    }

    public void ConfirmMatches()
    {
        if (IsBusy || Imported || SelectedMatchFamily == null) return;
        if (!SelectedMatchFamily.Suggestions.Any(s => s.Use))
        { Status = "Select a stored-evidence match, or skip this family."; return; }
        if (SelectedMatchFamily.Suggestions.Where(s => s.Use).Any(s => string.IsNullOrWhiteSpace(s.Text) || s.Text.Any(char.IsControl)))
        { Status = "Enter a one-line stored-evidence value for each selected match."; return; }
        SelectedMatchFamily.Choice = "Confirmed"; SelectNextMatch();
    }

    public void SkipMatch()
    {
        if (IsBusy || Imported || SelectedMatchFamily == null) return;
        RestoreOrSkip(SelectedMatchFamily); SelectNextMatch();
    }

    public void SkipAllMatches()
    {
        if (IsBusy || Imported) return;
        foreach (var family in MatchFamilies) RestoreOrSkip(family);
        Status = MatchFamilies.Any(f => f.PreviouslyConfirmed) ? "Previous confirmed matches kept; new matching skipped." : "Evidence matching skipped for this import; review it in Panel checklist."; Refresh();
    }

    private static void RestoreOrSkip(ImportMatchFamily family)
    {
        foreach (var suggestion in family.Suggestions)
        { if (suggestion.PreviousValue != null) suggestion.Text = suggestion.PreviousValue; suggestion.Use = suggestion.PreviousValue != null; }
        family.Choice = family.PreviouslyConfirmed ? "Kept from previous panel" : "Skipped";
    }

    private void SelectNextMatch()
    {
        SelectedMatchFamily = MatchFamilies.FirstOrDefault(f => !f.Decided) ?? SelectedMatchFamily;
        Status = MatchFamilies.Count(f => !f.Decided).ToString("N0", CultureInfo.CurrentCulture) + " families need a matching choice.";
        Refresh();
    }

    private async Task<bool> CommitAsync()
    {
        if (table == null || Imported) return false;
        if (!transaction.CheckCurrent(out string changed)) { Status = changed; return false; }
        Recheck();
        if (check.Panel == null) { Status = check.Problems.FirstOrDefault(p => p.Severity == PanelImportSeverity.Blocking)?.Message ?? "Review the import again."; return false; }
        var panel = FamilyPanelValidation.Clone(check.Panel);
        string expectedSource = table.SourceSha256;
        operation?.Dispose(); operation = new CancellationTokenSource();
        var token = operation.Token;
        IsBusy = true; Status = "Checking the workbook before import...";
        try
        {
            var current = await Task.Run(() => reader.ReadPreview(path, sheet), token);
            token.ThrowIfCancellationRequested();
            if (current.SourceSha256 != expectedSource || current.Problems.Any(p => p.Severity == PanelImportSeverity.Blocking))
            { Status = "The workbook changed or could not be read. Choose it again before importing."; return false; }
            if (KeepOtherSheets)
                foreach (string other in current.SheetNames.Where(name => name != sheet))
                {
                    var preview = await Task.Run(() => reader.ReadPreview(path, other), token);
                    token.ThrowIfCancellationRequested();
                    var problem = preview.Problems.FirstOrDefault(p => p.Severity == PanelImportSeverity.Blocking || p.Kind == PanelImportProblemKind.PreviewTruncated);
                    if (preview.SourceSha256 != expectedSource || problem != null)
                    { Status = "The sheet " + other + " could not be kept in full. Review it or choose to ignore the other sheets."; return false; }
                    var text = new StringBuilder();
                    foreach (var row in preview.Rows)
                        foreach (var cell in row.Cells.Where(c => c.Text.Length > 0))
                            text.Append(cell.ColumnLetter).Append(row.RowNumber.ToString(CultureInfo.InvariantCulture)).Append(": ").AppendLine(cell.Text);
                    panel.TextFields.Add(new FamilyPanelText { Name = "Source sheet: " + other, Text = text.ToString() });
                }
            token.ThrowIfCancellationRequested();
            if (matcher != null) panel = matcher.Accept(panel, MatchFamilies.ToArray());
            panel.TextFields.Add(new FamilyPanelText { Name = "Import matching choices", Text = string.Join("\n", MatchFamilies.Select(f => f.Key + ": " + f.Choice)) });
            FamilyPanelValidation.Validate(panel);
            if (!transaction.Import(panel, out string result)) { Status = result; return false; }
            committedPanel = FamilyPanelValidation.Clone(panel);
            Status = result + " File > Save keeps it with the project."; GoTo(6); return true;
        }
        catch (OperationCanceledException) { Status = "Import cancelled. Nothing was changed."; return false; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        { Status = "The family list was not imported. " + error.Message; return false; }
        finally { IsBusy = false; }
    }

    public void UndoImport()
    {
        if (transaction.Undo(out string result)) { committedPanel = null; GoTo(5); Recheck(); }
        Status = result; Refresh();
    }
}
