using FamilyQuant.Homology;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlProofGuardCommands() =>
    [new("--test-html-stored-proof-guard", 10498, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-html-stored-proof-guard", (_, _) =>
        {
            HtmlStoredProofGuardTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlStoredProofGuardTests()
    {
        Section("M1o stored-only census guard");
        string root = Path.Combine(Path.GetTempPath(), "HtmlStoredGuard", Guid.NewGuid().ToString("N"));
        string miss = Path.Combine(root, "miss"); Directory.CreateDirectory(miss);
        var targets = new List<FastaItemExtended> { new() { SequenceIdentifier = "GUARD_TARGET", Sequence = "TTACDEFGHIKGG", UserID = 1, Description = "Fixture" } };
        var database = HomologyDatabase.Build(targets.Select(t => (t.SequenceIdentifier, t.Sequence)), 3, false);
        var options = new HomologyOptions { MinIdentity = .99 };
        var attribution = new PeptideFamilyAttribution();
        void Set(string name, object value) => typeof(PeptideFamilyAttribution).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(attribution, value);
        Set("Database", database); Set("TargetItems", targets); Set("EffectiveOptions", options); Set("IdentifiedOptions", options);
        attribution.FamilyDescriptions[1] = "Fixture";
        var events = new List<OperationProgress>();
        var operation = HtmlStoredOnlyOperation(observe: events.Add);
        int searchDelegateCalls = 0;
        var census = typeof(FamilyEnvelopeQuant).Assembly.GetType("FamilyQuant.FamilySourceCensusCache")!.GetMethod("GetOrCompute", BindingFlags.Static | BindingFlags.NonPublic)!;
        FamilySourceSequenceSummary Native(string directory, OperationContext context, Func<FamilySourceSequenceSummary> search)
        {
            try { return (FamilySourceSequenceSummary)census.Invoke(null, new object?[] { Array.Empty<QuantRow>(), database, targets, options, options,
                attribution.FamilyDescriptions, directory, context, search })!; }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }
        bool stopped = Throws<OperationCanceledException>(() => Native(miss, operation, () => { searchDelegateCalls++; return new(); }));
        Check("a planted census cache miss stops before its search delegate runs", stopped && searchDelegateCalls == 0
            && events.Any(e => e.Stage == "report-census-cache" && e.Message.Contains("miss", StringComparison.OrdinalIgnoreCase)));
        events.Clear();
        stopped = Throws<OperationCanceledException>(() => HtmlStoredCensusFamilies(Array.Empty<QuantRow>(), attribution, HtmlStoredOnlyOperation(observe: events.Add), miss));
        Check("the actual proof driver uses the cached overload and stops on a missing entry", stopped
            && events.Any(e => e.Stage == "report-census-cache" && e.Message.Contains("miss", StringComparison.OrdinalIgnoreCase))
            && events.All(e => e.Stage is not "report-census-identified-start" and not "report-census-denovo-start"));
        string absent = Path.Combine(root, "absent");
        Check("an absent stored census directory is refused without creating it", Throws<OperationCanceledException>(() =>
            HtmlStoredCensusFamilies(Array.Empty<QuantRow>(), attribution, HtmlStoredOnlyOperation(), absent)) && !Directory.Exists(absent));
        Check("a direct uncached search-start phase is refused before searching", Throws<OperationCanceledException>(() =>
            HtmlStoredOnlyOperation().Report("report-census-identified-start", "Data step 2: source census 1 of 2, identified search")));
        string warm = Path.Combine(root, "warm"); Directory.CreateDirectory(warm);
        int seed = 0;
        Native(warm, new OperationContext(), () => { seed++; return new() { MappingMinimumIdentity = .99 }; });
        events.Clear();
        var result = HtmlStoredCensusFamilies(Array.Empty<QuantRow>(), attribution, HtmlStoredOnlyOperation(observe: events.Add), warm);
        Check("a verified stored census hit is read without either search", seed == 1 && result.SourceSequenceSummary.MappingMinimumIdentity == .99
            && events.Any(e => e.Stage == "report-census-cache" && e.Message.Contains("hit", StringComparison.OrdinalIgnoreCase))
            && events.Where(e => e.Stage is "report-census-identified-start" or "report-census-denovo-start").All(e => e.Message.EndsWith("cached", StringComparison.Ordinal)));
        Check("refused misses write no census artifact", !Directory.EnumerateFiles(miss, "*.fqcensus").Any());
    }
}
