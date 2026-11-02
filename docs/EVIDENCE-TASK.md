# Task discovery from a UniProt file

`EvidenceTaskDiscovery.DiscoverAsync` reads the non-decoy, non-contaminant rows of the open file.
The database's exact, case-sensitive `contaminant_` prefix is ignored. Each remaining row must have
a UniProt `sp|` or `tr|` accession and an `OS=` name followed by a positive `OX=` taxon identifier.
Unsupported headers are refused with the offending identifier; the program never guesses a taxon.

The taxon identifier is resolved through UniProt's taxonomy API. The proposal retains the source
organism names and identifiers, the nearest shared taxonomic family, and the taxonomy release.
When no common family rank exists, the deepest shared ancestor is offered instead. This describes
the taxa in the database. It does not identify the species of an experimental sample.

All keyword counts come from the reviewed proteins already stored in the file. The denominator is
the number of distinct reviewed non-contaminant accessions, with each accession counted once per
keyword. Unreviewed annotations, outside panel entries and decoys do not vote. Conflicting keyword
observations on duplicate reviewed accessions are refused. Counts describe annotations, not protein
abundance or experimental identifications.

Functional scope is proposed as the most frequent keyword in UniProt's molecular-function category
(`KW-9992`). The category comes from the live keyword vocabulary; the code contains no biological
keyword list. All keywords, counts and provider categories remain available for review. Ties use
ordinal keyword order. If there is no eligible keyword, the proposal contains no scope. The program
does not substitute a guessed term. Taxonomy and vocabulary must report the same nonempty release.
The proposal separately records releases of the stored keyword evidence.

The distinction between functional, location, structural and technical categories follows the
[UniProt keyword API](https://www.uniprot.org/api-documentation/support-data). A more frequent
location or technical annotation therefore does not silently become a functional scope.

The person confirms or edits the scope. `EvidenceTaskDiscovery.Confirm(file, proposal, scopeTerms)`
returns an `EvidenceTaskChoice`. Pass it as `EvidenceApplySelection.ConfirmedTask`, with the same
scope in the classification options. The confirmation is stored in the recipe and participates in
the request and state fingerprints. A changed header or changed evidence invalidates confirmation.
Recipe replay uses the stored facts offline and rejects an altered confirmation. Earlier recipes
without a confirmation keep their existing hash path.

Discovery changes no file state. Cancellation, service failure, incomplete taxonomy, a changed
release or a source edit during fetching returns no proposal. The native fixture command is
`--test-evidence-task`; `--evidence-task-check <evidence.fcf> <new-report.json>` writes a read-only
live discovery report. Neither command creates an applied grouping.
