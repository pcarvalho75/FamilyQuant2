using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Panels.Import;

public sealed class FamilyPanelSpreadsheetImporter : IFamilyPanelSpreadsheetImporter
{
    private static readonly Regex PfamAccession = new(@"(?<![A-Za-z0-9])PF\d{5}(?!\d)", RegexOptions.CultureInvariant);
    private static readonly Regex NamedPfam = new(@"(?<name>[^;,\r\n()]+?)\s*\(\s*(?<accession>PF\d{5})(?:\.\d+)?\s*\)", RegexOptions.CultureInvariant);

    public FamilyPanel Import(PanelSpreadsheetTable table, FamilyPanelImport choices,
        IReadOnlyList<FamilyPanelDecisionMapping> decisions, string panelName = "")
    {
        ArgumentNullException.ThrowIfNull(table); ArgumentNullException.ThrowIfNull(choices); ArgumentNullException.ThrowIfNull(decisions);
        if (table.Headers.Count == 0 || table.Headers.Any(string.IsNullOrWhiteSpace)
            || table.Headers.Distinct(StringComparer.Ordinal).Count() != table.Headers.Count)
            throw new InvalidDataException("The panel needs distinct nonempty headers.");
        if (choices.FamilySheet.Length > 0 && choices.FamilySheet != table.SheetName
            || choices.HeaderRowNumber != table.HeaderRowNumber
            || choices.SourceSha256.Length > 0 && !string.Equals(choices.SourceSha256, table.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The preview does not match the chosen sheet, header row or source hash.");
        string keyHeader = Header(PanelImportFields.FamilyKey), nameHeader = Header(PanelImportFields.DisplayName), decisionHeader = Header(PanelImportFields.Decision);
        string? domainHeader = choices.ColumnHeaders.ContainsKey(PanelImportFields.Pfam) ? Header(PanelImportFields.Pfam) : null;
        if (decisions.Any(d => d == null || !Enum.IsDefined(d.Decision))
            || decisions.Select(d => d.SourceValue).Distinct(StringComparer.Ordinal).Count() != decisions.Count)
            throw new InvalidDataException("Each source decision value must have exactly one valid mapping.");
        var mappings = decisions.ToDictionary(d => d.SourceValue, StringComparer.Ordinal);
        var panel = new FamilyPanel { Name = panelName, Import = Copy(choices), DecisionMappings = Copy(decisions.ToList()) };
        panel.Import.SourceName = table.SourceName; panel.Import.SourceSha256 = table.SourceSha256;
        panel.Import.FamilySheet = table.SheetName; panel.Import.HeaderRowNumber = table.HeaderRowNumber;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
        {
            string key = Value(row, keyHeader), name = Value(row, nameHeader), label = Value(row, decisionHeader);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name) || !keys.Add(key))
                throw new InvalidDataException("Every panel row needs a distinct nonempty family key and a name.");
            if (!mappings.TryGetValue(label, out var decision))
                throw new InvalidDataException("A source decision value has no user mapping: " + label);
            var family = new FamilyPanelFamily
            {
                Key = key, DisplayName = name, SourceDecision = label, Decision = decision.Decision,
                MergeTargetFamilyKey = decision.MergeTargetFamilyKey,
                TextFields = table.Headers.Select(header => new FamilyPanelText { Name = header, Text = Value(row, header) }).ToList(),
            };
            if (domainHeader != null)
            {
                string domainText = Value(row, domainHeader);
                var names = NamedPfam.Matches(domainText).ToDictionary(m => m.Groups["accession"].Index, m => m.Groups["name"].Value.Trim());
                family.Pfam = PfamAccession.Matches(domainText).Select(m => (Accession: m.Value, Name: names.GetValueOrDefault(m.Index, string.Empty)))
                    .Distinct().Select(d => new FamilyPanelDomain { Accession = d.Accession, Name = d.Name }).ToList();
            }
            panel.Families.Add(family);
        }
        if (panel.Families.Count == 0) throw new InvalidDataException("The selected table has no family rows.");
        var byKey = panel.Families.ToDictionary(f => f.Key, StringComparer.Ordinal);
        foreach (var family in panel.Families)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = family;
            while (current.Decision == FamilyPanelDecision.MergeIntoAnotherFamily)
            {
                if (!seen.Add(current.Key)) throw new InvalidDataException("The panel contains a merge cycle.");
                if (!byKey.TryGetValue(current.MergeTargetFamilyKey, out var target) || target.Decision == FamilyPanelDecision.NotAFamily)
                    throw new InvalidDataException("A merge must name a family that remains in the panel.");
                current = target;
            }
        }
        try { FamilyPanelValidation.Validate(panel); }
        catch (ArgumentException ex) { throw new InvalidDataException(ex.Message, ex); }
        return panel;

        string Header(string role)
        {
            if (!choices.ColumnHeaders.TryGetValue(role, out var header) || !table.Headers.Contains(header, StringComparer.Ordinal))
                throw new InvalidDataException("Choose an existing column header for " + role + ".");
            return header;
        }
    }

    public FamilyPanel Replay(string path, FamilyPanel previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var choice = previous.Import ?? throw new InvalidDataException("This panel has no stored import choices.");
        if (choice.SourceSha256.Length == 0) throw new InvalidDataException("The stored import has no source hash.");
        var table = new PanelSpreadsheetReader().ReadTable(path, choice.FamilySheet, choice.HeaderRowNumber);
        return Import(table, choice, previous.DecisionMappings, previous.Name);
    }

    private static string Value(PanelSpreadsheetRow row, string header) => row.Cells.TryGetValue(header, out var text)
        ? text : throw new InvalidDataException("A table row is missing a selected column.");

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
