# Import and Apply working memory

Z12p5f8 removes reference-table copies from operations that do not edit those tables. Scientific
content, provenance, stale-input checks and exact recipe replay remain unchanged.

The native family-list matcher owns a guarded stored-evidence view and ignores the interface's
detached project argument. The wizard now calls its panel-only overload. Other adapters still
receive a complete detached document, including a tested adapter that mutates its inputs.

Panel Undo retains a detached prior panel, subclass labels, complete recipe and both subclass
fingerprints, guarding the owning document and protein identities. The editor folds subclass
recomputation into that same Undo, so all of these fields restore together. Restore prepares every
clone before publication. Unrelated evidence and annotation edits survive panel Undo. Actual
evidence/grouping Undo retains its complete detached snapshot. Subclass-only publication uses this
lightweight snapshot too. Large panels use the existing bounded store instead of a complete string.

Apply's private provenance views detach metadata and the new search, borrowing the live reference
and family-world tables for read-only staging and hashing. The curation fingerprint still omits
`FamilyWorld` exactly as before. Selection/recipe copies detach every mutable list, record and
metadata field, reusing only immutable reference sequence strings. Serializer metadata and setters
preserve the default JSON contract, including null/empty values and public init properties.
Object-typed generic copies retain the default `JsonElement` behavior. No file format or scientific
threshold changes.

The approved oversized proof generates 1,078,306,552 JSON characters from 16,384 synthetic references
of 65,536 residues each. Native import, stage GC and the 20 ms private-memory sampler stay identical.

| Native wizard import | Before | Final binary |
| --- | ---: | ---: |
| Sampled peak private bytes | 6,661,517,312 | 2,307,448,832 |
| Elapsed seconds | 215.539 | 6.969 |

Peak private memory is 65.4% lower after the combined-Undo correction. An earlier after run measured
2,303,758,336 bytes and 8.129 seconds.
Both runs match the baseline scientific digest exactly:
`BF94925B030AA6B7C4F72100B34984A40AEFD6B59BD7B72E0223793D0E3F4669`.
It covers targets, grouping/locks, reference evidence and imported definitions, excluding run-specific
workbook/package provenance. Every reference and family is checked directly too. All generated large
scratch runs were verified and removed automatically.

The correction fixture allocated 6,752 bytes for panel Undo with a large reference table present.
Session staging fell from 9,036,512 to 1,144,880 allocated bytes; recipe copying fell from 33,756,992
to 1,818,648. A panel snapshot retaining labels, fingerprints and a recipe with large reference
sequences allocated 211,040 bytes without copying the source table or immutable sequences. These
thread-allocation measurements are separate from the oversized process-memory
measurement. Native Apply/replay/no-op/full Undo checks pass; real-project Apply peak measurement
belongs to the desk's copy. Results/report memory and report byte parity belong to Z12p5f9.

Run the import prefix after a Release build:

```powershell
$env:DOTNET_PROCESSOR_COUNT = '4'
$env:TEMP = Join-Path (Get-Location) 'out/tips-temp'
$env:TMP = $env:TEMP
$env:FAMILYQUANT_CURATION_PROOF_ROOT = Join-Path (Get-Location) 'out/curation-proof'
$env:FAMILYQUANT_CURATION_PROOF_LAST_STAGE = 'wizard-family-list-import'
./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --proof-curation-past-string-limit
```

These knobs affect only the opt-in proof process. Without them the full workflow and original
scratch default remain unchanged. The prefix explicitly does not claim the full f4 workflow.
`--test-curation-streaming-contract` covers bytes, allocation, copy counts and mutable isolation.
Native import, manual-family UI and evidence-fetch atomicity commands cover import, Apply/replay,
complete evidence snapshots and blocked-storage Undo failure/retry.

The editor correction is covered by an actual modal rule edit, native ApplyPanel and combined
subclass runner followed by one Undo. The full prior curation JSON, labels, recipe and fingerprints
restore exactly and subclass currentness returns. The regression failed on the initial f8 commit;
missing-label Undo, repeated detached restores and failed recipe-storage preparation are covered too.
