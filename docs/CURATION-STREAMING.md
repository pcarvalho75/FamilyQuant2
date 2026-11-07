# Curation streaming contract

Z12p5f0 preserves the existing curation JSON metadata, options, property order and bytes.
It changes how the data moves, without changing the stored scientific objects.

`FamilyClusterFile.DeepCopy(OperationContext? operation = null)` is the shared public copy API.
It writes canonical UTF-8 to a unique file under `Path.GetTempPath()`, reads it through the
shared reader and removes the spool with `FileOptions.DeleteOnClose`, including on failure.
Cancellation is checked before writing and at the write/read boundaries. It never creates a
whole-file string or byte array. Configure TEMP/TMP on a drive with room for the spool.

`FamilyClusterFile.JsonContentSha256()` returns the same uppercase SHA-256 as
`SHA256(Encoding.UTF8.GetBytes(ToJsonText()))`, with streamed serialization into the hash.
`internal void WriteJsonUtf8(Stream destination)` and
`internal static FamilyClusterFile ReadJsonUtf8(Func<Stream> open)` remain the shared Core
write/read implementations. The reader retains raw lock-field migration, BOM detection,
legacy encodings and the previous StreamReader replacement behavior without `ReadToEnd`.

`FamilyClusterFile.Save(string, OperationContext? = null)` and
`FamilyClusterFile.Load(string, OperationContext? = null)` keep their public signatures.
Save retains its verified staged write and atomic publication. The plain-FCF shell opener
streams deserialization and raw-byte hashing while holding a read-only file handle, so its
provenance still hashes the exact original bytes rather than a rewritten representation.

The import transaction caches its digest for a notified document state. Repeated CanUndo
reads reuse it; explicit import/undo publication boundaries still revalidate unnotified edits.
The transaction unsubscribes from document changes when its owning wizard model is disposed.

Run `FamilyQuant.Tests.exe --test-curation-streaming-contract [output-directory]` for the
small default-suite contract proof. It compares old-string bytes and SHA-256 on three
fixtures, checks detached state and spool cleanup, covers legacy encodings and cancellation,
counts serialization on repeated CanUndo reads and retains stale-edit refusal.
The existing `--test-streaming-save` command also checks canonical project-entry parity.

The internal `JsonWriteObserved` callback is a test-only serialization counter and is null
in production. `ApplicationPaths.LocalDataDirectoryOverride` likewise defaults to null;
the test harness sets it under its isolated TEMP directory so native caches and logs remain
inside the selected test scratch area.

The opt-in oversized proof and production-caller audit belong to Z12p5f4. Compact JSON is a
separate step, Z12p5j; it must change the shared options for both writing APIs together.
