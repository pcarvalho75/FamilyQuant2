# Experimental homology science options (J47)

All three options are off by default. `HomologyOptions.Science == null`, or a
clone with all experimental flags disabled, preserves the J38 search and its
serialized options/results. Identified queries retain their original rule.
Enabled callers explicitly name `Identified` or `DeNovo`; the role is never
inferred from a tag's letters or an organism.

The contract separates a decoy-calibrated length gate, a confidence-weighted
isobaric identity gate, and a PAM score tier. Each receives its own implementation
commit and fixtures. Scientific parameter choices are under desk review; no
option is being promoted to a production default.

Desk ruling M122 fixes the first experiments: length floor 0.8 (tighten only),
explicit abstention below support, per-search/per-length support and gate, no FDR
claim; at most one rewrite, original-tag PAM, per-position median confidence over
accepted PEAKS rows of the same sequence, unit weights when missing with coverage
reported; configurable score tier starting at margin zero.

The score-tier implementation preserves the original target-decoy competition
(including conservative ties), sorts the same way, then keeps `score >= best -
MaximumPamScoreDrop` using integer arithmetic. The existing BestScoreOnly rule and
top-hit limit still apply. Explicitly identified queries are unchanged. Query
audit reports target/decoy proteins reaching the gate and multiplicities before
the tier, after the tier, and after the top-hit cut. J28 cache keys append enabled
science settings only; the default/OFF byte stream remains unchanged.

Length calibration uses one observation per distinct normalized tag. At each
candidate gate a query is a target winner only when its best target identity
strictly beats its best decoy; an identity tie goes to the decoy. The observed
ratio is `(decoy winners + 1) / target winners * (target proteins / decoy proteins)`.
The first candidate is the greater of the nominal gate and the 0.8 exploration
floor; higher candidates are the original-length integer-identity fractions.
The smallest supported qualifying gate is selected separately per length. Fewer
than 100 distinct tags by default, missing exposure, or no qualifying gate means
an explicit empty-hit abstention with a reason in the experimental audit.
This is exploratory calibration, not an FDR guarantee. Floor results are reused;
only queries at genuinely tighter gates are searched again, with target-decoy
competition recomputed so a previously suppressed opposing hit can win.

The tag gate enumerates the original plus every permitted single rewrite, never
compositions of rewrites. N/GG and Q/AG (both AG orders), K/Q, and one adjacent
residue-order exchange are separate audited alternatives. Expansion splits the
original residue weight; contraction sums its two weights; an order exchange
moves each called residue's weight with it. Total confidence weight is preserved.
Per-position accepted-source medians are percentages; a missing or all-zero
profile uses uniform weights and reports that confidence was not used.

The adjacent rewrite is audited as "adjacent order swap (same pair mass)".
Exchanging two adjacent residues conserves the pair's summed mass; the two
residues need not have equal individual masses. It models the ordering ambiguity
when a fragment between those two positions is missing. The current option
enumerates every adjacent nonidentical pair: no per-position PEAKS confidence
cutoff restricts which positions can swap, including high-confidence positions
or tags without confidence profiles. Confidence still weights the aggregate
identity gate. This remains an off-by-default experiment.

The gate is the maximum ordered matched-weight/total-weight fraction over those
variant windows. A conservative count bound with a positive-sum rounding margin
permits the same lossless diagonal filter; the exact weighted comparison decides
admission. Unsupported bounds use the complete scan. PAM always scores the
original tag and only full original-length protein windows. The audit retains
each gated protein's winning variant, rule, gate offset/span and weighted fraction
separately from the scored PAM offset/substring. A protein too short for the
original PAM query remains unavailable, even if a contracted gate variant fits.

PAM30MS remains the original-query, full-window score. A gate can have another
best alignment, as in the existing identity/PAM split. The tag option reports
its gate variant and offset separately. Score tiers operate after target-decoy
competition and before the existing top-hit limit; protein multiplicity is
reported before/after the tier and after the top cut. Family multiplicity and
shares use the existing attribution/shared-peptide policy.

Validation uses the five recorded actual J38 searches and their verified old
results. Off parity must compare every scientific result byte. Option-on
experiments replay these inputs and reuse quantitation, curation attribution and
the stored world/known-answer data; they do not run full Compute. J42's protein
family LOO is a separate path and is reported as invariant when that path and
its inputs are unchanged. De novo correctness is measured on the native
PSM/tag known-answer pairs and the stored Database gap rules.

The native `--replay-current-homology-capture` command verifies captured file
hashes, replays the current engine and compares every scientific result byte.
With all options OFF, all five verified J38 calls passed: see
`D:/FamilyQuant-data/out-assay/j47-off-capture-replay/validation.json`.

Cached world APIs accept complete, verified search-result dictionaries and fail
on a missing required query. `RunLengthsFromResults`, `KnownAnswersFromResults`,
`CreditFromResults` and `CompleteLengthsFromResults` reuse the original aggregate,
error-rate and composition code. Native fixture comparisons preserve every pass
and summary byte. These entry points do not extract RAW data or silently treat a
missing search as no signal.

`--homology-science-study <config.json>` is a selected-input native study command.
It verifies all captured database/query/options/result files against their recorded
hashes and requires the completed five-call frozen-reference certificate. It
loads the saved quantitation, feeds replay hits through `CurationHomologyCache.FromReplay`,
verifies ordinary J28 hot-cache reuse, then uses J38c's prepared-peptide attribution,
the existing per-run family shares, explicit unobserved panel rows and the native
world/known-answer composition. It never extracts RAW data or calls full Compute.
OFF completion must equal the available stored quantitative views, not merely
the search hits. Unavailable effective-panel output remains unavailable.

`FromReplay` rejects a different current database, incomplete query coverage and
invalid protein/offset/original-span matches before writing the native compact
cache. Its focused fixtures also compare native attribution and cache reuse.
Cached world completion rejects null or missing required search-result inputs.

The optional `ReuseResultsDirectory` reuses an already completed measured packet
only for the same project SHA and science settings, and verifies every reused
scientific result hash. This permits adding diagnostics without repeating the
expensive tag search. The replayed result audit is retained. If curation changes
the residual cohort, the world calculation searches any new queries; a calibrated
length gate refits that whole affected world cohort because its support depends
on the cohort. `actual-world-results.json.gz`, `actual-world-length-gates.json`
and `actual-world-gate-summary.json` distinguish this cohort from captured call 3.

Unsupported length bins retain the exploration observations in their audit, but
have no applied gate. They are excluded from the native study's admitted target
and decoy gate counts. Abstentions and their reasons remain explicit. Gate D/T
counts distinct queries with an admitted protein on each side and allows overlap;
it is not a statistical error estimate. Database-gap envelope ratios and strict
known-answer wrong/landed rates retain their separate native denominators.

`--compare-homology-science-studies <project> <off> <tier> <length> [tag] <fresh-output>`
requires three or four completed packets with the same project SHA, the approved separate
settings and retained identified/OFF search hashes. It writes a before/after
Markdown table and JSON for headlines, every family share and multiplicity.
Multiplicity uses the native `MapToFamilies` target-member join. It copies J42's
recorded audit/provenance and labels that path structurally invariant, not newly
recomputed. No output changes a project, a production default or the user's
chosen family grouping. Science review and Paulo's default decision remain open.

Desk M130 additionally requires the J43 headline basis. The comparison calls
`ReportCompositionBasis.Select` on each option's native shares/effective-panel
bundle and records `UsesPanel`, `IsIdentity` and whether the original share object
was reused without arithmetic. Native panel-headline CSVs and complete panel-unit
shares/source membership are retained separately. A one-to-one panel preserves
each option's measured share deltas under J43; a multi-source panel uses the
computed collapsed bundle and cannot silently fall back to the old unit.

Under desk M135, J47b reports OFF, score tier and length calibration immediately;
the tag replay and weighted-seed work are J47c. `decision-table.md` puts the
known-answer right/wrong/non-landed counts, gate target/decoy counts, gap errors,
J43 listed-family changes, original timed search cost and user meaning in one
side-by-side table. Original replay timers exclude import/loading/completion and
use certified identified-hit reuse on ON runs, so they are not a controlled
speed comparison. A family listed in any compared option remains visible when
it crosses the user's threshold.
