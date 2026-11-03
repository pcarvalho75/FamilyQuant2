# Verified Save memory

Project Save writes curation JSON with the existing serializer options directly to its
UTF-8 ZIP entry. The validation and shell saved-state fingerprints use the same byte
sequence through a hashing stream. The public `ToJsonText()` behavior is retained.
Sequence, decoy, unassigned, lock, source and curation checks still run in their original
order, followed by the same verified publication and readback checks. Project curation
readback is streamed too; its legacy lock migration and unusual-encoding fallback remain.

Focused checks: `--test-streaming-save`, `--test-project-formats`, `--test-operations`.
The streaming check is registered once in DefaultSuite and compares exact JSON bytes,
SHA-256, verified ZIP entry bytes and changed-curation rejection in en-US and pt-BR.

The full-run computed copy was measured on DISCOTECH with 32 processors, no managed
heap cap and no Orion process listed in the before/during/after checks. The same
`ShellProjectOperations.WriteVerifiedProject` path was sampled every 20 ms during Save:

| Measurement | Before | After |
|---|---:|---:|
| Peak sampled managed use | 16,822,779,456 bytes | 7,137,354,432 bytes |
| Peak reported GC heap | 17,030,353,512 bytes | 7,468,319,888 bytes |
| Peak Save working set | 16,869,785,600 bytes | 7,379,410,944 bytes |
| Allocated during Save | 53,474,366,320 bytes | 23,609,776,856 bytes |
| Verified Save wall time | 75.10 s | 61.72 s |

Both saves passed the product file check and preserved the input SHA-256. Every stored
ZIP entry was byte-identical, including the 789,641,601-byte curation JSON. Compression
changed the curation entry's compressed size; ZIP timestamps also differ, so whole
container identity is not claimed. The exact 268,576-byte archive-size reduction equals
the curation entry's compressed-size reduction; entry order, Deflate method and
`CompressionLevel.Optimal` are unchanged, while stream write/flush boundaries differ.

The final Save entered with about 2.44 GB managed use and peaked during verification
project readback. The original project and reopened materialized curation/quantitation/
spectrum graphs still coexist; this change preserves all those integrity checks.
Getting close to loaded size would require sharing the verified readback result or
streaming semantic validation without retaining another full graph, a larger validation
redesign. The remaining roughly 7.38 GB process peak can still matter on a machine with
little free memory. These measurements exclude UI/plot caches and prior attribution.

Evidence: `D:/FamilyQuant-data/out-shell/J48-remote/entry-byte-comparison.json` and
`J48-save-{01,after02}/save-metrics.json`. The final comparison is
`entry-byte-comparison-final.json`; the first writer-only after run is retained separately.
The worker-only `--measure-product-save`
command rejects a managed-heap cap and records its actual process and memory samples.
