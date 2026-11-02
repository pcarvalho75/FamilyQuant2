using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests
{
    // A checker only. The family named here and the ADAM name pattern copied from exp5.py are the answer
    // key the program's flags are graded against; neither reaches the rule in Evidence/.
    internal static class CoherenceCheckRun
    {
        internal const string Argument = "--coherence-check";
        private const string SvmpFamily = "venom metalloproteinase (M12B) family";
        private static readonly Regex AdamNamed = new Regex(
            @"disintegrin and metalloproteinase domain|\badam\d*\b|adam\d+|adam metallopeptidase|thrombospondin",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) => TestArguments.EmptyFallsBack(args, flag, offset, fallback);
                string input = At(1, "D:/FamilyQuant-data/out-opus/e4-ana-panel.fcf");
                string classes = At(2, "D:/FamilyQuant-data/baseline/classes_all.tsv");
                string outDir = At(3, "D:/FamilyQuant-data/out-opus");

                FamilyClusterFile file = FamilyClusterFile.Load(input);
                Dictionary<string, FamilyProfile> profiles = CoherenceCheck.Learn(file);
                FamilyProfile svmp = profiles[SvmpFamily];
                Dictionary<string, string> python = File.ReadLines(classes).Skip(1).Select(l => l.Split('\t'))
                    .ToDictionary(c => c[0], c => c[1], StringComparer.Ordinal);

                // The SVMP set as any route could form it: every target naming the family, or sharing a domain
                // with its reviewed members.
                List<FastaItemExtended> candidates = NearestReviewed.Targets(file).Where(t => t.Evidence != null
                    && (ReferencePanelFetcher.FamiliesOf(t.Evidence).Contains(SvmpFamily, StringComparer.OrdinalIgnoreCase)
                        || t.Evidence.Pfam.Any(d => svmp.DomainCarriers.ContainsKey(d.Accession)))).ToList();

                List<(FastaItemExtended Item, List<CoherenceFlag> Flags)> Judge() =>
                    candidates.Select(c => (c, CoherenceCheck.Check(svmp, c))).ToList();
                var first = Judge();
                var second = Judge();
                check("flags are reproducible", JsonSerializer.Serialize(first.Select(f => f.Flags))
                    == JsonSerializer.Serialize(second.Select(f => f.Flags)), first.Count + " candidates");

                string Kind(FastaItemExtended t)
                {
                    string name = (t.Description ?? "").Split(" OS=")[0];
                    if (AdamNamed.IsMatch(name)) return "named ADAM or ADAMTS";
                    return python.TryGetValue(t.SequenceIdentifier, out string? c) ? "Python " + c : "Python no class";
                }
                var tally = first.GroupBy(f => Kind(f.Item)).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => new
                {
                    proteins = g.Count(),
                    flagged = g.Count(f => f.Flags.Count > 0),
                    byCode = g.SelectMany(f => f.Flags.Select(x => x.Code).Distinct()).GroupBy(x => x)
                        .OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count()),
                });

                var csv = new StringBuilder("Identifier,Kind,Length,Cysteines,Fragment,Transmembrane,Domains,Codes,Messages\r\n");
                foreach (var (item, flags) in first.OrderBy(f => Kind(f.Item), StringComparer.Ordinal).ThenBy(f => f.Flags.Count == 0)
                    .ThenBy(f => f.Item.SequenceIdentifier, StringComparer.Ordinal))
                    csv.Append(string.Join(",", new[]
                    {
                        item.SequenceIdentifier, Kind(item), item.Sequence.Length.ToString(CultureInfo.InvariantCulture),
                        CoherenceCheck.Cysteines(item.Sequence).ToString(CultureInfo.InvariantCulture),
                        item.Evidence?.Fragment?.ToString() ?? "", item.Evidence?.TransmembraneCount?.ToString(CultureInfo.InvariantCulture) ?? "",
                        string.Join(";", item.Evidence?.Pfam.Select(d => d.Name) ?? Array.Empty<string>()),
                        string.Join(";", flags.Select(f => f.Code)), string.Join(" | ", flags.Select(f => f.Message)),
                    }.Select(Field))).Append("\r\n");
                File.WriteAllText(Path.Combine(outDir, "e7-svmp-flags.csv"), csv.ToString());

                // Every family at once: each target judged against the single family its evidence names.
                var perFamily = NearestReviewed.Targets(file).Where(t => t.Evidence != null && t.Evidence.Family.Length > 0
                        && profiles.ContainsKey(t.Evidence.Family))
                    .GroupBy(t => profiles[t.Evidence!.Family].Family, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        family = g.Key, profiles[g.Key].Members, profiles[g.Key].CompleteMembers,
                        proteins = g.Count(), flagged = g.Count(t => CoherenceCheck.Check(profiles[g.Key], t).Count > 0),
                        byCode = g.SelectMany(t => CoherenceCheck.Check(profiles[g.Key], t).Select(x => x.Code).Distinct())
                            .GroupBy(x => x).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count()),
                    })
                    .Where(r => r.flagged > 0).OrderByDescending(r => r.flagged).ThenBy(r => r.family, StringComparer.Ordinal).ToList();

                var report = new
                {
                    input,
                    minimumMembers = CoherenceCheck.DefaultMinimumMembers,
                    svmpProfile = svmp,
                    candidates = candidates.Count,
                    byKind = tally,
                    namedAdamNotFlaggedForE14 = first.Where(f => Kind(f.Item).StartsWith("named", StringComparison.Ordinal) && f.Flags.Count == 0)
                        .Select(f => f.Item.SequenceIdentifier).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    pythonSvmpFlaggedForE14 = first.Where(f => Kind(f.Item) == "Python SVMP" && f.Flags.Count > 0)
                        .Select(f => f.Item.SequenceIdentifier + ": " + string.Join(" | ", f.Flags.Select(x => x.Message)))
                        .OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    familiesWithFlagsAmongSingleFamilyTargets = perFamily,
                };
                string text = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(outDir, "e7-report.json"), text);
                Console.WriteLine(text);
            }
            catch (Exception ex) { check("coherence real-data check completed", false, ex.GetBaseException().ToString()); }
        }

        private static string Field(string value) => TestCsvFields.Field(value);
    }
}
