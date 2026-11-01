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
                string svmpClasses = Path.Combine(Path.GetDirectoryName(classes)!, "classes.tsv");
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
                var verdicts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                Func<NearestNeighbour, ProteinEvidence?> referenceEvidence = NearestReviewed.ReferenceEvidence(file);
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
                    ProteinEvidence? nbEvidence = nb == null ? null : referenceEvidence(nb);
                    bool? nbInScope = nbEvidence == null ? null : EvidenceClassifier.IsInScope(nbEvidence, scope);
                    string verdict = Verdict(p, py.Label, item, kind);
                    verdicts[verdict] = verdicts.TryGetValue(verdict, out int v) ? v + 1 : 1;
                    rows.Add(new[]
                    {
                        verdict,
                        p.Identifier, kind, py.Label ?? "", py.Why ?? "", p.Family, p.Tier.ToString(), p.SuggestedSelected ? "yes" : "no",
                        reviewed ? "yes" : "no", p.Reason, string.Join(" | ", p.Flags.Select(f => f.Code + ": " + f.Message)),
                        nb == null ? "" : nb.Accession + " " + nb.IdentityPercent.ToString("0.0", CultureInfo.InvariantCulture) + "% " + nb.Family
                            + (nb.TiedFamilies.Count > 0 ? " (tied: " + string.Join(", ", nb.TiedFamilies) + ")" : ""),
                        nbInScope == null ? "" : nbInScope.Value ? "yes" : "no",
                        leftOut.GetValueOrDefault(p.Identifier, ""), (item.Description ?? "").Split(" OS=")[0],
                        (item.Sequence ?? "").Length.ToString(CultureInfo.InvariantCulture),
                    });
                }
                var csv = new StringBuilder("Verdict,Locus,Kind,PythonClass,PythonWhy,ProgramFamily,ProgramTier,ProgramTicked,Reviewed,ProgramReason,ProgramFlags,Neighbour,NeighbourInScope,LeftOutUnder,Name,Length\r\n");
                foreach (string[] r in rows) csv.Append(string.Join(",", r.Select(Field))).Append("\r\n");
                File.WriteAllText(Path.Combine(outDir, "e14-disagreements.csv"), csv.ToString());
                // The 256 reviewed SVMP and disintegrin entries of classes.tsv, with E6's default split emulated:
                // a member of the SVMP family whose only domain is the disintegrin domain is offered apart.
                var byId = items.ToDictionary(i => i.SequenceIdentifier, StringComparer.Ordinal);
                var rowByLocus = rows.ToDictionary(r => r[1], StringComparer.Ordinal);
                var reviewedVenom = File.ReadLines(svmpClasses).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t'))
                    .Where(c => c[6] == "yes").Select(c => (Locus: c[0], Class: c[1])).ToList();
                var venomOutcome = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var (locus, pyClass) in reviewedVenom)
                {
                    EvidenceProteinProposal p = proposal.Proteins.Single(x => x.Identifier == locus);
                    string family = p.Family.Length > 0 && p.SuggestedSelected ? p.Family : "";
                    var domains = byId[locus].Evidence?.Pfam.Select(d => d.Name).Distinct().ToList() ?? new List<string>();
                    string programClass = string.Equals(family, PythonLabel["Disintegrin"], StringComparison.OrdinalIgnoreCase)
                            || string.Equals(family, PythonLabel["SVMP"], StringComparison.OrdinalIgnoreCase) && domains.Count == 1 && domains[0] == "Disintegrin"
                        ? "Disintegrin" : string.Equals(family, PythonLabel["SVMP"], StringComparison.OrdinalIgnoreCase) ? "SVMP" : family.Length == 0 ? "(none)" : family;
                    string outcome = programClass == pyClass ? "consistent"
                        : "differs, verdict " + (rowByLocus.TryGetValue(locus, out string[]? row) ? row[0] : "MISSING");
                    venomOutcome[pyClass + ": " + outcome] = venomOutcome.TryGetValue(pyClass + ": " + outcome, out int k) ? k + 1 : 1;
                }

                string reportText = JsonSerializer.Serialize(new
                {
                    input, scope, disagreements = rows.Count, reviewedWithPythonClass = reviewedPython, reviewedAgree,
                    reviewedVenomEntries = reviewedVenom.Count, reviewedVenomWithE6DefaultSplit = venomOutcome, verdicts,
                    byKind = tally.OrderByDescending(t => t.Value).ThenBy(t => t.Key, StringComparer.Ordinal).ToDictionary(t => t.Key, t => t.Value),
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(outDir, "e14-disagreements.json"), reportText);
                Console.WriteLine(reportText);
                check("every disagreement has a verdict", !verdicts.ContainsKey(Unjudged), verdicts.GetValueOrDefault(Unjudged) + " unjudged");
                check("every reviewed venom entry is consistent or carries a verdict",
                    venomOutcome.Keys.All(k => !k.EndsWith("MISSING", StringComparison.Ordinal)), string.Join("; ", venomOutcome.Select(o => o.Key + " " + o.Value)));
            }
            catch (Exception ex) { check("science gate list completed", false, ex.GetBaseException().ToString()); }
        }

        private const string Unjudged = "UNJUDGED";

        /// <summary>The verdict a disagreement falls under; the verdicts are written in docs/SCIENCE-GATE.md.</summary>
        private static string Verdict(EvidenceProteinProposal p, string? pythonClass, FastaItemExtended item, string kind)
        {
            string flags = string.Join(" ", p.Flags.Select(f => f.Code));
            var domains = item.Evidence?.Pfam.Select(d => d.Name).Distinct().ToList() ?? new List<string>();
            bool programOnly = pythonClass == null;
            if (programOnly && item.SequenceIdentifier.StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.Ordinal)) return "V07";
            if (flags.Contains("multiple_source_families", StringComparison.Ordinal)) return "V03";
            if (pythonClass == "Disintegrin" && string.Equals(p.Family, PythonLabel["SVMP"], StringComparison.OrdinalIgnoreCase))
                return domains.Count == 1 && domains[0] == "Disintegrin" ? "V01" : domains.Count == 0 ? "V02" : Unjudged;
            if (pythonClass == "Disintegrin" && p.Family.Length == 0 && flags.Contains("neighbour_refused", StringComparison.Ordinal)) return "V02";
            if (p.Family.Length > 0 && !p.SuggestedSelected && flags.Contains("coherence.", StringComparison.Ordinal)) return "V04";
            if (p.Family.Length == 0 && flags.Contains("domain_compatibility", StringComparison.Ordinal)) return "V05";
            if (pythonClass == "SVMP" && p.Family.Length == 0 && flags.Length == 0) return "V06";
            if (programOnly && p.Tier == EvidenceTier.UnannotatedNeighbour) return "V08";
            if (programOnly && p.Tier == EvidenceTier.LearnedMarker) return "V09";
            if (pythonClass == "SVMP" && string.Equals(p.Family, PythonLabel["Disintegrin"], StringComparison.OrdinalIgnoreCase)) return "V10";
            return Unjudged;
        }

        private static string Field(string value)
        {
            string text = value ?? string.Empty;
            if (text.Length > 0 && "=+-@".IndexOf(text[0]) >= 0) text = "'" + text;
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }
    }
}
