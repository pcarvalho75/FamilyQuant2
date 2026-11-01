using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    // A checker only: it compares the program's neighbours with the frozen Python table and writes the
    // differences down. Nothing it reads from the baseline reaches a curation.
    internal static class NearestReviewedCheck
    {
        internal const string Argument = "--nearest-reviewed-check";

        // The scope exp6.py searched against, typed here as a user would type it: reviewed proteins with a
        // UniProt family line and the Toxin keyword or a venom-gland tissue. The rule holds no such word.
        private const string ScopeKeyword = "Toxin";
        private const string ScopeTissue = "venom gland";

        internal const string FullArgument = "--nearest-reviewed-full";

        /// <summary>The whole search as the program would run it on a file with a stored panel: every
        /// target against the scoped reviewed proteins and the panel, timed and stored.</summary>
        internal static void RunFull(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, FullArgument);
                string At(int offset, string fallback) =>
                    args.Length > flag + offset && args[flag + offset].Length > 0 ? args[flag + offset] : fallback;
                string input = At(1, "D:/FamilyQuant-data/out-opus/e4-ana-panel.fcf");
                string output = At(2, "D:/FamilyQuant-data/out-opus/e4-ana-panel-neighbours.fcf");
                FamilyClusterFile file = FamilyClusterFile.Load(input);
                Func<ProteinEvidence, bool> scope = e => e.Keywords.Contains(ScopeKeyword, StringComparer.Ordinal)
                    || e.TissueSpecificity.Any(t => t.Contains(ScopeTissue, StringComparison.OrdinalIgnoreCase));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                NeighbourSearch search = NearestReviewed.SearchAndStore(file, scope);
                double seconds = watch.Elapsed.TotalSeconds;
                file.Save(output);
                Console.WriteLine($"targets {search.Queries}, references {search.References}, neighbours {search.Neighbours.Count}, " +
                    $"from the panel {search.Neighbours.Count(n => n.Reference.Length == 0)}, " +
                    $"seconds {seconds.ToString("0", CultureInfo.InvariantCulture)}, saved {output}");
                check("the full search with the panel completed and was stored", file.EvidenceSource?.NearestReviewed == search,
                    search.Neighbours.Count + " neighbours");
            }
            catch (Exception ex) { check("full nearest reviewed search completed", false, ex.GetBaseException().ToString()); }
        }

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) =>
                    args.Length > flag + offset && args[flag + offset].Length > 0 ? args[flag + offset] : fallback;
                string input = At(1, "D:/FamilyQuant-data/out-evidence/ana-evidence.fcf");
                string baseline = At(2, "D:/FamilyQuant-data/baseline/vs_revtox2.tsv");
                string outDir = At(3, "D:/FamilyQuant-data/out-opus");
                Directory.CreateDirectory(outDir);

                FamilyClusterFile file = FamilyClusterFile.Load(input);
                List<FastaItemExtended> targets = NearestReviewed.Targets(file);
                // exp6 kept a protein with any family line; E2 leaves Family empty when there are several,
                // so the annotation, not the single family, is what matches exp6's set.
                List<NeighbourReference> scoped = NearestReviewed.ReviewedReferences(file, e =>
                    !string.IsNullOrEmpty(e.FamilyAnnotation)
                    && (e.Keywords.Contains(ScopeKeyword, StringComparer.Ordinal)
                        || e.TissueSpecificity.Any(t => t.Contains(ScopeTissue, StringComparison.OrdinalIgnoreCase))));
                Console.WriteLine($"targets {targets.Count}, scoped reviewed references {scoped.Count}");

                NearestReviewed.ForgetMemo();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                NeighbourSearch first = NearestReviewed.Search(targets, scoped);
                double firstSeconds = watch.Elapsed.TotalSeconds;
                NearestReviewed.ForgetMemo();
                NeighbourSearch second = NearestReviewed.Search(targets, scoped);
                string firstText = JsonSerializer.Serialize(first);
                string secondText = JsonSerializer.Serialize(second);
                File.WriteAllText(Path.Combine(outDir, "e3-neighbours-run1.json"), firstText);
                File.WriteAllText(Path.Combine(outDir, "e3-neighbours-run2.json"), secondText);
                check("two USEARCH runs on Ana's file give byte-identical neighbours", firstText == secondText,
                    first.Neighbours.Count + " neighbours");

                Dictionary<string, NearestNeighbour> program = first.ByQuery();
                Dictionary<int, List<string[]>> python = ReadBaseline(baseline);
                var rows = new List<string[]>();
                var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
                for (int x = 0; x < targets.Count; x++)
                {
                    string id = targets[x].SequenceIdentifier;
                    program.TryGetValue(id, out NearestNeighbour? mine);
                    python.TryGetValue(x, out List<string[]>? lines);
                    lines ??= new List<string[]>();
                    (double Identity, int Index, int Aln, int Ql)? theirs = PythonBest(x, lines, targets);
                    string verdict = Judge(mine, theirs, lines, targets);
                    tally[verdict] = tally.TryGetValue(verdict, out int n) ? n + 1 : 1;
                    if (verdict == "agree" || verdict == "agree, no neighbour") continue;
                    rows.Add(new[]
                    {
                        id, verdict,
                        mine?.Reference ?? "", mine?.Family ?? "", mine == null ? "" : Num(mine.IdentityPercent),
                        mine == null ? "" : mine.AlignmentLength + "/" + mine.QueryLength, string.Join(";", mine?.TiedFamilies ?? new List<string>()),
                        theirs == null ? "" : targets[theirs.Value.Index].SequenceIdentifier,
                        theirs == null ? "" : targets[theirs.Value.Index].Evidence?.Family ?? "",
                        theirs == null ? "" : Num(theirs.Value.Identity),
                        lines.Count.ToString(CultureInfo.InvariantCulture),
                    });
                }

                var csv = new StringBuilder();
                csv.Append("Query,Verdict,ProgramNeighbour,ProgramFamily,ProgramIdentity,ProgramAlignedOverQuery,ProgramTiedFamilies,PythonNeighbour,PythonFamily,PythonIdentity,PythonLines\r\n");
                foreach (string[] row in rows) csv.Append(string.Join(",", row.Select(Field))).Append("\r\n");
                File.WriteAllText(Path.Combine(outDir, "e3-differences.csv"), csv.ToString());

                var all = NearestReviewed.ReviewedReferences(file);
                NeighbourSearch unscoped = NearestReviewed.Search(targets, all);
                var report = new
                {
                    input,
                    baseline,
                    method = first.Method,
                    first.MinIdentityPercent,
                    first.MinQueryAlignedPercent,
                    targets = first.Queries,
                    scopedReferences = first.References,
                    scopedNeighbours = first.Neighbours.Count,
                    scopedTiesAcrossFamilies = first.Neighbours.Count(n => n.TiedFamilies.Count > 0),
                    firstRunSeconds = Math.Round(firstSeconds, 1),
                    verdicts = tally,
                    allReviewedReferences = unscoped.References,
                    allReviewedNeighbours = unscoped.Neighbours.Count,
                    allReviewedNeighboursWithoutSingleFamily = unscoped.Neighbours.Count(n => n.Family.Length == 0),
                };
                // The searched file with each search stored, so the classifier's checks need not search again.
                file.EvidenceSource ??= new EvidenceProvenance();
                file.EvidenceSource.NearestReviewed = first;
                file.Save(Path.Combine(outDir, "e3-ana-neighbours-scoped.fcf"));
                file.EvidenceSource.NearestReviewed = unscoped;
                file.Save(Path.Combine(outDir, "e3-ana-neighbours-all.fcf"));
                string reportText = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(outDir, "e3-report.json"), reportText);
                Console.WriteLine(reportText);

                int unexplained = tally.Where(t => t.Key.StartsWith("UNEXPLAINED", StringComparison.Ordinal)).Sum(t => t.Value);
                check("every difference from vs_revtox2.tsv has an explanation", unexplained == 0,
                    unexplained + " unexplained");
            }
            catch (Exception ex) { check("nearest reviewed real-data check completed", false, ex.GetBaseException().ToString()); }
        }

        /// <summary>Python's lines per query index: target, id, alnlen, ql, tl.</summary>
        private static Dictionary<int, List<string[]>> ReadBaseline(string path)
        {
            var byQuery = new Dictionary<int, List<string[]>>();
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                string[] cols = line.Split('\t');
                int q = int.Parse(cols[0].AsSpan(1), CultureInfo.InvariantCulture);
                if (!byQuery.TryGetValue(q, out var list)) byQuery[q] = list = new List<string[]>();
                list.Add(cols);
            }
            return byQuery;
        }

        private static string PythonAccession(string s)
        {
            if (s.StartsWith("contaminant_", StringComparison.Ordinal)) return s.Substring("contaminant_".Length).Split('|')[0];
            string[] p = s.Split('|');
            return p.Length >= 2 ? p[1] : s;
        }

        /// <summary>exp6.py's choice, line for line: first strictly higher identity among lines that are not
        /// the query, span at least 0.7 of the query in floating point and name another accession.</summary>
        private static (double, int, int, int)? PythonBest(int x, List<string[]> lines, List<FastaItemExtended> targets)
        {
            (double, int, int, int)? best = null;
            double bestId = 0;
            foreach (string[] c in lines)
            {
                int y = int.Parse(c[1].AsSpan(1), CultureInfo.InvariantCulture);
                double id = double.Parse(c[2], CultureInfo.InvariantCulture);
                int a = int.Parse(c[3], CultureInfo.InvariantCulture);
                int ql = int.Parse(c[4], CultureInfo.InvariantCulture);
                if (x != y && a >= 0.7 * ql
                    && PythonAccession(targets[x].SequenceIdentifier) != PythonAccession(targets[y].SequenceIdentifier)
                    && id > bestId)
                {
                    bestId = id;
                    best = (id, y, a, ql);
                }
            }
            return best;
        }

        private static string Judge(NearestNeighbour? mine, (double Identity, int Index, int Aln, int Ql)? theirs,
            List<string[]> lines, List<FastaItemExtended> targets)
        {
            if (mine == null && theirs == null) return "agree, no neighbour";
            if (mine == null) return "UNEXPLAINED Python only";
            if (theirs != null)
            {
                string theirId = targets[theirs.Value.Index].SequenceIdentifier;
                if (theirId == mine.Reference)
                    return mine.IdentityPercent == theirs.Value.Identity ? "agree" : "UNEXPLAINED same neighbour, other identity";
                if (mine.IdentityPercent == theirs.Value.Identity)
                {
                    string theirFamily = targets[theirs.Value.Index].Evidence?.Family ?? "";
                    return theirFamily == mine.Family
                        ? "tie at the best identity, same family"
                        : "tie at the best identity, other family (reported in TiedFamilies)";
                }
                if (mine.IdentityPercent < theirs.Value.Identity) return "UNEXPLAINED Python closer";
            }
            if (lines.Any(c => targets[int.Parse(c[1].AsSpan(1), CultureInfo.InvariantCulture)].SequenceIdentifier == mine.Reference))
                return 10L * mine.AlignmentLength == 7L * mine.QueryLength
                    ? "alignment exactly 70% of the query: Python's 0.7 * length in floating point drops it"
                    : "UNEXPLAINED Python saw the program's neighbour and did not take it";
            // usearch_global ran with -maxhits 6, the query's own hit among them. The cap can only hide a
            // hit weaker than all six lines; a stronger one missing was never aligned by its word index.
            bool capCouldHide = lines.Count >= 6
                && lines.All(c => double.Parse(c[2], CultureInfo.InvariantCulture) >= mine.IdentityPercent);
            string what = theirs == null ? "a neighbour" : "a closer neighbour";
            return capCouldHide
                ? "program finds " + what + " below Python's six reported hits (-maxhits 6)"
                : "program finds " + what + " Python's indexed usearch_global never aligned";
        }

        private static string Num(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

        private static string Field(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
