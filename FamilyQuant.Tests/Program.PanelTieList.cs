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
    private static IEnumerable<TestCommand> PanelTieListCommands() =>
    [
        new("--panel-tie-list", 74, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--panel-tie-list", (a, _) => PanelTieListCommand(a)),
        new("--test-panel-tie-list", 74, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--test-panel-tie-list", (_, _) =>
        {
            PanelTieListTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private sealed record PanelTieExample(string Accession, string Identifier, string Description, ProteinEvidence Evidence, string Source);

    private static int PanelTieAnnotationCount(PanelTieExample example)
    {
        var evidence = example.Evidence;
        static int Present(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : 1;
        return Present(example.Description) + ReferencePanelFetcher.FamiliesOf(evidence).Distinct(StringComparer.Ordinal).Count()
            + Present(evidence.Subfamily) + Present(evidence.FamilyAnnotation)
            + evidence.Keywords.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).Count()
            + evidence.TissueSpecificity.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).Count()
            + evidence.Pfam.Select(d => d.Accession).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).Count()
            + evidence.Panther.Select(d => d.Accession).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).Count()
            + (evidence.TransmembraneCount.HasValue ? 1 : 0) + (evidence.Fragment.HasValue ? 1 : 0);
    }

    private static int PanelTieListCommand(string[] args)
    {
        if (args.Length is not (3 or 4)) throw new ArgumentException("--panel-tie-list <project.fqproj> <new-output-directory> [scope]");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new output directory.");
        var file = ProjectStore.ReadFqproj(args[1]).fcf;
        var options = args.Length == 4 ? new EvidenceClassificationOptions { ScopeTerms = WordCluster.ParseKeywords(args[3]) }
            : file.EvidenceRecipe?.Options ?? throw new InvalidOperationException("The project has no saved grouping scope. Supply scope as the final argument.");
        var proposal = PanelEvidenceClassifier.Propose(file, options,
            args.Length == 3 ? file.EvidenceRecipe?.SessionSearch : null,
            args.Length == 3 ? file.EvidenceRecipe?.SessionReferences : null);
        var links = proposal.PanelContext!.Links;
        var pending = links.Where(link => link.NeedsConfirmation).OrderBy(link => link.Key, StringComparer.Ordinal).ToArray();
        var names = file.FamilyPanel!.Families.ToDictionary(f => f.Key, f => f.DisplayName.Length > 0 ? f.DisplayName : f.Key, StringComparer.Ordinal);
        var targets = file.MyExtendedFastaItems.Select((protein, index) => (protein, index))
            .Where(row => !SequenceLabels.IsDecoy(row.protein.SequenceIdentifier) && !SequenceLabels.IsContaminant(row.protein.SequenceIdentifier))
            .Select(row => (row.protein, row.index, Architecture: PanelEvidenceClassifier.ArchitectureKey(row.protein.Evidence),
                Families: row.protein.Evidence == null ? Array.Empty<string>() : ReferencePanelFetcher.FamiliesOf(row.protein.Evidence).ToArray())).ToArray();
        var localIdentifiers = targets.Select(row => row.protein.SequenceIdentifier).ToHashSet(StringComparer.Ordinal);
        var references = PanelEvidenceClassifier.References(file, options);
        var byLink = proposal.PanelAssignments!.SelectMany(fact => fact.LinkKeys.Select(key => (key, fact.ProteinIndex)))
            .ToLookup(row => row.key, row => row.ProteinIndex, StringComparer.Ordinal);
        var affected = new HashSet<int>(); int missingDescriptions = 0, zeroAffected = 0;
        static string Cell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        static string Domains(ProteinEvidence evidence) => string.Join("; ", JsonSerializer.Deserialize<string[]>(PanelEvidenceClassifier.ArchitectureKey(evidence))!);
        var csv = new StringBuilder("LinkKey,PanelFamilies,PanelFamilyKeys,SourceFamily,LinkPfamDomains,ProteinCount,ContextProteinCount,ExampleAccession,ExampleDescription,ExampleReviewed,UniProtFamily,PfamDomains,CoverageRulePick,ExampleSource\r\n");
        foreach (var link in pending)
        {
            var indices = byLink[link.Key].ToHashSet(); affected.UnionWith(indices);
            if (indices.Count == 0) zeroAffected++;
            var context = targets.Where(row => row.protein.Evidence != null && EvidenceClassifier.IsInScope(row.protein.Evidence, options.ScopeTerms)
                && (link.ArchitectureKey.Length == 0 || row.Architecture == link.ArchitectureKey)
                && (link.SourceFamily.Length == 0 || row.Families.Any(family => PanelEvidenceClassifier.LinkKey(family, link.ArchitectureKey) == link.Key))).ToArray();
            var contextIndices = context.Select(row => row.index).ToHashSet();
            var examples = targets.Where(row => indices.Contains(row.index) || contextIndices.Contains(row.index))
                .Select(row => new PanelTieExample(row.protein.Evidence?.Accession is { Length: > 0 } accession ? accession : NearestReviewed.EntryAccession(row.protein.SequenceIdentifier),
                    row.protein.SequenceIdentifier, row.protein.Description ?? "", row.protein.Evidence ?? new(), "Project protein"))
                .Concat(references.Where(reference => link.SupportingAccessions.Contains(reference.Accession, StringComparer.Ordinal)
                    && !localIdentifiers.Contains(reference.Identifier)).Select(reference => new PanelTieExample(reference.Accession,
                        reference.Identifier, "", reference.Evidence!, "Stored reviewed reference")))
                .OrderByDescending(example => example.Evidence.Reviewed == true).ThenByDescending(PanelTieAnnotationCount)
                .ThenBy(example => example.Accession, StringComparer.Ordinal).ThenBy(example => example.Identifier, StringComparer.Ordinal)
                .ThenBy(example => example.Description, StringComparer.Ordinal).ToArray();
            var example = examples.FirstOrDefault() ?? throw new InvalidOperationException("A panel link has no current supporting example.");
            if (example.Description.Length == 0) missingDescriptions++;
            string architecture = link.ArchitectureKey.Length == 0 ? "Any observed combination"
                : string.Join("; ", JsonSerializer.Deserialize<string[]>(link.ArchitectureKey)!);
            string[] cells = { link.Key, string.Join("; ", link.CandidateFamilyKeys.Select(key => names[key])),
                string.Join("; ", link.CandidateFamilyKeys), link.SourceFamily, architecture,
                indices.Count.ToString(CultureInfo.InvariantCulture), context.Length.ToString(CultureInfo.InvariantCulture),
                example.Accession, example.Description, example.Evidence.Reviewed == true ? "Yes" : example.Evidence.Reviewed == false ? "No" : "Unknown",
                string.Join("; ", ReferencePanelFetcher.FamiliesOf(example.Evidence).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                Domains(example.Evidence), link.SelectedFamilyKey == null ? "" : names[link.SelectedFamilyKey], example.Source };
            csv.Append(string.Join(",", cells.Select(Cell))).Append("\r\n");
        }
        var resolved = links.Where(link => link.IsPfamCoverageResolved).OrderBy(link => link.Key, StringComparer.Ordinal).Select(link => new
        {
            LinkKey = link.Key, link.SourceFamily, link.ArchitectureKey,
            CoverageRulePick = names[link.SelectedFamilyKey!], PanelFamilyKey = link.SelectedFamilyKey,
        }).ToArray();
        var summary = new
        {
            ChoicesStillAsked = pending.Length, UniqueAffectedProteins = affected.Count, ChoicesWithNoAffectedProteins = zeroAffected,
            CoverageResolvedChoices = resolved.Length, ConfirmedChoices = links.Count(link => link.IsConfirmed),
            TotalPanelLinks = links.Count, ExamplesWithoutStoredDescription = missingDescriptions,
            options.ScopeTerms, proposal.SourceFingerprint,
            ScopeSource = args.Length == 4 ? "Explicit scope with default classification settings" : "Saved grouping settings and session evidence",
            ProteinCountMeaning = "Distinct project targets whose fresh classifier LinkKeys include this choice. Counts can overlap; UniqueAffectedProteins is their union.",
            ContextProteinCountMeaning = "In-scope project targets sharing the source-family/domain context, including those using a more specific link.",
            ExampleOrder = "Reviewed first, then most nonempty description/family/annotation fields and distinct keyword/tissue/Pfam/Panther items plus observed feature flags, then ordinal accession, identifier and description. Stored reference descriptions are unavailable and stay blank.",
            CoverageRulePickMeaning = "Remaining choices have no unique pick, so their CSV cell is blank. CoverageResolvedLinks records actual unique RG2 picks; saved choices take precedence.",
            CoverageResolvedLinks = resolved,
        };
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "ties.csv"), csv.ToString(), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine("Panel links still asking for a choice: " + pending.Length.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Distinct affected proteins: " + affected.Count.ToString(CultureInfo.InvariantCulture));
        return 0;
    }

    private static void PanelTieListTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "panel-tie-list", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        static FamilyPanelFamily Row(string key, string name, params string[] domains) => new()
        { Key = key, DisplayName = name, Pfam = domains.Select(d => new FamilyPanelDomain { Accession = d }).ToList() };
        static FastaItemExtended Protein(string accession, bool reviewed, string domain, string family) => new()
        {
            SequenceIdentifier = "sp|" + accession + "|FIXTURE", Description = "Fixture, \"" + accession + "\"", Sequence = "ACDEFGHIKLMNPQRSTVWY", UserID = 1, USearchID = 1,
            Evidence = new() { Accession = accession, Reviewed = reviewed, Family = family, Keywords = new() { "selected" }, Pfam = new() { new() { Accession = domain } } },
        };
        var file = new FamilyClusterFile
        {
            FamilyPanel = new() { Families = new()
            {
                Row("equal-a", "User, \"one\"", "domain-z"), Row("equal-b", "User two", "domain-z"),
                Row("broad", "Broad row", "domain-a", "domain-b"), Row("complete", "Complete row", "domain-a"),
            } },
            MyExtendedFastaItems = new() { Protein("P61001", false, "domain-z", "Source equal"), Protein("P61002", true, "domain-z", "Source equal"),
                Protein("P61004", true, "domain-z", "Source equal"), Protein("P61003", true, "domain-z", "Source equal"),
                Protein("P61005", true, "domain-a", "Source resolved") },
            EvidenceRecipe = new() { Options = new() { ScopeTerms = new[] { "selected" } } },
        };
        file.MyExtendedFastaItems[0].Evidence!.Keywords.AddRange(new[] { "extra one", "extra two", "extra three" });
        file.MyExtendedFastaItems[2].Evidence!.FamilyAnnotation = "Additional supplied annotation";
        file.MyExtendedFastaItems[3].Evidence!.FamilyAnnotation = "Additional supplied annotation";
        string project = Path.Combine(root, "synthetic.fqproj"); ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        string hash = TestFileHash.Upper(project), before = file.ToJsonText();
        string first = Path.Combine(root, "first"), second = Path.Combine(root, "second");
        PanelTieListCommand(new[] { "--panel-tie-list", project, first });
        var records = File.ReadAllLines(Path.Combine(first, "ties.csv")).Skip(1).Select(ParseCsvLine).ToArray();
        var proposal = PanelEvidenceClassifier.Propose(file, file.EvidenceRecipe.Options);
        Check("AP1-tool exports every remaining screen choice once and no resolved links", records.Length == 3
            && records.Select(row => row[0]).SequenceEqual(proposal.PanelContext!.Links.Where(link => link.NeedsConfirmation).OrderBy(link => link.Key, StringComparer.Ordinal).Select(link => link.Key)));
        Check("AP1-tool ranks reviewed then annotation count then ordinal accession", records.All(row => row[7] == "P61003" && row[8] == "Fixture, \"P61003\"" && row[9] == "Yes"));
        Check("AP1-tool CSV retains user names, source family and Pfam with correct quoting", records.All(row => row[1] == "User, \"one\"; User two"
            && row[10] == "Source equal" && row[11] == "domain-z" && row[12] == ""));
        using (var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(first, "summary.json"))))
        {
            var json = summary.RootElement;
            Check("AP1-tool counts affected proteins without adding overlapping choice contexts", json.GetProperty("ChoicesStillAsked").GetInt32() == 3
                && json.GetProperty("UniqueAffectedProteins").GetInt32() == 4 && json.GetProperty("ChoicesWithNoAffectedProteins").GetInt32() == 2
                && records.Select(row => row[5]).Order(StringComparer.Ordinal).SequenceEqual(new[] { "0", "0", "4" }) && records.All(row => row[6] == "4"));
            Check("AP1-tool reports actual RG2 picks separately from unresolved ties", json.GetProperty("CoverageResolvedChoices").GetInt32() == 3
                && json.GetProperty("CoverageResolvedLinks").EnumerateArray().All(row => row.GetProperty("CoverageRulePick").GetString() == "Complete row"));
        }
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); PanelTieListCommand(new[] { "--panel-tie-list", project, second }); }
        finally { CultureInfo.CurrentCulture = culture; }
        Check("AP1-tool output repeats byte for byte across cultures", new[] { "ties.csv", "summary.json" }.All(name => File.ReadAllBytes(Path.Combine(first, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(second, name)))));
        bool refused = false;
        try { PanelTieListCommand(new[] { "--panel-tie-list", project, first }); } catch (IOException) { refused = true; }
        Check("AP1-tool refuses an existing output directory and never changes its project", refused && TestFileHash.Upper(project) == hash && file.ToJsonText() == before);
        string architecture = PanelEvidenceClassifier.ArchitectureKey(file.MyExtendedFastaItems[1].Evidence);
        var pending = proposal.PanelContext!.Links.Single(link => link.SourceFamily == "Source equal" && link.ArchitectureKey == architecture);
        file.FamilyPanel = PanelEvidenceClassifier.ConfirmLinks(file.FamilyPanel, proposal.PanelContext.Links,
            new[] { new PanelFamilyLinkSelection { LinkKey = pending.Key, PanelFamilyKey = "equal-b", Source = "Synthetic saved choice" } });
        string confirmed = Path.Combine(root, "confirmed.fqproj"); ProjectStore.WriteFqproj(confirmed, file, Array.Empty<QuantRow>(), new QuantManifest());
        string confirmedOutput = Path.Combine(root, "confirmed"); PanelTieListCommand(new[] { "--panel-tie-list", confirmed, confirmedOutput });
        Check("AP1-tool respects a saved choice and excludes it from remaining questions", File.ReadAllLines(Path.Combine(confirmedOutput, "ties.csv")).Skip(1).Select(ParseCsvLine).All(row => row[0] != pending.Key)
            && File.ReadAllLines(Path.Combine(confirmedOutput, "ties.csv")).Length == 3);
        string explicitScope = Path.Combine(root, "explicit"); PanelTieListCommand(new[] { "--panel-tie-list", project, explicitScope, "absent" });
        Check("AP1-tool explicit scope uses fresh scope evidence", File.ReadAllLines(Path.Combine(explicitScope, "ties.csv")).Length == 1 && TestFileHash.Upper(project) == hash);
    }
}
