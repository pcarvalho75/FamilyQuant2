# Project and FCF format boundaries

X7 separates the project models, versioned schema and entry codecs. Existing
`ProjectStore` APIs, constants and column arrays remain available. Z15b1 (2.1.11)
raised the storage format version to 3; see "Curation format 2" below.

| Component under `FamilyQuant.Core/Storage` | Owns |
|---|---|
| `QuantRow`, `DerivedRunFraction`, `QuantManifest` | Existing stored/derived data contracts, same namespaces and properties |
| `ProjectFileSchema` | Version 2, entry names, current/legacy columns and six-isotope storage limit |
| `ProjectArchiveEntries` | UTF-8 text entries and cancellation checkpoints |
| `ProjectManifestCodec` | Manifest JSON; legacy embedded spectrum rows remain readable but are omitted on writes |
| `ProjectQuantCodec` | Invariant, round-trip TSV/GZip read/write with columns resolved by header name |
| `ProjectSpectrumCodec` | Streaming spectrum JSONL/GZip read/write and physical-line error reporting |

The `ProjectStore` facade retains validation and atomic publication. It prepares
one temporary ZIP, closes it, verifies it with `SavedFileCheck`, then publishes
through the operation context's single commit. Cancellation/refusal preserves the
destination and the existing operation-aware manifest rollback behavior.

## Compatibility rules retained

- Version 1 can embed spectrum rows in the manifest. A transitional sidecar, when
  present, takes precedence exactly as before.
- Version 2 declares the exact spectrum row count. Missing metadata, negative
  counts, missing required rows and mismatched/truncated sidecars are refused.
- Future versions are refused on both read and write. A stale scan-only
  concordance audit is refused rather than shown as measured zero agreement.
- Old quant headers remain readable by column name. Absent later columns retain
  their existing legacy defaults; sentinels and round-trip floating-point values
  are not changed.
- The writer keeps its existing six-isotope ceiling and refuses a manifest that
  requests more than the table can store.
- FCF export uses the production `FamilyClusterFile.Save` path and its staged
  verification. No migration script produces a collaborator file.

## Curation format 2 (Z15b1, D78)

A version-3 project stores its curation as curation format 2 in `curation2.json`.
Format 2 is a root JSON array holding one document, in this order:
`FamilyQuantCurationFormat` (2), `Notice`, `Parts` (the names of the parts that
shaped the file), `Sections` (one shared table per part, written once) and
`Curation` (the FamilyClusterFile body as the parts shaped it). `curation.json`
keeps only a one-property notice object. FamilyQuant 2.1.10 reads that notice as
an empty curation, goes on to `manifest.json` and refuses the project with its own
message: "is a version 3 project, and this build understands up to version 2.
Update FamilyQuant to open it." (checked with the real 2.1.10 Core and in
`--test-curation-format2` with its reader transcribed). A format-2 text given to
2.1.10 as a .fcf stops at the first token (root array, not an object).

The export "Families for FamilyQuant 1.5 (.fcf)" stays curation format 1, so 1.5
and 2.1.10 open it (DU7). Every reader opens both formats: `ReadJsonUtf8` and
`FromJsonText` send a text whose first character is `[` to the format-2 reader. The
canonical content (`ToJsonText`, `JsonContentSha256`, `DeepCopy`) stays format 1,
so every hash and fingerprint is unchanged.

Parts. Each part is one sealed class in its own file under
`FamilyQuant.Core/Storage/CurationFormat2/`, deriving from `CurationFormatPart` and
marked `[CurationFormatPart("name", order)]`; `CurationFormatRegistry.Parts` finds
them, so no part edits another's file or a shared list. A part may prepare a shared
section before writing, add converters and type-contract changes for the body, and
rebuild what it left out after reading. Its converters live only in the options of
one format-2 write or read (`CurationFormatRegistry.CreateOptions`), never in
`CurationJsonOptions` or default options, so a gate that hashes
`JsonSerializer.Serialize(...)` sees the same bytes. On read only the parts the
file lists run; a part the build does not know, or a format above 2, is refused
with "Update FamilyQuant to open it." The test helper
`CheckCurationFormatRoundTrip` proves a part changes the wire only.

## Native commands

Feature-owned routes are in `Program.Commands.ProjectFormats.cs`:

```text
FamilyQuant.Tests.exe --test-project-formats
FamilyQuant.Tests.exe --export-fcf-compatibility <source.fcf-or-fqproj> <new-output.fcf> <snapshot.json>
FamilyQuant.Tests.exe --fcf-compatibility-snapshot <input.fcf-or-fqproj> <snapshot.json>
```

The shared snapshot is also linked into `ReferenceGoldenHost`. In that host it
loads the frozen DLLs, not the current Core. It records ordered protein identity,
sequence, description, both grouping IDs, annotation, XICUser, locks and grouping
settings. The helper refuses to overwrite an existing export or snapshot.

The lane built the host with the read-only reference root and isolated output/obj:

```text
dotnet build FamilyQuant.Tests/ReferenceGoldenHost/ReferenceGoldenHost.csproj -c Release -v q -o D:/FamilyQuant-data/out-floater/X7/frozen-reader -p:BaseIntermediateOutputPath=D:/FamilyQuant-data/out-floater/X7/frozen-obj/
```

The loaded `FamilyQuant.dll` was compared byte-for-byte with
`D:/FamilyQuant-data/reference-1.5.0.6/app/FamilyQuant.dll`. Its assembly identity
is `FamilyQuant, Version=1.5.0.0`; that does not replace the frozen-directory/hash
provenance with an inferred version number.

## X7 bounded evidence

The source-schema contract check was red before extraction (51 passed, 1 failed).
After the split, focused format checks passed 53/0/0. They exercise old/new
round trips, version-1 embedded rows, version-2 sidecar loss/truncation, future
version refusal, stale concordance refusal and the isotope ceiling.

The 2.0 writer exported the evidence-rich Ana FCF and the stored Bjararaca
project. The exact frozen reader opened both. Complete family snapshots from
source, current reader and frozen reader are byte-identical:

| Source | Proteins | Stored User families |
|---|---:|---:|
| Evidence-rich Ana | 63,554 | 40,407 |
| Bjararaca project | 584 | 312 |

Two exports from each identical source produced byte-identical FCF files and
snapshots. This proves the checked family/annotation/quant fields, ordering, locks
and grouping settings. It does not claim a 1.5 resave preserves fields introduced
only in 2.0.

The packet is `D:/FamilyQuant-data/out-floater/X7`, with `before`, `exports-1/2`,
`current-1/2`, `frozen-1` and the retained frozen host. Inputs, frozen binaries,
shared caches and golden snapshots remain read-only. Existing N5 storage proofs
are re-executed after the source split; stale source hashes are not ignored.

The native receipt rerun passed 73/0/0 and receipt validation passed 23/0/0.
Import renewed the 43 ProjectStore implementation bindings while retaining all
prior OPEN-only routing mappings. No previously unproved row was promoted:
counts remain 72 VERIFIED, 5,388 OPEN and 296 approved drops. The old receipt is
preserved under `receipt-import/previous-N5.json`; executed assemblies, checker
and split source snapshots are under `native-receipt-final/executed`.

The READY restore normalized source line endings to CRLF, correctly invalidating
the earlier exact-byte receipt. A fresh native proof on the final bytes passed
73/0/0 and was imported from `native-receipt-ready`; its executed source/assembly
snapshots are retained there. The initial quick failure is recorded, not hidden.
The corrected final quick suite passed 2,790/0/17 and frozen golden passed 4/0/0;
Release build had zero warnings/errors. Logs are `quick-ready-retry-summary.log`
and `golden-ready.log`. No guard was weakened, and the full suite was not run by
this lane.
