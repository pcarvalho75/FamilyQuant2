using FamilyQuant.Evidence;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    // A checker only. exp6's leave-one-out is rebuilt here from its own inputs (vs_revtox2.tsv and the
    // frozen UniProt table) as the answer key; the scope words and Ana's route words are typed as she typed
    // them. Nothing here reaches the rule.
    internal static class ValidationCheckRun
    {
        internal const string Argument = "--validation-check";
        private const string ScopeKeyword = "Toxin";
        private const string ScopeTissue = "venom gland";
        private const string RouteFamily = "venom metalloproteinase (M12B) family";
        private const string Keywords = "metalloprotease, metalloproteinase, svmp";
        private const string Exclude = "inhibitor, adam, matrix, thrombospondin, atp";

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) =>
                    args.Length > flag + offset && args[flag + offset].Length > 0 ? args[flag + offset] : fallback;
                string input = At(1, "D:/FamilyQuant-data/out-opus/e4-ana-panel-neighbours.fcf");
                string baseline = At(2, "D:/FamilyQuant-data/baseline/vs_revtox2.tsv");
                string frozen = At(3, "D:/FamilyQuant-data/baseline/viperidae_rich.tsv");
                string outDir = At(4, "D:/FamilyQuant-data/out-opus");

                FamilyClusterFile file = FamilyClusterFile.Load(input);
                Func<ProteinEvidence, bool> scope = e => e.Keywords.Contains(ScopeKeyword, StringComparer.Ordinal)
                    || e.TissueSpecificity.Any(t => t.Contains(ScopeTissue, StringComparison.OrdinalIgnoreCase));
                LeaveOneOutResult program = ValidationReport.LeaveOneOut(file, scope);
                LeaveOneOutResult again = ValidationReport.LeaveOneOut(file, scope);
                check("the leave-one-out is reproducible", ValidationReport.BuildLeaveOneOutCsv(program) == ValidationReport.BuildLeaveOneOutCsv(again),
                    program.Reviewed + " reviewed");
                LeaveOneOutResult withPanel = ValidationReport.LeaveOneOut(file, scope, withPanel: true);
                File.WriteAllText(Path.Combine(outDir, "e9-leave-one-out.csv"), ValidationReport.BuildLeaveOneOutCsv(program));
                File.WriteAllText(Path.Combine(outDir, "e9-leave-one-out-with-panel.csv"), ValidationReport.BuildLeaveOneOutCsv(withPanel));

                // exp6, line for line: reviewed toxins with a first family, best hit past the gate, right at 60%.
                List<FastaItemExtended> targets = NearestReviewed.Targets(file);
                Dictionary<string, (bool Reviewed, bool Toxin, string First)> tsv = ReadFrozen(frozen);
                (bool Reviewed, bool Toxin, string First)? Rec(int x) =>
                    tsv.TryGetValue(PythonAccession(targets[x].SequenceIdentifier), out var r) ? r : null;
                var best = new Dictionary<int, (double Id, int Y)>();
                foreach (string line in File.ReadLines(baseline))
                {
                    if (line.Length == 0) continue;
                    string[] c = line.Split('\t');
                    int x = int.Parse(c[0].AsSpan(1), CultureInfo.InvariantCulture), y = int.Parse(c[1].AsSpan(1), CultureInfo.InvariantCulture);
                    double id = double.Parse(c[2], CultureInfo.InvariantCulture);
                    int a = int.Parse(c[3], CultureInfo.InvariantCulture), ql = int.Parse(c[4], CultureInfo.InvariantCulture);
                    if (x != y && a >= 0.7 * ql && PythonAccession(targets[x].SequenceIdentifier) != PythonAccession(targets[y].SequenceIdentifier)
                        && id > (best.TryGetValue(x, out var b) ? b.Id : 0)) best[x] = (id, y);
                }
                var python = new Dictionary<string, (string Outcome, string Label, string Neighbour, string NeighbourLabel, double Id)>(StringComparer.Ordinal);
                for (int x = 0; x < targets.Count; x++)
                {
                    var r = Rec(x);
                    if (r is not { Reviewed: true, Toxin: true } || r.Value.First.Length == 0) continue;
                    if (best.TryGetValue(x, out var hit) && hit.Id >= 60)
                    {
                        string theirs = Rec(hit.Y)?.First ?? "";
                        python[targets[x].SequenceIdentifier] = (theirs == r.Value.First ? "Right" : "Wrong", r.Value.First,
                            targets[hit.Y].SequenceIdentifier, theirs, hit.Id);
                    }
                    else python[targets[x].SequenceIdentifier] = ("NoNeighbour", r.Value.First, "", "", 0);
                }
                var pyCounts = python.Values.GroupBy(v => v.Outcome).ToDictionary(g => g.Key, g => g.Count());

                // Each protein whose outcome differs, with the reason.
                Dictionary<string, LeaveOneOutRow> mine = program.Rows.ToDictionary(r => r.Identifier, StringComparer.Ordinal);
                var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var differences = new List<string[]>();
                foreach (string id in python.Keys.Union(mine.Keys).OrderBy(k => k, StringComparer.Ordinal))
                {
                    bool inPy = python.TryGetValue(id, out var py), inMine = mine.TryGetValue(id, out LeaveOneOutRow? me);
                    string pyOutcome = inPy ? py.Outcome : "(not in exp6's set)";
                    string myOutcome = inMine ? me!.Outcome.ToString() : "(not in the program's set)";
                    if (pyOutcome == myOutcome) continue;
                    string reason = Explain(inPy ? py : null, me, targets);
                    reasons[reason] = reasons.TryGetValue(reason, out int n) ? n + 1 : 1;
                    differences.Add(new[]
                    {
                        id, pyOutcome, myOutcome, reason, inPy ? py.Label : "", me?.Families ?? "", inPy ? py.Neighbour : "",
                        inPy ? py.Id.ToString("0.0", CultureInfo.InvariantCulture) : "", me?.NeighbourAccession ?? "",
                        me?.IdentityPercent?.ToString("0.0", CultureInfo.InvariantCulture) ?? "", me?.NeighbourFamilies ?? "",
                    });
                }
                File.WriteAllLines(Path.Combine(outDir, "e9-differences-from-exp6.csv"),
                    new[] { "Identifier,Exp6,Program,Reason,Exp6Label,ProgramLabel,Exp6Neighbour,Exp6Identity,ProgramNeighbour,ProgramIdentity,ProgramNeighbourLabel" }
                    .Concat(differences.Select(d => string.Join(",", d.Select(Field)))));

                // The routes, as the program runs them, with Ana's words, against the reviewed labels.
                var routes = new List<RouteScore>
                {
                    ValidationReport.ScoreRoute("words", RouteFamily, ValidationReport.WordsRoute(file.MyExtendedFastaItems, Keywords, ""), file.MyExtendedFastaItems),
                    ValidationReport.ScoreRoute("words with exclude", RouteFamily, ValidationReport.WordsRoute(file.MyExtendedFastaItems, Keywords, Exclude), file.MyExtendedFastaItems),
                    ValidationReport.ScoreRoute("recluster 0.80, words", RouteFamily, ValidationReport.ReclusterRoute(file.MyExtendedFastaItems, 0.80, Keywords, ""), file.MyExtendedFastaItems),
                    ValidationReport.ScoreRoute("recluster 0.80, words with exclude", RouteFamily, ValidationReport.ReclusterRoute(file.MyExtendedFastaItems, 0.80, Keywords, Exclude), file.MyExtendedFastaItems),
                };
                // The evidence route: E5's proposal from the stored search, then with E7's coherence flags,
                // where only the unflagged (ticked by default) proteins join.
                NeighbourSearch? stored = file.EvidenceSource?.NearestReviewed;
                if (stored != null)
                {
                    EvidenceProposal proposal = EvidenceClassifier.Propose(file.MyExtendedFastaItems,
                        new EvidenceClassificationOptions { ScopeTerms = new[] { ScopeKeyword, ScopeTissue } },
                        stored.ByQuery(), NearestReviewed.ReferenceEvidence(file));
                    EvidenceProposal flagged = CoherenceCheck.Flag(proposal, file.MyExtendedFastaItems, CoherenceCheck.Learn(file));
                    List<FastaItemExtended> Members(EvidenceProposal p, bool tickedOnly) => p.Proteins
                        .Where(r => string.Equals(r.Family, RouteFamily, StringComparison.OrdinalIgnoreCase) && (!tickedOnly || r.SuggestedSelected))
                        .Select(r => file.MyExtendedFastaItems[r.ProteinIndex]).ToList();
                    routes.Add(ValidationReport.ScoreRoute("evidence (E5)", RouteFamily, Members(proposal, false), file.MyExtendedFastaItems));
                    routes.Add(ValidationReport.ScoreRoute("evidence, ticked after E5 and E7 flags", RouteFamily, Members(flagged, true), file.MyExtendedFastaItems));
                }
                File.WriteAllText(Path.Combine(outDir, "e9-routes.csv"), ValidationReport.BuildRouteCsv(routes));

                int unexplained = reasons.Where(r => r.Key.StartsWith("UNEXPLAINED", StringComparison.Ordinal)).Sum(r => r.Value);
                var report = new
                {
                    input,
                    scope = "reviewed targets with a family and the keyword '" + ScopeKeyword + "' or a tissue containing '" + ScopeTissue + "'",
                    program = new { program.Reviewed, program.Right, program.Wrong, program.NoNeighbour, confusion = program.Confusion },
                    programWithPanel = new { withPanel.Reviewed, withPanel.Right, withPanel.Wrong, withPanel.NoNeighbour },
                    exp6 = new { reviewed = python.Count, right = pyCounts.GetValueOrDefault("Right"), wrong = pyCounts.GetValueOrDefault("Wrong"), noNeighbour = pyCounts.GetValueOrDefault("NoNeighbour") },
                    differences = reasons,
                    routeFamily = RouteFamily, keywords = Keywords, exclude = Exclude, routes,
                };
                string text = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(outDir, "e9-report.json"), text);
                Console.WriteLine(text);
                check("every difference from exp6's 977 / 15 / 100 is explained", unexplained == 0, unexplained + " unexplained");
            }
            catch (Exception ex) { check("validation real-data check completed", false, ex.GetBaseException().ToString()); }
        }

        private static string Explain((string Outcome, string Label, string Neighbour, string NeighbourLabel, double Id)? py,
            LeaveOneOutRow? me, List<FastaItemExtended> targets)
        {
            if (py == null) return "UNEXPLAINED only in the program's set";
            if (me == null) return "UNEXPLAINED only in exp6's set";
            var p = py.Value;
            bool multi = me.Families.Contains("; ", StringComparison.Ordinal) || me.NeighbourFamilies.Contains("; ", StringComparison.Ordinal);
            if (p.Outcome == "NoNeighbour" && me.Outcome != LeaveOneOutOutcome.NoNeighbour)
                return "the program finds a neighbour at 60% or more that exp6's indexed usearch_global never aligned";
            if (me.Outcome == LeaveOneOutOutcome.NoNeighbour) return "UNEXPLAINED exp6 has a neighbour the program lacks";
            bool sameNeighbour = NearestReviewed.EntryAccession(p.Neighbour) == me.NeighbourAccession;
            if (sameNeighbour && multi)
                return "same neighbour; a protein in several families: exp6 compared first families, the program compares every membership";
            if (sameNeighbour) return "UNEXPLAINED same neighbour, other outcome";
            if (Math.Abs(p.Id - (me.IdentityPercent ?? 0)) < 1e-9)
                return multi ? "tie at the same identity, a protein in several families on one side" : "tie at the same identity broken toward another family";
            if ((me.IdentityPercent ?? 0) > p.Id) return "the program finds a closer neighbour that exp6's indexed usearch_global never aligned";
            return "UNEXPLAINED exp6 closer";
        }

        private static Dictionary<string, (bool, bool, string)> ReadFrozen(string path)
        {
            using var reader = new TextFieldParser(path);
            reader.SetDelimiters("\t");
            reader.HasFieldsEnclosedInQuotes = true;
            reader.TrimWhiteSpace = false;
            string[] header = reader.ReadFields() ?? throw new InvalidDataException("No header.");
            int Col(string name) => Array.IndexOf(header, name);
            int entry = Col("Entry"), reviewed = Col("Reviewed"), families = Col("Protein families"), keywords = Col("Keywords"), tissue = Col("Tissue specificity");
            var rows = new Dictionary<string, (bool, bool, string)>(StringComparer.Ordinal);
            while (!reader.EndOfData)
            {
                string[] r = reader.ReadFields()!;
                string first = r[families].Split(',')[0].Split(';')[0].Trim();
                rows[r[entry]] = (r[reviewed] == "reviewed",
                    r[keywords].Contains(ScopeKeyword, StringComparison.Ordinal) || r[tissue].Contains(ScopeTissue, StringComparison.OrdinalIgnoreCase),
                    first);
            }
            return rows;
        }

        private static string PythonAccession(string s)
        {
            if (s.StartsWith("contaminant_", StringComparison.Ordinal)) return s.Substring("contaminant_".Length).Split('|')[0];
            string[] p = s.Split('|');
            return p.Length >= 2 ? p[1] : s;
        }

        private static string Field(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
