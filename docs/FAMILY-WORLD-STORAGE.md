# Stored family-world evidence

`FamilyWorldFetcher.FetchAndStoreAsync(file, confirmedTask, operation, handler)` fetches every
in-scope family found in non-contaminant target evidence. It asks for reviewed global members,
then all members of the confirmed clade for each family. No family or scope word is embedded in
the rule. Public family annotations must match the requested membership before an entry is kept.

The complete `FamilyWorldSnapshot` is stored as `EvidenceSource.FamilyWorld`. It carries the
confirmed task and scope, clade, sorted family names, accession-sorted sequence/evidence entries,
organism and lineage taxon IDs for every entry, query receipts, release and content hash.
An entry records whether it came from the reviewed panel, the clade query, or both. Full source
family memberships are retained; this fetch does not choose a protein or perform attribution.

Every page must have a stable release and announced result count. Duplicate pages, missing
sequences or taxonomy, an unexpected review status, a clade mismatch, a changed entry or an
incomplete query refuse the entire fetch. The input is checked again before one final commit.
Cancellation or failure leaves existing file state intact. The original input file is never
overwritten by the native checker.

`FamilyWorldFetcher.Stored(file)` checks the stored content hash, completeness and task/source
binding without network access. The world is independent of the curation transaction, so adding it
does not invalidate an existing recipe. Its optional null property keeps old hash payloads
unchanged, and the curation replay hash excludes a non-null world explicitly. The complete world
still participates in saved-file verification and persists in both FCF and project containers.

The B5 reader must use only `snapshot.Families` for eligible family credits and retain full
membership ambiguity. D11's minimum tag length is 9 residues. Length gating, reversed decoys,
attribution and per-family error estimates belong to B5/B6; this storage API makes none of those
claims.

Native commands:

```text
--test-family-world-fetch
--family-world-fetch <input.fcf> <new-output.fcf> <confirmed-scope>
```

The live command derives the clade with task discovery, confirms the supplied scope, fetches the
world, saves it, reopens it and performs a network-free second save. Both files must be
byte-identical. Its report records source/saved sizes and growth, counts, taxonomy completeness,
query receipts, release, content/file hashes and saved-file checks. Synthetic fixtures separately
cover interruption, paging, integrity and recipe compatibility.
