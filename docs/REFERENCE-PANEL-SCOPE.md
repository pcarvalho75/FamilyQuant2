# Reference panel scope

Fetch evidence stages protein annotations first. The user then chooses scope words or Full panel before the reviewed panel is fetched. Cancelling this choice retains the operation's original commit boundary.

A scoped panel records normalized scope phrases and the families for which whole reviewed membership is stored. The family universe is the same as the full route: families observed in database targets. It fetches scope families whole and scope-matching entries of the remaining families. Every family introduced by an in-scope reference is completed within that universe too, because coherence ranges and source counts need its out-of-scope reviewed members. Count and stream queries remain identical, bounded by URL length and the OR-condition limit, release checked, locally filtered and resumable.

Scopes are normalized with the classifier's letter/digit token boundaries. Quoted free-text phrase queries narrow the remote response; the existing local keyword/tissue matcher remains authoritative. Oversized queries and non-ASCII terms use the full route. UniProt's phrase-query syntax is documented in [its official text search manual](https://github.com/ebi-uniprot/uniprot-manual/blob/main/help/text-search.md).

Scope-aware search, Apply and recipe replay reject missing scope coverage. Steps that require the complete family universe reject a scoped panel and explain how to choose Full panel. Null coverage metadata retains the legacy full-panel meaning. Full stored entries are converted to scoped coverage only through an explicit fetch choice.

The first synthetic regression uses actual native neighbour search, compares complete serialized proposals and default ticks, and includes a case where omitting whole-family closure changes coherence flags. Timing receipts measure the fixture handler's fetch, parsing and filtering wall time and response bytes. They exclude live network latency and do not predict a live-data speed ratio.

The grouping review offers Fetch missing panel beside Update proposal. Widening asks only for new scope slices and newly needed whole profiles; narrowing reuses recorded coverage without downloading. A plain coverage message takes the Apply hint's place when the scope is not covered. Fetching updates stored reference metadata and clears the old review; Update proposal remains a separate offline action. Full panel stays available in the fetch choice.

Session metadata guards also use streamed part hashes. Comparing the source around expansion excludes ReferencePanel through serializer metadata, without copying or materializing that panel first. The curation adapter checks the original live source's hash before publishing its staged result, so in-place provenance edits are preserved too.
