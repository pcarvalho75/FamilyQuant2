using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TextFieldParser = Microsoft.VisualBasic.FileIO.TextFieldParser;

namespace FamilyQuant.Tests
{
    internal static class EvidenceClassifierCheck
    {
        internal const string Argument = "--evidence-classifier-check";
        internal const string ArchitectureArgument = "--evidence-architecture-check";
        internal const string ApplyArgument = "--evidence-apply-check";

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                bool architectureMode = args.Contains(ArchitectureArgument);
                bool applyMode = args.Contains(ApplyArgument);
                int flag = Array.IndexOf(args, applyMode ? ApplyArgument : architectureMode ? ArchitectureArgument : Argument);
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
                IReadOnlyList<EvidenceArchitectureProposal>? architecture = null;
                if (architectureMode)
                {
                    architecture = EvidenceArchitecture.Suggest(file.MyExtendedFastaItems, proposal);
                    var primary = architecture.Where(a => a.Family.IndexOf("metalloproteinase", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    check("native architecture offers a free binding-domain group apart from multi-domain members",
                        primary.Any(a => a.CanSplit && a.Groups.Any(g => g.Domains.Count == 1
                            && g.SuggestedName.Equals("Disintegrin", StringComparison.OrdinalIgnoreCase))
                        && a.Groups.Any(g => g.Domains.Count > 1)), null);
                    check("every native family offers distinct names for its domain combinations",
                        architecture.All(a => a.Groups.Select(g => g.SuggestedName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == a.Groups.Count), null);
                    check("native multi-domain names distinguish them from free binding domains",
                        primary.SelectMany(a => a.Groups).Where(g => g.Domains.Count > 1)
                            .All(g => !g.SuggestedName.Equals("Disintegrin", StringComparison.OrdinalIgnoreCase)), null);
                    var second = architecture.FirstOrDefault(a => a.Family.Equals("phospholipase A2 family", StringComparison.OrdinalIgnoreCase));
                    check("the same architecture code evaluates a second source family", second != null && second.Groups.Count > 0, null);
                    string firstArchitecture = JsonSerializer.Serialize(architecture);
                    check("native architecture suggestions are reproducible and preserve the source",
                        firstArchitecture == JsonSerializer.Serialize(EvidenceArchitecture.Suggest(file.MyExtendedFastaItems, proposal))
                        && before == EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems), null);
                }
                object? applyReport = null;
                if (applyMode)
                {
                    int[] selected = proposal.Proteins.Where(p => p.SuggestedSelected
                        && !file.LockedUserIds.Contains(file.MyExtendedFastaItems[p.ProteinIndex].UserID)).Select(p => p.ProteinIndex).ToArray();
                    var selection = new EvidenceApplySelection { SourceFingerprint = proposal.SourceFingerprint, SelectedProteinIndices = selected };
                    string originalDigest = Digest(file);
                    string invariant = Invariants(file);
                    var unselected = file.MyExtendedFastaItems.Select((p, i) => (p, i)).Where(r => !selected.Contains(r.i))
                        .ToDictionary(r => r.i, r => JsonSerializer.Serialize(r.p));
                    var applied = EvidenceApply.Apply(file, proposal, selection, AppVersionInfo.Current());
                    string appliedDigest = Digest(file);
                    check("native apply preserves sequences, automatic IDs, quantitative values and evidence",
                        Invariants(file) == invariant && unselected.All(p => JsonSerializer.Serialize(file.MyExtendedFastaItems[p.Key]) == p.Value), null);
                    check("native apply gives every selected protein its chosen annotation and a locked number",
                        applied.Recipe.Assignments.Count == selected.Length && applied.Recipe.Assignments.All(a =>
                            file.MyExtendedFastaItems[a.ProteinIndex].UserID == a.FamilyId
                            && file.MyExtendedFastaItems[a.ProteinIndex].UserAnnotation == a.Name && file.LockedUserIds.Contains(a.FamilyId)), null);
                    var repeated = EvidenceApply.Apply(file, proposal, selection, applied.Recipe.ProgramVersion);
                    check("native repeated apply is byte-identical and creates no undo", !repeated.Changed && repeated.Undo == null && Digest(file) == appliedDigest, null);
                    string savedPath = Path.ChangeExtension(output, ".applied.fcf");
                    if (Path.GetFullPath(input).Equals(Path.GetFullPath(savedPath), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The saved test copy must differ from its input.");
                    file.Save(savedPath);
                    var reopened = FamilyClusterFile.Load(savedPath);
                    check("native applied copy reopens with its exact recipe and assignments", Digest(reopened) == appliedDigest, null);
                    var replayed = EvidenceApply.ApplyRecipe(reopened, reopened.EvidenceRecipe!);
                    check("native recipe replay after reopening changes no bytes", !replayed.Changed && Digest(reopened) == appliedDigest, null);
                    applied.Undo!.Restore(file);
                    check("one native evidence undo restores the complete original curation", Digest(file) == originalDigest, null);
                    EvidenceApply.ApplyRecipe(file, applied.Recipe);
                    check("native saved recipe reproduces the same applied curation on its source", Digest(file) == appliedDigest, null);
                    applyReport = new { selected = selected.Length, families = applied.Recipe.Assignments.Select(a => a.FamilyId).Distinct().Count(), savedPath, originalDigest, appliedDigest };

                    static string Digest(FamilyClusterFile value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToJsonText())));
                    static string Invariants(FamilyClusterFile value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                        value.MyExtendedFastaItems.Select(p => new { p.SequenceIdentifier, p.Sequence, p.Description, p.USearchID, p.XIC, p.XICUser, p.FullXicShare, p.Domains, p.Evidence }))));
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
                    architecture,
                    applyReport,
                    sourceGuardViolations = violations,
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Classification report: " + output);
                string At(int offset, string fallback) => args.Length > flag + offset ? args[flag + offset] : fallback;
            }
            catch (Exception ex) { check("native evidence classification check completed", false, ex.GetBaseException().Message); }
        }
    }
}
