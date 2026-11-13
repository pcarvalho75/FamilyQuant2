using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;

namespace FamilyQuant.Report;

/// <summary>The stored facts a summary of findings may state. Every collection is the program's own stored data.</summary>
public sealed record ReportFindingsInput
{
    public FamilyRunSharesResult Shares { get; init; } = new();
    public FamilyReportThresholdResult Grouping { get; init; } = new();
    public FamilyProteinCountsResult? Counts { get; init; }
    public FamilySubclassPartsResult? Subclasses { get; init; }
    public IReadOnlyDictionary<int, string> Labels { get; init; } = new Dictionary<int, string>();
    /// <summary>Null without a panel; then no statement about panel families is made.</summary>
    public FamilyPanel? Panel { get; init; }
    public ReportFamilyNameChoice FamilyNameChoice { get; init; }
    public IReadOnlyDictionary<int, string?> PanelKeyByUserId { get; init; } = new Dictionary<int, string?>();
    public IReadOnlyDictionary<string, int> MembersByPanelKey { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> ScopeTerms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<FamilyPanelEvidenceLink> Links { get; init; } = Array.Empty<FamilyPanelEvidenceLink>();
    public IReadOnlyList<DeNovoInputManifest> DeNovoInputs { get; init; } = Array.Empty<DeNovoInputManifest>();
}

/// <summary>
/// "Summary of findings" written from fixed templates: values, counts, ranks, ranges and presence or absence read
/// from the stored studies, never a judgement, a cause or free text. Each sentence appears only when its data exist.
/// Every number printed is also a row of the section's supplement table and CSV, so a trace can find it.
/// </summary>
public static class ReportFindings
{
    public const string Title = "Summary of findings";
    public const string CsvFile = "report_findings.csv";
    public const string NotResolvedMethod = "A peptide is credited to a subclass only when every candidate protein carries the same subclass; peptides shared by subclasses are counted in the not-resolved part.";

    public static (ReportSection Section, string Csv)? FromProject(FamilyClusterFile curation, QuantManifest manifest,
        FamilyReportThresholdResult? grouping, IReadOnlyDictionary<int, string> labels)
        => InputFromProject(curation, manifest, grouping, labels) is { } input ? Build(input) : null;

    /// <summary>The project's stored facts as the summary reads them; null without stored shares or a grouping.</summary>
    public static ReportFindingsInput? InputFromProject(FamilyClusterFile curation, QuantManifest manifest,
        FamilyReportThresholdResult? grouping, IReadOnlyDictionary<int, string> labels)
    {
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.FamilyRunShares == null || grouping == null) return null;
        var keys = new Dictionary<int, string?>();
        var members = new Dictionary<string, int>(StringComparer.Ordinal);
        if (curation.FamilyPanel != null)
        {
            var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(curation);
            foreach (var pair in map.UserFamilies) keys[pair.Key] = pair.Value.HasConflict ? null : pair.Value.PanelFamilyKey;
            foreach (var assignment in curation.PanelProteinAssignments ?? new())
                if (assignment.IsApplied && !string.IsNullOrEmpty(assignment.PanelFamilyKey))
                    members[assignment.PanelFamilyKey] = members.GetValueOrDefault(assignment.PanelFamilyKey) + 1;
        }
        return new ReportFindingsInput
        {
            Shares = manifest.FamilyRunShares, Grouping = grouping, Counts = manifest.FamilyProteinCounts, Subclasses = manifest.FamilySubclassParts,
            Labels = labels, Panel = curation.FamilyPanel, FamilyNameChoice = curation.ReportFamilyNames, PanelKeyByUserId = keys, MembersByPanelKey = members,
            ScopeTerms = curation.EvidenceRecipe?.Options.ScopeTerms ?? Array.Empty<string>(),
            Links = curation.FamilyPanel?.EvidenceLinks ?? new List<FamilyPanelEvidenceLink>(),
            DeNovoInputs = manifest.DeNovoInputs,
        };
    }

    /// <summary>The effective panel family of a group, after merges; null without a panel or an unambiguous key.</summary>
    public static FamilyPanelFamily? PanelFamily(ReportFindingsInput input, int userId)
    {
        if (input.Panel is not { } panel || input.PanelKeyByUserId.GetValueOrDefault(userId) is not { } key) return null;
        string? effective = FamilyPanelValidation.ResolveFamilyKey(panel, key);
        return effective == null ? null : panel.Families.Single(f => f.Key == effective);
    }

    /// <summary>Panel keys of quantified panel families with database members and no peptide observed, most members first.</summary>
    public static IReadOnlyList<string> UnobservedPanelKeys(ReportFindingsInput input)
    {
        if (input.Panel == null) return Array.Empty<string>();
        return input.Shares.Families.Where(f => f.NoPeptideObserved && input.PanelKeyByUserId.GetValueOrDefault(f.FamilyUserId) is { } key
                && PanelFamily(input, f.FamilyUserId)?.Decision == FamilyPanelDecision.Quantify && input.MembersByPanelKey.GetValueOrDefault(key) > 0)
            .Select(f => input.PanelKeyByUserId[f.FamilyUserId]!).Distinct(StringComparer.Ordinal)
            .OrderByDescending(k => input.MembersByPanelKey[k]).ThenBy(k => k, StringComparer.Ordinal).ToList();
    }

    /// <summary>The subclass parts the summary states: families whose panel family carries subclass rules (elsewhere every
    /// part is unresolved by construction), never a family under Others (named only in the supplement, D35), largest family signal first.</summary>
    public static IReadOnlyList<FamilySignalBreakdown> StatedSubclassParts(ReportFindingsInput input)
    {
        var hidden = input.Grouping.OtherFamilies.Select(f => f.FamilyUserId).ToHashSet();
        return (input.Subclasses?.Families ?? new()).Where(f => f.Parts.Count > 0 && f.FamilySignal > 0 && !hidden.Contains(f.FamilyUserId)
                && PanelFamily(input, f.FamilyUserId)?.Subclasses.Count > 0)
            .OrderByDescending(f => f.FamilySignal).ThenBy(f => f.FamilyUserId).ToList();
    }

    public static (ReportSection Section, string Csv) Build(ReportFindingsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var blocks = new List<ReportBlock>();
        var numbers = new List<IReadOnlyList<string>>();
        string Record(string statement, string quantity, string value)
        {
            numbers.Add(new[] { statement, quantity, value });
            return value;
        }
        void Say(string id, string text, bool keepWithNext = false, bool compact = false) =>
            blocks.Add(new ReportParagraph("findings:" + id, text) { KeepWithNext = keepWithNext, Compact = compact });
        // Protein-name text as the rest of the report prints it: the header's OS= field and what follows are left out (J12).
        string Name(int userId) => input.Labels.TryGetValue(userId, out string? label) ? ProteinNameFormatting.ForNewOutput(label) : Integer(userId);
        var grouping = input.Grouping;
        string threshold = Percent(grouping.Threshold);

        var listed = grouping.ListedFamilies.Where(f => f.MeanShare.HasValue)
            .OrderByDescending(f => f.MeanShare).ThenBy(f => f.FamilyUserId).ToList();
        var counts = input.Counts?.Families.ToDictionary(f => f.FamilyUserId) ?? new Dictionary<int, FamilyProteinCount>();
        if (listed.Count > 0)
        {
            Record("listed", "report threshold (%)", threshold);
            Say("listed", Record("listed", "families above the threshold", Integer(listed.Count)) + (listed.Count == 1 ? " family is" : " families are")
                + " above the report threshold of " + threshold + "% of the credited family signal. In headline order, each with the mean of per-run shares, the sample SD in percentage points and the greedy estimate of database proteins:");
            // One paragraph per family, so each reads on its own; set close together as one list.
            foreach (var f in listed)
            {
                string name = Name(f.FamilyUserId);
                string mean = Record("listed", name + " mean share (%)", Percent(f.MeanShare));
                string sd = f.StandardDeviation.HasValue ? "SD " + Record("listed", name + " sample SD (percentage points)", Percent(f.StandardDeviation)) : "SD unavailable";
                string proteins = counts.TryGetValue(f.FamilyUserId, out var count)
                    ? Record("listed", name + " greedy protein estimate", Integer(count.GreedyProteinCount)) + (count.GreedyProteinCount == 1 ? " protein" : " proteins")
                    : "protein estimate unavailable";
                Say("listed:" + Integer(f.FamilyUserId), name + ": " + mean + "% (" + sd + "; " + proteins + ").", compact: f != listed[^1]);
            }
            var cvs = listed.Where(f => f.CoefficientOfVariation.HasValue).Select(f => f.CoefficientOfVariation!.Value).ToList();
            if (cvs.Count > 0)
            {
                int runCount = listed.Max(f => f.AvailableRunCount);
                Say("replicates", cvs.Count == 1
                    ? "The coefficient of variation of the per-run shares is " + Record("replicates", "coefficient of variation (%)", Percent(cvs[0])) + "% over " + Record("replicates", "runs", Integer(runCount)) + " runs."
                    : "Across these " + Record("replicates", "families with a coefficient of variation", Integer(cvs.Count)) + " families, the coefficient of variation of the per-run shares ranges from "
                        + Record("replicates", "lowest coefficient of variation (%)", Percent(cvs.Min())) + "% to " + Record("replicates", "highest coefficient of variation (%)", Percent(cvs.Max()))
                        + "% over " + Record("replicates", "runs", Integer(runCount)) + " runs.");
            }
        }

        if (grouping.OtherFamilies.Count > 0)
        {
            var reasons = grouping.OtherMembership.ToDictionary(m => m.FamilyUserId, m => m.Reason);
            bool Is(FamilyRunShareSummary f,FamilyReportOtherReason reason) => reasons.GetValueOrDefault(f.FamilyUserId).HasFlag(reason);
            // Disjoint counts, so the parts add up to the total.
            var why = new List<string>();
            void Reason(int count, string text) { if (count > 0) why.Add(Record("others", text, Integer(count)) + " " + text); }
            Reason(grouping.OtherFamilies.Count(f => Is(f, FamilyReportOtherReason.Threshold) && !Is(f, FamilyReportOtherReason.PanelDecision)), "below the threshold");
            Reason(grouping.OtherFamilies.Count(f => Is(f, FamilyReportOtherReason.PanelDecision) && !Is(f, FamilyReportOtherReason.Threshold)), "by panel decision");
            Reason(grouping.OtherFamilies.Count(f => Is(f, FamilyReportOtherReason.Threshold) && Is(f, FamilyReportOtherReason.PanelDecision)), "below the threshold and by panel decision");
            foreach (var member in grouping.OtherFamilies.OrderByDescending(f => f.MeanShare).ThenBy(f => f.FamilyUserId))
                numbers.Add(new[] { "others", Name(member.FamilyUserId) + " mean share (%)", Percent(member.MeanShare) });
            string total = grouping.OthersMeanShare.HasValue
                ? "; together they hold " + Record("others", "Others mean share (%)", Percent(grouping.OthersMeanShare)) + "% of the credited family signal" : "";
            Say("others", Record("others", "families under Others", Integer(grouping.OtherFamilies.Count)) + (grouping.OtherFamilies.Count == 1 ? " family is" : " families are")
                + " reported under Others" + (why.Count == 0 ? "" : " (" + string.Join(", ", why) + ")") + total + "; the supplement names them.");
        }

        if (input.Panel is { } panel)
        {
            var unobserved = UnobservedPanelKeys(input);
            if (unobserved.Count > 0)
            {
                foreach (string key in unobserved)
                    numbers.Add(new[] { "unobserved", ReportFamilyNames.DisplayName(panel, panel.Families.Single(f => f.Key == key), input.FamilyNameChoice) + " database members", Integer(input.MembersByPanelKey[key]) });
                Say("unobserved", Record("unobserved", "quantified panel families without an observed peptide", Integer(unobserved.Count))
                    + (unobserved.Count == 1 ? " quantified panel family has" : " quantified panel families have") + " database members and no peptide observed ("
                    + Record("unobserved", "their database members", Integer(unobserved.Sum(k => input.MembersByPanelKey[k]))) + " database members); the supplement names "
                    + (unobserved.Count == 1 ? "it." : "them."));
            }
        }

        bool anyUnresolved = false;
        foreach (var family in StatedSubclassParts(input))
        {
            string name = Name(family.FamilyUserId);
            anyUnresolved |= family.Parts.Any(p => p.IsUnresolved && p.ShareOfFamily > 0);
            Say("subclasses:" + Integer(family.FamilyUserId), name + " subclass parts, all runs pooled: "
                + string.Join(", ", family.Parts.Select(p => p.Label + " " + Record("subclasses", name + " " + p.Label + " (% of the family)", Percent(p.ShareOfFamily)) + "%")) + ".");
        }
        if (anyUnresolved) Say("subclasses-method", NotResolvedMethod);

        if (input.Panel != null)
        {
            double outside = input.Shares.Families.Where(f => !f.NoPeptideObserved && input.PanelKeyByUserId.GetValueOrDefault(f.FamilyUserId) == null)
                .Sum(f => f.MeanShare ?? 0);
            Say("outside", "Groups outside the panel families hold " + Record("outside", "outside-panel mean share (%)", Percent(outside)) + "% of the credited family signal.");
        }
        var runs = input.Shares.Runs.Where(r => r.MeasuredSignal > 0).ToList();
        if (runs.Count > 0)
            Say("credited", "The credited family signal is " + Join(runs.Select(r => Record("credited", r.Run + " credited share of measured (%)", Percent(r.AssignedSignal / r.MeasuredSignal)) + "%"))
                + " of the measured peptide signal in " + Join(runs.Select(r => r.Run)) + ".");

        var choices = new List<string>();
        if (input.ScopeTerms.Count > 0)
            choices.Add("evidence scope phrases " + string.Join(", ", input.ScopeTerms.Select(t => "\"" + t + "\"")));
        if (input.Panel != null)
        {
            var notes = input.Links.Select(l => l.Source).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();
            choices.Add(Record("choices", "confirmed panel links", Integer(input.Links.Count)) + " confirmed panel links" + (notes.Count == 0 ? "" : " (notes: " + string.Join("; ", notes) + ")"));
        }
        choices.Add("report threshold " + Record("choices", "report threshold (%)", threshold) + "%");
        foreach (var source in input.DeNovoInputs.GroupBy(i => i.SourceId, StringComparer.Ordinal).Select(g => g.First()))
            choices.Add("de novo source \"" + source.SourceDisplayName + "\" with " + source.ConfidenceField + " at least "
                + Record("choices", source.SourceDisplayName + " minimum " + source.ConfidenceField, source.MinimumConfidence.ToString("R", CultureInfo.InvariantCulture)));
        // The numbers table below becomes a workbook reference paragraph; it stays on this sentence's page.
        Say("choices", "Analysis choices in force: " + string.Join("; ", choices) + ".", keepWithNext: true);

        string csv = Csv(new[] { "statement", "quantity", "value" }, numbers);
        blocks.Add(new ReportTable("findings-numbers", "Numbers in the summary of findings", new[] { "Statement", "Quantity", "Value" }, numbers,
            "Every number printed in the summary of findings, as printed, with the statement that prints it.") { SupplementOnly = true });
        return (new ReportSection(ReportSectionIds.Findings, Title, blocks), csv);
    }

    private static string Join(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }

    // The same renderings as ReportStudyExports: percentages to six significant digits, integers invariant.
    private static string Percent(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? (100 * value.Value).ToString("G6", CultureInfo.InvariantCulture) : string.Empty;
    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Csv(IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string>> rows)
    {
        var text = new StringBuilder();
        void Append(IReadOnlyList<string> row) => text.Append(string.Join(",", row.Select(CsvFormatting.AlwaysQuoted))).Append('\n');
        Append(columns);
        foreach (var row in rows) Append(row);
        return text.ToString();
    }
}
