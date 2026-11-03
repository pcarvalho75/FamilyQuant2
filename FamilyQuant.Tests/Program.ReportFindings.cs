using FamilyQuant.DeNovo;
using FamilyQuant.Panels;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportFindingsCommands() =>
    [new("--test-report-findings", 10466, TestCommandPhase.Raw,
        args => args.Length == 1 && args[0] == "--test-report-findings", (_, _) => RunReportFindingsCommand())];

    private static int RunReportFindingsCommand()
    {
        ReportFindingsTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    // Synthetic stored studies: two listed families, one below the threshold, one Others by panel decision, one quantified
    // panel family with members and no peptide, one group outside the panel. Names are fixture data, never product text.
    private static ReportFindingsInput FindingsFixture()
    {
        FamilyRunShareSummary Family(int id, double? mean, double? sd, double? cv, bool none = false) => new()
        {
            FamilyUserId = id, MeanShare = mean, StandardDeviation = sd, CoefficientOfVariation = cv, NoPeptideObserved = none,
            AvailableRunCount = 2, MeanShareOfMeasuredSignal = mean * 0.8,
            Runs = new() { new() { Run = "run_a", ShareOfAssignedSignal = mean }, new() { Run = "run_b", ShareOfAssignedSignal = mean } },
        };
        var shares = new FamilyRunSharesResult
        {
            Runs = new() { new() { Run = "run_a", AssignedSignal = 80, MeasuredSignal = 100 }, new() { Run = "run_b", AssignedSignal = 81, MeasuredSignal = 100 } },
            Families = new() { Family(1, 0.6, 0.012, 0.02), Family(2, 0.385, 0.0154, 0.04), Family(3, 0.0006, 0.0001, 0.1667),
                Family(4, 0, 0, null, none: true), Family(5, 0.0008, 0.0001, 0.125), Family(6, 0.0024, 0.0002, 0.0833) },
        };
        var panel = new FamilyPanel { Families = new()
        {
            new() { Key = "a", DisplayName = "Alpha group", Decision = FamilyPanelDecision.Quantify },
            new() { Key = "b", DisplayName = "Beta group", Decision = FamilyPanelDecision.Quantify },
            new() { Key = "c", DisplayName = "Gamma group", Decision = FamilyPanelDecision.Quantify },
            new() { Key = "d", DisplayName = "Delta group", Decision = FamilyPanelDecision.Quantify },
            new() { Key = "e", DisplayName = "Epsilon group", Decision = FamilyPanelDecision.ReportUnderOthers },
        } };
        var keys = new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "a" }, [2] = new[] { "b" }, [3] = new[] { "c" }, [4] = new[] { "d" }, [6] = new[] { "e" } };
        var grouping = FamilyReportThreshold.Build(shares, new FamilyClusterFile { FamilyPanel = panel }, keys);
        return new ReportFindingsInput
        {
            Shares = shares, Grouping = grouping,
            Counts = new() { Families = new() { new() { FamilyUserId = 1, GreedyProteinCount = 12 }, new() { FamilyUserId = 2, GreedyProteinCount = 1 } } },
            Subclasses = new() { Families = new() { new() { FamilyUserId = 1, FamilySignal = 100, Parts = new()
            {
                new() { PartKey = "x", Label = "Part X", Signal = 40, ShareOfFamily = 0.4 },
                new() { PartKey = "y", Label = "Part Y", Signal = 15, ShareOfFamily = 0.15 },
                new() { PartKey = null, Label = "Subclass not resolved", IsUnresolved = true, Signal = 45, ShareOfFamily = 0.45 },
            } } } },
            Labels = new Dictionary<int, string> { [1] = "Alpha group", [2] = "Beta group", [3] = "Gamma group", [4] = "Delta group", [5] = "Outside group", [6] = "Epsilon group" },
            Panel = panel,
            PanelKeyByUserId = new Dictionary<int, string?> { [1] = "a", [2] = "b", [3] = "c", [4] = "d", [6] = "e" },
            MembersByPanelKey = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 10, ["b"] = 5, ["c"] = 2, ["d"] = 7, ["e"] = 1 },
            ScopeTerms = new[] { "term one", "term two" },
            Links = new List<FamilyPanelEvidenceLink> { new() { SourceFamily = "s1", PanelFamilyKey = "a", Source = "user note" }, new() { SourceFamily = "s2", PanelFamilyKey = null } },
            DeNovoInputs = new[] { new DeNovoInputManifest { SourceId = "source-one", SourceDisplayName = "source one", ConfidenceField = "score", MinimumConfidence = 80 } },
        };
    }

    private static void ReportFindingsTests()
    {
        Section("Summary of findings - fixed templates from stored studies, each sentence only when its data exist");
        var input = FindingsFixture();
        var (section, csv) = ReportFindings.Build(input);
        string Text(string id) => section.Blocks.OfType<ReportParagraph>().SingleOrDefault(p => p.Id == "findings:" + id)?.Text ?? "";
        var ids = section.Blocks.OfType<ReportParagraph>().Select(p => p.Id).ToArray();
        Check("J44 the section has its own id and title", section.Id == ReportSectionIds.Findings && section.Title == ReportFindings.Title);
        Check("J44 every sentence is present when its data exist", new[] { "listed", "replicates", "others", "unobserved", "subclasses:1", "subclasses-method", "outside", "credited", "choices" }
            .All(id => ids.Contains("findings:" + id)), string.Join(", ", ids));
        var headline = ReportCsvTable.Build("h", "h", ReportStudyExports.Headline(input.Grouping, input.Counts, input.Labels), "", "family", "status", "mean_pct", "sample_sd_pct", "greedy_protein_estimate");
        var listed = headline.Rows.Where(r => r[1] == "Listed").ToList();
        Check("J44 listed families follow the headline order with its exact share, SD and protein count", listed.Count == 2
            && Text("listed").Contains("Alpha group " + listed[0][2] + "% (SD " + listed[0][3] + "; " + listed[0][4] + " proteins); Beta group " + listed[1][2] + "% (SD " + listed[1][3] + "; " + listed[1][4] + " protein)", StringComparison.Ordinal)
            && Text("listed").StartsWith("2 families are above the report threshold of 0.1%", StringComparison.Ordinal), Text("listed"));
        Check("J44 replicate agreement is the range of the listed families' CVs", Text("replicates") == "Across these 2 families, the coefficient of variation of the per-run shares ranges from 2% to 4% over 2 runs.", Text("replicates"));
        string othersShare = (100 * input.Grouping.OthersMeanShare!.Value).ToString("G6", CultureInfo.InvariantCulture);
        Check("J44 Others gives the members in share order, both reasons and the stored Others share", othersShare == "0.38"
            && Text("others") == "4 families are reported under Others (3 below the threshold, 1 by panel decision); together they hold "
            + othersShare + "% of the credited family signal; the supplement names them.", Text("others"));
        Check("J44 no Others member is named in the document text, only in the supplement (D35)", section.Blocks.OfType<ReportParagraph>()
            .All(p => !new[] { "Epsilon group", "Outside group", "Gamma group", "Delta group" }.Any(n => p.Text.Contains(n, StringComparison.Ordinal)))
            && csv.Contains("Epsilon group mean share", StringComparison.Ordinal) && csv.Contains("Delta group database members", StringComparison.Ordinal));
        Check("J44 quantified panel families with members and no peptide are counted with their members", Text("unobserved") == "1 quantified panel family has database members and no peptide observed (7 database members); the supplement names it.", Text("unobserved"));
        var parts = ReportCsvTable.Build("p", "p", ReportStudyExports.Parts(input.Subclasses!.Families, input.Labels), "", "part", "within_family_pct");
        Check("J44 subclass parts equal the stored parts table, not-resolved named with its rule", Text("subclasses:1") == "Alpha group subclass parts, all runs pooled: "
            + string.Join(", ", parts.Rows.Select(r => r[0] + " " + r[1] + "%")) + "." && Text("subclasses-method") == ReportFindings.NotResolvedMethod, Text("subclasses:1"));
        Check("J44 outside-panel and credited shares come from the stored run totals", Text("outside") == "Groups outside the panel families hold 0.08% of the credited family signal."
            && Text("credited") == "The credited family signal is 80% and 81% of the measured peptide signal in run_a and run_b.", Text("outside") + " | " + Text("credited"));
        Check("J44 analysis choices print the scope phrases, links with notes, threshold and de novo cutoff", Text("choices") == "Analysis choices in force: evidence scope phrases \"term one\", \"term two\"; 2 confirmed panel links (notes: user note); report threshold 0.1%; de novo input source one with score at least 80.", Text("choices"));
        var values = ReportCsvTable.Build("c", "c", csv, "", "value").Rows.Select(r => r[0]).ToHashSet(StringComparer.Ordinal);
        var printed = section.Blocks.OfType<ReportParagraph>().SelectMany(p => Regex.Matches(p.Text.Replace("run_a", "").Replace("run_b", ""), @"\d+(\.\d+)?").Select(m => m.Value)).ToList();
        Check("J44 every number printed is a row of the findings supplement", printed.All(values.Contains), string.Join(", ", printed.Where(v => !values.Contains(v))));
        var table = section.Blocks.OfType<ReportTable>().Single();
        Check("J44 the numbers table is supplement-only and holds every recorded number", table.SupplementOnly && table.Id == "findings-numbers"
            && table.Rows.Select(r => r[2]).ToHashSet(StringComparer.Ordinal).SetEquals(values));
        string fixedText = string.Join(" ", section.Blocks.OfType<ReportParagraph>().Select(p => p.Text));
        foreach (string label in input.Labels.Values.Concat(new[] { "term one", "term two", "user note", "source one", "score", "Part X", "Part Y", "Subclass not resolved" })) fixedText = fixedText.Replace(label, "", StringComparison.Ordinal);
        Check("J44 fixed wording names no organism, venom or toxin and no family literal (D21)", !Regex.IsMatch(fixedText, @"venom|toxin|snake|viper|bothrops|lectin|metallo|protease|kinase|PLA2|SVMP", RegexOptions.IgnoreCase), fixedText);

        var bare = input with { Panel = null, PanelKeyByUserId = new Dictionary<int, string?>(), MembersByPanelKey = new Dictionary<string, int>(), Subclasses = null,
            ScopeTerms = Array.Empty<string>(), Links = Array.Empty<FamilyPanelEvidenceLink>(), DeNovoInputs = Array.Empty<DeNovoInputManifest>(),
            Grouping = FamilyReportThreshold.Build(input.Shares, 0) };
        var bareIds = ReportFindings.Build(bare).Section.Blocks.OfType<ReportParagraph>().Select(p => p.Id).ToArray();
        Check("J44 sentences without data are absent: no panel, subclass, Others, scope, link or de novo sentence",
            !bareIds.Any(id => id is "findings:unobserved" or "findings:outside" or "findings:others" or "findings:subclasses-method" || id.StartsWith("findings:subclasses:", StringComparison.Ordinal))
            && ReportFindings.Build(bare).Section.Blocks.OfType<ReportParagraph>().Single(p => p.Id == "findings:choices").Text == "Analysis choices in force: report threshold 0%.", string.Join(", ", bareIds));
        var noCv = input with { Grouping = FamilyReportThreshold.Build(new FamilyRunSharesResult { Runs = input.Shares.Runs,
            Families = input.Shares.Families.Select(f => new FamilyRunShareSummary { FamilyUserId = f.FamilyUserId, MeanShare = f.MeanShare, AvailableRunCount = 1, Runs = f.Runs, NoPeptideObserved = f.NoPeptideObserved }).ToList() },
            new FamilyClusterFile { FamilyPanel = input.Panel }, new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "a" }, [2] = new[] { "b" } }) };
        Check("J44 no CV, no replicate sentence; no SD, the line says so", !ReportFindings.Build(noCv).Section.Blocks.Any(b => b.Id == "findings:replicates")
            && ReportFindings.Build(noCv).Section.Blocks.OfType<ReportParagraph>().Single(p => p.Id == "findings:listed").Text.Contains("SD unavailable", StringComparison.Ordinal));
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var portuguese = ReportFindings.Build(FindingsFixture());
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var english = ReportFindings.Build(FindingsFixture());
            Check("J44 text and CSV are identical under pt-BR and en-US", portuguese.Csv == english.Csv
                && portuguese.Section.Blocks.OfType<ReportParagraph>().Select(p => p.Text).SequenceEqual(english.Section.Blocks.OfType<ReportParagraph>().Select(p => p.Text)));
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
        string order;
        try { order = string.Join(",", ReportDocumentBuilder.Build(new FamilyClusterFile(), new QuantManifest()).Sections.Select(s => s.Id)); }
        catch (Exception ex) { order = ex.GetType().Name + ": " + ex.Message; }
        Check("J44 the base document places Summary of findings right after Summary", order.Contains(ReportSectionIds.Summary + "," + ReportSectionIds.Findings + ",", StringComparison.Ordinal), order);
    }
}
