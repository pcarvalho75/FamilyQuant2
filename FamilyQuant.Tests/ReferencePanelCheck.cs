using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    // A checker only: it fetches the panel live, strips the accessions from Ana's file and scores a
    // leave-one-out on the reviewed proteins in scope. What it writes is a measurement, never a curation.
    internal static class ReferencePanelCheck
    {
        internal const string Argument = "--reference-panel-check";
        private const string ScopeKeyword = "Toxin";
        private const string ScopeTissue = "venom gland";
        private const double LeaveOneOutIdentity = 60.0;

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) =>
                    args.Length > flag + offset && args[flag + offset].Length > 0 ? args[flag + offset] : fallback;
                string input = At(1, "D:/FamilyQuant-data/out-evidence/ana-evidence.fcf");
                string outDir = At(2, "D:/FamilyQuant-data/out-opus");
                Directory.CreateDirectory(outDir);

                Func<ProteinEvidence, bool> scope = e =>
                    e.Keywords.Contains(ScopeKeyword, StringComparer.Ordinal)
                    || e.TissueSpecificity.Any(t => t.Contains(ScopeTissue, StringComparison.OrdinalIgnoreCase));
                FamilyClusterFile file = FamilyClusterFile.Load(input);
                List<FastaItemExtended> targets = NearestReviewed.Targets(file);
                List<FastaItemExtended> reviewed = targets
                    .Where(t => t.Evidence?.Reviewed == true && scope(t.Evidence) && t.Evidence.FamilyAnnotation.Length > 0)
                    .ToList();
                Console.WriteLine($"targets {targets.Count}, reviewed in scope with a family line {reviewed.Count}");

                var watch = System.Diagnostics.Stopwatch.StartNew();
                List<string> families = ReferencePanelFetcher.FamiliesFound(file, scope);
                ReferencePanel panel = ReferencePanelFetcher.FetchAndStoreAsync(file, scope,
                    progress: new Progress<FetchProgress>(p => Console.WriteLine(
                        $"  panel {p.CompletedBatches}/{p.TotalBatches} families, {p.Found} entries kept of {p.Asked}")))
                    .GetAwaiter().GetResult();
                double fetchSeconds = watch.Elapsed.TotalSeconds;
                check("the panel records the UniProt release it came from", panel.Release.Length > 0, panel.Release);
                string withPanel = Path.Combine(outDir, "e4-ana-panel.fcf");
                file.Save(withPanel);

                // The labels the leave-one-out hides: every family a reviewed protein belongs to.
                Dictionary<string, string> label = reviewed.ToDictionary(t => t.SequenceIdentifier, t => Label(t.Evidence!));

                // 1. Accessions present, the file's own reviewed proteins only (exp6's comparison).
                List<NeighbourReference> inFile = NearestReviewed.ReviewedReferences(file,
                    e => scope(e) && e.FamilyAnnotation.Length > 0);
                var inFileScore = Score(NearestReviewed.Search(reviewed, inFile), reviewed, label,
                    NearestReviewed.ReferenceEvidence(file));

                // 2. Accessions present, the file's reviewed proteins and the panel.
                List<NeighbourReference> withOutside = new List<NeighbourReference>(inFile);
                withOutside.AddRange(ReferencePanelFetcher.References(panel, inFile.Select(r => r.Accession)));
                var withPanelScore = Score(NearestReviewed.Search(reviewed, withOutside), reviewed, label,
                    NearestReviewed.ReferenceEvidence(file));

                // 3. Accessions stripped: no header names its entry, so no protein has evidence and only the
                // panel can answer. The checker alone keeps each entry's accession, to leave it out.
                FamilyClusterFile stripped = Strip(file, out Dictionary<string, string> own, out Dictionary<string, string> strippedLabel,
                    reviewed);
                stripped.EvidenceSource = new EvidenceProvenance { ReferencePanel = panel };
                List<FastaItemExtended> strippedReviewed = NearestReviewed.Targets(stripped)
                    .Where(t => strippedLabel.ContainsKey(t.SequenceIdentifier)).ToList();
                List<NeighbourReference> panelOnly = NearestReviewed.References(stripped);
                NearestReviewed.ForgetMemo();
                NeighbourSearch strippedSearch = NearestReviewed.Search(strippedReviewed, panelOnly, ownAccessions: own);
                var strippedScore = Score(strippedSearch, strippedReviewed, strippedLabel, NearestReviewed.ReferenceEvidence(stripped));
                check("with accessions stripped no protein has evidence and every reference is from the panel",
                    stripped.MyExtendedFastaItems.All(i => i.Evidence == null) && panelOnly.All(r => r.Identifier.Length == 0)
                    && panelOnly.Count == panel.Entries.Count, panelOnly.Count + " references");

                stripped.EvidenceSource.NearestReviewed = strippedSearch;
                string strippedPath = Path.Combine(outDir, "e4-ana-stripped-panel.fcf");
                stripped.Save(strippedPath);
                FamilyClusterFile reopened = FamilyClusterFile.Load(strippedPath);
                NearestReviewed.ForgetMemo();
                long runs = NearestReviewed.SearchRunCount;
                NeighbourSearch offline = NearestReviewed.Search(
                    NearestReviewed.Targets(reopened).Where(t => strippedLabel.ContainsKey(t.SequenceIdentifier)).ToList(),
                    NearestReviewed.References(reopened), ownAccessions: own);
                check("offline rerun from the saved panel is byte-identical",
                    NearestReviewed.SearchRunCount == runs + 1
                    && JsonSerializer.Serialize(offline) == JsonSerializer.Serialize(strippedSearch), offline.Neighbours.Count + " neighbours");
                check("leave-one-out with accessions stripped still scores",
                    strippedScore.Right >= inFileScore.Right, strippedScore.Right + " vs " + inFileScore.Right + " in the file");

                var report = new
                {
                    input,
                    scope = "reviewed targets with a family line and the keyword '" + ScopeKeyword + "' or a tissue containing '" + ScopeTissue + "'",
                    reviewedInScope = reviewed.Count,
                    multiFamilyLabels = label.Values.Count(v => v.Contains("; ", StringComparison.Ordinal)),
                    leaveOneOutIdentityPercent = LeaveOneOutIdentity,
                    panel = new
                    {
                        panel.Release, panel.RetrievedAtUtc, families = panel.Families.Count, entries = panel.Entries.Count,
                        entriesAlreadyInFile = panel.Entries.Count(e => inFile.Any(r => r.Accession == e.Accession)),
                        fetchSeconds = Math.Round(fetchSeconds, 1),
                        fileBytesWithoutPanel = new FileInfo(input).Length,
                        fileBytesWithPanel = new FileInfo(withPanel).Length,
                    },
                    inFileOnly = inFileScore,
                    inFileAndPanel = withPanelScore,
                    accessionsStrippedPanelOnly = strippedScore,
                };
                string text = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(outDir, "e4-report.json"), text);
                Console.WriteLine(text);
                WriteConfusion(Path.Combine(outDir, "e4-stripped-confusion.csv"), strippedSearch, strippedReviewed, strippedLabel,
                    NearestReviewed.ReferenceEvidence(stripped));
            }
            catch (Exception ex) { check("reference panel real-data check completed", false, ex.GetBaseException().ToString()); }
        }

        /// <summary>The families a protein belongs to, case folded and sorted, as one label.</summary>
        private static string Label(ProteinEvidence evidence) => string.Join("; ",
            ReferencePanelFetcher.FamiliesOf(evidence).Select(f => f.ToLowerInvariant()).Distinct().OrderBy(f => f, StringComparer.Ordinal));

        private sealed class LeaveOneOut
        {
            public int Right { get; set; }
            public int Wrong { get; set; }
            public int NoNeighbour { get; set; }
            public int NeighbourFromPanel { get; set; }
        }

        private static LeaveOneOut Score(NeighbourSearch search, List<FastaItemExtended> queries,
            Dictionary<string, string> label, Func<NearestNeighbour, ProteinEvidence?> evidenceOf)
        {
            var score = new LeaveOneOut();
            Dictionary<string, NearestNeighbour> by = search.ByQuery();
            foreach (FastaItemExtended q in queries)
            {
                if (!by.TryGetValue(q.SequenceIdentifier, out NearestNeighbour? n) || n.IdentityPercent < LeaveOneOutIdentity)
                {
                    score.NoNeighbour++;
                    continue;
                }
                if (n.Reference.Length == 0) score.NeighbourFromPanel++;
                ProteinEvidence? theirs = evidenceOf(n);
                if (theirs != null && Label(theirs) == label[q.SequenceIdentifier]) score.Right++;
                else score.Wrong++;
            }
            return score;
        }

        private static FamilyClusterFile Strip(FamilyClusterFile file, out Dictionary<string, string> own,
            out Dictionary<string, string> label, List<FastaItemExtended> reviewed)
        {
            var reviewedIds = new HashSet<string>(reviewed.Select(r => r.SequenceIdentifier), StringComparer.Ordinal);
            own = new Dictionary<string, string>(StringComparer.Ordinal);
            label = new Dictionary<string, string>(StringComparer.Ordinal);
            var stripped = new FamilyClusterFile();
            int n = 0;
            foreach (FastaItemExtended item in file.MyExtendedFastaItems)
            {
                bool decoy = NearestReviewed.IsDecoy(item);
                string id = (decoy ? SequenceLabels.DecoyPrefix : "") + "entry_" + (n++).ToString(CultureInfo.InvariantCulture);
                if (!decoy && reviewedIds.Contains(item.SequenceIdentifier))
                {
                    own[id] = NearestReviewed.EntryAccession(item.SequenceIdentifier);
                    label[id] = Label(item.Evidence!);
                }
                stripped.MyExtendedFastaItems.Add(new FastaItemExtended
                {
                    SequenceIdentifier = id, Description = "", Sequence = item.Sequence,
                    UserID = item.UserID, USearchID = item.USearchID,
                });
            }
            return stripped;
        }

        private static void WriteConfusion(string path, NeighbourSearch search, List<FastaItemExtended> queries,
            Dictionary<string, string> label, Func<NearestNeighbour, ProteinEvidence?> evidenceOf)
        {
            Dictionary<string, NearestNeighbour> by = search.ByQuery();
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (FastaItemExtended q in queries)
            {
                string mine = label[q.SequenceIdentifier];
                string got = by.TryGetValue(q.SequenceIdentifier, out NearestNeighbour? n) && n.IdentityPercent >= LeaveOneOutIdentity
                    ? (evidenceOf(n) is ProteinEvidence e ? Label(e) : "(no evidence)") : "(no neighbour)";
                if (got == mine) continue;
                string key = mine + "\t" + got;
                counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
            }
            File.WriteAllLines(path, new[] { "Count,TrueFamilies,NeighbourFamilies" }.Concat(counts
                .OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal)
                .Select(k => k.Value.ToString(CultureInfo.InvariantCulture) + "," + Field(k.Key.Split('\t')[0]) + "," + Field(k.Key.Split('\t')[1]))));
        }

        private static string Field(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
