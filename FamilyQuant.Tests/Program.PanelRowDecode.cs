using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelRowDecodeCommands() =>
    [
        new("--test-panel-row-decode", 74, TestCommandPhase.Raw, a => a.Contains("--test-panel-row-decode", StringComparer.Ordinal), (_, _) =>
        {
            PanelRowDecodeTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "panel-row-decode"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--panel-row-decode", 74, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--panel-row-decode", (a, _) => PanelRowDecodeCommand(a)),
    ];

    /// <summary>Read-only count of where one list row's matches, and the targets whose protein name matches a
    /// pattern given on the command line, ended in the saved grouping. Prints counts and row keys, never proteins.</summary>
    private static int PanelRowDecodeCommand(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("--panel-row-decode <project.fqproj> <row key or name> <protein-name pattern> <new-output-directory>");
        if (Directory.Exists(args[4]) || File.Exists(args[4])) throw new IOException("Choose a new output directory.");
        var file = ProjectStore.ReadFqproj(args[1]).fcf;
        var recipe = file.EvidenceRecipe ?? throw new InvalidOperationException("The project has no applied grouping.");
        var panel = recipe.FamilyPanel ?? file.FamilyPanel ?? throw new InvalidOperationException("The project has no family list.");
        var proposal = PanelEvidenceClassifier.Propose(file, recipe.Options, recipe.SessionSearch, recipe.SessionReferences, panel);
        var checklist = PanelFamilyChecklist.Build(file, proposal, panel);
        var row = checklist.SingleOrDefault(r => r.Key == args[2]) ?? checklist.Single(r => string.Equals(r.Name, args[2], StringComparison.OrdinalIgnoreCase));
        var definition = panel.Families.Single(f => f.Key == row.Key);
        string target = row.TargetKey ?? row.Key;
        var rowDomains = definition.Pfam.Select(d => d.Accession).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var named = definition.UniProtFamilies.Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pattern = new Regex(args[3], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var scope = recipe.Options.ScopeTerms;
        var resolver = PanelEvidenceClassifier.CreateCurrentResolver(file);
        var applied = (file.PanelProteinAssignments ?? new()).ToDictionary(a => a.ProteinIndex);
        var matches = row.Proteins.Select(p => p.ProteinIndex).ToHashSet();
        var rows = proposal.Proteins.ToDictionary(p => p.ProteinIndex);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var families = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        void Count(string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
        int reproduced = 0, recorded = 0;
        for (int index = 0; index < file.MyExtendedFastaItems.Count; index++)
        {
            var protein = file.MyExtendedFastaItems[index];
            if (NearestReviewed.IsDecoy(protein) || SequenceLabels.IsContaminant(protein.SequenceIdentifier) || !rows.TryGetValue(index, out var proposed)) continue;
            bool listed = matches.Contains(index), described = pattern.IsMatch(FamilyNaming.ProteinName(protein.Description));
            var evidence = protein.Evidence;
            var domains = (evidence?.Pfam ?? new()).Select(d => d.Accession).Where(d => !string.IsNullOrWhiteSpace(d)).ToHashSet(StringComparer.Ordinal);
            string saved = applied.TryGetValue(index, out var fact) && fact.IsApplied ? fact.PanelFamilyKey ?? "" : "";
            if (fact != null) { recorded++; if (saved == proposed.Family) reproduced++; }
            if (saved != proposed.Family) Count("change | saved " + (saved.Length == 0 ? "outside" : saved) + " to proposed " + (proposed.Family.Length == 0 ? "outside" : proposed.Family)
                + (file.LockedUserIds.Contains(protein.UserID) ? ", locked family" : ", unlocked family"));
            if (proposed.Family.Length > 0 && saved != proposed.Family)
            {
                var listedDomains = panel.Families.Single(f => f.Key == proposed.Family).Pfam.Select(d => d.Accession).ToHashSet(StringComparer.Ordinal);
                Count("new in " + proposed.Family + " | total");
                Count("new in " + proposed.Family + " | name matches the pattern " + (described ? "yes" : "no"));
                Count("new in " + proposed.Family + " | reviewed " + (evidence?.Reviewed == true ? "yes" : "no"));
                Count("new in " + proposed.Family + " | Pfam " + (domains.Count == 0 ? "none" : domains.All(listedDomains.Contains) ? "all listed by the row" : "partly listed by the row"));
                Count("new in " + proposed.Family + " | tier " + proposed.Tier);
            }
            if (saved.Length > 0) Count("row " + saved + " | saved");
            if (proposed.Family.Length > 0) Count("row " + proposed.Family + " | proposed");
            if (proposed.Family.Length == 0)
            {
                // Census of what each candidate GA2 rule would admit, over every target the proposal leaves outside.
                string[] ownFamilies = evidence == null ? Array.Empty<string>() : ReferencePanelFetcher.FamiliesOf(evidence)
                    .Select(f => f.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                string words = proposed.HasScopeAnnotation ? "has the scope words" : "lacks the scope words";
                if (ownFamilies.Length > 0)
                {
                    string? linked = resolver.PanelFamilyFor(evidence);
                    Count("census outside, " + words + " | own UniProt family " + (linked == null ? "maps to no row" : linked == target ? "maps to the row" : "maps to row " + linked));
                }
                var (best, fits) = BestRow(panel, domains);
                Count("census outside, " + words + " | " + (ownFamilies.Length > 0 ? "own UniProt family; " : "no UniProt family; ")
                    + (best == null ? domains.Count == 0 ? "no Pfam" : "no single best row" : (best == target ? "Pfam best is the row" : "Pfam best is row " + best) + (fits ? ", fits" : ", extra domain"))
                    + (described ? "; name matches" : ""));
            }
            if (!listed && !described) continue;
            string set = listed && described ? "both" : listed ? "row match" : "name only";
            string place = proposed.Family == target ? "placed in the row"
                : proposed.Family.Length > 0 ? "placed in row " + proposed.Family
                : proposed.HasScopeAnnotation ? "outside, has the scope words" : "outside, lacks the scope words";
            string[] sources = evidence == null ? Array.Empty<string>() : ReferencePanelFetcher.FamiliesOf(evidence)
                .Select(f => f.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string? own = sources.Length == 0 ? null : resolver.PanelFamilyFor(evidence);
            bool Words(List<string>? values) => values != null && EvidenceClassifier.IsInScope(new ProteinEvidence { Keywords = values }, scope);
            string Best(bool overlap)
            {
                if (domains.Count == 0) return "no Pfam";
                var scored = panel.Families.Select(f =>
                {
                    var listedDomains = f.Pfam.Select(d => d.Accession).Distinct(StringComparer.Ordinal).ToArray();
                    int shared = listedDomains.Count(domains.Contains);
                    return (Key: FamilyPanelValidation.ResolveFamilyKey(panel, f.Key) ?? f.Key, Shared: shared, Listed: listedDomains.Length,
                        Union: listedDomains.Length + domains.Count - shared);
                }).Where(s => s.Shared > 0).ToArray();
                if (scored.Length == 0) return "no row domain";
                string[] best;
                if (overlap)
                {
                    // Cross-multiplied so equal fractions compare exactly.
                    var top = scored.Aggregate((a, b) => (long)b.Shared * a.Union > (long)a.Shared * b.Union ? b : a);
                    best = scored.Where(s => (long)s.Shared * top.Union == (long)top.Shared * s.Union).Select(s => s.Key).Distinct(StringComparer.Ordinal).ToArray();
                }
                else
                {
                    int most = scored.Max(s => s.Shared), smallest = scored.Where(s => s.Shared == most).Min(s => s.Listed);
                    best = scored.Where(s => s.Shared == most && s.Listed == smallest).Select(s => s.Key).Distinct(StringComparer.Ordinal).ToArray();
                }
                return best.Length > 1 ? best.Contains(target) ? "tie with the row" : "tie elsewhere" : best[0] == target ? "the row" : "another row";
            }
            var traits = new List<string>
            {
                "reviewed " + (evidence?.Reviewed switch { true => "yes", false => "no", _ => "unknown" }),
                "UniProt family " + (sources.Length == 0 ? "none" : sources.Any(named.Contains) ? "named by the row"
                    : own == target ? "maps to the row" : own != null ? "maps to another row" : "maps to no row"),
                "Pfam " + (domains.Count == 0 ? "none" : domains.All(rowDomains.Contains) ? "all listed by the row"
                    : domains.Overlaps(rowDomains) ? "partly listed by the row" : "none listed by the row"),
                // D111: most shared domains, then the smallest row list. D120: shared over all domains in either.
                "best row by D111 " + Best(false),
                "best row by D120 " + Best(true),
                "keywords " + (evidence?.Keywords is not { Count: > 0 } ? "empty" : Words(evidence.Keywords) ? "have the scope words" : "lack the scope words"),
                "tissue " + (evidence?.TissueSpecificity is not { Count: > 0 } ? "empty" : Words(evidence.TissueSpecificity) ? "has the scope words" : "lacks the scope words"),
                "neighbour " + (proposed.Neighbour == null ? "none" : "recorded"),
                "family lock " + (file.LockedUserIds.Contains(protein.UserID) ? "locked" : "unlocked"),
                "reason " + (proposed.Flags.Count > 0 ? proposed.Flags[0].Code : proposed.Family.Length > 0 ? "placed" : "no flag"),
                "fragment class " + (domains.Count == 0 && sources.Length == 0 ? "no Pfam and no UniProt family" : "has Pfam or UniProt family"),
            };
            traits.Add("profile " + string.Join("; ", traits[1], traits[2], traits[4], traits[9]));
            Count(place + " | total"); Count(place + " | set " + set);
            foreach (string trait in traits) Count(place + " | " + trait);
            if (!families.TryGetValue(place, out var ids)) families[place] = ids = new HashSet<int>();
            ids.Add(protein.UserID);
        }
        foreach (var pair in families) counts[pair.Key + " | sequence families"] = pair.Value.Count;
        var sizes = file.MyExtendedFastaItems.Where(p => !NearestReviewed.IsDecoy(p)).GroupBy(p => p.UserID).ToDictionary(g => g.Key, g => g.Count());
        foreach (var pair in families)
            foreach (int id in pair.Value.OrderByDescending(id => sizes[id]).Take(3))
                counts[pair.Key + " | largest family #" + id.ToString(CultureInfo.InvariantCulture) + " of " + sizes[id].ToString(CultureInfo.InvariantCulture)
                    + (file.LockedUserIds.Contains(id) ? ", locked" : "")] = 0;
        // Fetch UniProt families stores only EvidenceSource.FamilyWorld; propose again without it to show what it changes.
        var withoutWorld = FamilyClusterFile.FromJsonText(file.ToJsonText());
        int worldEntries = withoutWorld.EvidenceSource?.FamilyWorld?.Entries.Count ?? 0;
        if (withoutWorld.EvidenceSource != null) withoutWorld.EvidenceSource.FamilyWorld = null;
        var second = PanelEvidenceClassifier.Propose(withoutWorld, recipe.Options, recipe.SessionSearch, recipe.SessionReferences, panel);
        int changed = second.Proteins.Zip(proposal.Proteins).Count(pair => pair.First.Family != pair.Second.Family);
        Directory.CreateDirectory(args[4]);
        var csv = new StringBuilder("Class,Trait,Proteins\r\n");
        foreach (var pair in counts)
        {
            string[] parts = pair.Key.Split(" | ");
            csv.Append('"').Append(parts[0]).Append("\",\"").Append(parts[1]).Append("\",").Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }
        var summary = new StringBuilder();
        summary.Append("Row ").Append(row.Key).Append(": ").Append(row.Matches.ToString(CultureInfo.InvariantCulture)).Append(" matches, ")
            .Append(row.Reviewed.ToString(CultureInfo.InvariantCulture)).Append(" reviewed, ").Append(row.Placed.ToString(CultureInfo.InvariantCulture))
            .Append(" placed, ").Append(row.Outside.ToString(CultureInfo.InvariantCulture)).Append(" outside\r\n");
        summary.Append("Saved assignments reproduced: ").Append(reproduced.ToString(CultureInfo.InvariantCulture)).Append(" of ")
            .Append(recorded.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        summary.Append("Locked families: ").Append(file.LockedUserIds.Count.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        summary.Append("Tie questions: ").Append(proposal.PanelContext!.Links.Count(l => l.NeedsConfirmation).ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        summary.Append("Stored UniProt family entries: ").Append(worldEntries.ToString(CultureInfo.InvariantCulture))
            .Append("; placements changed without them: ").Append(changed.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        File.WriteAllText(Path.Combine(args[4], "row-decode.csv"), csv.ToString(), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(args[4], "row-decode.txt"), summary.ToString(), new UTF8Encoding(true));
        Console.Write(summary);
        Console.Write(csv);
        return 0;
    }

    /// <summary>The single row whose Pfam list overlaps the protein's domains most, counted over both (D120), and
    /// whether every domain of the protein is listed by that row.</summary>
    private static (string? Row, bool Fits) BestRow(FamilyPanel panel, HashSet<string> domains)
    {
        if (domains.Count == 0) return (null, false);
        var scored = panel.Families.Select(f =>
        {
            var listed = f.Pfam.Select(d => d.Accession).ToHashSet(StringComparer.Ordinal);
            int shared = listed.Count(domains.Contains);
            return (Key: FamilyPanelValidation.ResolveFamilyKey(panel, f.Key) ?? f.Key, Shared: shared, Union: listed.Count + domains.Count - shared,
                Fits: domains.All(listed.Contains));
        }).Where(s => s.Shared > 0).ToArray();
        if (scored.Length == 0) return (null, false);
        var top = scored.Aggregate((a, b) => (long)b.Shared * a.Union > (long)a.Shared * b.Union ? b : a);
        var best = scored.Where(s => (long)s.Shared * top.Union == (long)top.Shared * s.Union).ToArray();
        return best.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() == 1 ? (best[0].Key, best.All(s => s.Fits)) : (null, false);
    }

    private static void PanelRowDecodeTests(string output)
    {
        Directory.CreateDirectory(output);
        static FastaItemExtended Protein(string accession, string name, string source, bool reviewed, bool words, int userId, params string[] domains) => new()
        {
            SequenceIdentifier = (reviewed ? "sp|" : "tr|") + accession + "|SYNTHETIC", Description = name + " OS=Synthetic", Sequence = "ACDEFGHIKLMNPQRSTVWY" + new string('W', userId),
            UserID = userId, USearchID = userId,
            Evidence = new() { Accession = accession, Family = source, Reviewed = reviewed, Keywords = words ? new() { "selected" } : new() { "other" },
                Pfam = domains.Select(d => new EvidenceDomain { Accession = d }).ToList() },
        };
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
        var file = new FamilyClusterFile
        {
            FamilyPanel = new() { Families = new()
            {
                new() { Key = "wide", DisplayName = "User row wide", Pfam = new() { new() { Accession = "domain-a" }, new() { Accession = "domain-b" }, new() { Accession = "domain-c" } } },
                new() { Key = "narrow", DisplayName = "User row narrow", Pfam = new() { new() { Accession = "domain-c" } } },
            } },
            MyExtendedFastaItems = new()
            {
                Protein("P52001", "Alpha enzyme", "Source alpha", true, true, 1, "domain-a", "domain-b"),
                Protein("P52002", "Alpha enzyme", "Source alpha", false, false, 2, "domain-a", "domain-b"),
                Protein("P52003", "Alpha enzyme", "", false, true, 3),
                Protein("P52004", "Beta binder", "Source beta", true, true, 4, "domain-c"),
                Protein("P52005", "Gamma other", "", false, false, 5),
            },
        };
        var proposal = PanelEvidenceClassifier.Propose(file, options);
        var selected = proposal.Proteins.Where(p => p.SuggestedSelected).ToArray();
        var recipe = EvidenceApply.Apply(file, proposal, new EvidenceApplySelection { SourceFingerprint = proposal.SourceFingerprint,
            SelectedProteinIndices = selected.Select(p => p.ProteinIndex).ToArray(), FamilyPanel = FamilyPanelValidation.Clone(file.FamilyPanel!) }, "fixture").Recipe;
        string project = Path.Combine(output, "synthetic-" + Guid.NewGuid().ToString("N") + ".fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        string hash = TestFileHash.Upper(project), report = Path.Combine(output, "decode-" + Guid.NewGuid().ToString("N"));
        var console = Console.Out; var captured = new StringWriter(CultureInfo.InvariantCulture);
        try { Console.SetOut(captured); PanelRowDecodeCommand(new[] { "--panel-row-decode", project, "wide", "alpha", report }); }
        finally { Console.SetOut(console); }
        string csv = File.ReadAllText(Path.Combine(report, "row-decode.csv"));
        int Cell(string place, string trait) => csv.Split("\r\n").Where(line => line.StartsWith("\"" + place + "\",\"" + trait + "\",", StringComparison.Ordinal))
            .Select(line => int.Parse(line[(line.LastIndexOf(',') + 1)..], CultureInfo.InvariantCulture)).SingleOrDefault();
        Check("GA1 decode reproduces the saved grouping and counts the row", captured.ToString().Contains("Saved assignments reproduced: 5 of 5", StringComparison.Ordinal)
            && recipe.Assignments.Count > 0);
        Check("GA1 decode sorts the reviewed scope member into the row and the unreviewed one outside by the scope words",
            Cell("placed in the row", "reviewed yes") == 1 && Cell("outside, lacks the scope words", "reviewed no") == 1
            && Cell("outside, lacks the scope words", "UniProt family maps to the row") == 1);
        Check("GA1 decode counts a name-only target without Pfam or UniProt family as the fragment class",
            Cell("outside, has the scope words", "fragment class no Pfam and no UniProt family") == 1 && Cell("outside, has the scope words", "set name only") == 1);
        Check("GA1 decode reports D111 and D120 best rows", Cell("placed in the row", "best row by D111 the row") == 1
            && Cell("placed in the row", "best row by D120 the row") == 1);
        Check("GA1 decode names no protein accession and leaves the project unchanged", file.MyExtendedFastaItems
            .All(p => !(captured.ToString() + csv).Contains(p.Evidence!.Accession, StringComparison.Ordinal)) && TestFileHash.Upper(project) == hash);
    }
}
