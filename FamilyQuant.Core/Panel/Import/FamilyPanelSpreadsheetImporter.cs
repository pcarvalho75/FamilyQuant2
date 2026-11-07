using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Panels.Import;

public sealed partial class FamilyPanelSpreadsheetImporter : IFamilyPanelSpreadsheetImporter, IFamilyPanelWorkbookImporter
{
    private static readonly Regex PfamAccession = new(@"(?<![A-Za-z0-9])PF\d{5}(?!\d)", RegexOptions.CultureInvariant);
    private static readonly Regex NamedPfam = new(@"(?<name>[^;,\r\n()]+?)\s*\(\s*(?<accession>PF\d{5})(?:\.\d+)?\s*\)", RegexOptions.CultureInvariant);
    private static readonly Regex Bullet = new(@"^[•·*\-]\s+", RegexOptions.CultureInvariant);
    private static readonly Regex LeadingLabel = new(@"^(?<label>[^:()\[\]]+):\s*(?<rest>\S.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex TrailingAbbreviation = new(@"\s*\(\s*(?<key>[\p{L}\p{N}][\p{L}\p{N}\-]{0,11})\s*\)\s*$", RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, string> RoleNames = new(StringComparer.Ordinal)
    {
        [PanelImportFields.FamilyKey] = "abbreviation", [PanelImportFields.DisplayName] = "name", [PanelImportFields.Decision] = "decision",
        [PanelImportFields.Pfam] = "Pfam domains", [PanelImportFields.OtherName] = "other-language name", [PanelImportFields.Notes] = "notes",
        [PanelImportFields.ColorCategory] = "colour category",
    };

    public FamilyPanel Import(PanelSpreadsheetTable table, FamilyPanelImport choices,
        IReadOnlyList<FamilyPanelDecisionMapping> decisions, string panelName = "")
    {
        var check = Check(table, choices, decisions, panelName);
        return check.Panel ?? throw new InvalidDataException(check.Problems.First(p => p.Severity == PanelImportSeverity.Blocking).Message);
    }

    public FamilyPanel Replay(string path, FamilyPanel previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var choice = previous.Import ?? throw new InvalidDataException("This panel has no stored import choices.");
        if (choice.SourceSha256.Length == 0) throw new InvalidDataException("The stored import has no source hash.");
        var table = new PanelSpreadsheetReader().ReadTable(path, choice.FamilySheet, choice.HeaderRowNumber);
        return Import(table, choice, previous.DecisionMappings, previous.Name);
    }

    public PanelImportCheck Check(PanelSpreadsheetTable table, FamilyPanelImport choices,
        IReadOnlyList<FamilyPanelDecisionMapping> decisions, string panelName = "")
    {
        ArgumentNullException.ThrowIfNull(table); ArgumentNullException.ThrowIfNull(choices); ArgumentNullException.ThrowIfNull(decisions);
        var check = new PanelImportCheck();
        var problems = check.Problems;
        problems.AddRange(Copy(table.Problems));
        bool graceful = choices.ImportVersion >= PanelImportRules.Graceful;
        string Letter(string header) { int i = table.Headers.IndexOf(header); return i >= 0 && i < table.ColumnLetters.Count ? table.ColumnLetters[i] : string.Empty; }
        void Add(PanelImportProblemKind kind, PanelImportSeverity severity, int row, string column, string value, string message, string suggestion = "", IEnumerable<int>? others = null) =>
            problems.Add(new PanelImportProblem { Kind = kind, Severity = severity, RowNumber = row, Column = column, ColumnLetter = Letter(column),
                Value = value, Message = message, Suggestion = suggestion, OtherRowNumbers = others?.ToList() ?? new() });
        void Block(PanelImportProblemKind kind, int row, string column, string value, string message, string suggestion = "", IEnumerable<int>? others = null) =>
            Add(kind, PanelImportSeverity.Blocking, row, column, value, message, suggestion, others);

        foreach (var run in Runs(table.EmptyRows))
            Add(PanelImportProblemKind.EmptyRow, PanelImportSeverity.Note, run[0], string.Empty, string.Empty,
                (run.Count == 1 ? "Row " + N(run[0]) + " is empty" : "Rows " + N(run[0]) + " to " + N(run[^1]) + " are empty") + " and " + (run.Count == 1 ? "was" : "were") + " skipped.",
                others: run.Skip(1));
        if (table.Headers.Count == 0 || table.Headers.Any(string.IsNullOrWhiteSpace) || table.Headers.Distinct(StringComparer.Ordinal).Count() != table.Headers.Count)
        {
            if (!problems.Any(p => p.Kind == PanelImportProblemKind.HeaderRowEmpty))
                Block(PanelImportProblemKind.HeaderRowEmpty, table.HeaderRowNumber, string.Empty, string.Empty,
                    "Row " + N(table.HeaderRowNumber) + " does not hold distinct, nonempty column names. Choose the row that does.");
            return check;
        }
        if (choices.FamilySheet.Length > 0 && choices.FamilySheet != table.SheetName || choices.HeaderRowNumber != table.HeaderRowNumber)
            Block(PanelImportProblemKind.PreviewMismatch, 0, string.Empty, string.Empty,
                "These choices were made for sheet \"" + choices.FamilySheet + "\" with names in row " + N(choices.HeaderRowNumber)
                + ", but the table read is sheet \"" + table.SheetName + "\" with names in row " + N(table.HeaderRowNumber) + ". Read the sheet again.");
        if (choices.SourceSha256.Length > 0 && !string.Equals(choices.SourceSha256, table.SourceSha256, StringComparison.OrdinalIgnoreCase))
            Block(PanelImportProblemKind.SourceChanged, 0, string.Empty, string.Empty,
                "The workbook \"" + table.SourceName + "\" changed after these choices were made, so they are not applied to it. Read the file again and check the choices.");

        string? Role(string role, bool required)
        {
            if (!choices.ColumnHeaders.TryGetValue(role, out var header) || string.IsNullOrWhiteSpace(header))
            {
                if (required) Block(PanelImportProblemKind.ColumnNotChosen, 0, string.Empty, string.Empty, "Choose the column that holds the " + RoleNames[role] + ".");
                return null;
            }
            if (table.Headers.Contains(header, StringComparer.Ordinal)) return header;
            Block(PanelImportProblemKind.ColumnMissing, 0, header, header, "The column \"" + OneLine(header) + "\" chosen for the " + RoleNames[role]
                + " is not in row " + N(table.HeaderRowNumber) + " of sheet \"" + table.SheetName + "\". Choose the column again.");
            return null;
        }
        string? keyHeader = Role(PanelImportFields.FamilyKey, true), nameHeader = Role(PanelImportFields.DisplayName, true);
        string? decisionHeader = Role(PanelImportFields.Decision, false), domainHeader = Role(PanelImportFields.Pfam, false);
        string? otherHeader = Role(PanelImportFields.OtherName, false), colorHeader = Role(PanelImportFields.ColorCategory, false);
        Role(PanelImportFields.Notes, false);
        if (decisions.Any(d => d == null || !Enum.IsDefined(d.Decision))
            || decisions.Select(d => d.SourceValue).Distinct(StringComparer.Ordinal).Count() != decisions.Count)
            Block(PanelImportProblemKind.DecisionMappingInvalid, 0, decisionHeader ?? string.Empty, string.Empty,
                "Each decision label needs exactly one meaning. Choose the meaning of each label again.");
        var mappings = decisions.Where(d => d != null).GroupBy(d => d.SourceValue, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var kept = table.Rows.Where(r => !Skipped(table, r, choices)).ToList();
        if (decisionHeader != null)
            check.DecisionValues = kept.Select(r => Value(r, decisionHeader)).Where(v => !graceful || !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToList();
        var colorLabels = (choices.ColorLabels ?? new()).Where(l => l != null).GroupBy(l => l.Color, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
        if (colorHeader != null)
            check.Colors = kept.Where(r => r.Fills.ContainsKey(colorHeader)).GroupBy(r => r.Fills[colorHeader], StringComparer.OrdinalIgnoreCase)
                .Select(g => new PanelImportColorUse { Color = g.Key, ColorName = PanelColorNames.Describe(g.Key), RowNumbers = g.Select(r => r.RowNumber).ToList(),
                    Label = colorLabels.GetValueOrDefault(g.Key, string.Empty) }).ToList();
        if (problems.Any(p => p.Severity == PanelImportSeverity.Blocking) || keyHeader == null || nameHeader == null) return check;

        var rowChoices = new Dictionary<int, FamilyPanelRowChoice>();
        var rowNumbers = table.Rows.Select(r => r.RowNumber).ToHashSet();
        foreach (var choice in choices.RowChoices ?? new())
        {
            if (choice == null || !Enum.IsDefined(choice.Action))
                Block(PanelImportProblemKind.ChoiceInvalid, choice?.RowNumber ?? 0, string.Empty, string.Empty, "A row choice is not one the program knows. Choose again.");
            else if (!rowNumbers.Contains(choice.RowNumber))
                Block(PanelImportProblemKind.ChoiceRowMissing, choice.RowNumber, string.Empty, string.Empty,
                    "A choice refers to row " + N(choice.RowNumber) + ", which is not a row of this table. Choose again.");
            else if (!rowChoices.TryAdd(choice.RowNumber, choice))
                Block(PanelImportProblemKind.ChoiceInvalid, choice.RowNumber, string.Empty, string.Empty, "Row " + N(choice.RowNumber) + " has two choices. Keep one.");
        }

        string Clean(string text) => graceful ? text.Trim() : text;
        var reserved = new HashSet<string>(table.Rows.Select(r => Clean(Value(r, keyHeader))).Where(k => !string.IsNullOrWhiteSpace(k)), StringComparer.Ordinal);
        foreach (var choice in rowChoices.Values) { if (choice.Key.Length > 0) reserved.Add(Clean(choice.Key)); }
        // A part's key: the abbreviation in its trailing parentheses, otherwise the row's abbreviation and its number.
        string PartKey(string rowKey, string part, int number)
        {
            string proposed = Abbreviation(part).Key is { Length: > 0 } abbreviation ? abbreviation
                : rowKey.Length > 0 ? rowKey + "-" + N(number) : OneLine(part);
            return reserved.Add(proposed) ? proposed : Suggest(proposed);
        }
        string Suggest(string key)
        {
            for (int n = 2; ; n++)
            {
                string candidate = key + "-" + N(n);
                if (reserved.Add(candidate)) return candidate;
            }
        }

        var families = new List<(FamilyPanelFamily Family, int Row)>();
        var firstRow = new Dictionary<string, int>(StringComparer.Ordinal);
        var unmapped = new Dictionary<string, int>(StringComparer.Ordinal);
        var unnamed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var blankDecisions = new List<int>();
        foreach (var row in table.Rows)
        {
            var outcome = new PanelImportRowOutcome { RowNumber = row.RowNumber };
            check.Rows.Add(outcome);
            rowChoices.TryGetValue(row.RowNumber, out var chosen);
            outcome.ChosenByUser = chosen != null;
            string? skipReason = graceful ? DecorativeReason(table, row) : null;
            outcome.Action = chosen?.Action ?? (skipReason != null ? FamilyPanelRowAction.Skip : FamilyPanelRowAction.Import);
            if (skipReason != null && chosen == null)
                Add(skipReason.StartsWith("repeats", StringComparison.Ordinal) ? PanelImportProblemKind.RepeatedHeaderRow : PanelImportProblemKind.SectionRow,
                    PanelImportSeverity.Note, row.RowNumber, string.Empty, string.Empty,
                    "Row " + N(row.RowNumber) + " " + skipReason + ", so it was skipped. Choose Import to keep it as a family.");
            if (outcome.Action == FamilyPanelRowAction.Skip) continue;
            string cellKey = Clean(Value(row, keyHeader)), cellName = Clean(Value(row, nameHeader));
            string key = chosen is { Key.Length: > 0 } ? Clean(chosen.Key) : cellKey;
            string name = chosen is { Name.Length: > 0 } ? Clean(chosen.Name) : cellName;
            var nameList = graceful ? ReadList(Value(row, nameHeader)) : CellList.None;
            var otherList = graceful && otherHeader != null ? ReadList(Value(row, otherHeader)) : CellList.None;
            var nameParts = nameList.Parts; var otherParts = otherList.Parts;
            string? listHeader = nameParts.Count >= 2 && nameParts.Count >= otherParts.Count ? nameHeader : otherParts.Count >= 2 ? otherHeader : null;
            if (listHeader != null)
            {
                var listed = listHeader == nameHeader ? nameList : otherList;
                outcome.ListColumn = listHeader;
                outcome.ListLabel = nameList.Label.Length > 0 ? nameList.Label : otherList.Label;
                // Each part is named from the name column, like every other family, when that column lists the same number of names.
                outcome.ProposedParts = listed.Parts.Select((part, i) =>
                {
                    string named = nameParts.Count == listed.Parts.Count ? nameParts[i] : part;
                    return new FamilyPanelSplitPart { Key = PartKey(key, Abbreviation(named).Key.Length > 0 ? named : part, i + 1), Name = Abbreviation(named).Name };
                }).ToList();
                if (listed.Parts.Count == 2 && listed.CommaOnly && listed.Label.Length == 0) outcome.SuggestedAction = FamilyPanelRowAction.Import;
                if (chosen == null)
                    Add(PanelImportProblemKind.SeveralFamilies, PanelImportSeverity.Note, row.RowNumber, listHeader, Value(row, listHeader),
                        At(row.RowNumber, listHeader) + ": the cell lists " + N(listed.Parts.Count) + " names (" + string.Join("; ", listed.Parts.Select(OneLine))
                        + ")" + (outcome.ListLabel.Length > 0 ? " under the label \"" + OneLine(outcome.ListLabel) + "\"" : string.Empty)
                        + ". Choose one family per name, or one group reported under others; until then the row is one family named as written.");
            }
            bool ready = true;
            string sourceDecision = string.Empty, mergeTarget = string.Empty;
            var decided = FamilyPanelDecision.Quantify;
            if (decisionHeader != null)
            {
                string label = Value(row, decisionHeader);
                sourceDecision = label;
                if (graceful && string.IsNullOrWhiteSpace(label)) blankDecisions.Add(row.RowNumber);
                else if (mappings.TryGetValue(label, out var decision)) { decided = decision.Decision; mergeTarget = decision.MergeTargetFamilyKey; }
                else { unmapped.TryAdd(label, row.RowNumber); ready = false; }
            }
            if (chosen?.Action == FamilyPanelRowAction.MergeInto) { decided = FamilyPanelDecision.MergeIntoAnotherFamily; mergeTarget = Clean(chosen.MergeTargetFamilyKey); }
            if (chosen?.Action == FamilyPanelRowAction.ReportUnderOthers) { decided = FamilyPanelDecision.ReportUnderOthers; mergeTarget = string.Empty; }
            FamilyPanelText? colorField = null;
            if (colorHeader != null)
            {
                string fill = row.Fills.GetValueOrDefault(colorHeader, string.Empty);
                string label = fill.Length > 0 ? colorLabels.GetValueOrDefault(fill, string.Empty) : string.Empty;
                if (fill.Length > 0 && string.IsNullOrWhiteSpace(label)) { unnamed.TryAdd(fill, row.RowNumber); ready = false; }
                colorField = new FamilyPanelText { Name = choices.ColorCategoryName is { Length: > 0 } category ? category : colorHeader + " colour", Text = label };
            }
            string domainText = domainHeader == null ? string.Empty : Value(row, domainHeader);

            var units = new List<(string Key, string Name, string Other, int Part)>();
            if (outcome.Action == FamilyPanelRowAction.Split)
            {
                var parts = chosen!.Parts.Count > 0 ? chosen.Parts : outcome.ProposedParts;
                if (parts.Count == 0)
                {
                    Block(PanelImportProblemKind.ChoiceInvalid, row.RowNumber, nameHeader, string.Empty, At(row.RowNumber, nameHeader)
                        + ": the cell does not list several names, so the row cannot be split. Type the families, or choose Import, Report under others or Skip.");
                    continue;
                }
                if (domainHeader != null && PfamAccession.IsMatch(domainText))
                    Add(PanelImportProblemKind.SeveralFamilies, PanelImportSeverity.Note, row.RowNumber, domainHeader, domainText, At(row.RowNumber, domainHeader)
                        + ": the Pfam domains are kept as text only, because they cannot be told apart for the " + N(parts.Count) + " families of the row.");
                for (int i = 0; i < parts.Count; i++)
                    units.Add((Clean(parts[i]?.Key ?? string.Empty), Clean(parts[i]?.Name ?? string.Empty), otherParts.Count == parts.Count ? otherParts[i] : string.Empty, i + 1));
            }
            else if (outcome.Action == FamilyPanelRowAction.ReportUnderOthers && chosen!.Name.Length == 0 && outcome.ListLabel.Length > 0)
                units.Add((key, nameList.Label.Length > 0 ? nameList.Label : name,
                    otherList.Label.Length > 0 ? otherList.Label : otherHeader == null ? string.Empty : Clean(Value(row, otherHeader)), 0));
            else units.Add((key, name, otherHeader == null ? string.Empty : Clean(Value(row, otherHeader)), 0));

            foreach (var unit in units)
            {
                bool unitReady = ready;
                string where = unit.Part == 0 ? At(row.RowNumber, keyHeader) : At(row.RowNumber, keyHeader) + ", name " + N(unit.Part) + " of the list (\"" + OneLine(unit.Name) + "\")";
                if (string.IsNullOrWhiteSpace(unit.Key))
                {
                    string suggestion = string.IsNullOrWhiteSpace(unit.Name) ? string.Empty : unit.Part == 0 ? OneLine(unit.Name)
                        : outcome.ProposedParts.FirstOrDefault(p => p.Name == unit.Name)?.Key ?? PartKey(key, unit.Name, unit.Part);
                    Block(PanelImportProblemKind.MissingKey, row.RowNumber, keyHeader, string.Empty,
                        where + ": the abbreviation is empty. Type one" + (suggestion.Length > 0 ? " (for example \"" + suggestion + "\")" : string.Empty) + (unit.Part == 0 ? ", or skip the row." : "."),
                        suggestion);
                    unitReady = false;
                }
                if (string.IsNullOrWhiteSpace(unit.Name))
                {
                    string suggestion = string.IsNullOrWhiteSpace(unit.Key) ? string.Empty : OneLine(unit.Key);
                    Block(PanelImportProblemKind.MissingName, row.RowNumber, nameHeader, string.Empty, unit.Part == 0
                        ? At(row.RowNumber, nameHeader) + ": the name is empty. " + (suggestion.Length > 0 ? "Use the abbreviation \"" + suggestion + "\" as the name, type a name," : "Type a name,") + " or skip the row."
                        : At(row.RowNumber, nameHeader) + ", name " + N(unit.Part) + " of the list: the name is empty. Type one.", suggestion);
                    unitReady = false;
                }
                if (!string.IsNullOrWhiteSpace(unit.Key) && !string.IsNullOrWhiteSpace(unit.Name) && firstRow.TryGetValue(unit.Key, out int earlier))
                {
                    string suggestion = Suggest(unit.Key);
                    Block(PanelImportProblemKind.DuplicateKey, row.RowNumber, keyHeader, unit.Key, unit.Part == 0
                        ? where + ": the abbreviation \"" + OneLine(unit.Key) + "\" is also used in row " + N(earlier) + ". Rename it (for example \""
                            + OneLine(suggestion) + "\"), merge it into the family of row " + N(earlier) + ", or skip it."
                        : where + ": the abbreviation \"" + OneLine(unit.Key) + "\" is also used " + (earlier == row.RowNumber ? "in this row" : "in row " + N(earlier))
                            + ". Rename it (for example \"" + OneLine(suggestion) + "\").", suggestion, earlier == row.RowNumber ? null : new[] { earlier });
                    unitReady = false;
                }
                if (!string.IsNullOrWhiteSpace(unit.Key)) firstRow.TryAdd(unit.Key, row.RowNumber);
                if (!unitReady) continue;
                var family = new FamilyPanelFamily
                {
                    Key = unit.Key, DisplayName = unit.Name, SourceDecision = sourceDecision, Decision = decided, MergeTargetFamilyKey = mergeTarget,
                    TextFields = table.Headers.Select(header => new FamilyPanelText { Name = header, Text = Value(row, header) }).ToList(),
                };
                if (otherHeader != null && unit.Other.Length > 0) family.Names.Add(new FamilyPanelText { Name = otherHeader, Text = unit.Other });
                if (colorField != null) family.TextFields.Add(new FamilyPanelText { Name = colorField.Name, Text = colorField.Text });
                if (domainHeader != null && unit.Part == 0)
                {
                    var names = NamedPfam.Matches(domainText).ToDictionary(m => m.Groups["accession"].Index, m => m.Groups["name"].Value.Trim());
                    family.Pfam = PfamAccession.Matches(domainText).Select(m => (Accession: m.Value, Name: names.GetValueOrDefault(m.Index, string.Empty)))
                        .Distinct().Select(d => new FamilyPanelDomain { Accession = d.Accession, Name = d.Name }).ToList();
                }
                outcome.FamilyKeys.Add(unit.Key);
                families.Add((family, row.RowNumber));
            }
        }
        foreach (var (label, row) in unmapped)
            Block(PanelImportProblemKind.DecisionNotMapped, row, decisionHeader!, label,
                At(row, decisionHeader!) + ": the label \"" + OneLine(label) + "\" has no meaning yet. Choose Quantify, Report under others, Not a family or Merge into family for it.");
        foreach (var (fill, row) in unnamed)
            Block(PanelImportProblemKind.ColorNotNamed, row, colorHeader!, fill,
                At(row, colorHeader!) + ": the colour " + fill + " (" + PanelColorNames.Describe(fill) + ") has no category name yet. Type what this colour stands for in your sheet.");
        if (blankDecisions.Count > 0)
            Add(PanelImportProblemKind.DecisionNotMapped, PanelImportSeverity.Note, blankDecisions[0], decisionHeader!, string.Empty,
                (blankDecisions.Count == 1 ? "Row " + N(blankDecisions[0]) + " has" : "Rows " + string.Join(", ", blankDecisions.Select(N)) + " have")
                + " an empty decision in column \"" + OneLine(decisionHeader!) + "\", so " + (blankDecisions.Count == 1 ? "that family is" : "those families are") + " quantified.",
                others: blankDecisions.Skip(1));
        if (problems.Any(p => p.Severity == PanelImportSeverity.Blocking)) return check;

        var byKey = families.ToDictionary(f => f.Family.Key, StringComparer.Ordinal);
        foreach (var mapping in mappings.Values.Where(m => m.Decision == FamilyPanelDecision.MergeIntoAnotherFamily && !byKey.ContainsKey(m.MergeTargetFamilyKey)))
            Block(PanelImportProblemKind.MergeTargetMissing, 0, decisionHeader ?? string.Empty, mapping.SourceValue,
                "The label \"" + OneLine(mapping.SourceValue) + "\" merges into \"" + OneLine(mapping.MergeTargetFamilyKey) + "\", but no family has that abbreviation. Choose a family that remains in the panel.");
        foreach (var (family, row) in families.Where(f => f.Family.Decision == FamilyPanelDecision.MergeIntoAnotherFamily))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = family;
            while (current.Decision == FamilyPanelDecision.MergeIntoAnotherFamily)
            {
                if (!seen.Add(current.Key))
                {
                    Block(PanelImportProblemKind.MergeCycle, row, keyHeader, family.Key,
                        "Row " + N(row) + ": merging \"" + OneLine(family.Key) + "\" leads back to itself (" + string.Join(" > ", seen.Select(OneLine)) + "). Choose a family that is not merged.");
                    break;
                }
                if (!byKey.TryGetValue(current.MergeTargetFamilyKey, out var target) || target.Family.Decision == FamilyPanelDecision.NotAFamily)
                {
                    Block(PanelImportProblemKind.MergeTargetMissing, row, keyHeader, family.Key,
                        "Row " + N(row) + ": \"" + OneLine(family.Key) + "\" merges into \"" + OneLine(current.MergeTargetFamilyKey) + "\", which is not a family that remains in the panel. Choose another family.");
                    break;
                }
                current = target.Family;
            }
        }
        if (families.Count == 0)
            Block(PanelImportProblemKind.NoFamilies, 0, string.Empty, string.Empty, "No row of sheet \"" + table.SheetName + "\" below row " + N(table.HeaderRowNumber) + " becomes a family.");
        if (problems.Any(p => p.Severity == PanelImportSeverity.Blocking)) return check;

        var panel = new FamilyPanel { Name = panelName, Import = Copy(choices), DecisionMappings = decisionHeader == null ? new() : Copy(decisions.ToList()) };
        panel.Import.SourceName = table.SourceName; panel.Import.SourceSha256 = table.SourceSha256;
        panel.Import.FamilySheet = table.SheetName; panel.Import.HeaderRowNumber = table.HeaderRowNumber;
        panel.Families.AddRange(families.Select(f => f.Family));
        try { FamilyPanelValidation.Validate(panel); }
        catch (ArgumentException ex)
        {
            Block(PanelImportProblemKind.PanelInvalid, 0, string.Empty, string.Empty, "The panel could not be built: " + ex.Message);
            return check;
        }
        check.Panel = panel;
        return check;

        string At(int row, string header) => "Row " + N(row) + ", column \"" + OneLine(header) + "\"";
    }

    /// <summary>Whether the row is skipped by the user's choice or, without one, by default.</summary>
    private static bool Skipped(PanelSpreadsheetTable table, PanelSpreadsheetRow row, FamilyPanelImport choices) =>
        choices.RowChoices?.LastOrDefault(c => c?.RowNumber == row.RowNumber) is { } chosen
            ? chosen.Action == FamilyPanelRowAction.Skip
            : choices.ImportVersion >= PanelImportRules.Graceful && DecorativeReason(table, row) != null;

    /// <summary>Why a graceful import skips a row by default, or null. Only layout is read, never the words.</summary>
    private static string? DecorativeReason(PanelSpreadsheetTable table, PanelSpreadsheetRow row)
    {
        var filled = table.Headers.Where(h => !string.IsNullOrWhiteSpace(Value(row, h))).ToArray();
        if (filled.Length >= 2 && filled.All(h => string.Equals(Value(row, h).Trim(), h.Trim(), StringComparison.Ordinal)))
            return "repeats the column names";
        if (filled.Length >= 2 && filled.All(h => row.Merges.ContainsKey(h)) && filled.Select(h => row.Merges[h]).Distinct(StringComparer.Ordinal).Count() == 1)
            return "is one merged cell across columns (" + row.Merges[filled[0]] + ", \"" + OneLine(Value(row, filled[0])) + "\"), like a section title";
        return null;
    }

    private sealed record CellList(List<string> Parts, string Label, bool CommaOnly)
    {
        public static readonly CellList None = new(new List<string>(), string.Empty, false);
    }

    /// <summary>Reads a cell listing several names: commas, semicolons and line breaks outside parentheses or brackets
    /// separate names, a leading bullet is dropped, and a leading label ("Other rare families: a; b") is kept apart
    /// when the cell lists two or more names. One name gives a list of one.</summary>
    private static CellList ReadList(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;
        bool commaOnly = true;
        foreach (char c in text)
        {
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth = Math.Max(0, depth - 1);
            if (depth == 0 && c is ',' or ';' or '\n' or '\r')
            {
                if (c != ',' && current.ToString().Trim().Length > 0) commaOnly = false;
                Flush(); continue;
            }
            current.Append(c);
        }
        Flush();
        string label = string.Empty;
        if (parts.Count >= 2 && LeadingLabel.Match(parts[0]) is { Success: true } match)
        { label = match.Groups["label"].Value.Trim(); parts[0] = match.Groups["rest"].Value.Trim(); }
        return new CellList(parts, label, commaOnly);

        void Flush()
        {
            string part = Bullet.Replace(current.ToString().Trim(), string.Empty).Trim();
            if (part.Length > 0) parts.Add(part);
            current.Clear();
        }
    }

    /// <summary>A short abbreviation in trailing parentheses ("hyaluronidase (HYA)"): one word with a letter, at most
    /// twelve characters and not a Pfam accession. Returns it and the name without it, or an empty key.</summary>
    private static (string Key, string Name) Abbreviation(string part)
    {
        var match = TrailingAbbreviation.Match(part);
        if (!match.Success || !match.Groups["key"].Value.Any(char.IsLetter) || PfamAccession.IsMatch(match.Groups["key"].Value)) return (string.Empty, part);
        return (match.Groups["key"].Value, part[..match.Index].Trim());
    }

    private static List<List<int>> Runs(IEnumerable<int> rows)
    {
        var result = new List<List<int>>();
        foreach (int row in rows.Order())
        {
            if (result.Count > 0 && result[^1][^1] == row - 1) result[^1].Add(row);
            else result.Add(new List<int> { row });
        }
        return result;
    }

    private static string N(int value) => PanelSpreadsheetReader.Number(value);
    private static string OneLine(string text) => PanelSpreadsheetReader.OneLine(text);

    private static string Value(PanelSpreadsheetRow row, string header) => row.Cells.TryGetValue(header, out var text) ? text : string.Empty;

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
