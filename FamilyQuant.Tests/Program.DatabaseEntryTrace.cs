using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DatabaseEntryTraceCommands() =>
    [new("--test-database-entry-trace", 10512, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-database-entry-trace", (args, _) =>
        {
            DatabaseEntryTraceTests(args.Length > 1 ? args[1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void DatabaseEntryTraceTests(string? output = null)
    {
        Section("G1d database-entry stored-source trace and preservation contract");
        string root = output ?? Path.Combine(Path.GetTempPath(), "FamilyQuant.DatabaseEntryTrace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string word = Path.Combine(root, "report.docx"), book = Path.Combine(root, "report_supplement.xlsx"), trace = Path.Combine(root, "trace.csv");
        var document = new ReportDocument { Title = "Family", Sections = Array.Empty<ReportSection>() };
        WordReportWriter.Write(word, document);
        ReportSupplementWorkbook.Write(book, document, Array.Empty<ReportSupplementSheet>());
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string wordHash = Hash(word), bookHash = Hash(book);
        var project = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "TARGET_A", Sequence = "AAAA", UserID = 7 },
            new() { SequenceIdentifier = "TARGET_B", Sequence = "AAAA", UserID = 7 },
            new() { SequenceIdentifier = "Reverse_TARGET", Sequence = "BBBB", UserID = 7 },
            new() { SequenceIdentifier = "contaminant_TARGET", Sequence = "CCCC", UserID = 7 },
            new() { SequenceIdentifier = "REAL_ZERO_FAMILY", Sequence = "DDDD", UserID = 0 },
            new() { SequenceIdentifier = "contaminant_ONLY", Sequence = "EEEE", UserID = 44 },
        } };
        project.MyExtendedFastaItems.Add(JsonSerializer.Deserialize<FastaItemExtended>("{\"SequenceIdentifier\":\"UNASSIGNED\",\"Sequence\":\"FFFF\"}")!);
        string projectBefore = project.ToJsonText();
        var source = new HtmlReportReference { Kind = "database-entry-count", Property = "/project/MyExtendedFastaItems", Study = "UserID", Family = "7" };
        var fields = new Dictionary<string, HtmlReportField>
        {
            ["name"] = new("Family", null, "", new() { Kind = "word-property", Property = "Title" }),
            ["count"] = new("2", 2, "", source),
        };
        foreach (var pair in new[] { (Id: "label", Source: new HtmlReportReference { Kind = "axis-label", Property = "databaseEntries" }),
            (Id: "tick", Source: new HtmlReportReference { Kind = "axis-tick", Property = "databaseEntries/log10/0", Format = "axis-plain" }) })
        {
            if (!HtmlReportAxisTicks.TryResolve(pair.Source, out var field)) throw new InvalidDataException("Fixture axis source invalid.");
            fields.Add(pair.Id, field);
        }
        HtmlReportPayload Payload(string family = "7", bool others = false) => new()
        {
            Fields = fields,
            Composition = new() { Families = new[] { new HtmlReportFamily { Id = family, Name = "name", DatabaseEntries = "count", IsOthers = others } } },
            DotMeasures = new[] { new HtmlDotMeasure("databaseEntries", "label", "", "log", new[] { "tick" }, new Dictionary<string, string> { [family] = "count" }) },
        };
        int Run(HtmlReportPayload? payload = null, FamilyClusterFile? input = null, QuantManifest? manifest = null)
            => ReportNumberTrace.RunHtmlBindings(payload ?? Payload(), word, book, input ?? project, manifest, trace);
        bool positive = Run() == 0;
        Check("database count binds target entries from its exact stored UserID", positive && !File.ReadAllText(trace).Contains("NOT FOUND", StringComparison.Ordinal));
        var original = fields["count"];
        void Reject(string name, HtmlReportField mutation)
        { fields["count"] = mutation; Check(name, Run() == 1); fields["count"] = original; }
        Reject("count trace rejects a changed printed integer", original with { Text = "3" });
        Reject("count trace rejects changed geometry", original with { Value = 3 });
        Reject("count trace rejects units for a dimensionless count", original with { Unit = "%" });
        Reject("count trace rejects an unrequested scale", original with { Source = source with { Scale = 100 } });
        Reject("count trace rejects a locator from another source kind", original with { Source = source with { Row = 1 } });
        Reject("count trace rejects a replacement record collection", original with { Source = source with { Property = "/project/SourceSequenceCount" } });
        Reject("count trace rejects an unknown basis", original with { Source = source with { Study = "other" } });
        Reject("count trace rejects rounded count formatting", original with { Source = source with { Format = "G3" } });
        Reject("count trace rejects noncanonical family identifiers", original with { Source = source with { Family = "007" } });
        Reject("a valid number from another family cannot be reassigned to this point", original with { Text = "1", Value = 1, Source = source with { Family = "0" } });
        Check("Others is not allowed a database population point", Run(Payload(others: true)) == 1);
        var dangling = Payload() with { Composition = new() { Families = new[] { Payload().Composition.Families[0] with { DatabaseEntries = "missing" } } } };
        Check("the new family field reference is checked", Run(dangling) == 1);
        var wrongMap = Payload() with { DotMeasures = new[] { new HtmlDotMeasure("databaseEntries", "label", "", "log", new[] { "tick" }, new Dictionary<string, string> { ["7"] = "name" }) } };
        Check("dot values must reference that family's actual database count", Run(wrongMap) == 1);
        fields["count"] = original with { Text = "1", Value = 1, Source = source with { Family = "0" } };
        Check("zero UserID resolves its own recorded entry rather than unassigned records", Run(Payload("0")) == 0);
        fields["count"] = original with { Text = "0", Value = 0, Source = source with { Family = "44" } };
        Check("a filtered zero remains a bound available count", Run(Payload("44")) == 0);
        fields["count"] = original;
        var panelFixture = EffectivePanelFixture();
        var outsideAssignment = panelFixture.File.PanelProteinAssignments!.Single(row => row.FamilyUserId == 33);
        outsideAssignment.IsApplied = false; outsideAssignment.PanelFamilyKey = null;
        panelFixture = PrepareEffectivePanelFixture(panelFixture.File, panelFixture.Rows);
        EffectivePanelFamilyStudy.Store(panelFixture.Manifest, panelFixture.Assigned, panelFixture.Hits, panelFixture.File);
        var panelBasis = ReportCompositionBasis.Select(panelFixture.File, FamilyUnobservedStudies.View(panelFixture.File, panelFixture.Manifest));
        string mainId = panelBasis.Map!.UserFamilies.Single(pair => pair.Value.PanelFamilyKey == "main").Key.ToString(CultureInfo.InvariantCulture);
        string outsideId = panelBasis.SourceUserIds.Single(pair => pair.Value.SequenceEqual(new[] { 33 })).Key.ToString(CultureInfo.InvariantCulture);
        fields["count"] = original with { Source = source with { Study = "panel-key", Family = "main" } };
        Check("collapsed panel counts bind the canonical effective key", Run(Payload(mainId), panelFixture.File, panelFixture.Manifest) == 0);
        fields["count"] = original with { Text = "1", Value = 1, Source = source with { Family = "33" } };
        Check("a collapsed outside row binds its single original UserID", Run(Payload(outsideId), panelFixture.File, panelFixture.Manifest) == 0);
        fields["count"] = original with { Text = "1", Value = 1, Source = source with { Family = outsideId } };
        Check("an effective slot cannot be used as its source UserID", Run(Payload(outsideId), panelFixture.File, panelFixture.Manifest) == 1);
        fields["count"] = original with { Text = "0", Value = 0, Source = source with { Study = "panel-key", Family = "other" } };
        Check("a valid other panel population cannot be reassigned to a headline", Run(Payload(mainId), panelFixture.File, panelFixture.Manifest) == 1);
        fields["count"] = original with { Text = "0", Value = 0, Source = source with { Study = "panel-key", Family = "main" } };
        Check("a conflicted panel population cannot be reported as zero", Run(Payload("11"), DatabaseEntryFixture(true), new QuantManifest()) == 1);
        fields["count"] = original;
        Check("database counts require their project rather than searching documents for an equal integer",
            ReportNumberTrace.RunHtmlBindings(Payload(), word, book, null, null, trace) == 1);
        string repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "FamilyQuant2.sln"))) repo = Directory.GetParent(repo)?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
        string[] ledgerRows = File.ReadAllLines(Path.Combine(repo, "docs", "FEATURE-LEDGER.md")).Where(line => line.StartsWith("| FQ2-DATABASE-ENTRIES |", StringComparison.Ordinal)).ToArray();
        Check("the added database population function has one guarded ledger contract", ledgerRows.Length == 1
            && ledgerRows[0].Contains("test:DatabaseEntryTraceTests", StringComparison.Ordinal));
        Check("trace reads preserve curation and native Word/workbook bytes", projectBefore == project.ToJsonText() && wordHash == Hash(word) && bookHash == Hash(book));
    }
}
