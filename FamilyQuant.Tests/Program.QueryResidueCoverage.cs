using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void QueryResidueCoverageTests()
    {
        var first = new NeighbourReference { Accession = "fixture_a", Identifier = "fixture_a", Family = "Fixture A", Sequence = new string('A', 100) };
        var second = new NeighbourReference { Accession = "fixture_b", Identifier = "fixture_b", Family = "Fixture B", Sequence = new string('A', 100) };
        var hits = new[] { (first, 80.0, 120, 100, 100, 69), (second, 75.0, 80, 100, 100, 80) };
        var chosen = NearestReviewed.Choose("query", "own", hits, 50, 70);
        Check("A6 query-letter coverage excludes a gapped span that consumed too few query residues", chosen?.Family == "Fixture B"
            && chosen.QueryAlignedResidues == 80 && chosen.QueryCoveragePercent == 80);
        Check("A6 coverage percentage is unknown for legacy or invalid residue counts",
            new NearestNeighbour { QueryLength = 100 }.QueryCoveragePercent == null
            && new NearestNeighbour { QueryLength = 100, QueryAlignedResidues = 101 }.QueryCoveragePercent == null);
        var protein = new FastaItemExtended { SequenceIdentifier = "query", Sequence = new string('A', 100),
            Evidence = new ProteinEvidence { Keywords = new List<string> { "selected" } } };
        var neighbour = new NearestNeighbour { Query = "query", Accession = first.Accession, Reference = first.Identifier,
            Family = first.Family, IdentityPercent = 80, AlignmentLength = 120, QueryLength = 100, ReferenceLength = 100, QueryAlignedResidues = 69 };
        var proposal = EvidenceClassifier.Propose(new[] { protein }, new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } },
            new Dictionary<string, NearestNeighbour> { ["query"] = neighbour }, _ => new ProteinEvidence { Reviewed = true, Family = first.Family });
        Check("A6 classification uses the recorded query residues rather than gap-inclusive columns", proposal.Proteins.Single().Tier == EvidenceTier.Unassigned);
        var query = new FastaItemExtended { SequenceIdentifier = "coverage_query", Sequence = "ACDEFGHIKLMNPQRSTVWY" };
        var reference = new NeighbourReference { Accession = "coverage_reference", Identifier = "coverage_reference", Family = "Fixture family", Sequence = query.Sequence };
        var search = NearestReviewed.Search(new[] { query }, new[] { reference });
        Check("A6 native search records the coverage method and aligned query residue count", search.CoverageMethod == NearestReviewed.QueryCoverageMethod
            && search.Neighbours.Single().QueryAlignedResidues == query.Sequence.Length && search.Neighbours.Single().QueryCoveragePercent == 100);
        var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(search))!.AsObject();
        legacyJson.Remove("CoverageMethod");
        foreach (var node in legacyJson["Neighbours"]!.AsArray()) node!.AsObject().Remove("QueryAlignedResidues");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<NeighbourSearch>(legacyJson.ToJsonString())!;
        Check("A6 old search facts require refresh while historical replay can validate their original inputs",
            !NearestReviewed.IsCurrent(legacy, new[] { query }, new[] { reference })
            && NearestReviewed.IsCurrent(legacy, new[] { query }, new[] { reference }, allowLegacyCoverage: true));
        Check("A6 missing legacy coverage facts do not change their serialized hash payload",
            System.Text.Json.JsonSerializer.Serialize(legacy) == legacyJson.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
    }
}
