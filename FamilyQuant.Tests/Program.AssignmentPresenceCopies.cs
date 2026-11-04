using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AssignmentPresenceCopyCommands() =>
    [new("--test-assignment-presence-copies", 10513, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-assignment-presence-copies", (args, _) =>
        {
            AssignmentPresenceCopyTests(args.Length > 1 ? args[1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void AssignmentPresenceCopyTests(string? output = null)
    {
        Section("M201 assignment absence survives copies, snapshots and save");
        FastaItemExtended Missing() => JsonSerializer.Deserialize<FastaItemExtended>("{\"SequenceIdentifier\":\"UNASSIGNED\",\"Sequence\":\"AAAA\"}")!;
        bool Absent(FastaItemExtended p)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(p, FamilyClusterFile.CurationJsonOptions));
            return !p.HasUserId && !json.RootElement.TryGetProperty("UserID", out _);
        }
        var source = Missing();
        Check("JSON copy retains an absent assignment", Absent(JsonSerializer.Deserialize<FastaItemExtended>(JsonSerializer.Serialize(source))!));
        var memberwise = (FastaItemExtended)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, null)!;
        Check("memberwise copy retains an absent assignment", Absent(memberwise));
        foreach (var capture in new (string Name, Func<FamilyClusterFile, GroupingSnapshot> Capture)[]
        {
            ("grouping", file => GroupingSnapshot.Capture(file)), ("evidence", GroupingSnapshot.CaptureEvidence),
        })
        {
            var file = new FamilyClusterFile { MyExtendedFastaItems = new() { Missing(), new() { SequenceIdentifier = "REAL_ZERO", Sequence = "CCCC", UserID = 0 } } };
            string before = file.ToJsonText();
            var snapshot = capture.Capture(file);
            file.MyExtendedFastaItems[0].UserID = 21; file.MyExtendedFastaItems[1].UserID = 22;
            snapshot.Restore(file);
            Check(capture.Name + " undo preserves absence separately from the real zero family", before == file.ToJsonText()
                && Absent(file.MyExtendedFastaItems[0]) && file.MyExtendedFastaItems[1].HasUserId && file.MyExtendedFastaItems[1].UserID == 0);
        }
        foreach (var path in new[]
        {
            (Owner: typeof(EvidenceFetchSession), Method: "CloneProteinForFetch", Name: "evidence staging"),
            (Owner: typeof(ValidationReport), Method: "CloneProteinForValidation", Name: "validation staging"),
            (Owner: typeof(EffectivePanelFamilyStudy), Method: "CloneProteinForStudy", Name: "effective study projection"),
        })
        {
            var method = path.Owner.GetMethod(path.Method, BindingFlags.Static | BindingFlags.NonPublic);
            object?[] arguments = path.Owner == typeof(EffectivePanelFamilyStudy) ? new object?[] { source, 17 } : new object?[] { source };
            var copied = method?.Invoke(null, arguments) as FastaItemExtended;
            Check(path.Name + " copy preserves missing assignment", copied != null && Absent(copied));
            if (path.Owner == typeof(EffectivePanelFamilyStudy))
                Check("study projection keeps the legacy remapped integer while preserving absence", copied != null && copied.UserID == 17);
        }
        var assigned = DatabaseEntryFixture();
        string original = assigned.ToJsonText();
        string root = output ?? Path.Combine(Path.GetTempPath(), "FamilyQuant.AssignmentCopies", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string project = Path.Combine(root, "assigned.fqproj");
        ProjectStore.WriteFqproj(project, assigned, Array.Empty<QuantRow>(), new QuantManifest());
        using (var zip = ZipFile.OpenRead(project))
        using (var reader = new StreamReader(zip.GetEntry("curation.json")!.Open()))
            Check("all-assigned project writes the exact original curation bytes", reader.ReadToEnd() == original);
        var reopened = ProjectStore.ReadFqproj(project);
        Check("all-assigned project reopen preserves recorded assignments and values", reopened.fcf.ToJsonText() == original
            && reopened.fcf.MyExtendedFastaItems.All(p => p.HasUserId));
        File.WriteAllText(Path.Combine(root, "assigned-curation.json"), original);
    }
}
