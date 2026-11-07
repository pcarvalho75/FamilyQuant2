using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReferencePanelSecondDatabaseCommands() =>
    [new("--test-reference-panel-second", 10182, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-reference-panel-second", (args, _) =>
        {
            ReferencePanelSecondDatabaseTests(args.Length == 2 ? Path.GetFullPath(args[1])
                : Path.Combine(Path.GetTempPath(), "FamilyQuant.ReferencePanelSecond"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReferencePanelSecondDatabaseTests(string output)
    {
        Section("Z15a3 second repository fixture, with and without an imported family list");
        Directory.CreateDirectory(output);
        var requestType = typeof(ReferencePanelFetcher).Assembly.GetType("FamilyQuant.Evidence.ReferencePanelRequest");
        var fetch = typeof(ReferencePanelFetcher).GetMethod("FetchForScopeAsync", BindingFlags.Public | BindingFlags.Static);
        bool available = requestType != null && fetch?.GetParameters().Length == 7;
        Check("Z15a3 the scoped reference-panel contract is present", available,
            "Requires approved Z15a1 c26199e4; absence is a failing dependency, never a skipped parity proof.");
        if (!available) return;
        const BindingFlags members = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
        var database = typeof(Program).GetMethod("ScopedPanelDatabase", members);
        var handlerType = typeof(Program).GetNestedType("ScopedPanelHandler", BindingFlags.NonPublic);
        Check("Z15a3 the second repository fixture and its offline handler are present", database != null && handlerType != null);
        if (database == null || handlerType == null) return;
        var fixture = ((FamilyClusterFile File, List<(PanelEntry Entry, string Json)> Catalogue))database.Invoke(null, new object[] { true })!;
        string original = fixture.File.ToJsonText();
        var imported = ImportSecondDatabasePanel(fixture.File, fixture.Catalogue, output);
        Check("Z15a3 a real spreadsheet import supplies the comparison family list",
            imported.Import is { SourceSha256.Length: > 0 } && imported.Families.Count >= 3
            && imported.Families.All(family => family.Decision == FamilyPanelDecision.Quantify));
        var options = new UniProtFetchOptions
        {
            DelayAsync = (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            UtcNow = () => DateTimeOffset.UnixEpoch
        };
        var reports = new List<object>();
        foreach (bool withPanel in new[] { false, true })
        {
            var source = FamilyClusterFile.FromJsonText(original);
            source.FamilyPanel = withPanel ? FamilyPanelValidation.Clone(imported) : null;
            string sourceJson = source.ToJsonText();
            using var fullHandler = Handler();
            var watch = Stopwatch.StartNew();
            var full = ReferencePanelFetcher.FetchAsync(ReferencePanelFetcher.FamiliesFound(source), fullHandler, options: options).GetAwaiter().GetResult();
            double fullFetchMs = watch.Elapsed.TotalMilliseconds;
            string fullBefore = JsonSerializer.Serialize(full);
            foreach (string scope in new[] { "\"Chosen phase\"", "\"Later phase\"", "\"Chosen phase\", \"Later phase\"", "\"Unrelated phase\"" })
            {
                var terms = WordCluster.ParseKeywords(scope).ToArray();
                using var scopedHandler = Handler();
                var request = Activator.CreateInstance(requestType!, new object[] { terms, false })!;
                watch.Restart();
                var task = (Task)fetch!.Invoke(null, new object?[] { source, request, scopedHandler, null, CancellationToken.None, null, options })!;
                task.GetAwaiter().GetResult();
                var scoped = (ReferencePanel)task.GetType().GetProperty("Result")!.GetValue(task)!;
                double scopedFetchMs = watch.Elapsed.TotalMilliseconds;
                string scopedBefore = JsonSerializer.Serialize(scoped);
                string label = "Z15a3 " + (withPanel ? "imported-list" : "no-panel") + " " + scope;
                Check(label + " detached fetch preserves the source and stays inside the full entry universe",
                    full.IsComplete && scoped.IsComplete && source.ToJsonText() == sourceJson
                    && scoped.Entries.All(entry => full.Entries.Any(candidate => candidate.Accession == entry.Accession)));

                // Warm both independent proposal routes, then alternate the timed order. The timing is
                // fixture-only and includes detached input preparation and the complete proposal snapshot.
                var expected = Snapshot(full); var actual = Snapshot(scoped);
                Check(label + " complete serialized proposal and every default tick equal the full panel byte for byte",
                    expected.Proposal == actual.Proposal && expected.Rows == actual.Rows && expected.Ticks.SequenceEqual(actual.Ticks));
                var fullTimes = new List<double>(); var scopedTimes = new List<double>();
                bool repeatedEqual = true;
                for (int pair = 0; pair < 3; pair++)
                {
                    (string Proposal, string Rows, bool[] Ticks, double Ms) a, b;
                    if (pair % 2 == 0) { a = Snapshot(full); b = Snapshot(scoped); }
                    else { b = Snapshot(scoped); a = Snapshot(full); }
                    fullTimes.Add(a.Ms); scopedTimes.Add(b.Ms);
                    repeatedEqual &= a.Proposal == expected.Proposal && b.Proposal == expected.Proposal
                        && a.Rows == expected.Rows && b.Rows == expected.Rows
                        && a.Ticks.SequenceEqual(expected.Ticks) && b.Ticks.SequenceEqual(expected.Ticks);
                }
                Check(label + " repeated native proposals stay identical without modifying either reference panel",
                    repeatedEqual && JsonSerializer.Serialize(full) == fullBefore && JsonSerializer.Serialize(scoped) == scopedBefore);
                double fullMs = Median(fullTimes), scopedMs = Median(scopedTimes), savedMs = fullMs - scopedMs;
                long fullBytes = Bytes(fullHandler), scopedBytes = Bytes(scopedHandler);
                Console.WriteLine(FormattableString.Invariant(
                    $"Z15a3 route={(withPanel ? "imported-list" : "no-panel")} scope={scope} proposal_changes={(expected.Proposal == actual.Proposal ? 0 : 1)} full_proposal_ms={fullMs:F3} scoped_proposal_ms={scopedMs:F3} saved_ms={savedMs:F3} full_fetch_ms={fullFetchMs:F3} scoped_fetch_ms={scopedFetchMs:F3} full_bytes={fullBytes} scoped_bytes={scopedBytes}"));
                reports.Add(new { importedList = withPanel, scope, fullProposalMs = fullMs, scopedProposalMs = scopedMs, savedMs,
                    fullFetchMs, scopedFetchMs, fullBytes, scopedBytes, fullEntries = full.Entries.Count, scopedEntries = scoped.Entries.Count,
                    fullTimes, scopedTimes, proposalEqual = expected.Proposal == actual.Proposal, rowsEqual = expected.Rows == actual.Rows,
                    defaultTicksEqual = expected.Ticks.SequenceEqual(actual.Ticks), repeatedEqual });
                if (!withPanel && scope == "\"Chosen phase\"")
                {
                    var unclosed = JsonSerializer.Deserialize<ReferencePanel>(scopedBefore)!;
                    unclosed.Entries = unclosed.Entries.Where(entry => ReferencePanelFetcher.FamiliesOf(entry.Evidence)
                        .Contains("Invented alpha family", StringComparer.Ordinal) || EvidenceClassifier.IsInScope(entry.Evidence, terms)).ToList();
                    bool noticed;
                    try { noticed = Snapshot(unclosed).Proposal != expected.Proposal; }
                    catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { noticed = true; }
                    Check("Z15a3 an unclosed scope slice is refused or changes the proposal, so equality is not a vacuous count check", noticed);
                    Check("Z15a3 the second fixture transfers fewer scoped response bytes", scopedBytes < fullBytes && scoped.Entries.Count < full.Entries.Count);
                }

                (string Proposal, string Rows, bool[] Ticks, double Ms) Snapshot(ReferencePanel panel)
                {
                    var timer = Stopwatch.StartNew();
                    var copy = FamilyClusterFile.FromJsonText(sourceJson);
                    copy.EvidenceSource!.ReferencePanel = panel;
                    var session = new EvidenceGroupingSession(copy, "second fixture");
                    var review = session.Propose(scope);
                    var proposal = session.Proposal ?? throw new InvalidOperationException("The fixture produced no native proposal.");
                    if (review.Proteins.Count == 0) throw new InvalidOperationException("The second fixture produced no protein review rows.");
                    var result = (JsonSerializer.Serialize(proposal), JsonSerializer.Serialize(review.Proteins),
                        new EvidenceReviewViewModel(review).Proteins.Select(row => row.Selected).ToArray());
                    return (result.Item1, result.Item2, result.Item3, timer.Elapsed.TotalMilliseconds);
                }
            }
        }
        File.WriteAllText(Path.Combine(output, "second-database.json"), JsonSerializer.Serialize(new
        {
            Fixture = "ScopedPanelDatabase(true), derived from repository EvidenceMatchingFile and deterministic sequences",
            Timing = "Offline handler fetch plus paired warm native proposal timings; no speed threshold and no real-file claim",
            Comparison = "Complete proposal JSON, complete protein-row JSON and every default tick; no field normalization",
            Measurements = reports
        }, new JsonSerializerOptions { WriteIndented = true }));

        HttpMessageHandler Handler() => (HttpMessageHandler)Activator.CreateInstance(handlerType!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: new object[] { fixture.Catalogue }, culture: CultureInfo.InvariantCulture)!;
        long Bytes(HttpMessageHandler handler) => (long)handlerType!.GetField("Bytes", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(handler)!;
        static double Median(IEnumerable<double> values) => values.OrderBy(value => value).ElementAt(1);
    }

    private static FamilyPanel ImportSecondDatabasePanel(FamilyClusterFile source, List<(PanelEntry Entry, string Json)> catalogue, string output)
    {
        var families = ReferencePanelFetcher.FamiliesFound(source);
        var entries = catalogue.Select(item => item.Entry).ToArray();
        var panel = new FamilyPanel { Name = "Second fixture imported list", Families = families.Select((name, index) => new FamilyPanelFamily
        {
            Key = "fixture-" + index.ToString(CultureInfo.InvariantCulture), DisplayName = name, Decision = FamilyPanelDecision.Quantify,
            Pfam = entries.Where(entry => ReferencePanelFetcher.FamiliesOf(entry.Evidence).Contains(name, StringComparer.OrdinalIgnoreCase))
                .SelectMany(entry => entry.Evidence.Pfam).Select(domain => domain.Accession).Distinct(StringComparer.Ordinal)
                .Select(accession => new FamilyPanelDomain { Accession = accession }).ToList()
        }).ToList() };
        string workbook = Path.Combine(output, "second-fixture-list.xlsx");
        FamilyPanelTemplate.Write(workbook, panel);
        var table = new PanelSpreadsheetReader().ReadTable(workbook, "Families", 2);
        var choices = new FamilyPanelImport { FamilySheet = table.SheetName, HeaderRowNumber = table.HeaderRowNumber, SourceSha256 = table.SourceSha256,
            ColumnHeaders = new() { [PanelImportFields.FamilyKey] = "Key", [PanelImportFields.DisplayName] = "Name",
                [PanelImportFields.Decision] = "Decision", [PanelImportFields.Pfam] = "Pfam" } };
        return new FamilyPanelSpreadsheetImporter().Import(table, choices, new[]
        {
            new FamilyPanelDecisionMapping { SourceValue = "Quantify", Decision = FamilyPanelDecision.Quantify }
        }, panel.Name);
    }
}
