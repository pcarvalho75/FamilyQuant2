# Query residue coverage

The reviewed-neighbour gate requires at least 70% of the query protein's residues to occur in the
alignment. Count the non-gap letters in the aligned query row and divide by the full query length.
Gap columns inserted into that row do not count as query residues. Query letters opposite a target
gap remain query residues. This is the query-letter reading approved for A6.

Native `qrow` is the measured source. `alnlen` counts columns, including query gaps, and can be larger
than the query length. This native build's `qs` and coordinates can include unaligned flanks, so they
are not substituted for aligned-row letters. Native capture verified the row-letter calculation
against the reported `qcov` on the A2 cohort.

`NearestNeighbour.QueryAlignedResidues` stores the count. `QueryCoveragePercent` returns the bounded
percentage only for a count between zero and query length. Missing or invalid facts return null.
The interface must show that coverage was not recorded rather than inventing zero or clamping an
alignment-column ratio. `QueryAlignedPercent` remains a legacy diagnostic API and must not be shown
as residue coverage.

`NeighbourSearch.CoverageMethod` records `NearestReviewed.QueryCoverageMethod`. A search without
this method and validated residue counts cannot be reused as a current search. Refreshing creates
new facts without changing the old stored grouping on load. Historical recipe replay has an
explicit legacy validation path and omits null new fields, retaining its recorded hash semantics.

The current gate, chooser and classifier use the recorded query-residue count. Identity, ranking,
tie handling, domain rules and scope rules keep their previous meanings. The audit states the
percentage and aligned/full query counts and may also show alignment-column counts as a separate
diagnostic. The evidence window and Help use the same definition.

The native delivery `refresh-coverage` command uses a verified prepared evidence snapshot, recomputes
the program's current search, records neighbour changes and creates a new preparation for ordinary
review/Create/Save/Export. It retains the source evidence release and panel snapshot so the rule's
effect can be checked without confounding a new database fetch.
