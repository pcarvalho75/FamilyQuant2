using FamilyQuant.Homology;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HomologyScienceComparisonCommands() =>
    [new("--compare-homology-science-studies", 22, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--compare-homology-science-studies", (args, _) =>
        {
            int reference = Array.IndexOf(args, "--reference-project");
            int count = reference < 0 ? args.Length : reference;
            if (count is not (6 or 7) || reference >= 0 && reference != args.Length - 2)
                throw new ArgumentException("Use --compare-homology-science-studies <project> <off> <tier> <length> [tag] <fresh-output> [--reference-project <delivered-project>].");
            return CompareHomologyScienceStudies(args[1], args.Skip(2).Take(count - 3).ToArray(), args[count - 1], reference < 0 ? null : args[^1]);
        })];

    private static int CompareHomologyScienceStudies(string project, string[] directories, string output, string? referenceProject)
    {
        output = Path.GetFullPath(output);
        if (!output.StartsWith(@"D:\FamilyQuant-data\out-assay\", StringComparison.OrdinalIgnoreCase)
            && !output.StartsWith(@"C:\tmp\familyquant\FQAssay\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Comparison output must stay in the worker-owned proof root.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Fresh comparison output required.");
        string projectSha = StudyHash(project);
        var saved = ProjectStore.ReadFqproj(project);
        string cohort = "This table uses the earlier J38 study curation: " + ComparisonCohort(saved.fcf, saved.manifest)
            + ". Its share changes apply to this curation only, not another delivered-project headline.";
        if (referenceProject != null)
        {
            var reference = ProjectStore.ReadFqproj(referenceProject);
            cohort += " The selected delivered reference is a different cohort: " + ComparisonCohort(reference.fcf, reference.manifest)
                + ". Use the separately verified J47d delivered-project before/after row for the actual release effect; do not quote this historical table as that row.";
        }
        var targets = saved.fcf.MyExtendedFastaItems.Where(p => p != null
            && !(p.SequenceIdentifier ?? "").StartsWith(HomologyOptions.DefaultDecoyLabel, StringComparison.Ordinal)).ToList();
        var names = FamilyNaming.Names(targets);
        string[] labels = { "OFF", "Score tier (margin 0)", "Length calibration", "One rewrite + confidence" };
        var summaries = new List<JsonElement[]>();
        var headlines = new List<JsonElement>();
        var shares = new List<FamilyRunSharesResult>();
        var multiplicities = new List<object>();
        var j42 = new List<JsonElement>();
        var appliedGates = new Dictionary<(int Study, int Call), (int Targets, int Decoys)>();
        var panelBases = new List<ReportCompositionBasis>();
        var replaySeconds = new List<double>();
        for (int i = 0; i < directories.Length; i++)
        {
            string directory = directories[i];
            var validation = StudyRead<JsonElement>(Path.Combine(directory, "validation.json"));
            if (!validation.GetProperty("Complete").GetBoolean() || !validation.GetProperty("NoFullCompute").GetBoolean()
                || validation.GetProperty("Calls").GetInt32() != 5 || validation.GetProperty("ProjectSha256").GetString() != projectSha
                || validation.GetProperty("DefaultOff").GetBoolean() != (i == 0))
                throw new InvalidDataException("Incomplete, unmatched or incorrectly labelled study packet.");
            var config = validation.GetProperty("Config").Deserialize<HomologyStudyConfig>(HomologyStudyJson)!;
            string measuredDirectory = directory;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var measuredConfig = config;
            while (measuredConfig.ReuseResultsDirectory.Length > 0)
            {
                if (!visited.Add(measuredDirectory) || visited.Count > 32) throw new InvalidDataException("Cyclic measured-packet provenance.");
                measuredDirectory = measuredConfig.ReuseResultsDirectory;
                measuredConfig = StudyRead<JsonElement>(Path.Combine(measuredDirectory, "validation.json")).GetProperty("Config").Deserialize<HomologyStudyConfig>(HomologyStudyJson)!;
            }
            replaySeconds.Add(StudyRead<JsonElement>(Path.Combine(measuredDirectory, "search-summary.json"))
                .EnumerateArray().Sum(r => r.GetProperty("Seconds").GetDouble()));
            var science = config.Science;
            bool tier = science?.ScoreTier?.Enabled == true, lengthGate = science?.DecoyGate?.Enabled == true,
                tag = science?.TagErrors?.Enabled == true;
            if (tier != (i == 1) || lengthGate != (i == 2) || tag != (i == 3)
                || i == 1 && science!.ScoreTier!.MaximumPamScoreDrop != 0
                || i == 2 && (science!.DecoyGate!.MinimumSearchIdentity != 0.8 || science.DecoyGate.MinimumLengthQueries != 100 || science.DecoyGate.MaximumDecoyRatio != 0.01)
                || i == 3 && (science!.TagErrors!.MaximumRewrites != 1 || !science.TagErrors.WeightByLocalConfidence
                    || !science.TagErrors.AllowAdjacentOrderSwap || !science.TagErrors.AllowKAndQ || !science.TagErrors.AllowNAndGG || !science.TagErrors.AllowQAndAG))
                throw new InvalidDataException("Comparison columns require the approved separate-option experiment settings.");
            var summary = StudyRead<JsonElement>(Path.Combine(directory, "search-summary.json")).EnumerateArray().ToArray();
            if (summary.Length != 5 || summary.Any(r => (i == 0 || r.GetProperty("Role").GetString() == "Identified")
                && !r.GetProperty("EqualToReference").GetBoolean()))
                throw new InvalidDataException("Certified OFF/identified search parity is missing.");
            summaries.Add(summary);
            headlines.Add(StudyRead<JsonElement>(Path.Combine(directory, "headline.json")));
            shares.Add(StudyRead<FamilyRunSharesResult>(Path.Combine(directory, "family-run-shares.json")));
            var effective = StudyRead<JsonElement>(Path.Combine(directory, "effective-panel-families.json"));
            var basisStudies = new QuantManifest { FamilyRunShares = shares[i],
                EffectivePanelFamilies = effective.ValueKind == JsonValueKind.Null ? null : effective.Deserialize<EffectivePanelFamilyResult>(HomologyStudyJson) };
            panelBases.Add(ReportCompositionBasis.Select(saved.fcf, basisStudies));
            j42.Add(StudyRead<JsonElement>(Path.Combine(directory, "j42-reference.json")));
            var found = StudyRestoreResults(Path.Combine(directory, "results-2.json.gz"));
            foreach (int call in new[] { 2, 3, 5 })
            {
                var callResults = call == 2 ? found : StudyRestoreResults(Path.Combine(directory, "results-" + call + ".json.gz"));
                appliedGates[(i, call)] = (callResults.Count(StudyTargetGate), callResults.Count(StudyDecoyGate));
            }
            // The production join with target members only, including its repeated-identifier rule.
            var mapped = HomologySearch.MapToFamilies(found, targets);
            int attributed = mapped.Count(r => r.FamilyIds.Count > 0);
            multiplicities.Add(new
            {
                Option = labels[i], Queries = mapped.Count, Attributed = attributed,
                Unique = mapped.Count(r => r.FamilyIds.Count == 1), Shared = mapped.Count(r => r.FamilyIds.Count > 1),
                MeanFamiliesAllQueries = mapped.Count == 0 ? 0 : mapped.Average(r => r.FamilyIds.Count),
                MeanFamiliesAttributed = attributed == 0 ? (double?)null : mapped.Where(r => r.FamilyIds.Count > 0).Average(r => r.FamilyIds.Count),
                MaximumFamilies = mapped.Count == 0 ? 0 : mapped.Max(r => r.FamilyIds.Count),
            });
        }
        if (j42.Any(r => r.GetProperty("AuditSha256").GetString() != j42[0].GetProperty("AuditSha256").GetString()
            || r.GetProperty("ProvenanceSha256").GetString() != j42[0].GetProperty("ProvenanceSha256").GetString()))
            throw new InvalidDataException("J42 reference inputs differ between studies.");
        Directory.CreateDirectory(output);
        var decision = new StringBuilder("# RESULT J47b\n\n");
        decision.AppendLine(cohort + "\n");
        decision.AppendLine("Separate-option comparison on the historical study curation, with settings fixed before the later default decision recorded in J47d. The tag gate is deferred to J47c when absent. Known-answer abstained means known pairs that did not land, including below-minimum-length or unsupported queries. Gate counts are distinct normalized queries and can overlap. Gap peptide counts use run/sequence/charge measurements. Family shares are arithmetic per-run means as percent of credited primary family signal; deltas are percentage points from OFF. Families listed in any option under the native threshold/panel-decision rule are included, so a threshold crossing remains visible.");
        decision.Append("\n| Metric | "); decision.Append(string.Join(" | ", labels.Take(directories.Length))); decision.AppendLine(" |\n|---|" + string.Join("|", Enumerable.Repeat("---:", directories.Length)) + "|");
        void Row(string metric, Func<int, string> value) => decision.AppendLine("| " + Escape(metric) + " | " + string.Join(" | ", Enumerable.Range(0, directories.Length).Select(value)) + " |");
        JsonElement DefaultWorld(int i) => headlines[i].GetProperty("World").EnumerateArray().Single(r => r.GetProperty("MinimumTagLength").GetInt32() == saved.manifest.FamilyWorldResult!.MinimumTagLength);
        Row("Known-answer right", i => headlines[i].GetProperty("KnownRight").ToString());
        Row("Known-answer wrong", i => headlines[i].GetProperty("KnownWrong").ToString());
        Row("Known-answer abstained / known pairs", i => (headlines[i].GetProperty("KnownPairs").GetInt32() - headlines[i].GetProperty("KnownLanded").GetInt32()).ToString(CultureInfo.InvariantCulture) + " / " + headlines[i].GetProperty("KnownPairs"));
        Row("Known-answer wrong / landed", i => Fraction(headlines[i].GetProperty("KnownWrong").GetDouble(), headlines[i].GetProperty("KnownLanded").GetDouble()));
        foreach (int call in new[] { 2, 3, 5 })
            Row("Captured call " + call + " admitted target / decoy gate queries", i => appliedGates[(i, call)].Targets + " / " + appliedGates[(i, call)].Decoys);
        var actualGates = directories.Select(d => StudyRead<JsonElement>(Path.Combine(d, "actual-world-gate-summary.json"))).ToArray();
        Row("Actual residual admitted target / decoy gate queries", i => actualGates[i].GetProperty("TargetGateQueries") + " / " + actualGates[i].GetProperty("DecoyGateQueries"));
        Row("Actual residual calibration abstentions / queries", i => actualGates[i].GetProperty("CalibrationAbstentions") + " / " + actualGates[i].GetProperty("Queries"));
        Row("Gap target / decoy peptide measurements", i => DefaultWorld(i).GetProperty("TargetPeptides") + " / " + DefaultWorld(i).GetProperty("DecoyPeptides"));
        Row("Gap decoy / credited envelope", i => Fraction(DefaultWorld(i).GetProperty("DecoyEnvelope").GetDouble(), DefaultWorld(i).GetProperty("CreditedEnvelope").GetDouble()));
        Row("Residual / measured primary envelope", i => Fraction(headlines[i].GetProperty("ResidualEnvelope").GetDouble(), headlines[i].GetProperty("MeasuredTotalEnvelope").GetDouble()));
        Row("World credited / measured primary envelope", i => Fraction(DefaultWorld(i).GetProperty("CreditedEnvelope").GetDouble(), headlines[i].GetProperty("MeasuredTotalEnvelope").GetDouble()));
        Row("J43 panel basis one-to-one / direct share reuse", i => panelBases[i].IsIdentity + " / " + ReferenceEquals(panelBases[i].Studies.FamilyRunShares, shares[i]));
        var panelRows = panelBases.Select(b => b.Studies.FamilyRunShares!.Families.ToDictionary(f => f.FamilyUserId)).ToArray();
        var listed = panelBases.SelectMany(b => b.Grouping!.ListedFamilies).Select(f => f.FamilyUserId).Distinct()
            .OrderByDescending(id => panelRows[0].GetValueOrDefault(id)?.MeanShare).ThenBy(id => id);
        foreach (int id in listed)
            Row("J43 " + panelBases[0].Labels.GetValueOrDefault(id, id.ToString(CultureInfo.InvariantCulture)) + " mean assigned % (delta pp)", i =>
            {
                double? current = panelRows[i].GetValueOrDefault(id)?.MeanShare, before = panelRows[0].GetValueOrDefault(id)?.MeanShare;
                return Percent(current) + (i == 0 ? "" : " (" + (current.HasValue && before.HasValue ? (100 * (current.Value - before.Value)).ToString("+0.000000;-0.000000;0.000000", CultureInfo.InvariantCulture) : "unavailable") + ")");
            });
        Row("Captured-search timed wall seconds (sum)", i => Number(replaySeconds[i]));
        string[] meaning = { "Current mapping retained.", "Credits only best-score protein ties; narrows protein ambiguity and can move family shares.", "Tightens supported lengths and abstains elsewhere; weigh lost coverage against the measured error evidence.", "One-rewrite confidence experiment; assess its measured coverage/error tradeoff before choosing a default." };
        Row("What this means for a user", i => meaning[i]);
        var decisionAudit = j42[0].GetProperty("RecordedAudit");
        decision.AppendLine($"\nRuntime is the sum of original per-search native wall timers, not the later cached diagnostic refresh. OFF actually searches identified calls 1/4; ON reuses their certified results. Import, project loading, cache/attribution/completion are excluded, so these are workflow costs, not a controlled speed benchmark. Detailed gate ratios, family multiplicity, per-length gates and all shares are in comparison.md and the JSON/CSV packet. J42 is structurally invariant, not recomputed; its prior audit remains {decisionAudit.GetProperty("Right")} right / {decisionAudit.GetProperty("Wrong")} wrong / {decisionAudit.GetProperty("Abstained")} abstained with separate cohort provenance. J43 basis identity and direct reuse are recorded above for each option.");
        File.WriteAllText(Path.Combine(output, "decision-table.md"), decision.ToString(), new UTF8Encoding(false));
        StudyWrite(Path.Combine(output, "panel-headline-basis.json"), panelBases.Select((basis, i) => new
        {
            Option = labels[i], basis.UsesPanel, basis.IsIdentity,
            ReusesOriginalSharesWithoutArithmetic = ReferenceEquals(basis.Studies.FamilyRunShares, shares[i]),
            Labels = basis.Labels, SourceUserIds = basis.SourceUserIds, RunShares = basis.Studies.FamilyRunShares,
        }).ToArray());
        for (int i = 0; i < panelBases.Count; i++)
        {
            var basis = panelBases[i];
            if (basis.Grouping != null)
                File.WriteAllText(Path.Combine(output, "panel-headline-" + i + ".csv"),
                    ReportStudyExports.Headline(basis.Grouping, basis.Studies.FamilyProteinCounts, basis.Labels), new UTF8Encoding(false));
        }
        StudyWrite(Path.Combine(output, "multiplicity.json"), multiplicities);
        var text = new StringBuilder("# J47 separate-option replay comparison\n\n");
        text.AppendLine(cohort + "\n");
        text.AppendLine("Produced by FamilyQuant.Tests from the five verified actual-Compute search captures and unchanged saved quantitation. No full Compute or RAW extraction. Each option was measured alone before the later default decision in J47d. The OFF run matches all five certified scientific hashes and the available stored native quantitative views.");
        text.AppendLine("\nGate counts below are distinct normalized query counts with at least one protein of that side reaching the identity gate, before PAM competition/tiering. A query can reach both sides. D/T at the gate is descriptive, not FDR. Protein multiplicity is the mean retained proteins over all queries, including unmatched queries. Identified calls 1 and 4 retain their certified results in all experiments.");
        text.AppendLine("\n| Option | Call | Queries | Target gate | Decoy gate | Gate D/T | Target wins | Decoy wins | Retained proteins/query | Abstained calibration | Confidence coverage |\n|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        for (int i = 0; i < summaries.Count; i++)
            foreach (var row in summaries[i].Where(r => r.GetProperty("Role").GetString() == "DeNovo"))
            {
                int queries = row.GetProperty("Queries").GetInt32();
                var (t, d) = appliedGates[(i, row.GetProperty("Call").GetInt32())];
                text.AppendLine($"| {labels[i]} | {row.GetProperty("Call")} | {queries} | {t} | {d} | {Fraction(d, t)} | {row.GetProperty("TargetWinningQueries")} | {row.GetProperty("DecoyWinningQueries")} | {Number(row.GetProperty("MeanProteinMultiplicity").GetDouble())} | {row.GetProperty("CalibrationAbstentions")} | {row.GetProperty("ConfidenceCoveredQueries")}/{queries} |");
            }
        text.AppendLine("\nThe captured world call describes the original cohort. Curation changes can change the actual residual cohort; its native gate counts below include all lengths used in the stored sensitivity study, with unsupported bins excluded from admitted gate counts. Full support/gate records are in actual-world-length-gates.json. A length-calibrated changed cohort is refit, rather than borrowing gates from the original cohort.");
        text.AppendLine("\n| Option | Actual residual queries | Target gate | Decoy gate | Gate D/T | Calibration abstentions | Supplementary searches |\n|---|---:|---:|---:|---:|---:|---:|");
        for (int i = 0; i < directories.Length; i++)
        {
            var actual = StudyRead<JsonElement>(Path.Combine(directories[i], "actual-world-gate-summary.json"));
            text.AppendLine($"| {labels[i]} | {actual.GetProperty("Queries")} | {actual.GetProperty("TargetGateQueries")} | {actual.GetProperty("DecoyGateQueries")} | {Fraction(actual.GetProperty("DecoyGateQueries").GetDouble(), actual.GetProperty("TargetGateQueries").GetDouble())} | {actual.GetProperty("CalibrationAbstentions")} | {actual.GetProperty("SupplementalQueries")} |");
        }
        int length = saved.manifest.FamilyWorldResult!.MinimumTagLength;
        text.AppendLine($"\nDatabase gap at the stored minimum length {length}. Wrong/landed uses native strict known-answer family correctness; source PSM labels are validation labels, not independent biological truth. Gap peptide counts retain the native run/sequence/charge measurement grain, unlike the distinct normalized-query gate counts above. Decoy/credited envelope uses the original native basis. Unavailable ratios are shown as unavailable.");
        text.AppendLine("\n| Option | Known pairs | Landed | Right | Wrong | Wrong/landed | Gap target peptide measurements | Gap decoy peptide measurements | Gap decoy/credited envelope | Residual/measured | World credited/measured | Unexplained/measured |\n|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        for (int i = 0; i < headlines.Count; i++)
        {
            var h = headlines[i];
            var w = h.GetProperty("World").EnumerateArray().Single(r => r.GetProperty("MinimumTagLength").GetInt32() == length);
            double total = h.GetProperty("MeasuredTotalEnvelope").GetDouble();
            text.AppendLine($"| {labels[i]} | {h.GetProperty("KnownPairs")} | {h.GetProperty("KnownLanded")} | {h.GetProperty("KnownRight")} | {h.GetProperty("KnownWrong")} | {Fraction(h.GetProperty("KnownWrong").GetDouble(), h.GetProperty("KnownLanded").GetDouble())} | {w.GetProperty("TargetPeptides")} | {w.GetProperty("DecoyPeptides")} | {Fraction(w.GetProperty("DecoyEnvelope").GetDouble(), w.GetProperty("CreditedEnvelope").GetDouble())} | {Fraction(h.GetProperty("ResidualEnvelope").GetDouble(), total)} | {Fraction(w.GetProperty("CreditedEnvelope").GetDouble(), total)} | {Fraction(h.GetProperty("UnexplainedEnvelope").GetDouble(), total)} |");
        }
        text.AppendLine("\n## Family shares\n\nArithmetic mean of per-run shares, percent of credited primary family signal. The second value is the signed change from OFF in percentage points. These are composition shares, not percent of the whole specimen. All panel rows are retained, including explicit zero rows. Full measured-envelope shares are in family-share-comparison.json.");
        text.AppendLine("\nJ43 panel-family headline basis is selected by the native ReportCompositionBasis.Select for each option. Identity and direct reuse of the original shares prove that the option's headline effect is unchanged by J43; otherwise the native collapsed panel-unit rows are reported separately. The original UserID comparisons below retain their original unit. Full panel-unit values and source membership are in panel-headline-basis.json, with native headline CSVs for all four options.");
        text.AppendLine("\n| Option | Panel in use | One-to-one identity | Original share object reused without arithmetic |\n|---|---|---|---|");
        for (int i = 0; i < panelBases.Count; i++)
            text.AppendLine($"| {labels[i]} | {panelBases[i].UsesPanel} | {panelBases[i].IsIdentity} | {ReferenceEquals(panelBases[i].Studies.FamilyRunShares, shares[i])} |");
        text.AppendLine("\n| Current family ID and name | " + string.Join(" | ", labels.Take(directories.Length).Select((label, i) => label + (i == 0 ? " %" : " % (delta pp)"))) + " |\n|---|" + string.Join("|", Enumerable.Repeat("---:", directories.Length)) + "|");
        var familyMaps = shares.Select(s => s.Families.ToDictionary(f => f.FamilyUserId)).ToArray();
        var familyRows = new List<object>();
        foreach (int id in familyMaps.SelectMany(m => m.Keys).Distinct().OrderByDescending(id => familyMaps[0].TryGetValue(id, out var f) ? f.MeanShare : 0).ThenBy(id => id))
        {
            double? baseline = familyMaps[0].TryGetValue(id, out var b) ? b.MeanShare : null;
            text.Append($"| {id}: {Escape(names.GetValueOrDefault(id, ""))} ");
            var values = new List<object>();
            for (int i = 0; i < familyMaps.Length; i++)
            {
                familyMaps[i].TryGetValue(id, out var f);
                double? delta = f?.MeanShare - baseline;
                values.Add(new { Option = labels[i], MeanAssignedShare = f?.MeanShare, MeanMeasuredShare = f?.MeanShareOfMeasuredSignal, AssignedDeltaPercentagePoints = delta * 100 });
                text.Append("| " + Percent(f?.MeanShare));
                if (i > 0) text.Append(" (" + (delta.HasValue ? (100 * delta.Value).ToString("+0.000000;-0.000000;0.000000", CultureInfo.InvariantCulture) : "unavailable") + ")");
                text.Append(' ');
            }
            text.AppendLine("|");
            familyRows.Add(new { FamilyUserId = id, Name = names.GetValueOrDefault(id, ""), Values = values });
        }
        text.AppendLine("\n## Family multiplicity\n\nThe native MapToFamilies target-member join, pooled distinct normalized de novo queries from curation call 2. Unique means exactly one current family; shared means more than one. See multiplicity.json for means and denominators. Score tiers affect protein multiplicity even when all surviving proteins belong to the same family.");
        text.AppendLine("\n```json\n" + JsonSerializer.Serialize(multiplicities, HomologyStudyJson) + "\n```");
        text.AppendLine("\n## Calibration, confidence and J42\n\nPer-search/per-length support, selected gates and explicit abstention reasons are retained in length-gates-{call}.json. Gates use a 0.8 exploration floor, tighten only, minimum 100 distinct tags and a plus-one empirical decoy/target criterion of 0.01. This is exploratory calibration, not an FDR guarantee. The actual residual-world cohort is refit if calibration changes that cohort; headline.json records the supplementary search count. Tag confidence uses per-position medians over accepted PEAKS rows; missing/all-zero profiles use unit weights and are counted separately. At most one rewrite is allowed, and PAM scores the original tag.");
        var audit = j42[0].GetProperty("RecordedAudit");
        text.AppendLine($"\nJ42 is structurally invariant: the independent protein-family neighbour path does not call HomologySearch. The recorded audit is {audit.GetProperty("Right")} right, {audit.GetProperty("Wrong")} wrong, {audit.GetProperty("Abstained")} abstained among {audit.GetProperty("Scorable")} scorable entries, with {audit.GetProperty("Unscorable")} unresolved among {audit.GetProperty("ReviewedEntries")} total. It is not recomputed here. Its annotation-derived reference labels are not independent biological truth; j42-reference.json retains the exact prior cohort provenance and hashes. Paulo chooses defaults after science review; this packet makes no default choice.");
        text.AppendLine("\nProject SHA256: " + projectSha + ". Input project unchanged by comparison.");
        File.WriteAllText(Path.Combine(output, "comparison.md"), text.ToString(), new UTF8Encoding(false));
        StudyWrite(Path.Combine(output, "family-share-comparison.json"), familyRows);
        StudyWrite(Path.Combine(output, "headlines.json"), headlines);
        StudyWrite(Path.Combine(output, "j42-reference.json"), j42[0]);
        if (StudyHash(project) != projectSha) throw new InvalidDataException("Input project changed during comparison.");
        string[] artifacts = { "validation.json", "search-summary.json", "results-2.json.gz", "results-3.json.gz", "results-5.json.gz",
            "headline.json", "family-run-shares.json", "family-world.json", "effective-panel-families.json",
            "actual-world-gate-summary.json", "actual-world-length-gates.json", "j42-reference.json" };
        StudyWrite(Path.Combine(output, "input-hashes.json"), directories.Select((directory, i) => new
        {
            Option = labels[i], Directory = directory,
            Sha256 = artifacts.ToDictionary(name => name, name => StudyHash(Path.Combine(directory, name)), StringComparer.Ordinal),
        }).ToArray());
        StudyWrite(Path.Combine(output, "validation.json"), new { Complete = true, ProjectSha256 = projectSha,
            Directories = directories, NoFullCompute = true, J42Recomputed = false, TagDeferredToJ47c = directories.Length == 3,
            ProducerAssemblySha256 = StudyHash(typeof(Program).Assembly.Location),
            CoreAssemblySha256 = StudyHash(typeof(HomologySearch).Assembly.Location) });
        Console.WriteLine(directories.Length + " native replay packets compared; input project unchanged. " + Path.Combine(output, "decision-table.md"));
        return 0;

        static string Number(double value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
        static string Percent(double? value) => value.HasValue && double.IsFinite(value.Value) ? Number(100 * value.Value) : "unavailable";
        static string Fraction(double numerator, double denominator) => denominator > 0 ? Percent(numerator / denominator) + "%" : "unavailable";
        static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    private static string ComparisonCohort(FamilyClusterFile curation, QuantManifest manifest)
    {
        var top = manifest.FamilyRunShares!.Families.OrderByDescending(f => f.MeanShare).First();
        var labels = FamilyNaming.Names(curation.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)));
        return "membership " + FamilyStudyMembership.Fingerprint(curation) + "; leading family " + labels.GetValueOrDefault(top.FamilyUserId, "")
            + ", UserID " + top.FamilyUserId.ToString(CultureInfo.InvariantCulture) + ", assigned mean "
            + (100 * top.MeanShare!.Value).ToString("0.000000", CultureInfo.InvariantCulture) + "%";
    }
}
