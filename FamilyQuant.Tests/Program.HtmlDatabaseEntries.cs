using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlDatabaseEntryCommands() =>
    [new("--test-html-database-entries", 10511, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-html-database-entries", (_, _) =>
        {
            HtmlDatabaseEntryTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlDatabaseEntryTests()
    {
        Section("G1c bound database-entry dot measure");
        var fixture = HtmlDataFixture();
        var project = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "TARGET_A", Sequence = "AAAA", UserID = 7 },
            new() { SequenceIdentifier = "TARGET_B", Sequence = "AAAA", UserID = 7 },
            new() { SequenceIdentifier = "Reverse_TARGET", Sequence = "BBBB", UserID = 7 },
            new() { SequenceIdentifier = "contaminant_TARGET", Sequence = "CCCC", UserID = 7 },
        } };
        HtmlReportPayload Build(HtmlReportContext context, ReportSelection? selection = null, ReportDocument? document = null)
            => HtmlReportPayloadBuilder.Build(document ?? fixture.Document, Array.Empty<ReportSupplementSheet>(), selection, context);
        var baseline = Build(fixture.Context);
        var context = fixture.Context with { DatabaseEntryCounts = FamilyDatabaseEntryCounts.Read(project) };
        var payload = Build(context);
        var family = payload.Composition.Families[0];
        var measure = payload.DotMeasures.SingleOrDefault(m => m.Id == "databaseEntries");
        Check("databaseEntries is offered with native field references, log scale and bound ticks", measure != null
            && measure.Scale == "log" && payload.Fields[measure.Label].Text == "Database entries"
            && measure.Values.GetValueOrDefault(family.Id) == family.DatabaseEntries && measure.Ticks.Count > 0
            && measure.Ticks.All(id => payload.Fields[id].Source is { Kind: "axis-tick", Format: "axis-plain" }));
        Check("database entry count is not a peptide or greedy estimate and binds its stored curation", family.DatabaseEntries != null
            && payload.Fields[family.DatabaseEntries] is { Text: "2", Value: 2, Unit: "", Source: { Kind: "database-entry-count",
                Property: "/project/MyExtendedFastaItems", Study: "UserID", Family: "7", Scale: 1 } });
        Check("all pre-existing bound fields and their identifiers stay literal", baseline.Fields.All(pair =>
            payload.Fields.TryGetValue(pair.Key, out var after) && after == pair.Value));
        Check("canonical Others never receives a database-entry point", payload.Composition.Families[1].DatabaseEntries == null
            && measure != null && !measure.Values.ContainsKey(payload.Composition.Families[1].Id));
        var hidden = Build(context, new() { ExcludedIds = new[] { ReportSectionIds.Results } });
        Check("unchecked Results omit both the measure and its count bindings", hidden.DotMeasures.All(m => m.Id != "databaseEntries")
            && hidden.Fields.Values.All(f => f.Source.Kind != "database-entry-count"));
        var zeroProject = new FamilyClusterFile { MyExtendedFastaItems = new()
        { new() { SequenceIdentifier = "contaminant_ONLY", Sequence = "AAAA", UserID = 7 } } };
        var zero = Build(fixture.Context with { DatabaseEntryCounts = FamilyDatabaseEntryCounts.Read(zeroProject) });
        var zeroMeasure = zero.DotMeasures.SingleOrDefault(m => m.Id == "databaseEntries");
        Check("a real filtered zero is offered rather than converted to missing", zeroMeasure != null
            && zero.Fields[zeroMeasure.Values["7"]].Value == 0 && zero.Fields[zeroMeasure.Values["7"]].Text == "0");
        var panelDocument = fixture.Document with { Sections = fixture.Document.Sections.Select(s => s with
        { Blocks = s.Blocks.Select(b => b is ReportTable t && t.Id == "family-run-means" ? (ReportBlock)(t with { Id = "panel-family-run-means" }) : b).ToArray() }).ToArray() };
        var panelContext = fixture.Context with { UsesPanel = true, IsIdentity = false,
            PanelKeys = new Dictionary<string, string> { ["7"] = "main" },
            DatabaseEntryCounts = FamilyDatabaseEntryCounts.Read(DatabaseEntryFixture()) };
        var panel = Build(panelContext, document: panelDocument);
        var panelFamily = panel.Composition.Families[0];
        Check("collapsed study IDs resolve the canonical panel union rather than a same-numbered UserID", panelFamily.DatabaseEntries != null
            && panel.Fields[panelFamily.DatabaseEntries] is { Text: "4", Value: 4, Source: { Study: "panel-key", Family: "main" } });
        var unavailable = Build(panelContext with { DatabaseEntryCounts = FamilyDatabaseEntryCounts.Read(DatabaseEntryFixture(true)) }, document: panelDocument);
        Check("conflicted panel population is unavailable, never zero or a partial value", unavailable.Composition.Families[0].DatabaseEntries == null
            && unavailable.DotMeasures.All(m => m.Id != "databaseEntries"));
        Check("no curation count view means no invented measure", baseline.DotMeasures.All(m => m.Id != "databaseEntries")
            && baseline.Composition.Families.All(f => f.DatabaseEntries == null));
        var prior = CultureInfo.CurrentCulture; var priorUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            byte[] english = HtmlReportWriter.BuildBytes(Build(context));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            Check("new integer values and log ticks preserve complete HTML bytes across cultures", english.SequenceEqual(HtmlReportWriter.BuildBytes(Build(context))));
        }
        finally { CultureInfo.CurrentCulture = prior; CultureInfo.CurrentUICulture = priorUi; }
    }
}
