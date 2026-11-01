using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TextFieldParser = Microsoft.VisualBasic.FileIO.TextFieldParser;

namespace FamilyQuant.Tests
{
    internal static class EvidenceClassifierCheck
    {
        internal const string Argument = "--evidence-classifier-check";

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string input = At(1, "D:/FamilyQuant-data/out-evidence/ana-neighbours.fcf");
                string output = At(2, "D:/FamilyQuant-data/out-evidence/e5-classifier-report.json");
                string[] scope = At(3, "Toxin;venom gland").Split(';', StringSplitOptions.RemoveEmptyEntries);
                FamilyClusterFile file = FamilyClusterFile.Load(input);
                NeighbourSearch search = file.EvidenceSource?.NearestReviewed
                    ?? throw new InvalidDataException("The input has no stored E3 neighbour search. This checker does not launch a search.");
                var scopedReferences = NearestReviewed.ReviewedReferences(file, e => e.FamilyAnnotation.Length > 0
                    && EvidenceClassifier.IsInScope(e, scope));
                var exactKeywordReferences = NearestReviewed.ReviewedReferences(file, e => e.FamilyAnnotation.Length > 0
                    && scope.Any(term => e.Keywords.Contains(term, StringComparer.Ordinal)
                        || e.TissueSpecificity.Any(text => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)));
                Console.WriteLine("Stored search references: " + search.References + "; word scope: " + scopedReferences.Count
                    + "; exact-keyword/tissue scope: " + exactKeywordReferences.Count);
                if (!NearestReviewed.IsCurrent(search, NearestReviewed.Targets(file), scopedReferences)
                    && !NearestReviewed.IsCurrent(search, NearestReviewed.Targets(file), exactKeywordReferences)
                    && !NearestReviewed.IsCurrent(search, NearestReviewed.Targets(file), NearestReviewed.ReviewedReferences(file)))
                    throw new InvalidDataException("The stored neighbour search does not match the current proteins and reviewed labels.");
                if (search.MinIdentityPercent > 60 || search.MinQueryAlignedPercent > 70)
                    throw new InvalidDataException("The stored search omits hits needed by the classification thresholds.");
                string before = EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems);
                var options = new EvidenceClassificationOptions { ScopeTerms = scope };
                EvidenceProposal proposal = EvidenceClassifier.Propose(file.MyExtendedFastaItems, options,
                    search.ByQuery(), NearestReviewed.ReferenceEvidence(file));
                check("native proposal preserves every source protein and grouping value",
                    before == EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems), null);
                check("every target has a family tier and a reason", proposal.Proteins.Count == NearestReviewed.Targets(file).Count
                    && proposal.Proteins.All(p => p.Reason.Length > 0 && (p.Tier == EvidenceTier.Unassigned || p.Family.Length > 0)), null);
                check("all learned markers meet the configured data vote", proposal.Markers.All(m => m.VoteFraction >= options.MarkerVoteFraction), null);
                var names = file.MyExtendedFastaItems.Where(i => i.Evidence != null).SelectMany(i =>
                    new[] { i.Evidence!.Family, i.Evidence.Subfamily }.Concat(i.Evidence.FamilyMemberships?.Select(m => m.Family) ?? Array.Empty<string>())
                    .Concat(i.Evidence.Pfam.Select(p => p.Name))).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                string root = GoldenNumbers.FindRepository();
                string engine = Directory.Exists(Path.Combine(root, "FamilyQuant.Core", "Evidence"))
                    ? Path.Combine(root, "FamilyQuant.Core", "Evidence") : Path.Combine(root, "FamilyQuant", "Evidence");
                var violations = Directory.GetFiles(engine, "*.cs", SearchOption.AllDirectories)
                    .SelectMany(path => EvidenceRuleGuard.Violations(File.ReadAllText(path), names).Select(v => Path.GetFileName(path) + ": " + v)).ToList();
                check("native source-name corpus finds no family/domain or fixed biological literals in rules", violations.Count == 0,
                    string.Join("; ", violations));
                string repeat = JsonSerializer.Serialize(EvidenceClassifier.Propose(file.MyExtendedFastaItems, options,
                    search.ByQuery(), NearestReviewed.ReferenceEvidence(file)));
                check("two native proposals are byte-identical", JsonSerializer.Serialize(proposal) == repeat, null);
                string baselinePath = "D:/FamilyQuant-data/baseline/classes_all.tsv";
                var baselineRows = new List<Dictionary<string, string>>();
                using (var reader = new TextFieldParser(baselinePath))
                {
                    reader.SetDelimiters("\t");
                    string[] header = reader.ReadFields() ?? throw new InvalidDataException("The frozen class table has no header.");
                    while (!reader.EndOfData)
                    {
                        string[] fields = reader.ReadFields()!;
                        baselineRows.Add(header.Select((name, i) => (name, value: fields[i])).ToDictionary(p => p.name, p => p.value, StringComparer.Ordinal));
                    }
                }
                var byIdentifier = proposal.Proteins.ToDictionary(p => p.Identifier, StringComparer.Ordinal);
                var recovery = new List<object>();
                foreach (string label in new[] { "PLA2", "SVSP" })
                {
                    var expected = baselineRows.Where(r => r["Class"] == label && r["ToxinAnnotated"] != "sim").ToList();
                    var missed = expected.Where(r => !byIdentifier.TryGetValue(r["Locus"], out EvidenceProteinProposal? p)
                        || p.Tier != EvidenceTier.UnannotatedNeighbour).ToList();
                    var exclusions = missed.Select(r => byIdentifier.GetValueOrDefault(r["Locus"])).Where(p => p != null).ToList();
                    bool explained = exclusions.Count == missed.Count && exclusions.All(p => p!.Tier == EvidenceTier.Unassigned
                        && p.Flags.Any(f => f.Code == "domain_compatibility")
                        && file.MyExtendedFastaItems[p.ProteinIndex].Evidence?.Pfam.Count == 0);
                    check("native unannotated recovery comparison: " + label, missed.Count == 0 || explained,
                        (expected.Count - missed.Count) + "/" + expected.Count + " recovered; " + missed.Count + " flagged without domain evidence");
                    recovery.Add(new
                    {
                        baselineClass = label, expected = expected.Count, recovered = expected.Count - missed.Count,
                        flagged = exclusions.Select(p => new { p!.Identifier, tier = p.Tier.ToString(), p.Reason, p.Neighbour, p.Flags }).ToList(),
                    });
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllText(output, JsonSerializer.Serialize(new
                {
                    source = input, options, proposal.SourceFingerprint,
                    scopeAnnotated = proposal.Proteins.Count(p => p.HasScopeAnnotation),
                    tierCounts = proposal.Proteins.GroupBy(p => p.Tier).Select(g => new { tier = g.Key.ToString(), count = g.Count() }).ToList(),
                    proposal.Families, proposal.Markers,
                    proteins = proposal.Proteins,
                    recoveryComparison = recovery,
                    sourceGuardViolations = violations,
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Classification report: " + output);
                string At(int offset, string fallback) => args.Length > flag + offset ? args[flag + offset] : fallback;
            }
            catch (Exception ex) { check("native evidence classification check completed", false, ex.GetBaseException().Message); }
        }
    }
}
