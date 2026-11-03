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
