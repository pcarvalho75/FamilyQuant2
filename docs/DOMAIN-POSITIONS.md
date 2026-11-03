# Domain positions contract, J4a

`ProteinEvidence.Positions` is an optional `FamilyQuant.Evidence.Positions.ProteinPositionEvidence`.
It lives inside each protein's persisted evidence, so the existing FCF/project serializer carries it.
Null means not fetched. A non-null record with empty lists means the sources returned no annotations.

All coordinates are 1-based and inclusive on the source's canonical sequence. Consumers must compare
`SequenceSha256` with the uppercase protein sequence's UTF-8 SHA256 before projecting peptides.
Sequence length alone does not establish coordinate compatibility. An isoform or changed sequence
must be reported as unavailable until a coordinate mapping exists.

- `Pfam`: one `PfamDomainPosition` per hit, with accession, name, model, enclosing Start/End and all
  `Fragments` (Start, End, source Status). Repeats are distinct. A gap between fragments is outside
  that domain, even though it lies between the enclosing Start and End.
- `Features`: UniProt Chain, Peptide, Propeptide and Signal annotations, with description, feature ID,
  nullable Start/End, source boundary modifiers and evidence references. Consumers may only treat
  exact coordinates as exact. Overlapping products are annotations, not additive signal buckets.
- `DomainSource` and `FeatureSource`: provider, release, UTC retrieval timestamp and every response's
  URL and SHA256. A paginated InterPro reply keeps every page. An unsuccessful fetch publishes none
  of the new evidence. No network access is needed after saving.

This first commit defines types only. Fetching and parser/round-trip tests follow in J4a.
