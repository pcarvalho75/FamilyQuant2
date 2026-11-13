using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using FamilyQuant.Operations;
using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class FamilyListImportViewModel
{
    public IReadOnlyList<ImportMatchFamily> PfamQuestions => MatchFamilies.Where(family => !family.Automatic).ToArray();
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

    private async Task<bool> PrepareMatchesAsync(FamilyPanel? panelOverride = null)
    {
        var panel = panelOverride ?? check.Panel;
        if (panel == null) return true;
        string fingerprint = MatchPanelFingerprint(panel);
        if (fingerprint == matchesFingerprint) return true;
        var detached = FamilyPanelValidation.Clone(panel);
        operation?.Cancel(); operation?.Dispose(); operation = new CancellationTokenSource();
        var token = operation.Token;
        int generation = ++readGeneration;
        IsBusy = true; Status = "Preparing evidence matches...";
        try
        {
            var suggestions = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                IReadOnlyList<ImportMatchFamily> result;
                if (matcher is FamilyListStoredEvidenceMatches stored && copyProjectOverride == null)
                    result = stored.Suggest(detached);
                else
                {
                    var context = new OperationContext(token);
                    var source = copyProjectOverride != null ? copyProjectOverride(transaction.Project, context)
                        : transaction.Project.DeepCopy(context);
                    token.ThrowIfCancellationRequested();
                    result = matcher?.Suggest(source, detached) ?? Array.Empty<ImportMatchFamily>();
                }
                token.ThrowIfCancellationRequested();
                return result;
            }, token);
            token.ThrowIfCancellationRequested();
            if (generation != readGeneration) return false;
            if ((panelOverride == null && !ReferenceEquals(check.Panel, panel)) || MatchPanelFingerprint(panel) != fingerprint)
            { Status = "Your family choices changed. Review the special rows again."; return false; }
            PublishMatches(panel, suggestions);
            matchesFingerprint = fingerprint;
            return true;
        }
        catch (OperationCanceledException)
        { if (generation == readGeneration) Status = "Evidence matching cancelled. Your choices are unchanged."; return false; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        { if (generation == readGeneration) Status = "Evidence matches could not be prepared. " + error.Message; return false; }
        finally { if (generation == readGeneration) IsBusy = false; }
    }

    private string MatchPanelFingerprint(FamilyPanel panel) => System.Text.Json.JsonSerializer.Serialize(new
    {
        PfamColumn = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Pfam, ""),
        Families = panel.Families.Select(f => new { f.Key, f.DisplayName, f.Names, f.Pfam, f.Decision, f.MergeTargetFamilyKey }),
    });

    private void PublishMatches(FamilyPanel panel, IReadOnlyList<ImportMatchFamily> suggestions)
    {
        var previous = MatchFamilies.ToDictionary(f => f.Key, StringComparer.Ordinal);
        var storedMatching = matcher is FamilyListStoredEvidenceMatches stored ? stored.WorkbookSnapshot : new PanelEvidenceMatching(transaction.Project);
        var vocabulary = storedMatching.RecognizedPfamDomains();
        string pfamHeader = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Pfam, "");
        MatchFamilies.Clear();
        foreach (var family in panel.Families.Where(f => FamilyPanelValidation.ResolveFamilyKey(panel, f.Key) == f.Key))
        {
            var supplied = suggestions.FirstOrDefault(f => f.Key == family.Key);
            string pfamText = family.TextFields.FirstOrDefault(field => field.Name == pfamHeader)?.Text ?? "";
            bool missing = family.Pfam.Count == 0 && string.IsNullOrWhiteSpace(pfamText);
            bool suppliedCodes = family.Pfam.Count > 0 && !FamilyPanelSpreadsheetImporter.NeedsPfamChoice(pfamText.Length > 0
                ? pfamText : string.Join("; ", family.Pfam.Select(domain => domain.Accession)));
            var proposed = (supplied?.Suggestions ?? Array.Empty<ImportEvidenceMatch>()).ToList();
            foreach (var domain in vocabulary.Where(domain => domain.Name.Length > 0
                && pfamText.Split(';', ',').Any(name => name.Trim().Equals(domain.Name, StringComparison.OrdinalIgnoreCase))))
            {
                var match = storedMatching.Coverage(PanelEvidenceMatchKind.Pfam, domain.Accession);
                if (!proposed.Any(row => row.Key == match.Key))
                    proposed.Add(new ImportEvidenceMatch { Key = match.Key, Text = match.Value, Type = "Pfam domain", Proteins = match.DatabaseTargets,
                        Reviewed = match.ReviewedTargets, Details = domain.Name });
            }
            bool automatic = suppliedCodes || missing || supplied?.PreviouslyConfirmed == true;
            string signature = System.Text.Json.JsonSerializer.Serialize(new { family.DisplayName, family.Names, family.Pfam, pfamText });
            var row = new ImportMatchFamily { Key = family.Key, Name = family.DisplayName, NamesSignature = signature,
                Suggestions = proposed, PreviouslyConfirmed = supplied?.PreviouslyConfirmed == true, Automatic = automatic, MissingPfam = missing,
                ShowAdditional = supplied?.ShowAdditional == true,
                Choice = supplied?.PreviouslyConfirmed == true ? "Kept from previous panel" : automatic ? "Confirmed" : "Needs a choice" };
            if (!row.PreviouslyConfirmed) foreach (var suggestion in row.Suggestions) suggestion.Use = !missing && suggestion.IsFromSheet;
            if (previous.TryGetValue(family.Key, out var retained) && retained.NamesSignature == signature)
            {
                var retainedChoices = retained.Suggestions.ToDictionary(s => s.Key, StringComparer.Ordinal);
                foreach (var suggestion in row.Suggestions)
                {
                    if (!retainedChoices.TryGetValue(suggestion.Key, out var choice)) continue;
                    suggestion.Text = choice.Text;
                    suggestion.Use = choice.Use;
                }
                row.Choice = retained.Choice;
                row.ShowAdditional = retained.ShowAdditional || row.Suggestions.Any(s => s.Use && s.IsAdditional);
            }
            foreach (var suggestion in row.Suggestions) suggestion.PropertyChanged += (_, _) =>
            { row.Choice = "Needs a choice"; Changed(nameof(CanConfirmMatches)); Refresh(); };
            row.PropertyChanged += (_, _) => Refresh();
            MatchFamilies.Add(row);
        }
        SelectedMatchFamily = PfamQuestions.FirstOrDefault();
        Changed(nameof(PfamQuestions)); Changed(nameof(MatchStatus)); Changed(nameof(MatchingChangeNotice)); Refresh();
    }

    public void ConfirmMatches(Func<string, bool>? confirmBroad = null)
    {
        if (IsBusy || Imported || SelectedMatchFamily == null) return;
        if (!SelectedMatchFamily.Suggestions.Any(s => s.Use))
        { Status = "Select a stored-evidence match, or skip this family."; return; }
        if (SelectedMatchFamily.Suggestions.Where(s => s.Use).Any(s => string.IsNullOrWhiteSpace(s.Text) || s.Text.Any(char.IsControl)))
        { Status = "Enter a one-line stored-evidence value for each selected match."; return; }
        var family = SelectedMatchFamily;
        var selected = family.Suggestions.Where(s => s.Use).ToArray();
        var values = selected.Select(s => (s.Key, s.Text)).ToArray();
        try
        {
            string warning = matcher?.ConfirmationWarning(selected) ?? "";
            if (warning.Length > 0 && confirmBroad?.Invoke(warning) != true) return;
            if (IsBusy || Imported || !ReferenceEquals(SelectedMatchFamily, family)
                || !values.SequenceEqual(family.Suggestions.Where(s => s.Use).Select(s => (s.Key, s.Text)))) return;
            if ((matcher?.ConfirmationWarning(selected) ?? "") != warning) return;
            family.Choice = "Confirmed"; SelectNextMatch();
        }
        catch (InvalidOperationException error) { Status = error.Message; }
    }

    public void SkipMatch()
    {
        if (IsBusy || Imported || SelectedMatchFamily == null) return;
        RestoreOrSkip(SelectedMatchFamily); SelectNextMatch();
    }

    public void SkipAllMatches()
    {
        if (IsBusy || Imported) return;
        foreach (var family in PfamQuestions) RestoreOrSkip(family);
        Status = MatchFamilies.Any(f => f.PreviouslyConfirmed) ? "Previous confirmed matches kept; new matching skipped." : "Evidence matching skipped for this import; review it in Family checklist."; Refresh();
    }

    public void UnconfirmMatches(string familyKey)
    {
        if (IsBusy || Imported || page != 5) return;
        var family = MatchFamilies.FirstOrDefault(row => row.Key == familyKey);
        if (family?.Choice is not ("Confirmed" or "Kept from previous panel")) return;
        foreach (var suggestion in family.Suggestions) suggestion.Use = false;
        family.Choice = "Unconfirmed";
        if (WorkbookReviewEnabled)
        {
            evidencePagePrepared = true;
            reviewedEvidence = MatchFamilies.Where(row => row.Decided).ToDictionary(row => row.Key,
                row => (IReadOnlyList<PanelEvidenceChoice>)row.Suggestions.Where(choice => choice.Use)
                    .Select(choice => PanelEvidenceMatching.ChoiceFromKey(choice.Key) with { Value = choice.Text }).ToArray(), StringComparer.Ordinal);
            RefreshWorkbookReview();
        }
        Status = "Matching unconfirmed for " + family.Name + ". The source codes stay in the list; Accept and import keeps this choice.";
        Refresh();
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
        Status = MatchingChoiceStatus;
        Refresh();
    }

    private string MatchingChoiceStatus => MatchFamilies.Count(f => !f.Decided) is var count && count == 0
        ? "Every family has a matching choice."
        : Families(count) + (count == 1 ? " needs a matching choice." : " need a matching choice.");

    private async Task<bool> CommitAsync(FamilyPanel? reviewedPanel = null)
    {
        if (table == null || Imported) return false;
        if (!transaction.CheckCurrent(out string changed)) { Status = changed; return false; }
        if (reviewedPanel == null) Recheck();
        var sourcePanel = reviewedPanel ?? check.Panel;
        if (sourcePanel == null) { Status = check.Problems.FirstOrDefault(p => p.Severity == PanelImportSeverity.Blocking)?.Message ?? "Review the import again."; return false; }
        var panel = FamilyPanelValidation.Clone(sourcePanel);
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
            if (reviewedPanel == null && workbookSnapshot != null && workbookSource != null)
                panel = workbookSource.CompletePanel(panel, workbookSnapshot);
            else if (reviewedPanel == null && KeepOtherSheets)
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
            if (reviewedPanel == null)
            {
                if (matcher != null) panel = matcher.Accept(panel, MatchFamilies.ToArray());
                panel.TextFields.Add(new FamilyPanelText { Name = "Import matching choices", Text = string.Join("\n", MatchFamilies.Select(f => f.Key + ": " + f.Choice)) });
            }
            foreach (var family in MatchFamilies.Where(row => row.Choice == "Unconfirmed"))
                panel.ConfirmedEvidenceMatches?.Remove(family.Key);
            FamilyPanelValidation.Validate(panel);
            if (!transaction.Import(panel, out string result)) { Status = result; return false; }
            committedPanel = FamilyPanelValidation.Clone(panel);
            GoTo(6); Status = result + " File > Save keeps it with the project."; return true;
        }
        catch (OperationCanceledException) { Status = "Import cancelled. Nothing was changed."; return false; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        { Status = "The family list was not imported. " + error.Message; return false; }
        finally { IsBusy = false; }
    }

    public void UndoImport()
    {
        try
        {
            if (transaction.Undo(out string result)) { committedPanel = null; GoTo(5); Recheck(); }
            Status = result;
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = "Undo could not run because temporary storage could not be used, and nothing changed. Check its space and permissions, then try Undo again.";
        }
        Refresh();
    }
}
