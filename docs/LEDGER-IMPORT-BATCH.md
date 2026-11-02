# Ordered ledger imports

Use one process for several registers, in the order given:

```text
FamilyQuant.Tests.exe --feature-ledger-evidence-import-batch <output> <register1> <register2> ... [--replace-existing]
```

The existing single-register command is unchanged. Each incoming register goes through the existing receipt validator. Every intermediate ledger state is applied and checked, preserving planned-test changes even when a later register reopens a row. Hashes and assertion logs are shared only within that transaction, with no persistent validation cache. The final reports are rendered once.

A register refusal stops before any register, backup or ledger is written. Explicit replacement permission and previous-register backups are retained. Conflicting backup filenames are refused. Commit writes use temporary files, and a failed commit restores files already replaced.

`--test-ledger-import-batch` covers the same stale-proof refusal, stopping before later inputs, an unchanged ledger and register on failure, ordered replacement, exact backup bytes, parity of final ledger/reports, and explicit replacement permission. The check also runs in the quick suite. Before implementation it returned 1 passed and 8 failed; after implementation it returned 9 passed and 0 failed twice, with byte-identical logs.

The real comparison used the 22 register names from gate `20261102-115935-FamilyQuant-gate`, with their checked-in input bytes at source commit `75f3476b344bc573e58f932f603fef546e036fa4`. Each comparison began with identical source, ledger and register copies. All 22 separate imports succeeded.

| Measured operation | Seconds |
|---|---:|
| 22 separate imports, complete loop | 729.615 |
| Sum of the 22 native process times | 727.348 |
| Ordered batch, first accepted comparison | 19.392 |
| Ordered batch, repeat | 14.292 |

Both batch runs matched the separate imports byte for byte: the ledger, all 47 register files, seven final report files, and all 20 previous-register backups. The ledger SHA256 was `97E56AC4CE5AC478C0C26D9248C63CDCAB0B84BD87F81392283553F2278CB1A3`. A corrupted real shared-receipt register, after a valid first register, produced the identical stale-implementation refusal and left the ledger and register files unchanged, including a target deliberately absent before the batch.

These are elapsed measurements on the shared machine, with file caches warming during the serial run. They are bound to the source and retained artifacts above. They do not measure scientific quantitation.

The retained comparison packet is `D:/FamilyQuant-data/out-assay/import-batch/`: `verification.json`, `serial-timing.json`, `input-manifest.json`, immutable `inputs/`, the two native binaries in `baseline-bin/` and `batch-bin/`, source copies, logs and reports. The final build and quick suite run after merging newer master; the timing comparison remains pinned to the earlier snapshot.

Final validation on master `d06cd239acada96f96bd09795575c50aaeb8e633` plus this change: Release build had 0 warnings and 0 errors; the quick suite returned 3,256 passed, 0 failed and 17 skipped in 161 seconds. No full suite or golden run was performed for this tooling change.
