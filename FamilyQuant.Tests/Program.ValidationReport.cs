using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void ValidationReportTests()
        {
            ProteinEvidence E(string accession, bool reviewed, params string[] families) => new ProteinEvidence
            {
                Accession = accession, Reviewed = reviewed, Family = families.Length == 1 ? families[0] : "",
                FamilyMemberships = families.Length > 1 ? families.Select(f => new EvidenceFamilyMembership { Family = f }).ToList() : null,
            };
            Check("a protein's label is every family it belongs to, case folded and sorted",
                ValidationReport.Label(E("P1", true, "Zeta family", "alpha Family")) == "alpha family; zeta family"
                && ValidationReport.Label(E("P1", true, "Beta family")) == "beta family" && ValidationReport.Label(null) == "");

            FastaItemExtended R(string id, ProteinEvidence e) => new FastaItemExtended { SequenceIdentifier = id, Sequence = "MA", Evidence = e };
            var proteins = new List<FastaItemExtended>
            {
                R("sp|P50001|A", E("P50001", true, "Alpha family")),
                R("sp|P50002|B", E("P50002", true, "Alpha family")),
                R("sp|P50003|C", E("P50003", true, "Beta family")),
                R("sp|P50004|D", E("P50004", true, "Alpha family")),
                R("sp|P50005|E", E("P50005", true, "Gamma family")),
            };
            var evidence = proteins.ToDictionary(p => NearestReviewed.EntryAccession(p.SequenceIdentifier), p => p.Evidence!);
            NearestNeighbour N(string query, string accession, double identity) =>
                new NearestNeighbour { Query = query, Accession = accession, IdentityPercent = identity, AlignmentLength = 10, QueryLength = 10 };
            var search = new NeighbourSearch
            {
                Neighbours = new List<NearestNeighbour>
                {
                    N("sp|P50001|A", "P50002", 90), N("sp|P50002|B", "P50001", 90), N("sp|P50003|C", "P50001", 75),
                    N("sp|P50004|D", "P50001", 59.9), N("sp|P50005|E", "P50001", 60),
                },
            };
            LeaveOneOutResult result = ValidationReport.Score(search, proteins, n => evidence.GetValueOrDefault(n.Accession));
            Check("right when the neighbour has the same families, wrong when not, none under the identity line",
                result.Right == 2 && result.Wrong == 2 && result.NoNeighbour == 1 && result.Reviewed == 5,
                $"{result.Right}/{result.Wrong}/{result.NoNeighbour}");
            Check("exactly 60% counts as a neighbour, 59.9% does not",
                result.Rows[4].Outcome == LeaveOneOutOutcome.Wrong && result.Rows[3].Outcome == LeaveOneOutOutcome.NoNeighbour);
            Check("the confusion list gives each wrong call by its two labels, most frequent first",
                result.Confusion.Select(c => c.Families + ">" + c.NeighbourFamilies + ":" + c.Count)
                    .SequenceEqual(new[] { "beta family>alpha family:1", "gamma family>alpha family:1" }),
                string.Join(" | ", result.Confusion.Select(c => c.Families + ">" + c.NeighbourFamilies + ":" + c.Count)));
            string csv = ValidationReport.BuildLeaveOneOutCsv(result);
            string[] lines = csv.Split("\r\n");
            Check("the leave-one-out CSV has a header, one row per protein and invariant numbers",
                lines[0] == "Identifier,Accession,Families,NeighbourAccession,NeighbourFamilies,IdentityPercent,Outcome"
                && lines[1] == "sp|P50001|A,P50001,alpha family,P50002,alpha family,90.0,Right"
                && lines[4] == "sp|P50004|D,P50004,alpha family,,,,NoNeighbour" && lines.Length == 7, lines.Length.ToString());

            var targets = new List<FastaItemExtended>
            {
                R("sp|P60001|A", E("P60001", true, "Alpha family")),
                R("sp|P60002|B", E("P60002", true, "alpha FAMILY", "Beta family")),
                R("sp|P60003|C", E("P60003", true, "Beta family")),
                R("sp|P60004|D", E("P60004", true, "Alpha family")),
                R("tr|A0A601|E", E("A0A601", false, "Alpha family")),
                R("Reverse_sp|P60001|A", E("", false)),
            };
            RouteScore score = ValidationReport.ScoreRoute("words", "Alpha family",
                targets.Where(t => t.SequenceIdentifier is "sp|P60001|A" or "sp|P60002|B" or "sp|P60003|C" or "tr|A0A601|E" or "Reverse_sp|P60001|A"), targets);
            Check("a route is scored on reviewed members taken, others taken and members missed; decoys never count",
                score.Proteins == 4 && score.ReviewedMembersTaken == 2 && score.ReviewedOthersTaken == 1
                && score.ReviewedMembersMissed == 1 && score.UnreviewedTaken == 1 && score.OthersByFamily.Single().Families == "beta family",
                $"{score.Proteins} {score.ReviewedMembersTaken} {score.ReviewedOthersTaken} {score.ReviewedMembersMissed} {score.UnreviewedTaken}");
            Check("the route CSV carries every count",
                ValidationReport.BuildRouteCsv(new[] { score }) ==
                "Route,Proteins,ReviewedMembersTaken,ReviewedOthersTaken,ReviewedMembersMissed,UnreviewedTaken\r\nwords,4,2,1,1,1\r\n");

            var items = new List<FastaItemExtended>
            {
                new() { SequenceIdentifier = "a1", Description = "Snake venom metalloproteinase alpha OS=Testus", UserID = 1 },
                new() { SequenceIdentifier = "a2", Description = "Hypothetical protein OS=Testus", UserID = 1 },
                new() { SequenceIdentifier = "b1", Description = "Metalloproteinase inhibitor OS=Testus", UserID = 2 },
                new() { SequenceIdentifier = "c1", Description = "Unrelated enzyme OS=Testus", UserID = 3 },
            };
            Check("the words route takes whole families as the Super Cluster preview ticks them",
                string.Join(",", ValidationReport.WordsRoute(items, "metalloproteinase", "").Select(i => i.SequenceIdentifier)) == "a1,a2,b1");
            Check("and an exclude word keeps its carrier out, as the preview does",
                string.Join(",", ValidationReport.WordsRoute(items, "metalloproteinase", "inhibitor").Select(i => i.SequenceIdentifier)) == "a1,a2");
            Check("the recluster route runs on copies and leaves the file's families as they were",
                ValidationReport.ReclusterRoute(items, 0.1, "metalloproteinase", "").Count >= 3 && items.Select(i => i.UserID).SequenceEqual(new[] { 1, 1, 2, 3 }));
            Check("a CSV field that starts like a formula is written as text",
                ValidationReport.BuildRouteCsv(new[] { new RouteScore { Route = "=cmd" } }).Split("\r\n")[1].StartsWith("'=cmd,", StringComparison.Ordinal));
        }
    }
}
