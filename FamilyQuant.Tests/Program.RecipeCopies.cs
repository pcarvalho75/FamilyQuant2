using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> RecipeCopiesCommands() =>
    [new("--test-recipe-copies", 452, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-recipe-copies", (args, _) =>
        {
            if (args.Length is not (1 or 2)) throw new ArgumentException("Use --test-recipe-copies [output-directory].");
            RecipeCopiesTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.RecipeCopies"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static T RecipeFixtureClone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private static FamilyClusterFile RecipeCopiesFixture(string field)
    {
        string padding = new('r', 2048);
        var file = new FamilyClusterFile
        {
            MyExtendedFastaItems = new() { new() { SequenceIdentifier = "synthetic_entry", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 } },
            EvidenceRecipe = new EvidenceRecipe()
        };
        switch (field)
        {
            case "PanelProteinAssignments":
                file.PanelProteinAssignments = Enumerable.Range(0, 16).Select(index => new PanelProteinAssignment
                { ProteinIndex = index, Identifier = "synthetic_" + index.ToString(CultureInfo.InvariantCulture), FamilyUserId = 1, PanelFamilyKey = "synthetic", Reason = padding }).ToList();
                file.EvidenceRecipe.PanelProteinAssignments = RecipeFixtureClone(file.PanelProteinAssignments);
                break;
            case "ProteinSubclassLabels":
                file.ProteinSubclassLabels = Enumerable.Range(0, 16).Select(index => new ProteinSubclassLabel
                { ProteinIndex = index, Identifier = "synthetic_" + index.ToString(CultureInfo.InvariantCulture), FamilyUserId = 1, PanelFamilyKey = "synthetic", SubclassKey = "part", DefinitionKey = "rule", Status = ProteinSubclassStatus.Assigned, Reason = padding }).ToList();
                file.EvidenceRecipe.ProteinSubclassLabels = RecipeFixtureClone(file.ProteinSubclassLabels);
                break;
            case "SessionSearch":
                file.EvidenceSource = new EvidenceProvenance
                {
                    NearestReviewed = new NeighbourSearch
                    {
                        MinIdentityPercent = 70, MinQueryAlignedPercent = 70, Queries = 16, References = 16,
                        Neighbours = Enumerable.Range(0, 16).Select(index => new NearestNeighbour
                        { Query = "synthetic_" + index.ToString(CultureInfo.InvariantCulture), Accession = "synthetic_reference", Reference = "synthetic_reference", Family = padding,
                            IdentityPercent = 90, AlignmentLength = 8, QueryLength = 8, ReferenceLength = 10, QueryAlignedResidues = 8 }).ToList()
                    }
                };
                file.EvidenceRecipe.SessionSearch = RecipeFixtureClone(file.EvidenceSource.NearestReviewed);
                break;
            case "FamilyPanel":
                file.FamilyPanel = new FamilyPanel
                {
                    Name = "Synthetic current and recipe panel", Families = new() { new() { Key = "synthetic", DisplayName = "Synthetic family" } },
                    TextFields = Enumerable.Range(0, 16).Select(index => new FamilyPanelText { Name = "note_" + index.ToString(CultureInfo.InvariantCulture), Text = padding }).ToList()
                };
                file.EvidenceRecipe.FamilyPanel = RecipeFixtureClone(file.FamilyPanel);
                break;
            default: throw new ArgumentException("Unknown synthetic recipe field.", nameof(field));
        }
        return file;
    }

    private static byte[] RecipeCopiesWire(FamilyClusterFile file, IReadOnlyList<CurationFormatPart> parts)
    {
        using var output = new MemoryStream();
        CurationFormat.Write(file, output, parts);
        return output.ToArray();
    }

    private static FamilyClusterFile RecipeCopiesRead(byte[] bytes, IReadOnlyList<CurationFormatPart> parts) =>
        CurationFormat.Read(() => new MemoryStream(bytes, writable: false), parts);

    private static void RecipeCopiesTests(string folder)
    {
        Section("Z15b3 recipe copies are wire references only while byte-identical");
        Directory.CreateDirectory(folder);
        var parts = CurationFormatRegistry.Parts.Where(part => part.Name == "recipe-copies").ToArray();
        Check("Z15b3 recipe storage registers one stateless part", parts.Length == 1);
        if (parts.Length != 1) return;
        foreach (string field in new[] { "PanelProteinAssignments", "ProteinSubclassLabels", "SessionSearch", "FamilyPanel" })
        {
            var file = RecipeCopiesFixture(field);
            byte[] baseline = RecipeCopiesWire(file, Array.Empty<CurationFormatPart>());
            byte[] bytes = CheckCurationFormatRoundTrip("Z15b3 " + field, file, parts);
            var root = JsonNode.Parse(bytes)![0]!;
            string[] references = root["Sections"]!["recipe-copies"]!.Deserialize<string[]>()!;
            Check("Z15b3 " + field + " is explicitly referenced and its recipe copy is absent", references.SequenceEqual(new[] { field })
                && !root["Curation"]!["EvidenceRecipe"]!.AsObject().ContainsKey(field));
            Check("Z15b3 " + field + " is stored once with a measured byte reduction", bytes.Length + 4096 < baseline.Length,
                baseline.Length.ToString(CultureInfo.InvariantCulture) + " -> " + bytes.Length.ToString(CultureInfo.InvariantCulture));
            var read = RecipeCopiesRead(bytes, parts);
            Check("Z15b3 " + field + " restores the exact canonical curation", read.JsonContentSha256() == file.JsonContentSha256());
            Check("Z15b3 " + field + " writes deterministically", bytes.SequenceEqual(RecipeCopiesWire(file, parts)));
            var withoutPart = RecipeCopiesRead(baseline, parts);
            Check("Z15b3 " + field + " still reads files written before this part existed", withoutPart.JsonContentSha256() == file.JsonContentSha256());

            var different = RecipeFixtureClone(file);
            MakeRecipeCopyDifferent(different, field);
            string expectedDifferent = different.JsonContentSha256();
            byte[] separate = CheckCurationFormatRoundTrip("Z15b3 unequal " + field, different, parts);
            var unequalRoot = JsonNode.Parse(separate)![0]!;
            Check("Z15b3 unequal " + field + " keeps its historical copy", unequalRoot["Sections"]!["recipe-copies"]!.AsArray().Count == 0
                && unequalRoot["Curation"]!["EvidenceRecipe"]!.AsObject().ContainsKey(field)
                && RecipeCopiesRead(separate, parts).JsonContentSha256() == expectedDifferent);
            CheckRecipeDetached(read, field);

            Check("Z15b3 " + field + " rejects a missing primary reference", Throws<InvalidDataException>(() =>
                RecipeCopiesRead(EditRecipeWire(bytes, node =>
                {
                    if (field == "SessionSearch") node["Curation"]!["EvidenceSource"]!["NearestReviewed"] = null;
                    else node["Curation"]![field] = null;
                }), parts)));
            Check("Z15b3 " + field + " rejects both stored and referenced data", Throws<InvalidDataException>(() =>
                RecipeCopiesRead(EditRecipeWire(bytes, node => node["Curation"]!["EvidenceRecipe"]![field] =
                    JsonNode.Parse(baseline)![0]!["Curation"]!["EvidenceRecipe"]![field]!.DeepClone()), parts)));
            File.WriteAllBytes(Path.Combine(folder, field + ".json"), bytes);
        }
        var sample = RecipeCopiesWire(RecipeCopiesFixture("PanelProteinAssignments"), parts);
        Check("Z15b3 unknown references are refused", Throws<InvalidDataException>(() => RecipeCopiesRead(EditRecipeWire(sample,
            node => node["Sections"]!["recipe-copies"] = new JsonArray(JsonValue.Create("unknown"))), parts)));
        Check("Z15b3 duplicate reference markers are refused", Throws<InvalidDataException>(() => RecipeCopiesRead(EditRecipeWire(sample,
            node => node["Sections"]!["recipe-copies"] = new JsonArray(JsonValue.Create("PanelProteinAssignments"), JsonValue.Create("PanelProteinAssignments"))), parts)));
        Check("Z15b3 a listed part requires its reference table", Throws<InvalidDataException>(() => RecipeCopiesRead(EditRecipeWire(sample,
            node => node["Sections"]!.AsObject().Remove("recipe-copies")), parts)));
        RecipeCopiesNullAndEmpty(parts);
        RecipeCopiesReplay(parts);
        var projectFile = RecipeCopiesFixture("FamilyPanel");
        string project = Path.Combine(folder, "recipe-copies.fqproj");
        ProjectStore.WriteFqproj(project, projectFile, Array.Empty<QuantRow>(), new QuantManifest());
        Check("Z15b3 the production project route restores canonical content", ProjectStore.ReadFqproj(project).fcf.JsonContentSha256() == projectFile.JsonContentSha256());
    }

    private static byte[] EditRecipeWire(byte[] bytes, Action<JsonNode> edit)
    {
        var root = JsonNode.Parse(bytes)!; edit(root[0]!);
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    private static void MakeRecipeCopyDifferent(FamilyClusterFile file, string field)
    {
        var recipe = file.EvidenceRecipe!;
        switch (field)
        {
            case "PanelProteinAssignments": recipe.PanelProteinAssignments![0].Reason = "Historical reason differs"; break;
            case "ProteinSubclassLabels": recipe.ProteinSubclassLabels![0].Reason = "Historical subclass reason differs"; break;
            case "FamilyPanel": recipe.FamilyPanel!.Name = "Historical panel name differs, rules unchanged"; break;
            case "SessionSearch":
                var node = JsonSerializer.SerializeToNode(recipe.SessionSearch)!;
                node["Neighbours"]![0]!["QueryAlignedResidues"] = 7;
                recipe.SessionSearch = node.Deserialize<NeighbourSearch>();
                break;
        }
    }

    private static void CheckRecipeDetached(FamilyClusterFile read, string field)
    {
        string recipeBefore = JsonSerializer.Serialize(read.EvidenceRecipe);
        switch (field)
        {
            case "PanelProteinAssignments": read.PanelProteinAssignments![0].Reason = "Current row edited"; break;
            case "ProteinSubclassLabels": read.ProteinSubclassLabels![0].Reason = "Current subclass edited"; break;
            case "SessionSearch": read.EvidenceSource!.NearestReviewed!.Neighbours.Clear(); break;
            case "FamilyPanel": read.FamilyPanel!.Name = "Current panel edited"; read.FamilyPanel.TextFields.Clear(); break;
        }
        Check("Z15b3 " + field + " rehydrates detached history", JsonSerializer.Serialize(read.EvidenceRecipe) == recipeBefore);
    }

    private static void RecipeCopiesNullAndEmpty(IReadOnlyList<CurationFormatPart> parts)
    {
        var file = new FamilyClusterFile { EvidenceRecipe = new EvidenceRecipe() };
        var empty = RecipeCopiesRead(RecipeCopiesWire(file, parts), parts);
        Check("Z15b3 null recipe values remain null rather than invented empty values", empty.EvidenceRecipe!.PanelProteinAssignments == null
            && empty.EvidenceRecipe.ProteinSubclassLabels == null && empty.EvidenceRecipe.SessionSearch == null && empty.EvidenceRecipe.FamilyPanel == null);
        file.PanelProteinAssignments = new(); file.EvidenceRecipe.PanelProteinAssignments = new();
        var lists = RecipeCopiesRead(RecipeCopiesWire(file, parts), parts);
        Check("Z15b3 explicit empty copies stay explicit and detached", lists.PanelProteinAssignments is { Count: 0 }
            && lists.EvidenceRecipe!.PanelProteinAssignments is { Count: 0 } && !ReferenceEquals(lists.PanelProteinAssignments, lists.EvidenceRecipe.PanelProteinAssignments));
        file.PanelProteinAssignments = null;
        var different = RecipeCopiesRead(RecipeCopiesWire(file, parts), parts);
        Check("Z15b3 null and empty are not treated as identical", different.PanelProteinAssignments == null && different.EvidenceRecipe!.PanelProteinAssignments is { Count: 0 });
    }

    private static void RecipeCopiesReplay(IReadOnlyList<CurationFormatPart> parts)
    {
        var file = ManualChoiceFile();
        var session = new EvidenceGroupingSession(file, "synthetic recipe storage");
        var proposal = session.Propose("scope");
        var applied = session.Apply(new EvidenceReviewViewModel(proposal).CaptureSelection());
        string expected = file.JsonContentSha256();
        var read = RecipeCopiesRead(RecipeCopiesWire(file, parts), parts);
        applied.Undo!.Restore(file);
        EvidenceApply.ApplyRecipe(file, read.EvidenceRecipe!);
        Check("Z15b3 a stored native recipe replays to the identical curation", file.JsonContentSha256() == expected);
    }
}
