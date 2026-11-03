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
