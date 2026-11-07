# Panel-entry identity in curation format 2

The registered part is `panel-entry-identity`, order 40. Its converter handles one `PanelEntry` at a time. The canonical/default serializer, fingerprints and streaming deep copies do not use it.

The entry keeps its top-level `Accession`. Equal `Evidence.Accession` and `Evidence.Positions.Accession` values are omitted and rebuilt from that top-level value. Different, empty or null legacy values remain explicit so reading never changes their meaning.

Valid position hashes are omitted and rebuilt using `ProteinPositionFetcher.SequenceHash(Sequence)`. Its normal uppercase spelling needs no metadata. The optional `$panel-identity-lower-hash` flag preserves a valid lowercase spelling. Mixed-case, stale, empty or null hashes remain explicit; replacing stale hashes could incorrectly make coordinates current. `SequenceLength` and the position annotations are retained.

Fallback serializer options remove only this item converter and retain every other part's converter and type modifier. The part has no shared mutable state and makes no changes to the source objects. Files from before this part remain readable because their active-parts list does not enable it.

`--test-panel-entry-identity` uses the format contract's round-trip hash helper, legacy/null/stale cases, shared evidence objects, a nested provenance converter and concurrent writes/reads. The 128-entry synthetic fixture changes from 317,261 to 294,001 bytes, saving 23,260 bytes. This is a fixture measurement, not a live-data estimate.
