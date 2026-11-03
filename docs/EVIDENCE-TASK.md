# Task discovery from a UniProt file

`EvidenceTaskDiscovery.DiscoverAsync` reads the non-decoy, non-contaminant rows of the open file.
The database's exact, case-sensitive `contaminant_` prefix is ignored. Each remaining row must have
a UniProt `sp|` or `tr|` accession and an `OS=` name followed by a positive `OX=` taxon identifier.
Unsupported headers are refused with the offending identifier; the program never guesses a taxon.

A sequence with joined headers keeps every distinct parsed taxon. It remains one target protein;
all its taxa take part in the common-clade proposal. Repeated aliases of the same taxon retain
the first source name. Keyword votes still count reviewed accessions once. The original joined
description remains stored unchanged, and confirmed proposals store all the discovered taxa.
Family-world own-species removal reads every direct OX= taxon of each target, both when collecting
sample taxa and when checking a joined world target against them. The first-taxon compatibility
property remains available; it is not the membership rule for removal.
The older native `--family-world` study uses these same collection/membership helpers for
species and genus removal, including its provider taxonomy-list and accession fallbacks.
`--test-family-world` pins the study predicate on a secondary joined taxon.

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
ordinal keyword order. A second suggestion can come from the most common phrase in the reviewed
proteins' stored tissue-specificity text. It counts each reviewed accession at most once per phrase,
ignores negated clauses and parenthetical annotations, and uses deterministic ordinal tie-breaking.
When duplicate records for one accession disagree, only phrases present in every copy can receive
that accession's vote. General grammatical words separate phrases; no tissue name is hard-coded.
The phrase is a review suggestion, not proof of sample origin. If no keyword is eligible, a tissue
phrase may be the only suggestion; if neither exists, no scope is invented. Taxonomy and vocabulary
must report the same nonempty release. The proposal separately records releases of the stored
keyword evidence.

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

In the grouping review window, the Evidence tab reads database taxa and the scope proposal.
It shows supplied taxon identifiers, the clade, total targets, ignored contaminants, keyword
counts and their distinct reviewed-accession denominator. Stored keyword-evidence releases
remain separate from the taxonomy/vocabulary release. No per-taxon protein count is inferred.
Edit or keep the suggested scope and choose Update proposal to confirm it through Core.
Apply grouping stores that confirmation with the recipe. Editing the scope again disables
Apply until the proposal is updated. Discovery failure leaves manual scope entry available;
closing the window cancels discovery and rejects late facts. `--test-evidence-task-scope`
checks the native edit/confirmation path, saved recipe replay and both themes.
