using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationStreamingContractCommands() =>
    [new("--test-curation-streaming-contract", 449, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-curation-streaming-contract", (args, _) =>
        {
            CurationStreamingContractTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.CurationStreamingContract"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static FamilyClusterFile CurationStreamingFixture(int entries, int padding)
    {
        // Only this opt-in fixture's stored metadata grows; scientific inputs remain small and synthetic.
        string text = new('A', padding);
        return new FamilyClusterFile
        {
            ClusteringIdentity = 85, SourceSha256 = new string('a', 64), SourceSequenceCount = entries,
            ReportDetails = new() { Title = "Synthetic streaming report", Authors = "Fixture" },
            MyExtendedFastaItems = Enumerable.Range(0, entries).Select(index => new FastaItemExtended
            {
                SequenceIdentifier = index == 0 ? "sp|X|PROT1" : "streaming_entry_" + index.ToString(CultureInfo.InvariantCulture),
                Sequence = "PEPTIDEAPEPTIDEK", Description = "Synthetic streaming protein", UserAnnotation = "Synthetic family",
                UserID = 1, USearchID = 1,
                Evidence = new ProteinEvidence
                {
                    Accession = "streaming_entry_" + index.ToString(CultureInfo.InvariantCulture), Reviewed = true,
                    Family = "Synthetic family", Keywords = new() { "Synthetic scope" },
                    KeywordCategories = new() { ["Synthetic padding"] = text }
                }
            }).ToList()
        };
    }

    private static void CurationStreamingContractTests(string folder)
    {
        Section("Z12p5f curation streams, detached copies and import fingerprint reuse");
        EnsureApplication(); Directory.CreateDirectory(folder);
        var variants = new[] { CurationStreamingFixture(64, 4096), ManualChoiceFile(), CensusFixture() };
        for (int index = 0; index < variants.Length; index++)
        {
            var file = variants[index];
            byte[] expected = Encoding.UTF8.GetBytes(file.ToJsonText());
            string expectedHash = Convert.ToHexString(SHA256.HashData(expected));
            string path = Path.Combine(folder, "parity-" + index.ToString(CultureInfo.InvariantCulture) + ".fcf");
            file.Save(path);
            var read = FamilyClusterFile.Load(path);
            var shellRead = ShellProjectOperations.Read(path);
            var spoolBefore = Directory.GetFiles(Path.GetTempPath(), "FamilyQuant-curation-*.tmp").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var copy = file.DeepCopy();
            using var canonical = new MemoryStream();
            typeof(FamilyClusterFile).GetMethod("WriteJsonUtf8", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(file, new object[] { canonical });
            Check("Z12p5f fixture " + index + " canonical stream bytes equal the string writer", expected.SequenceEqual(canonical.ToArray()));
            Check("Z15b7 fixture " + index + " saved FCF is format 2", CurationFormat.Detect(() => File.OpenRead(path)) == CurationFormat.Current);
            Check("Z12p5f fixture " + index + " streamed hashes equal UTF8 of the legacy JSON", file.JsonContentSha256() == expectedHash
                && read.JsonContentSha256() == expectedHash && copy.JsonContentSha256() == expectedHash);
            Check("Z12p5f fixture " + index + " shell open preserves exact raw-file provenance", shellRead.DatabaseFileSha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
                && shellRead.Curation.JsonContentSha256() == expectedHash);
            Check("Z12p5f fixture " + index + " copy shares no mutable protein or evidence objects", !ReferenceEquals(copy, file)
                && copy.MyExtendedFastaItems.Zip(file.MyExtendedFastaItems).All(pair => !ReferenceEquals(pair.First, pair.Second)
                    && (pair.First.Evidence == null || !ReferenceEquals(pair.First.Evidence, pair.Second.Evidence))));
            Check("Z12p5f fixture " + index + " detached-copy spool is removed", Directory.GetFiles(Path.GetTempPath(), "FamilyQuant-curation-*.tmp")
                .All(spoolBefore.Contains));
        }
        var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Check("Z12p5f a cancelled copy refuses before creating a spool", Throws<OperationCanceledException>(() => variants[0].DeepCopy(new OperationContext(cancelled.Token))));
        cancelled.Dispose();
        CurationStreamingEncodingTests(folder);
        CurationStreamingImportCacheTests();
        CurationWorkingMemoryTests(folder);
        CurationStreamingBoundaryTests(Path.Combine(folder, "boundaries"));
    }

    private static void CurationStreamingEncodingTests(string folder)
    {
        string text = "{\"ClusteringIdentity\":85,\"MyExtendedFastaItems\":[{\"SequenceIdentifier\":\"legacy\",\"Sequence\":\"PEPTIDEK\",\"Description\":\"Unicode \u00e1 \ud83e\uddec\",\"USearchID\":1,\"UserID\":2}]}";
        string expected = FamilyClusterFile.FromJsonText(text).JsonContentSha256();
        foreach (var encoding in new Encoding[] { new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32 })
        {
            string path = Path.Combine(folder, "encoding-" + encoding.CodePage.ToString(CultureInfo.InvariantCulture) + ".fcf");
            byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
            File.WriteAllBytes(path, bytes);
            var read = ShellProjectOperations.Read(path);
            Check("Z12p5f legacy encoding " + encoding.CodePage + " retains migration and raw-byte hash", read.Curation.JsonContentSha256() == expected
                && read.Curation.LockedUserIds.SetEquals(new[] { 1, 2 })
                && read.DatabaseFileSha256 == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        byte[] invalid = Encoding.UTF8.GetBytes("{\"ClusteringIdentity\":85,\"MyExtendedFastaItems\":[],\"DomainSource\":\"replace ? byte\"}");
        invalid[Array.IndexOf(invalid, (byte)'?')] = 0xff;
        string replacement;
        using (var reader = new StreamReader(new MemoryStream(invalid), Encoding.UTF8, true)) replacement = reader.ReadToEnd();
        string badUtf8 = Path.Combine(folder, "replacement.fcf"); File.WriteAllBytes(badUtf8, invalid);
        Check("Z12p5f invalid UTF8 keeps the old StreamReader replacement behavior", FamilyClusterFile.Load(badUtf8).JsonContentSha256()
            == FamilyClusterFile.FromJsonText(replacement).JsonContentSha256());
    }

    private static void CurationStreamingImportCacheTests()
    {
        var file = CurationStreamingFixture(12, 8192);
        var model = new CurationViewModel(file);
        using var transaction = new FamilyListImportTransaction(model);
        Check("Z12p5f import publishes a panel before fingerprint reuse", transaction.Import(new FamilyPanel
        { Name = "Synthetic panel", Families = new() { new() { Key = "synthetic", DisplayName = "Synthetic family" } } }, out _));
        var observer = typeof(FamilyClusterFile).GetProperty("JsonWriteObserved", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = observer.GetValue(null);
        int serializations = 0;
        observer.SetValue(null, (Action)(() => serializations++));
        try
        {
        _ = transaction.CanUndo;
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool available = true;
        for (int index = 0; index < 128; index++) available &= transaction.CanUndo;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("Z12p5f 128 CanUndo reads reuse the same state's fingerprint without JSON-sized allocations", available && allocated < 65536,
            allocated.ToString(CultureInfo.InvariantCulture) + " allocated bytes");
        Check("Z12p5f repeated CanUndo reads perform zero curation serializations", serializations == 0,
            serializations.ToString(CultureInfo.InvariantCulture));
        model.ApplyBatch(new[] { file.MyExtendedFastaItems[0] }, "", "Later change");
        Check("Z12p5f a later notified edit refuses the import Undo", !transaction.CanUndo);
        model.Undo();
        Check("Z12p5f Undo of the later edit makes the import Undo available again", transaction.CanUndo);
        int afterState = serializations;
        for (int index = 0; index < 128; index++) _ = transaction.CanUndo;
        Check("Z12p5f one fingerprint is computed for a restored state and then reused", afterState == 1 && serializations == afterState,
            afterState.ToString(CultureInfo.InvariantCulture) + " serializations for the state");
        file.MyExtendedFastaItems[0].Description = "Direct unnotified edit";
        Check("Z12p5f actual Undo rechecks unnotified edits and never consumes their snapshot", !transaction.Undo(out _) && model.UndoStack.Count == 1
            && file.MyExtendedFastaItems[0].Description == "Direct unnotified edit");
        var second = CurationStreamingFixture(2, 16); using var pending = new FamilyListImportTransaction(new CurationViewModel(second));
        second.MyExtendedFastaItems[0].Description = "Direct edit before import";
        Check("Z12p5f import boundary still rejects an unnotified edit", !pending.CheckCurrent(out _));
        }
        finally { observer.SetValue(null, previous); }
    }

}
