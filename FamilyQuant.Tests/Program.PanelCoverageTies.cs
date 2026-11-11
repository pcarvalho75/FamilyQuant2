using FamilyQuant.Evidence;
using FamilyQuant.Panels;
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
    private static IEnumerable<TestCommand> PanelCoverageTieCommands() =>
    [
        new("--test-panel-coverage-ties", 74, TestCommandPhase.Raw, a => a.Contains("--test-panel-coverage-ties", StringComparer.Ordinal), (_, _) =>
        {
            PanelCoverageTieTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "panel-coverage-ties"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--panel-tie-counts", 74, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--panel-tie-counts", (a, _) => PanelTieCountsCommand(a)),
    ];

    private static int PanelTieCountsCommand(string[] args)
    {
        if (args.Length is not (3 or 4)) throw new ArgumentException("--panel-tie-counts <project.fqproj> <new-output-directory> [scope]");
        if (Directory.Exists(args[2])) throw new IOException("Choose a new output directory.");
        var file = ProjectStore.ReadFqproj(args[1]).fcf;
        var options = args.Length == 4 ? new EvidenceClassificationOptions { ScopeTerms = WordCluster.ParseKeywords(args[3]) }
            : file.EvidenceRecipe?.Options ?? throw new InvalidOperationException("The project has no saved grouping scope. Supply scope as the final argument.");
        var counts = PanelEvidenceClassifier.CountPfamCoverageTies(file, options,
            args.Length == 3 ? file.EvidenceRecipe?.SessionSearch : null,
            args.Length == 3 ? file.EvidenceRecipe?.SessionReferences : null);
        Directory.CreateDirectory(args[2]);
        static string Cell(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
        var csv = new StringBuilder("Family,Before,After\r\n");
        foreach (var row in counts.Families)
            csv.Append(Cell(row.Family)).Append(',').Append(row.Before.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(row.After.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        File.WriteAllText(Path.Combine(args[2], "family-counts.csv"), csv.ToString(), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(args[2], "tie-counts.json"), JsonSerializer.Serialize(counts, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Ties asked before RG2: " + counts.TiesBefore.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Ties asked after RG2: " + counts.TiesAfter.ToString(CultureInfo.InvariantCulture));
        Console.Write(csv);
        return 0;
    }

    private static void PanelCoverageTieTests(string output)
    {
        Directory.CreateDirectory(output);
        static FamilyPanelFamily Row(string key, params string[] domains) => new()
        {
            Key = key, DisplayName = "User row " + key,
            Pfam = domains.Select(d => new FamilyPanelDomain { Accession = d }).ToList(),
        };
        static FastaItemExtended Protein(string accession, string source, bool reviewed, params string[] domains) => new()
        {
            SequenceIdentifier = "sp|" + accession + "|SYNTHETIC", Sequence = "ACDEFGHIKLMNPQRSTVWY", UserID = 1, USearchID = 1,
            Evidence = new() { Accession = accession, Family = source, Reviewed = reviewed, Keywords = new() { "selected" },
                Pfam = domains.Select(d => new EvidenceDomain { Accession = d }).ToList() },
        };
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
        var file = new FamilyClusterFile
        {
            FamilyPanel = new() { Families = new()
            {
                Row("broad", "domain-a", "domain-b", "domain-c"), Row("complete", "domain-a", "domain-a"),
                Row("equal-one", "domain-z"), Row("equal-two", "domain-z"), Row("empty"),
            } },
            MyExtendedFastaItems = new()
            {
                Protein("P51001", "Source one", true, "domain-a"), Protein("P51002", "Source equal", true, "domain-z"),
                Protein("P51003", "Source full", true, "domain-a", "domain-b", "domain-c"),
                Protein("P51004", "Source one", false, "domain-a"),
            },
            EvidenceRecipe = new() { Options = options },
        };
        string unchanged = file.ToJsonText();
        var proposal = PanelEvidenceClassifier.Propose(file, options);
        Check("RG2 complete Pfam list wins a matched-domain tie", proposal.Proteins[0].Family == "complete" && proposal.Proteins[3].Family == "complete");
        Check("RG2 equal coverage still asks and remains unassigned", proposal.PanelContext!.Links.Count(l => l.NeedsConfirmation) == 3
            && proposal.Proteins[1].Family == "");
        Check("RG2 unique best match and empty rows keep their behaviour", proposal.Proteins[2].Family == "broad"
            && proposal.PanelContext.Links.All(l => !l.CandidateFamilyKeys.Contains("empty")));
        Check("RG2 duplicate row domains do not inflate the denominator", proposal.PanelContext.Links.Where(l => l.SourceFamily == "Source one")
            .All(l => l.SelectedFamilyKey == "complete" && !l.IsConfirmed && !l.NeedsConfirmation));
        var counts = PanelEvidenceClassifier.CountPfamCoverageTies(file, options);
        Check("RG2 aggregate reports old and new questions and every user row", counts.TiesBefore == 6 && counts.TiesAfter == 3
            && counts.Families.Count == 5 && counts.Families.Single(f => f.Family == "User row complete") is { Before: 0, After: 2 }
            && counts.Families.Single(f => f.Family == "User row broad") is { Before: 1, After: 1 });
        Check("RG2 proposing and comparing leave stored numbers and data untouched", file.ToJsonText() == unchanged);

        string architecture = PanelEvidenceClassifier.ArchitectureKey(file.MyExtendedFastaItems[0].Evidence);
        var prior = new PanelFamilyLinkProposal { Key = PanelEvidenceClassifier.LinkKey("Source one", architecture),
            SourceFamily = "Source one", ArchitectureKey = architecture, CandidateFamilyKeys = new[] { "broad", "complete" } };
        var chosen = PanelEvidenceClassifier.ConfirmLinks(file.FamilyPanel, new[] { prior },
            new[] { new PanelFamilyLinkSelection { LinkKey = prior.Key, PanelFamilyKey = "broad", Source = "User choice" } });
        Check("RG2 a pending choice from the previous proposal wins", PanelEvidenceClassifier.Propose(file, options, panel: chosen).Proteins[0].Family == "broad");
        var stored = FamilyPanelValidation.Clone(chosen);
        Check("RG2 a stored choice retains its family and note", PanelEvidenceClassifier.Propose(file, options, panel: stored).PanelContext!.Links
            .Single(l => l.Key == prior.Key) is { IsConfirmed: true, SelectedFamilyKey: "broad", SourceNote: "User choice" });
        stored.EvidenceLinks[0].PanelFamilyKey = null;
        Check("RG2 an explicit leave-out choice wins", PanelEvidenceClassifier.Propose(file, options, panel: stored).Proteins[0].Family == "");
        stored.EvidenceLinks[0].PanelFamilyKey = "broad"; stored.EvidenceLinks[0].ArchitectureKey = "";
        Check("RG2 a stored source-family choice wins over automatic architecture", PanelEvidenceClassifier.Propose(file, options, panel: stored).Proteins[0].Family == "broad");
        var unique = FamilyPanelValidation.Clone(stored);
        unique.EvidenceLinks[0].SourceFamily = "Source full"; unique.EvidenceLinks[0].PanelFamilyKey = "complete";
        Check("RG2 a source-family choice does not replace a previously unique architecture",
            PanelEvidenceClassifier.Propose(file, options, panel: unique).Proteins[2].Family == "broad");

        var project = Path.Combine(output, "synthetic.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        var hash = TestFileHash.Upper(project);
        string report = Path.Combine(output, "counts-" + Guid.NewGuid().ToString("N"));
        var console = Console.Out; var captured = new StringWriter(CultureInfo.InvariantCulture);
        try { Console.SetOut(captured); PanelTieCountsCommand(new[] { "--panel-tie-counts", project, report }); }
        finally { Console.SetOut(console); }
        var read = JsonSerializer.Deserialize<PanelTieCounts>(File.ReadAllText(Path.Combine(report, "tie-counts.json")))!;
        string all = captured + File.ReadAllText(Path.Combine(report, "family-counts.csv")) + File.ReadAllText(Path.Combine(report, "tie-counts.json"));
        Check("RG2 project command matches synthetic counts without accessions", read.TiesBefore == 6 && read.TiesAfter == 3
            && file.MyExtendedFastaItems.All(p => !all.Contains(p.Evidence!.Accession, StringComparison.Ordinal)));
        Check("RG2 count command leaves its project bytes unchanged", TestFileHash.Upper(project) == hash);
    }
}
