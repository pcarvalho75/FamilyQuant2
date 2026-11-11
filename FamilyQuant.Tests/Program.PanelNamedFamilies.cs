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
    private static IEnumerable<TestCommand> PanelNamedFamilyCommands() =>
    [
        new("--test-panel-named-families", 74, TestCommandPhase.Raw, a => a.Contains("--test-panel-named-families", StringComparer.Ordinal), (_, _) =>
        {
            PanelNamedFamilyTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "panel-named-families"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--panel-family-counts", 74, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--panel-family-counts", (a, _) => PanelFamilyCountsCommand(a)),
    ];

    private static int PanelFamilyCountsCommand(string[] args)
    {
        if (args.Length is not (3 or 4)) throw new ArgumentException("--panel-family-counts <project.fqproj> <new-output-directory> [scope]");
        if (Directory.Exists(args[2])) throw new IOException("Choose a new output directory.");
        var file = ProjectStore.ReadFqproj(args[1]).fcf;
        var options = args.Length == 4 ? new EvidenceClassificationOptions { ScopeTerms = WordCluster.ParseKeywords(args[3]) }
            : file.EvidenceRecipe?.Options ?? throw new InvalidOperationException("The project has no saved grouping scope. Supply scope as the final argument.");
        var counts = PanelEvidenceClassifier.CountNamedFamilyRules(file, options,
            args.Length == 3 ? file.EvidenceRecipe?.SessionSearch : null,
            args.Length == 3 ? file.EvidenceRecipe?.SessionReferences : null);
        var csv = new StringBuilder("Family,Before RG5,After RG5\r\n");
        foreach (var row in counts.Families)
            csv.Append('"').Append(row.Family.Replace("\"", "\"\"")).Append("\",")
                .Append(row.Before.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.After.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        Directory.CreateDirectory(args[2]);
        File.WriteAllText(Path.Combine(args[2], "family-counts.csv"), csv.ToString(), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(args[2], "family-counts.json"), JsonSerializer.Serialize(counts, new JsonSerializerOptions { WriteIndented = true }));
        Console.Write(csv);
        return 0;
    }

    private static void PanelNamedFamilyTests(string output)
    {
        Directory.CreateDirectory(output);
        static FastaItemExtended Protein(string id, string source, bool words, bool reviewed = false, bool domain = true) => new()
        {
            SequenceIdentifier = id, Sequence = "ACDEFGHIKLMNPQRSTVWY", UserID = 1, USearchID = 1,
            Evidence = new() { Accession = NearestReviewed.EntryAccession(id), Family = source, Reviewed = reviewed,
                Keywords = words ? new() { "selected" } : new(),
                Pfam = domain ? new() { new() { Accession = "PF90201" } } : new() },
        };
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
        var panel = new FamilyPanel { Families = new()
        {
            new() { Key = "G", DisplayName = "User general row", Pfam = new() { new() { Accession = "PF90201" } } },
            new() { Key = "N", DisplayName = "User named row", Pfam = new() { new() { Accession = "PF90201" } },
                UniProtFamilies = new() { "Named source family" } },
        } };
        var file = new FamilyClusterFile { FamilyPanel = panel, EvidenceRecipe = new() { Options = options }, MyExtendedFastaItems = new()
        {
            Protein("sp|P62001|SYNTHETIC", "Other source family", true, true),
            Protein("tr|P62002|SYNTHETIC", "Named source family", false),
            Protein("tr|P62003|SYNTHETIC", "named SOURCE family", false, domain: false),
            Protein("tr|P62004|SYNTHETIC", "Named source family extension", false),
            Protein("tr|P62005|SYNTHETIC", "", false),
            Protein("contaminant_sp|P62006|SYNTHETIC", "Named source family", false),
            Protein("Reverse_sp|P62007|SYNTHETIC", "Named source family", false),
        } };
        var current = PanelEvidenceClassifier.Propose(file, options);
        var choices = current.PanelContext!.Links.Where(l => l.SourceFamily == "Other source family")
            .Select(l => new PanelFamilyLinkSelection { LinkKey = l.Key, PanelFamilyKey = "G" }).ToArray();
        file.FamilyPanel = PanelEvidenceClassifier.ConfirmLinks(panel, current.PanelContext.Links, choices);
        string unchanged = file.ToJsonText();
        var proposal = PanelEvidenceClassifier.Propose(file, options);
        Check("RG5 shared-Pfam generic row keeps the other in-scope family", proposal.Proteins.Single(p => p.ProteinIndex == 0).Family == "G");
        Check("RG5 exact row family brings unreviewed proteins in without scope words", proposal.Proteins.Single(p => p.ProteinIndex == 1)
            is { Family: "N", HasScopeAnnotation: false, SuggestedSelected: true });
        Check("RG5 explicit row family also works when domain evidence is absent", proposal.Proteins.Single(p => p.ProteinIndex == 2)
            is { Family: "N", HasScopeAnnotation: false });
        Check("RG5 a similar family name and a shared domain do not expand scope", proposal.Proteins.Single(p => p.ProteinIndex == 3).Family == ""
            && proposal.Proteins.Single(p => p.ProteinIndex == 4).Family == "");
        Check("RG5 contaminants and decoys never enter through a named row", proposal.Proteins.Single(p => p.ProteinIndex == 5).Family == ""
            && proposal.Proteins.All(p => p.ProteinIndex != 6));
        Check("RG5 named and residual rows leave no domain-only tie question", proposal.PanelContext!.Links.All(l => !l.NeedsConfirmation));
        Check("RG5 named rows do not expand the reviewed-neighbour scope", PanelEvidenceClassifier.References(file, options).Count == 1);
        var counts = PanelEvidenceClassifier.CountNamedFamilyRules(file, options);
        Check("RG5 counts compare the RG4 baseline with exact row-specific scope", counts.Families.Single(r => r.Family == "User general row")
            is { Before: 1, After: 1 } && counts.Families.Single(r => r.Family == "User named row") is { Before: 0, After: 2 });
        Check("RG5 comparison leaves the complete project unchanged", file.ToJsonText() == unchanged);

        var transfer = new FamilyClusterFile { FamilyPanel = FamilyPanelValidation.Clone(panel), MyExtendedFastaItems = new()
        {
            Protein("sp|P62101|SYNTHETIC", "Named source family", true, true),
            Protein("tr|P62102|SYNTHETIC", "", false),
        } };
        var references = PanelEvidenceClassifier.References(transfer, options);
        var search = NearestReviewed.Search(NearestReviewed.Targets(transfer), references);
        var searched = PanelEvidenceClassifier.Propose(transfer, options, search, references);
        Check("RG5 a qualifying native neighbour cannot give an unrelated query named membership",
            searched.Proteins.Single(p => p.ProteinIndex == 1).Family == ""
            && searched.Proteins.Single(p => p.ProteinIndex == 1).Flags.Any(f => f.Code == "panel_source_family_required"));
        var old = new PanelFamilyLinkProposal { SourceFamily = "Named source family",
            ArchitectureKey = PanelEvidenceClassifier.ArchitectureKey(transfer.MyExtendedFastaItems[0].Evidence),
            Key = PanelEvidenceClassifier.LinkKey("Named source family", PanelEvidenceClassifier.ArchitectureKey(transfer.MyExtendedFastaItems[0].Evidence)),
            CandidateFamilyKeys = new[] { "G", "N" } };
        var manual = PanelEvidenceClassifier.ConfirmLinks(panel, new[] { old },
            new[] { new PanelFamilyLinkSelection { LinkKey = old.Key, PanelFamilyKey = "G" } });
        Check("RG5 a current saved source choice still wins over the row rule", PanelEvidenceClassifier.Propose(transfer, options, panel: manual)
            .Proteins.Single(p => p.ProteinIndex == 0).Family == "G");
        manual.EvidenceLinks[0].ArchitectureKey = "";
        Check("RG5 a source-wide saved choice wins over an automatic named architecture", PanelEvidenceClassifier.Propose(transfer, options, panel: manual)
            .Proteins.Single(p => p.ProteinIndex == 0).Family == "G");

        var scoredPanel = FamilyPanelValidation.Clone(panel);
        scoredPanel.Families.Single(f => f.Key == "N").Pfam.Add(new() { Accession = "PF90202" });
        var scored = new FamilyClusterFile { FamilyPanel = scoredPanel, MyExtendedFastaItems = new()
            { Protein("sp|P62201|SYNTHETIC", "Other source family", true, true) } };
        scored.MyExtendedFastaItems[0].Evidence!.Pfam.Add(new() { Accession = "PF90202" });
        var restricted = PanelEvidenceClassifier.Propose(scored, options);
        Check("RG5 extra shared-domain support cannot put another source family in the named row", restricted.Proteins.Single().Family == "G");
        var genericLink = restricted.PanelContext!.Links.Single(l => l.SourceFamily.Length == 0);
        var genericChoice = PanelEvidenceClassifier.ConfirmLinks(scoredPanel, restricted.PanelContext.Links,
            new[] { new PanelFamilyLinkSelection { LinkKey = genericLink.Key, PanelFamilyKey = "G" } });
        Check("RG5 generic-domain choices remain current after named rows are excluded", PanelEvidenceClassifier.Propose(scored, options, panel: genericChoice)
            .PanelContext!.Links.Single(l => l.Key == genericLink.Key).IsConfirmed);

        string project = Path.Combine(output, "synthetic.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        string hash = TestFileHash.Upper(project), report = Path.Combine(output, "counts-" + Guid.NewGuid().ToString("N"));
        var original = Console.Out; var captured = new StringWriter(CultureInfo.InvariantCulture);
        try { Console.SetOut(captured); PanelFamilyCountsCommand(new[] { "--panel-family-counts", project, report }); }
        finally { Console.SetOut(original); }
        var read = JsonSerializer.Deserialize<PanelTieCounts>(File.ReadAllText(Path.Combine(report, "family-counts.json")))!;
        string all = captured + File.ReadAllText(Path.Combine(report, "family-counts.csv")) + File.ReadAllText(Path.Combine(report, "family-counts.json"));
        Check("RG5 project command reports only user row names and counts without accessions", read.Families.Count == 2
            && read.Families.Single(r => r.Family == "User named row") is { Before: 0, After: 2 }
            && file.MyExtendedFastaItems.All(p => p.Evidence!.Accession.Length == 0 || !all.Contains(p.Evidence.Accession, StringComparison.Ordinal)));
        Check("RG5 command preserves the input project's bytes", TestFileHash.Upper(project) == hash);
    }
}
