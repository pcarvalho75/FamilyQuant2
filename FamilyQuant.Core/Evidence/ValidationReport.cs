using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.Evidence
{
    public enum LeaveOneOutOutcome
    {
        Right,
        Wrong,
        NoNeighbour,
    }

    /// <summary>One reviewed protein with its own families hidden, called from its nearest other reviewed protein.</summary>
    public sealed class LeaveOneOutRow
    {
        public string Identifier { get; init; } = string.Empty;
        public string Accession { get; init; } = string.Empty;

        /// <summary>The protein's families, case folded, sorted, joined with "; ": the label it must get back.</summary>
        public string Families { get; init; } = string.Empty;
        public string NeighbourAccession { get; init; } = string.Empty;
        public string NeighbourFamilies { get; init; } = string.Empty;
        public double? IdentityPercent { get; init; }
        public LeaveOneOutOutcome Outcome { get; init; }
    }

    public sealed class ConfusionRow
    {
        public string Families { get; init; } = string.Empty;
        public string NeighbourFamilies { get; init; } = string.Empty;
        public int Count { get; init; }
    }

    public sealed class LeaveOneOutResult
    {
        public double MinIdentityPercent { get; init; }
        public int Reviewed { get; init; }
        public int Right { get; init; }
        public int Wrong { get; init; }
        public int NoNeighbour { get; init; }

        /// <summary>The wrong calls by (families, neighbour families), most frequent first.</summary>
        public List<ConfusionRow> Confusion { get; init; } = new List<ConfusionRow>();
        public List<LeaveOneOutRow> Rows { get; init; } = new List<LeaveOneOutRow>();
    }

    /// <summary>How one way of forming a family catches the reviewed proteins of that family.</summary>
    public sealed class RouteScore
    {
        public string Route { get; init; } = string.Empty;
        public int Proteins { get; init; }

        /// <summary>Reviewed members of the family the route took (right), reviewed proteins of other families
        /// it took (wrong), and reviewed members it left out (missed).</summary>
        public int ReviewedMembersTaken { get; init; }
        public int ReviewedOthersTaken { get; init; }
        public int ReviewedMembersMissed { get; init; }
        public int UnreviewedTaken { get; init; }

        /// <summary>The reviewed proteins of other families taken, by family, most frequent first.</summary>
        public List<ConfusionRow> OthersByFamily { get; init; } = new List<ConfusionRow>();
    }

    /// <summary>
    /// The validation report: a leave-one-out on reviewed proteins (each hides its own families and is called
    /// from its nearest other reviewed protein, never itself) and a comparison of the ways a family can be
    /// formed, scored against the families UniProt's curators gave the reviewed proteins.
    /// </summary>
    public static class ValidationReport
    {
        public const double DefaultLeaveOneOutIdentityPercent = 60.0;

        /// <summary>A protein's families as one label: every membership, case folded, sorted.</summary>
        public static string Label(ProteinEvidence? evidence) => evidence == null ? string.Empty : string.Join("; ",
            ReferencePanelFetcher.FamiliesOf(evidence).Select(f => f.ToLowerInvariant()).Distinct(StringComparer.Ordinal)
                .OrderBy(f => f, StringComparer.Ordinal));

        /// <summary>The reviewed targets with a family, in a scope the caller chose: the proteins the
        /// leave-one-out hides and calls back.</summary>
        public static List<FastaItemExtended> ReviewedWithFamily(FamilyClusterFile file, Func<ProteinEvidence, bool>? inScope = null) =>
            NearestReviewed.Targets(file).Where(t => t.Evidence?.Reviewed == true && Label(t.Evidence).Length > 0
                && (inScope == null || inScope(t.Evidence))).ToList();

        /// <summary>
        /// Every reviewed protein in scope searched against the others (and the stored panel when
        /// <paramref name="withPanel"/>), never itself; right when the nearest one at or above
        /// <paramref name="minIdentityPercent"/> has exactly the same families.
        /// </summary>
        public static LeaveOneOutResult LeaveOneOut(FamilyClusterFile file, Func<ProteinEvidence, bool>? inScope = null,
            double minIdentityPercent = DefaultLeaveOneOutIdentityPercent, bool withPanel = false)
        {
            List<FastaItemExtended> reviewed = ReviewedWithFamily(file, inScope);
            var references = reviewed.Select(t => new NeighbourReference
            {
                Accession = NearestReviewed.EntryAccession(t.SequenceIdentifier),
                Identifier = t.SequenceIdentifier,
                Family = t.Evidence!.Family ?? string.Empty,
                Sequence = t.Sequence,
                Evidence = t.Evidence,
            }).Where(r => !string.IsNullOrEmpty(r.Sequence)).ToList();
            if (withPanel)
                references.AddRange(ReferencePanelFetcher.References(file.EvidenceSource?.ReferencePanel, references.Select(r => r.Accession)));
            return Score(NearestReviewed.Search(reviewed, references), reviewed, NearestReviewed.ReferenceEvidence(file), minIdentityPercent);
        }

        /// <summary>Grades a finished search of reviewed proteins; split from the search so a caller with
        /// its own references (headers stripped, a panel only) grades the same way.</summary>
        public static LeaveOneOutResult Score(NeighbourSearch search, IReadOnlyList<FastaItemExtended> reviewed,
            Func<NearestNeighbour, ProteinEvidence?> evidenceOf, double minIdentityPercent = DefaultLeaveOneOutIdentityPercent,
            IReadOnlyDictionary<string, string>? labels = null)
        {
            Dictionary<string, NearestNeighbour> by = search.ByQuery();
            var rows = new List<LeaveOneOutRow>();
            foreach (FastaItemExtended protein in reviewed)
            {
                string own = labels != null && labels.TryGetValue(protein.SequenceIdentifier, out string? given) ? given : Label(protein.Evidence);
                by.TryGetValue(protein.SequenceIdentifier, out NearestNeighbour? neighbour);
                bool close = neighbour != null && neighbour.IdentityPercent + 1e-9 >= minIdentityPercent;
                string theirs = close ? Label(evidenceOf(neighbour!)) : string.Empty;
                rows.Add(new LeaveOneOutRow
                {
                    Identifier = protein.SequenceIdentifier,
                    Accession = NearestReviewed.EntryAccession(protein.SequenceIdentifier),
                    Families = own,
                    NeighbourAccession = close ? neighbour!.Accession : string.Empty,
                    NeighbourFamilies = theirs,
                    IdentityPercent = close ? neighbour!.IdentityPercent : null,
                    Outcome = !close ? LeaveOneOutOutcome.NoNeighbour
                        : string.Equals(theirs, own, StringComparison.Ordinal) ? LeaveOneOutOutcome.Right : LeaveOneOutOutcome.Wrong,
                });
            }
            return new LeaveOneOutResult
            {
                MinIdentityPercent = minIdentityPercent,
                Reviewed = rows.Count,
                Right = rows.Count(r => r.Outcome == LeaveOneOutOutcome.Right),
                Wrong = rows.Count(r => r.Outcome == LeaveOneOutOutcome.Wrong),
                NoNeighbour = rows.Count(r => r.Outcome == LeaveOneOutOutcome.NoNeighbour),
                Confusion = Confuse(rows.Where(r => r.Outcome == LeaveOneOutOutcome.Wrong).Select(r => (r.Families, r.NeighbourFamilies))),
                Rows = rows,
            };
        }

        /// <summary>Scores the proteins a route took against one family: a reviewed protein is a member when
        /// the family is among its memberships (any case).</summary>
        public static RouteScore ScoreRoute(string route, string family, IEnumerable<FastaItemExtended> taken,
            IEnumerable<FastaItemExtended> targets)
        {
            var took = new HashSet<string>(taken.Select(t => t.SequenceIdentifier), StringComparer.Ordinal);
            bool Member(FastaItemExtended t) => ReferencePanelFetcher.FamiliesOf(t.Evidence!).Contains(family, StringComparer.OrdinalIgnoreCase);
            List<FastaItemExtended> all = targets.Where(t => !NearestReviewed.IsDecoy(t)).ToList();
            List<FastaItemExtended> reviewed = all.Where(t => t.Evidence?.Reviewed == true).ToList();
            return new RouteScore
            {
                Route = route,
                Proteins = all.Count(t => took.Contains(t.SequenceIdentifier)),
                ReviewedMembersTaken = reviewed.Count(t => Member(t) && took.Contains(t.SequenceIdentifier)),
                ReviewedOthersTaken = reviewed.Count(t => !Member(t) && took.Contains(t.SequenceIdentifier)),
                ReviewedMembersMissed = reviewed.Count(t => Member(t) && !took.Contains(t.SequenceIdentifier)),
                UnreviewedTaken = all.Count(t => t.Evidence?.Reviewed != true && took.Contains(t.SequenceIdentifier)),
                OthersByFamily = Confuse(reviewed.Where(t => !Member(t) && took.Contains(t.SequenceIdentifier))
                    .Select(t => (Label(t.Evidence), string.Empty))),
            };
        }

        /// <summary>What the Super Cluster preview would join for these words, every protein it ticks,
        /// on the file's current families; the same code the window runs.</summary>
        public static List<FastaItemExtended> WordsRoute(IReadOnlyList<FastaItemExtended> items, string keywords,
            string excludeWords, IEnumerable<int>? locked = null)
        {
            var preview = new SuperClusterPreview(items, null, keywords, excludeWords, KeywordMatch.Any, null, locked);
            var ticked = new HashSet<string>(preview.Proteins.Where(p => p.Join).Select(p => p.Locus), StringComparer.Ordinal);
            return items.Where(i => ticked.Contains(i.SequenceIdentifier)).ToList();
        }

        /// <summary>The words route after Recluster by Words at <paramref name="threshold"/>, run on copies so
        /// the file's families are untouched.</summary>
        public static List<FastaItemExtended> ReclusterRoute(IReadOnlyList<FastaItemExtended> items, double threshold,
            string keywords, string excludeWords, ISet<int>? locked = null)
        {
            List<FastaItemExtended> copies = items.Select(i => new FastaItemExtended
            {
                SequenceIdentifier = i.SequenceIdentifier, Description = i.Description, Sequence = i.Sequence,
                UserID = i.UserID, USearchID = i.USearchID, Domains = i.Domains, Evidence = i.Evidence,
            }).ToList();
            List<List<int>> groups = new WordCluster(copies, new List<string>()).GroupByWords(threshold, locked ?? new HashSet<int>());
            Dictionary<int, int> mapping = WordCluster.BuildStableMapping(groups);
            foreach (FastaItemExtended copy in copies)
                if (mapping.TryGetValue(copy.UserID, out int merged)) copy.UserID = merged;
            var taken = new HashSet<string>(WordsRoute(copies, keywords, excludeWords, locked).Select(c => c.SequenceIdentifier), StringComparer.Ordinal);
            return items.Where(i => taken.Contains(i.SequenceIdentifier)).ToList();
        }

        public static string BuildLeaveOneOutCsv(LeaveOneOutResult result)
        {
            var csv = new StringBuilder();
            csv.Append("Identifier,Accession,Families,NeighbourAccession,NeighbourFamilies,IdentityPercent,Outcome\r\n");
            foreach (LeaveOneOutRow row in result.Rows)
                csv.Append(string.Join(",", new[]
                {
                    row.Identifier, row.Accession, row.Families, row.NeighbourAccession, row.NeighbourFamilies,
                    row.IdentityPercent?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty, row.Outcome.ToString(),
                }.Select(Field))).Append("\r\n");
            return csv.ToString();
        }

        public static string BuildRouteCsv(IEnumerable<RouteScore> scores)
        {
            var csv = new StringBuilder("Route,Proteins,ReviewedMembersTaken,ReviewedOthersTaken,ReviewedMembersMissed,UnreviewedTaken\r\n");
            foreach (RouteScore s in scores)
                csv.Append(string.Join(",", new[]
                {
                    Field(s.Route), Number(s.Proteins), Number(s.ReviewedMembersTaken), Number(s.ReviewedOthersTaken),
                    Number(s.ReviewedMembersMissed), Number(s.UnreviewedTaken),
                })).Append("\r\n");
            return csv.ToString();
        }

        private static List<ConfusionRow> Confuse(IEnumerable<(string Families, string NeighbourFamilies)> pairs) =>
            pairs.GroupBy(p => p).Select(g => new ConfusionRow { Families = g.Key.Families, NeighbourFamilies = g.Key.NeighbourFamilies, Count = g.Count() })
                .OrderByDescending(r => r.Count).ThenBy(r => r.Families, StringComparer.Ordinal)
                .ThenBy(r => r.NeighbourFamilies, StringComparer.Ordinal).ToList();

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>RFC 4180, and a leading formula character is quoted out so a spreadsheet shows text.</summary>
        private static string Field(string? value)
        {
            string text = value ?? string.Empty;
            if (text.Length > 0 && "=+-@".IndexOf(text[0]) >= 0) text = "'" + text;
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }
    }
}
