using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void EvidenceSnapshotCopyTests()
    {
        Section("Z12p5f2f streamed evidence snapshot copies");
        var copyMethod = typeof(EvidenceApply).GetMethod("Clone", BindingFlags.Static | BindingFlags.NonPublic)!;
        T Copy<T>(T value) => (T)copyMethod.MakeGenericMethod(typeof(T)).Invoke(null, new object?[] { value })!;
        string[] Spools() => Directory.GetFiles(Path.GetTempPath(), "FamilyQuant-json-copy-*.tmp").Order(StringComparer.Ordinal).ToArray();
        string[] initialSpools = Spools();
        var large = new EvidenceProvenance
        {
            Provider = "Synthetic snapshot source", Release = "fixture-release",
            ReferencePanel = new ReferencePanel
            {
                Provider = "Synthetic snapshot source", Release = "fixture-release",
                Entries = Enumerable.Range(0, 512).Select(index => new PanelEntry
                {
                    Accession = "REFERENCE_" + index, Sequence = "ACDEFGHIK",
                    Evidence = new ProteinEvidence { Family = "Stored fixture group", Keywords = new() { new string('A', 1024) } },
                }).ToList(),
            },
        };
        var variants = new[]
        {
            new EvidenceProvenance { Provider = "Empty source", ReferencePanel = new() },
            EvidenceMatchingFile().EvidenceSource!,
            large,
        };
        for (int index = 0; index < variants.Length; index++)
        {
            var source = variants[index];
            string expected = JsonSerializer.Serialize(JsonSerializer.Deserialize<EvidenceProvenance>(JsonSerializer.Serialize(source)));
            var copied = Copy(source);
            Check("Z12p5f2f source " + index + " has the same complete objects as the prior JSON copy",
                JsonSerializer.Serialize(copied) == expected && !ReferenceEquals(copied, source)
                && !ReferenceEquals(copied.ReferencePanel, source.ReferencePanel));
            var file = ManualChoiceFile(); file.EvidenceSource = source;
            string before = file.ToJsonText();
            var snapshot = GroupingSnapshot.CaptureEvidence(file);
            source.Provider = "Changed after capture";
            file.MyExtendedFastaItems[0].UserID = 99;
            snapshot.Restore(file);
            Check("Z12p5f2f source " + index + " snapshot restores every stored field with independent evidence",
                file.ToJsonText() == before && !ReferenceEquals(file.EvidenceSource, source));
            file.EvidenceSource!.Provider = "Changed after restore";
            snapshot.Restore(file);
            Check("Z12p5f2f source " + index + " repeated restore never aliases its saved source", file.ToJsonText() == before);
        }
        Check("Z12p5f2f null values and primitive arrays retain generic copy behavior", Copy<string?>(null) == null
            && Copy(new[] { 0, 1, -1, int.MaxValue }).SequenceEqual(new[] { 0, 1, -1, int.MaxValue }));
        var nativeFile = ManualChoiceFile(); nativeFile.EvidenceSource = large;
        string nativeBefore = nativeFile.ToJsonText();
        var model = new CurationViewModel(nativeFile);
        model.ApplyFamilyPanel(new FamilyPanel { Name = "Snapshot copy fixture", Families = new() { new() { Key = "group", DisplayName = "Fixture group" } } });
        nativeFile.EvidenceSource.Provider = "Changed while panel is open";
        model.Undo();
        Check("Z12p5f2f ApplyFamilyPanel and native Undo restore the complete large source once", model.UndoStack.Count == 0
            && nativeFile.ToJsonText() == nativeBefore);

        model.ApplyFamilyPanel(new FamilyPanel { Name = "Retry snapshot", Families = new() { new() { Key = "group", DisplayName = "Fixture group" } } });
        string beforeFailure = nativeFile.ToJsonText();
        string blocker = Path.Combine(Path.GetTempPath(), "snapshot-temp-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "Synthetic temporary folder blocker.");
        string? previousTemp = Environment.GetEnvironmentVariable("TEMP"), previousTmp = Environment.GetEnvironmentVariable("TMP");
        bool failedUndo = false;
        try
        {
            Environment.SetEnvironmentVariable("TEMP", blocker);
            Environment.SetEnvironmentVariable("TMP", blocker);
            Check("Z12p5f2f small copies need no temporary file", Copy("Small snapshot") == "Small snapshot");
            try { model.Undo(); }
            catch (IOException) { failedUndo = true; }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMP", previousTemp);
            Environment.SetEnvironmentVariable("TMP", previousTmp);
            File.Delete(blocker);
        }
        Check("Z12p5f2f failed spool preparation preserves the document and Undo snapshot", failedUndo
            && nativeFile.ToJsonText() == beforeFailure && model.UndoStack.Count == 1);
        model.Undo();
        Check("Z12p5f2f Undo can retry after temporary storage recovers", nativeFile.ToJsonText() == nativeBefore && model.UndoStack.Count == 0);

        var bad = new FailingSnapshotCopy();
        bool refused = false;
        try { Copy(bad); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { refused = true; }
        Check("Z12p5f2f a failure after the buffer fills propagates and removes its spool", refused && bad.SawSpool
            && initialSpools.SequenceEqual(Spools()));
        Check("Z12p5f2f successful large copies leave no temporary file", initialSpools.SequenceEqual(Spools()));
    }

    private sealed class FailingSnapshotCopy
    {
        [JsonPropertyOrder(0)]
        public List<string> Padding { get; } = Enumerable.Repeat(new string('A', 1024), 512).ToList();
        [JsonIgnore]
        public bool SawSpool { get; private set; }
        [JsonPropertyOrder(1)]
        public string Failure
        {
            get
            {
                SawSpool = Directory.EnumerateFiles(Path.GetTempPath(), "FamilyQuant-json-copy-*.tmp").Any();
                throw new InvalidOperationException("Synthetic snapshot serialization failure.");
            }
        }
    }
}
