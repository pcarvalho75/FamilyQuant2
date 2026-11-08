using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Runtime.CompilerServices;

namespace FamilyQuant.Evidence
{
    public sealed record SavedFileCheckItem(string Code, bool Passed);
    public sealed record SavedFileCheckResult(bool Passed, string Message, IReadOnlyList<SavedFileCheckItem> Checks);

    public static class SavedFileCheck
    {
        public static SavedFileCheckResult Check(FamilyClusterFile expected, string path, bool? project = null,
            Operations.OperationContext? operation = null) => Verify(Capture(expected), path, project, operation);

        internal sealed record Snapshot(string Sequences, string Decoys, string Unassigned, string Locks,
            string Source, string Curation, int[] DecoyIndices, int[] UnassignedIndices);

        internal static Snapshot Capture(FamilyClusterFile file)
        {
            ArgumentNullException.ThrowIfNull(file);
            var assigned = file.EvidenceRecipe?.Assignments.Select(a => a.ProteinIndex).ToHashSet() ?? new HashSet<int>();
            int[] decoys = Enumerable.Range(0, file.MyExtendedFastaItems.Count)
                .Where(i => SequenceLabels.IsDecoy(file.MyExtendedFastaItems[i].SequenceIdentifier)).ToArray();
            int[] unassigned = Enumerable.Range(0, file.MyExtendedFastaItems.Count).Except(decoys).Where(i => !assigned.Contains(i)).ToArray();
            return new Snapshot(Sequences(file), Rows(file, decoys), Rows(file, unassigned), Locks(file), Source(file), Curation(file), decoys, unassigned);
        }

        internal static SavedFileCheckResult Verify(Snapshot expected, string path, bool? project = null,
            Operations.OperationContext? operation = null)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            try { return VerifyCore(expected, path, project, operation); }
            finally { CurationReadMemory.ReleaseIfLarge(GC.GetAllocatedBytesForCurrentThread() - before); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static SavedFileCheckResult VerifyCore(Snapshot expected, string path, bool? project,
            Operations.OperationContext? operation)
        {
            operation?.Report("file-check", "Reopening the saved file", 0, 1);
            try
            {
                FamilyClusterFile actual = (project ?? ProjectStore.IsProjectPath(path))
                    ? ProjectStore.ReadFqproj(path, operation).fcf : FamilyClusterFile.Load(path, operation);
                var checks = new List<SavedFileCheckItem>
                {
                    new("sequences", expected.Sequences == Sequences(actual)),
                    new("decoys", expected.Decoys == Rows(actual, expected.DecoyIndices)),
                    new("unassigned", expected.Unassigned == Rows(actual, expected.UnassignedIndices)),
                    new("locks", expected.Locks == Locks(actual)),
                    new("source", expected.Source == Source(actual)),
                    new("curation", expected.Curation == Curation(actual)),
                };
                bool passed = checks.All(c => c.Passed);
                string message = passed ? "File check passed: sequences, decoys, unassigned proteins, locks and source hash."
                    : "File check failed: " + string.Join(", ", checks.Where(c => !c.Passed).Select(c => c.Code)) + ".";
                operation?.Report("file-check", message, 1, 1);
                return new SavedFileCheckResult(passed, message, checks);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                return new SavedFileCheckResult(false, "File check failed: the saved file could not be reopened. " + ex.Message,
                    new[] { new SavedFileCheckItem("read", false) });
            }
        }

        internal static void RequirePassed(SavedFileCheckResult result)
        {
            if (!result.Passed) throw new InvalidDataException(result.Message);
        }

        private static string Sequences(FamilyClusterFile file) => Hash(file.MyExtendedFastaItems.Select(p => new { p.SequenceIdentifier, p.Sequence }));
        private static string Rows(FamilyClusterFile file, IEnumerable<int> indices) => Hash(indices.Select(i => new
        {
            Index = i, Protein = i < file.MyExtendedFastaItems.Count ? file.MyExtendedFastaItems[i] : null,
        }));
        private static string Locks(FamilyClusterFile file) => Hash(file.LockedUserIds.OrderBy(i => i));
        private static string Source(FamilyClusterFile file) => Hash(new { file.SourceSha256, file.SourceSequenceCount });
        private static string Curation(FamilyClusterFile file) => file.JsonContentSha256();
        private static string Hash<T>(T value)
        {
            using var hash = SHA256.Create();
            using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
            JsonSerializer.Serialize(stream, value); stream.FlushFinalBlock();
            return Convert.ToHexString(hash.Hash!);
        }
    }
}

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public Evidence.SavedFileCheckResult? LastSaveCheck { get; internal set; }
    }
}
