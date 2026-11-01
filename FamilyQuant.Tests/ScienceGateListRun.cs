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
    // Support for E14, a checker only: every protein where the program's proposal (E5, with E7's flags)
    // and the frozen Python classes disagree, with the program's own evidence beside it. exp8's label table
    // is copied here as the answer key that turns Python's labels back into UniProt families.
    internal static class ScienceGateListRun
    {
        internal const string Argument = "--science-gate-list";
        private static readonly Dictionary<string, string> PythonLabel = new(StringComparer.Ordinal)
        {
            ["SVMP"] = "venom metalloproteinase (M12B) family", ["Disintegrin"] = "disintegrin family",
            ["PLA2"] = "phospholipase A2 family", ["SVSP"] = "peptidase S1 family", ["Snaclec (CTL)"] = "snaclec family",
            ["LAAO"] = "flavin monoamine oxidase family", ["CRISP"] = "CRISP family", ["svVEGF"] = "PDGF/VEGF growth factor family",
            ["Natriuretic peptide"] = "natriuretic peptide family", ["BPP"] = "bradykinin-potentiating peptide family",
            ["Vespryn"] = "ohanin/vespryn family", ["Kunitz"] = "venom Kunitz-type family", ["Ficolin (ryncolin)"] = "ficolin lectin family",
            ["NGF"] = "NGF-beta family", ["Crotamine/myotoxin"] = "crotamine-myotoxin family", ["Venom lectin"] = "true venom lectin family",
            ["Hyaluronidase"] = "glycosyl hydrolase 56 family", ["PDE"] = "nucleotide pyrophosphatase/phosphodiesterase family",
            ["Glutaminyl cyclase"] = "glutaminyl-peptide cyclotransferase family", ["5'-nucleotidase"] = "5'-nucleotidase family",
            ["PLB"] = "phospholipase B-like family", ["3FTx"] = "three-finger toxin family", ["Cystatin"] = "cystatin family",
            ["Sarafotoxin"] = "endothelin/sarafotoxin family", ["Cathelicidin"] = "cathelicidin family", ["pHpG"] = "pHpG family",
            ["Waglerin"] = "waglerin family",
        };

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) =>
                    args.Length > flag + offset && args[flag + offset].Length > 0 ? args[flag + offset] : fallback;
                string input = At(1, "D:/FamilyQuant-data/out-opus/e4-ana-panel-neighbours.fcf");
                string classes = At(2, "D:/FamilyQuant-data/baseline/classes_all.tsv");
                string outDir = At(3, "D:/FamilyQuant-data/out-opus");
                string[] scope = At(4, "Toxin;venom gland").Split(';', StringSplitOptions.RemoveEmptyEntries);

                FamilyClusterFile file = FamilyClusterFile.Load(input);
                List<FastaItemExtended> items = file.MyExtendedFastaItems;
                NeighbourSearch search = file.EvidenceSource?.NearestReviewed ?? throw new InvalidDataException("No stored neighbour search.");
                EvidenceProposal proposal = CoherenceCheck.Flag(
                    EvidenceClassifier.Propose(items, new EvidenceClassificationOptions { ScopeTerms = scope },
                        search.ByQuery(), NearestReviewed.ReferenceEvidence(file)),
                    items, CoherenceCheck.Learn(file));
                var leftOut = LeftOutList.Build(items, proposal).GroupBy(r => r.Identifier, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => string.Join(" | ", g.Select(r => r.Family + " by " + r.MarkerName)), StringComparer.Ordinal);
                var python = File.ReadLines(classes).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t'))
                    .ToDictionary(c => c[0], c => (Label: c[1], Why: c[2]), StringComparer.Ordinal);
                var pythonFamilies = new HashSet<string>(PythonLabel.Values, StringComparer.OrdinalIgnoreCase);

                var rows = new List<string[]>();
                var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
                int reviewedPython = 0, reviewedAgree = 0;
                foreach (EvidenceProteinProposal p in proposal.Proteins)
                {
                    FastaItemExtended item = items[p.ProteinIndex];
                    python.TryGetValue(p.Identifier, out var py);
                    string theirs = py.Label == null ? "" : PythonLabel[py.Label];
                    string mine = p.Family.Length > 0 && p.SuggestedSelected ? p.Family : "";
                    bool inScopeOfComparison = theirs.Length > 0 || pythonFamilies.Contains(p.Family);
                    if (!inScopeOfComparison) continue;
                    bool reviewed = item.Evidence?.Reviewed == true;
                    if (reviewed && theirs.Length > 0)
                    {
                        reviewedPython++;
                        if (string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase)) reviewedAgree++;
                    }
                    if (string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase)) continue;
                    string kind = theirs.Length == 0 ? "program only" : mine.Length == 0
                        ? (p.Family.Length > 0 ? "Python only, the program proposes but flags it" : "Python only, the program leaves it unassigned")
                        : "both, different families";
                    string key = kind + ": " + (py.Label ?? "-") + " -> " + (p.Family.Length > 0 ? p.Family : "-");
                    tally[key] = tally.TryGetValue(key, out int n) ? n + 1 : 1;
                    NearestNeighbour? nb = p.Neighbour;
                    rows.Add(new[]
                    {
                        p.Identifier, kind, py.Label ?? "", py.Why ?? "", p.Family, p.Tier.ToString(), p.SuggestedSelected ? "yes" : "no",
                        reviewed ? "yes" : "no", p.Reason, string.Join(" | ", p.Flags.Select(f => f.Code + ": " + f.Message)),
                        nb == null ? "" : nb.Accession + " " + nb.IdentityPercent.ToString("0.0", CultureInfo.InvariantCulture) + "% " + nb.Family
                            + (nb.TiedFamilies.Count > 0 ? " (tied: " + string.Join(", ", nb.TiedFamilies) + ")" : ""),
                        leftOut.GetValueOrDefault(p.Identifier, ""), (item.Description ?? "").Split(" OS=")[0],
                        (item.Sequence ?? "").Length.ToString(CultureInfo.InvariantCulture),
                    });
                }
                var csv = new StringBuilder("Locus,Kind,PythonClass,PythonWhy,ProgramFamily,ProgramTier,ProgramTicked,Reviewed,ProgramReason,ProgramFlags,Neighbour,LeftOutUnder,Name,Length\r\n");
                foreach (string[] r in rows) csv.Append(string.Join(",", r.Select(Field))).Append("\r\n");
                File.WriteAllText(Path.Combine(outDir, "e14-disagreements.csv"), csv.ToString());
                string reportText = JsonSerializer.Serialize(new
                {
                    input, scope, disagreements = rows.Count, reviewedWithPythonClass = reviewedPython, reviewedAgree,
                    byKind = tally.OrderByDescending(t => t.Value).ThenBy(t => t.Key, StringComparer.Ordinal).ToDictionary(t => t.Key, t => t.Value),
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(outDir, "e14-disagreements.json"), reportText);
                Console.WriteLine(reportText);
                check("the disagreement list was written", rows.Count >= 0, rows.Count + " rows");
            }
            catch (Exception ex) { check("science gate list completed", false, ex.GetBaseException().ToString()); }
        }

        private static string Field(string value)
        {
            string text = value ?? string.Empty;
            if (text.Length > 0 && "=+-@".IndexOf(text[0]) >= 0) text = "'" + text;
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }
    }
}
