using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static class LedgerPauloRulings
{
    internal const string Step = "Paulo-rulings";
    internal const string Command = "--ledger-apply-rulings";
    internal const string RulesPath = "D:/FamilyQuant-data/reviews/wording-rulings.md";
    internal const string InputPath = "D:/FamilyQuant-data/reviews/wording-for-paulo.tsv";
    internal const string FactChecksPath = "D:/FamilyQuant-data/reviews/rulings-fact-check.tsv";
    internal const string LeftPath = "D:/FamilyQuant-data/reviews/wording-left-for-paulo.tsv";
    internal const string FrozenRoot = "D:/FamilyQuant-data/out-shell/N3/source";
    internal const string GroupRulingPath = "D:/FamilyQuant-data/reviews/paulo-group-ruling.tsv";
    internal const string RulesSha256 = "3C6B1EC5979F0B593A609D0555AB84BC5EC05FCC60B370A647E11030993A8605";
    internal sealed record Input(int Line, string Lane, string Id, string OldText, string NewText, string Reason);
    internal sealed record Decision(string Id, string Lane, string OldText, string NewText, string Rule, string Reason,
        string WitnessAssembly = "", string WitnessSymbol = "", string WitnessTextSha256 = "", bool Closed = false, string FactCheckNote = "");
    internal sealed record FactCheck(string Verdict, string Note);
    internal sealed record GroupRuling(string Group, string Note);
    internal sealed record Packet(int SchemaVersion, string Producer, string TsvArtifact, string RulesArtifact,
        string TsvSha256, string RuleSourceSha256, List<Decision> Decisions, string FactChecksArtifact = "", string FactChecksSha256 = "",
        string GroupRulingArtifact = "", string GroupRulingSha256 = "");

    /// <summary>Paulo's group ruling (rule 4): row_id, group (fragment, format or superseded), note. A missing or empty file holds no rows.</summary>
    internal static Dictionary<string, GroupRuling> ReadGroupRulings(string path)
    {
        var result = new Dictionary<string, GroupRuling>(StringComparer.Ordinal);
        if (!File.Exists(path)) return result;
        string text = File.ReadAllText(path);
        if (text.Trim().Length == 0) return result;
        var records = ParseTsv(text);
        if (!records[0].Fields.SequenceEqual(new[] { "row_id", "group", "note" }))
            throw new InvalidDataException("Group ruling TSV must use row_id, group, note columns.");
        foreach (var record in records.Skip(1).Where(record => record.Fields.Any(field => field.Length > 0)))
            if (record.Fields.Count != 3 || string.IsNullOrWhiteSpace(record.Fields[0]) || record.Fields[1] is not ("fragment" or "format" or "superseded")
                || !result.TryAdd(record.Fields[0], new(record.Fields[1], record.Fields[2])))
                throw new InvalidDataException("Malformed, duplicate or unknown group ruling at TSV line " + record.Line.ToString(CultureInfo.InvariantCulture) + ".");
        return result;
    }

    internal static string GroupReason(GroupRuling ruling) => ruling.Group switch
    {
        "fragment" => "Paulo group ruling: fragment, no user fact of its own",
        "format" => "Paulo group ruling: format, same precision today" + (ruling.Note.Length == 0 ? "" : ", " + ruling.Note),
        _ => "Paulo group ruling: superseded, " + ruling.Note,
    };

    /// <summary>The rule text a rule-4 receipt cites: the group ruling's own reason for this row.</summary>
    internal static string GroupRule(Decision decision) => "Rule 4, Paulo group ruling (" + Path.GetFileName(GroupRulingPath) + "): " + decision.Reason;

    internal static Dictionary<string, FactCheck> ReadFactChecks(string path)
    {
        var records = ParseTsv(File.ReadAllText(path));
        if (records.Count == 0 || !records[0].Fields.SequenceEqual(new[] { "row_id", "verdict", "note" }))
            throw new InvalidDataException("Fact-check TSV must use row_id, verdict, note columns.");
        var result = new Dictionary<string, FactCheck>(StringComparer.Ordinal);
        foreach (var record in records.Skip(1).Where(record => record.Fields.Any(field => field.Length > 0)))
            if (record.Fields.Count != 3 || string.IsNullOrWhiteSpace(record.Fields[0]) || record.Fields[1] is not ("kept" or "lost")
                || !result.TryAdd(record.Fields[0], new(record.Fields[1], record.Fields[2])))
                throw new InvalidDataException("Malformed, duplicate or unknown fact verdict at TSV line " + record.Line.ToString(CultureInfo.InvariantCulture) + ".");
        return result;
    }

    internal static string Decode(string value)
    {
        for (int iteration = 0; iteration < 2; iteration++)
        {
            value = Regex.Replace(value, @"\\u([0-9a-fA-F]{4})", match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
            value = value.Replace("\\r\\n", "\n", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal).Replace("\\t", "\t", StringComparison.Ordinal);
        }
        return value;
    }

    internal static List<Input> ReadInputs(string path)
    {
        var records = ParseTsv(File.ReadAllText(path));
        if (records.Count == 0 || !records[0].Fields.SequenceEqual(new[] { "lane", "row_id", "old_text", "new_text", "reason" }))
            throw new InvalidDataException("Wording TSV must use lane, row_id, old_text, new_text, reason columns.");
        return records.Skip(1).Where(record => record.Fields.Any(field => field.Length > 0)).Select(record =>
            record.Fields.Count == 5 ? new Input(record.Line, record.Fields[0], record.Fields[1], Decode(record.Fields[2]), Decode(record.Fields[3]), record.Fields[4])
                : new Input(record.Line, "", "", "", "", "Malformed TSV record: " + record.Fields.Count.ToString(CultureInfo.InvariantCulture) + " fields.")).ToList();
    }

    private static List<(int Line, List<string> Fields)> ParseTsv(string text)
    {
        var result = new List<(int, List<string>)>(); var fields = new List<string>(); var field = new StringBuilder();
        bool quoted = false, atStart = true; int line = 1, start = 1;
        for (int index = 0; index < text.Length; index++)
        {
            char value = text[index];
            if (quoted)
            {
                if (value == '"') { if (index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; } else quoted = false; }
                else { field.Append(value); if (value == '\n') line++; }
                continue;
            }
            if (atStart && value == '"') { quoted = true; atStart = false; continue; }
            if (value == '\t') { fields.Add(field.ToString()); field.Clear(); atStart = true; }
            else if (value is '\r' or '\n')
            {
                if (value == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                fields.Add(field.ToString()); field.Clear(); result.Add((start, fields)); fields = new(); atStart = true; line++; start = line;
            }
            else { field.Append(value); atStart = false; }
        }
        if (quoted) throw new InvalidDataException("Unterminated quoted TSV field.");
        if (field.Length > 0 || fields.Count > 0) { fields.Add(field.ToString()); result.Add((start, fields)); }
        return result;
    }

    /// <summary>The wording rules (1 to 3) first; a row they leave open closes under rule 4 when Paulo's group ruling lists it,
    /// unless it is unknown, an approved DROP, or an export/storage or format/export row, which need execution.</summary>
    internal static Decision Evaluate(Input input, FeatureLedger.Observation? row, Func<string, LedgerRulingLiveText.Match> live,
        IReadOnlyDictionary<string, FactCheck>? factChecks = null, IReadOnlyDictionary<string, GroupRuling>? groupRulings = null)
    {
        var decision = EvaluateWording(input, row, live, factChecks);
        if (decision.Closed || row == null || groupRulings == null || !groupRulings.TryGetValue(input.Id, out var ruling)
            || FeatureLedger.IsApprovedDrop(row) || row.Kind is "export/storage fact" or "format/export") return decision;
        return decision with { Rule = "4", Reason = GroupReason(ruling), Closed = true };
    }

    private static Decision EvaluateWording(Input input, FeatureLedger.Observation? row, Func<string, LedgerRulingLiveText.Match> live,
        IReadOnlyDictionary<string, FactCheck>? factChecks)
    {
        Decision Left(string reason, string note = "") => new(input.Id, input.Lane, input.OldText, input.NewText, "", reason, FactCheckNote: note);
        if (row == null) return Left("Unknown or malformed observation ID.");
        if (FeatureLedger.IsApprovedDrop(row)) return Left("Approved DROP is not a wording ruling.");
        if (row.Kind is "export/storage fact" or "format/export") return Left("Export/storage and format/export rows require execution.");
        if (LedgerRulingLiveText.Normalize(input.OldText) == LedgerRulingLiveText.Normalize(input.NewText))
        {
            // Identical old and new text closes under Rule 1 only with an independent kept verdict, a wording row of the frozen
            // fact and a live text witness; otherwise it stays with the text tool, as before.
            const string identical = "Identical old/new text belongs to the text tool.";
            if (factChecks == null || !factChecks.TryGetValue(input.Id, out var kept) || kept.Verdict != "kept"
                || string.IsNullOrWhiteSpace(input.NewText) || !input.NewText.Any(char.IsLetterOrDigit) || input.NewText.TrimStart().StartsWith('<')
                || !OldFactMatches(row, input.OldText) || row.Detail.StartsWith("Style.", StringComparison.Ordinal)
                || row.Detail.StartsWith("Setter.", StringComparison.Ordinal) || !IsWordingKind(row)) return Left(identical);
            var witness = live(input.NewText);
            if (!witness.Found) return Left(identical);
            return new(input.Id, input.Lane, input.OldText, input.NewText, "1",
                "Identical old/new text; independent kept verdict and live text witness retained; independent sample required.",
                witness.AssemblyName, witness.Symbol, witness.TextSha256, true, kept.Note);
        }
        if (string.IsNullOrWhiteSpace(input.NewText) || !input.NewText.Any(char.IsLetterOrDigit)) return Left("New text is empty, whitespace or bare punctuation/newline.");
        if (input.NewText.Contains("<No exact current value captured>", StringComparison.OrdinalIgnoreCase)
            || input.NewText.TrimStart().StartsWith('<')) return Left("New text is a placeholder, not captured user text.");
        if (!OldFactMatches(row, input.OldText)) return Left("Old text does not identify the frozen observation's fact.");
        if (IsStyleOrSetter(row, out string styleReason))
            return new(input.Id, input.Lane, input.OldText, input.NewText, "2", styleReason, Closed: true);
        if (row.Detail.StartsWith("Style.", StringComparison.Ordinal) || row.Detail.StartsWith("Setter.", StringComparison.Ordinal))
            return Left(styleReason);
        if (!IsWordingKind(row)) return Left("Behavior/control/handler/binding row is outside wording rules.");
        if (factChecks == null || !factChecks.TryGetValue(input.Id, out var fact))
            return Left("Independent fact-check verdict is missing.");
        if (fact.Verdict != "kept") return Left("Independent fact check marks the old fact lost.", fact.Note);
        string rule = Regex.IsMatch(input.OldText, @"\bLayer\s+\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? "3" : "1";
        var match = live(input.NewText);
        if (!match.Found) return Left("New text is not present in the compiled App/Core or resolved current Help.");
        return new(input.Id, input.Lane, input.OldText, input.NewText, rule, "Recorded Paulo wording ruling; live text witness retained; independent sample required.",
            match.AssemblyName, match.Symbol, match.TextSha256, true, fact.Note);
    }

    private static bool IsWordingKind(FeatureLedger.Observation row) => row.Kind is "option/copy" or "tooltip" or "help fact" or "help page" or "code fact"
        || row.Kind == "column" && row.Detail.Contains(".Header = ", StringComparison.Ordinal);

    private static bool OldFactMatches(FeatureLedger.Observation row, string old)
    {
        // A frozen C# literal is read first: its text may itself hold " = ", which only a XAML assignment uses as a separator.
        string detail = row.Detail; int equals = detail.IndexOf(" = ", StringComparison.Ordinal);
        if (detail.StartsWith("@\"", StringComparison.Ordinal) && detail.EndsWith('"')) detail = detail[2..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        else if (detail.StartsWith('"')) { try { detail = JsonSerializer.Deserialize<string>(detail) ?? ""; } catch (JsonException) { return false; } }
        else if (equals >= 0) detail = detail[(equals + 3)..];
        string fact = LedgerRulingLiveText.Normalize(Decode(detail)), prior = LedgerRulingLiveText.Normalize(old);
        return fact.Length > 0 && (fact == prior || prior.Contains(fact, StringComparison.Ordinal));
    }

    internal static bool IsStyleOrSetter(FeatureLedger.Observation row, out string reason)
    {
        reason = "Rule 2 requires an anchored frozen Style or visual Setter row.";
        if (!row.Source.EndsWith(".xaml", StringComparison.Ordinal) || !row.Detail.StartsWith("Style.", StringComparison.Ordinal) && !row.Detail.StartsWith("Setter.", StringComparison.Ordinal)) return false;
        string path = Path.Combine(FrozenRoot, row.Source);
        if (!File.Exists(path)) { reason = "Frozen style source unavailable."; return false; }
        var document = XDocument.Load(path, LoadOptions.SetLineInfo);
        string type = row.Detail[..row.Detail.IndexOf('.')]; int equals = row.Detail.IndexOf(" = ", StringComparison.Ordinal);
        string property = row.Detail[(row.Detail.IndexOf('.') + 1)..equals], expected = row.Detail[(equals + 3)..];
        var nodes = document.Descendants().Where(node => node.Name.LocalName == type && node.Attributes().Any(attribute => attribute.Name.LocalName == property
            && attribute.Value == expected && (((IXmlLineInfo)attribute).LineNumber == row.Line || ((IXmlLineInfo)node).LineNumber == row.Line))).ToArray();
        if (nodes.Length != 1) { reason = "Frozen style/setter anchor is missing or ambiguous."; return false; }
        var visual = new HashSet<string>(new[] { "Background", "Foreground", "BorderBrush", "BorderThickness", "FontFamily", "FontSize", "FontWeight", "FontStyle",
            "TextDecorations", "Cursor", "Opacity", "Margin", "Padding", "HorizontalAlignment", "VerticalAlignment", "HorizontalContentAlignment", "VerticalContentAlignment",
            "TextAlignment", "TextWrapping", "CornerRadius", "FocusVisualStyle", "Template", "ContentTemplate", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
            "SnapsToDevicePixels", "UseLayoutRounding" }, StringComparer.Ordinal);
        if (nodes[0].DescendantsAndSelf().Any(node => node.Name.LocalName == "EventSetter"
            || node.Name.LocalName == "Setter" && !visual.Contains(node.Attribute("Property")?.Value ?? "")))
        { reason = "Style/setter also changes behavior or an unsupported property."; return false; }
        reason = "Paulo Rule 2: anchored style/setter changes appearance only; current theme accepted."; return true;
    }
}
