# Domain positions contract, J4a

`ProteinEvidence.Positions` is an optional `FamilyQuant.Evidence.Positions.ProteinPositionEvidence`.
It lives inside each protein's persisted evidence, so the existing FCF/project serializer carries it.
Null means not fetched. A null DomainSource means domain positions have not been fetched; a non-null
DomainSource with an empty Pfam list means InterPro returned no matches. FeatureSource independently
records the processing annotations obtained with the UniProt metadata batch.

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

## Fetching

The broad metadata request stays batched (500 accessions) and requests the UniProt source sequence
and the four processing feature types together with the evidence. Remote InterPro requests are
bounded by `positionAccessions` on `EvidenceFetcher` / `EvidenceFetchSession`; the default empty scope
never scans the database. `PositionFetchScope.FromMappedPeptides` reads the existing production
mapper's compact protein hits. The Families screen uses that scope after Compute. The optional
"Include selected families in position fetch" checkbox adds all members of explicitly selected
current families. It defaults off. No grouping or number changes.

InterPro lookups use at most four concurrent proteins. Each reads InterPro's own sequence metadata,
requires exact sequence identity with the UniProt source, and follows every Pfam page. Every source
reply's URL/hash is stored. Missing releases, a release change, a missing page, bad coordinates or a
source sequence mismatch fail the transaction. Cancellation or a late failure publishes none of
the new evidence. A broad metadata refresh retains older domain positions only when the canonical
sequence hash still matches. The fetch result/summary state requested and completed counts and time.

The sources are the [InterPro API](https://interpro-documentation.readthedocs.io/en/latest/api.html)
and [UniProt processing feature fields](https://www.uniprot.org/help/return_fields).
Fixtures run with `--test-domain-positions`; `--test-evidence-fetch-atomic` covers the surrounding
protein/reference-panel transaction. The native command
`--domain-positions-live <input.fcf> <new-output-dir> <comma-separated-accessions>` selects validation
proteins from an actual database, runs the production fetch, saves `positions.fqproj`, reopens it
offline, compares all evidence and checks the original input hash. It writes `validation.json`.

J4a live validation selected P30431, P0DM77 and O93517 from the actual mission FCF. All three fetched
with matching source/database sequences; the saved project reopened with identical full evidence
and the source stayed byte-identical (4/0/0). Artifacts:
`D:/FamilyQuant-data/out-assay/j4a-live-01/`. This is a bounded live validation, not the whole-database
analysis. Per the desk, OP performs the one analysis fetch on the computed project's scoped proteins.
