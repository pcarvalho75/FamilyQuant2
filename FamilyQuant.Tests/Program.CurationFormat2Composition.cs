using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationFormat2CompositionCommands() =>
    [new("--test-curation-format2-composition", 453, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-curation-format2-composition", (args, _) =>
        {
            CurationFormat2CompositionTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.CurationFormat2Composition"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static readonly string[] ComposedFormat2Parts = { "feature-provenance", "recipe-copies", "panel-entry-identity" };

    /// <summary>One synthetic curation with work for every shipped format-2 part: proteins and panel entries share
    /// one feature source (feature provenance), the recipe holds byte-identical copies of the four current values
    /// (recipe copies), and every panel entry carries its accession and a derivable sequence hash (panel entry identity).</summary>
    private static FamilyClusterFile CurationFormat2CompositionFixture()
    {
        var file = CurationFormat2Fixture();
        foreach (var entry in file.EvidenceSource!.ReferencePanel!.Entries)
            entry.Evidence.Positions!.SequenceSha256 = ProteinPositionFetcher.SequenceHash(entry.Sequence);
        var targets = file.MyExtendedFastaItems.Select((protein, index) => (protein, index)).Where(pair => pair.protein.Evidence != null).ToList();
        file.PanelProteinAssignments = targets.Select(pair => new PanelProteinAssignment
        { ProteinIndex = pair.index, Identifier = pair.protein.SequenceIdentifier, FamilyUserId = pair.protein.UserID, PanelFamilyKey = "INV", Reason = "Invented reason" }).ToList();
        file.ProteinSubclassLabels = targets.Select(pair => new ProteinSubclassLabel
        { ProteinIndex = pair.index, Identifier = pair.protein.SequenceIdentifier, FamilyUserId = pair.protein.UserID, PanelFamilyKey = "INV",
          SubclassKey = "part", DefinitionKey = "rule", Status = ProteinSubclassStatus.Assigned, Reason = "Invented rule" }).ToList();
        file.EvidenceSource.NearestReviewed = new NeighbourSearch
        {
            MinIdentityPercent = 70, MinQueryAlignedPercent = 70, Queries = targets.Count, References = 3,
            Neighbours = targets.Select(pair => new NearestNeighbour
            { Query = pair.protein.SequenceIdentifier, Accession = "R0", Reference = "R0", Family = "Invented family",
              IdentityPercent = 90, AlignmentLength = 16, QueryLength = 16, ReferenceLength = 16, QueryAlignedResidues = 16 }).ToList(),
        };
        file.EvidenceRecipe = new EvidenceRecipe
        {
            PanelProteinAssignments = RecipeFixtureClone(file.PanelProteinAssignments),
            ProteinSubclassLabels = RecipeFixtureClone(file.ProteinSubclassLabels),
            SessionSearch = RecipeFixtureClone(file.EvidenceSource.NearestReviewed),
            FamilyPanel = RecipeFixtureClone(file.FamilyPanel),
        };
        return file;
    }

    /// <summary>Z12h2b integration: the format-2 parts were each checked alone; this writes one file with all of
    /// them active, shows each one shapes that file's wire, and saves and reopens it as a project through the
    /// program's verified save.</summary>
    private static void CurationFormat2CompositionTests(string folder)
    {
        Section("Z15b2 to Z15b4 the format-2 parts together on one curation and one project");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        var registered = CurationFormatRegistry.Parts;
        Check("the build registers the three shipped parts", ComposedFormat2Parts.All(name => registered.Any(part => part.Name == name)),
            string.Join(", ", registered.Select(part => part.Name)));

        var file = CurationFormat2CompositionFixture();
        var fingerprints = CurationFingerprints(file);
        string canonical = file.JsonContentSha256();
        byte[] composed = CheckCurationFormatRoundTrip("all registered parts on one curation", file);
        var listed = JsonNode.Parse(composed)![0]!["Parts"]!.AsArray().Select(name => name!.GetValue<string>()).ToArray();
        Check("the written header lists every registered part", listed.SequenceEqual(registered.Select(part => part.Name)), string.Join(", ", listed));
        foreach (string name in ComposedFormat2Parts)
        {
            using var stream = new MemoryStream();
            CurationFormat.Write(file, stream, registered.Where(part => part.Name != name).ToArray());
            byte[] without = stream.ToArray();
            Check(name + " shortens this curation's wire next to the other parts", without.Length > composed.Length,
                without.Length + " bytes without it, " + composed.Length + " with every part");
        }
        Check("reading the composed bytes reproduces the canonical content hash",
            CurationFormat.Read(() => new MemoryStream(composed, writable: false)).JsonContentSha256() == canonical);

        string project = Path.Combine(folder, "composed.fqproj");
        var rows = new List<QuantRow> { new() { Side = "identified", Run = "r1", Scan = 7, Charge = 2, Sequence = "MKTAYIAK", PrecursorMz = 400.5,
            IsotopeAreas = new[] { 1.5, 2.5 }, EnvelopeSum = 4, FamilyUserId = 1 } };
        string message = ShellProjectOperations.WriteVerifiedProject(project, file, rows, new QuantManifest());
        Check("the verified project save passes its own reopening check", file.LastSaveCheck?.Passed == true, message);
        byte[] Stored(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry(ProjectStore.CurationFormat2EntryName);
            if (entry == null) return Array.Empty<byte>();
            using var output = new MemoryStream(); using (var input = entry.Open()) input.CopyTo(output);
            return output.ToArray();
        }
        byte[] stored = Stored(project);
        Check("the project stores its curation as format 2 with every registered part",
            stored.Length > 0 && JsonNode.Parse(stored)![0]!["Parts"]!.AsArray().Select(name => name!.GetValue<string>()).SequenceEqual(registered.Select(part => part.Name)));
        var reopened = ShellProjectOperations.Read(project);
        var moved = fingerprints.Where(pair => CurationFingerprints(reopened.Curation)[pair.Key] != pair.Value).Select(pair => pair.Key).ToList();
        Check("the reopened project has every fingerprint and the canonical hash of the curation saved",
            moved.Count == 0 && reopened.Curation.JsonContentSha256() == canonical && reopened.Rows.Count == rows.Count, string.Join(", ", moved));

        string again = Path.Combine(folder, "composed-again.fqproj");
        ShellProjectOperations.WriteVerifiedProject(again, reopened.Curation, reopened.Rows, reopened.Manifest ?? new QuantManifest());
        Check("saving the reopened project writes the same stored curation bytes", Stored(again).SequenceEqual(stored),
            Encoding.UTF8.GetString(Stored(again)).Length + " and " + Encoding.UTF8.GetString(stored).Length + " characters");
    }
}
