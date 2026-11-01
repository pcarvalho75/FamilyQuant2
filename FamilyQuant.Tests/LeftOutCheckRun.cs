using FamilyQuant.Evidence;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    // A checker only. exp8's six-row domain table is the answer key it grades the learned markers against;
    // it is written here, never in Evidence/.
    internal static class LeftOutCheckRun
    {
        internal const string Argument = "--left-out-check";
        private static readonly Dictionary<string, string> Exp8Domains = new(StringComparer.Ordinal)
        {
            ["PLA2"] = "PF00068", ["SVSP"] = "PF00089", ["Snaclec (CTL)"] = "PF00059",
            ["LAAO"] = "PF01593", ["CRISP"] = "PF00188", ["Kunitz"] = "PF00014",
        };

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) =>
                    args.Length > flag + offset && args[flag + offset].Length > 0 ? args[flag + offset] : fallback;
                string input = At(1, "D:/FamilyQuant-data/out-opus/e3-ana-neighbours-scoped.fcf");
                string baseline = At(2, "D:/FamilyQuant-data/baseline/fora_com_dominio_de_veneno.csv");
                string classes = At(3, "D:/FamilyQuant-data/baseline/classes_all.tsv");
                string outDir = At(4, "D:/FamilyQuant-data/out-opus");
                string[] scope = At(5, "Toxin;venom gland").Split(';', StringSplitOptions.RemoveEmptyEntries);

                FamilyClusterFile file = FamilyClusterFile.Load(input);
                NeighbourSearch search = file.EvidenceSource?.NearestReviewed
                    ?? throw new InvalidDataException("The input has no stored neighbour search.");
                EvidenceProposal proposal = EvidenceClassifier.Propose(file.MyExtendedFastaItems,
                    new EvidenceClassificationOptions { ScopeTerms = scope }, search.ByQuery(), NearestReviewed.ReferenceEvidence(file));
                List<LeftOutRow> rows = LeftOutList.Build(file.MyExtendedFastaItems, proposal);
                string csv = LeftOutList.BuildCsv(rows);
                check("the left-out list is reproducible",
                    csv == LeftOutList.BuildCsv(LeftOutList.Build(file.MyExtendedFastaItems, proposal)), rows.Count + " rows");
                File.WriteAllText(Path.Combine(outDir, "e10-left-out.csv"), csv);

                Dictionary<string, string> exp8Class = File.ReadLines(classes).Skip(1).Where(l => l.Length > 0)
                    .Select(l => l.Split('\t')).ToDictionary(c => c[0], c => c[1], StringComparer.Ordinal);
                var exp8 = ReadSemicolonCsv(baseline).Select(r => (Label: r[0], Locus: r[2])).ToList();
                var proposedFamily = proposal.Proteins.ToDictionary(p => p.Identifier, p => p.Family, StringComparer.Ordinal);
                var markerFamily = proposal.Markers.ToDictionary(m => m.Accession, m => m.Family, StringComparer.Ordinal);

                var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var perLabel = new List<object>();
                var lines = new List<string> { "Label,Locus,Side,Reason" };
                foreach ((string label, string domain) in Exp8Domains)
                {
                    bool learned = markerFamily.TryGetValue(domain, out string? family);
                    var theirs = exp8.Where(r => r.Label == label).Select(r => r.Locus).ToHashSet(StringComparer.Ordinal);
                    var mine = learned ? rows.Where(r => string.Equals(r.Family, family, StringComparison.OrdinalIgnoreCase)
                            && r.MarkerAccession == domain).Select(r => r.Identifier).ToHashSet(StringComparer.Ordinal)
                        : new HashSet<string>(StringComparer.Ordinal);
                    // A protein can be listed under the family through another of its markers.
                    var mineAny = learned ? rows.Where(r => string.Equals(r.Family, family, StringComparison.OrdinalIgnoreCase))
                        .Select(r => r.Identifier).ToHashSet(StringComparer.Ordinal) : mine;
                    foreach (string locus in theirs.Where(l => !mineAny.Contains(l)))
                    {
                        string reason = !learned ? "exp8's domain is not learned as a marker at 80% here"
                            : string.Equals(proposedFamily.GetValueOrDefault(locus), family, StringComparison.OrdinalIgnoreCase)
                                ? "the program assigns it to the family; exp8 left it out"
                                : "UNEXPLAINED exp8 lists it, the program does not";
                        Note("exp8 only", locus, label, reason);
                    }
                    foreach (string locus in mineAny.Where(l => !theirs.Contains(l)))
                    {
                        string reason = exp8Class.TryGetValue(locus, out string? c) && c == label
                            ? "exp8 assigns it to the family; the program leaves it out"
                            : !rows.Any(r => r.Identifier == locus && r.MarkerAccession == domain)
                                ? "listed through another marker the program learned for the family"
                                : "UNEXPLAINED the program lists it, exp8 does not";
                        Note("program only", locus, label, reason);
                    }
                    perLabel.Add(new
                    {
                        label, domain, learnedFamily = family ?? "", exp8 = theirs.Count, program = mineAny.Count,
                        both = theirs.Count(mineAny.Contains),
                    });
                }
                File.WriteAllLines(Path.Combine(outDir, "e10-differences-from-exp8.csv"), lines);
                var beyond = rows.Where(r => !Exp8Domains.Values.Any(d => markerFamily.TryGetValue(d, out string? f)
                        && string.Equals(f, r.Family, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(r => r.Family, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count())
                    .ToDictionary(g => g.Key, g => g.Count());
                int unexplained = reasons.Where(r => r.Key.StartsWith("UNEXPLAINED", StringComparison.Ordinal)).Sum(r => r.Value);
                var report = new
                {
                    input, scope, markers = proposal.Markers.Count, rows = rows.Count, exp8Rows = exp8.Count,
                    byExp8Family = perLabel, differences = reasons, beyondExp8sSixFamilies = beyond,
                };
                string text = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(outDir, "e10-report.json"), text);
                Console.WriteLine(text);
                check("every difference from exp8's 359 is explained", unexplained == 0, unexplained + " unexplained");

                void Note(string side, string locus, string label, string reason)
                {
                    string key = side + ": " + reason;
                    reasons[key] = reasons.TryGetValue(key, out int n) ? n + 1 : 1;
                    lines.Add(string.Join(",", new[] { label, locus, side, reason }.Select(Field)));
                }
            }
            catch (Exception ex) { check("left-out real-data check completed", false, ex.GetBaseException().ToString()); }
        }

        private static List<string[]> ReadSemicolonCsv(string path)
        {
            using var reader = new TextFieldParser(path, System.Text.Encoding.UTF8);
            reader.SetDelimiters(";");
            reader.HasFieldsEnclosedInQuotes = true;
            reader.ReadFields();
            var rows = new List<string[]>();
            while (!reader.EndOfData) rows.Add(reader.ReadFields()!);
            return rows;
        }

        private static string Field(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
