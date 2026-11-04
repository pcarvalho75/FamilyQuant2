# J47 selected-input validation

This study compares the three independent, default-OFF experiments described in
[the options contract](HOMOLOGY-SCIENCE-OPTIONS.md). Paulo chooses defaults after
science review. This work does not run full Compute or extract RAW data.

The native study inputs are the five actual J38 captured searches in
`D:/FamilyQuant-data/out-assay/j38-measure-01/discotech-capture05/search-captures-05`.
The old mapper's completed certificate is
`j38-measure-01/workhorse-replay05/search-replay-05/validation.json` under the same
worker output root. Captured database, query, option and result files must match
their recorded hashes before replay. Calls 1/4 are identified; calls 2/3/5 are
de novo curation, residual-world and leave-species-out known-answer searches.

Quantitation is reused from
`j38-measure-01/discotech-clean10-after/search-after-10/computed.fqproj`.
This is the **earlier J38 study curation**, membership
`42A6AE21B65347249B2F9711EE759B2A87EDF297472E2A1C6355DBB707C21E71`:
metalloprotease UserID 40421, mean assigned share 39.142608%. It is a different
cohort from Ana's delivered curation, membership
`D31CFBBFC32AB7D3262EBD442FDCC44CDE38D13C468719A0A74C5ED754355A2C`,
UserID 40425, mean assigned share 38.598758%. The different working memberships
change unique-family credit and its denominator. The historical J47b table's
share changes must not be quoted as her delivered-project headline.
Its SHA256 is `4F913079F0568C528B8BE42E4DF748E69FEBE67E23DA34E91709F36889AF670B`.
The native study checks that the input project remains unchanged. It uses the
J28 compact-cache codec/hot path and J38c prepared-peptide attribution, then the
existing per-run family share and Database gap aggregation methods.

The post-master-merge five-call OFF proof is
`D:/FamilyQuant-data/out-assay/j47-merged-off-capture-replay/validation.json`:
expected 5, completed 5, AllEqual true. This compares all scientific result fields;
work diagnostics are outside that scientific shape. The cached OFF completion
also equals the available stored family shares and Database gap summary. The
effective-panel bundle is unavailable in this one-to-one project and remains
unavailable, rather than being invented.

All experiments use the desk's M122 settings. The length experiment explores at
0.8, tightens only, requires 100 distinct tags per length and a plus-one empirical
decoy/target criterion of 0.01, with explicit abstention and no FDR claim. The tag
experiment allows one rewrite, scores the original tag with PAM and uses
per-position medians over accepted PEAKS rows, with unit weights for missing or
all-zero profiles. The score tier keeps best-score ties, margin zero.

The actual residual cohort may change after curation. The calibrated experiment
refits that whole changed world cohort; other experiments search only newly
required residual queries. Captured call-3 counts and actual residual-world
counts are reported separately. Gate counts are distinct normalized queries;
native Database gap peptide counts retain run/sequence/charge measurement grain.
Neither is interchangeable with envelope-weighted decoy/credited signal or
strict known-answer wrong/landed rates. An abstained length bin's exploration
observations are not admitted gate hits.

J42 is an unchanged protein-family neighbour path outside HomologySearch. Its
prior native audit and provenance are copied as references, not recomputed or
presented as new independent biological truth. In particular, that prior audit
has its own source-project cohort; it is not silently relabelled as this replay's
quantitation cohort.

The required J43 proof is
`D:/FamilyQuant-data/out-assay/j47-science-01/j43-identity-proof/proof.json`.
The native project proof reports one-to-one identity, equality of every non-label
headline cell and an unchanged source project. The final comparison additionally
calls `ReportCompositionBasis.Select` for each option and retains selected panel
shares, source membership and native headline CSVs. Original UserID shares stay
explicitly identified as their own unit.

The tag measurement started before wording-only fix `cb9562f`. Its original
receipt/audit files retain that historical wording. The current rule is named
"adjacent order swap (same pair mass)"; the earlier wording did not change the
gate, confidence weights, variant sequence, PAM scoring or admission policy.
Historical receipt bytes are preserved. The options contract explains the
missing-fragment interpretation and the absence of a positional confidence cutoff.

All artifacts are produced by native FamilyQuant test commands. The study command
supports verified reuse of an already measured packet for cached completion;
the comparison command records input and producer/Core assembly hashes. The
exact syntax and exclusions from ordinary gates are in the options contract and
command inventory. An ordinary fixture run does not regenerate this study.

The native J47b three-packet comparison completed with exit zero and unchanged
source at `D:/FamilyQuant-data/out-assay/j47-science-01/j47b-result-02/`.
`decision-table.md` contains the requested side-by-side table. Score tier changes
individual family shares even though the measured envelope headline totals stay
unchanged; its known-answer wrong count falls while the number landed stays the
same. Length calibration loses known-answer coverage entirely at these settings,
so zero wrong answers is not evidence of improved accuracy. The packet retains
all numerical rows, denominators, original runtime sources and native J43 shares.

Desk M135 moved the tag measurement and weighted-seed validation to J47c. That
measurement is still running under its existing guard. J47b is a result for
review, not a science approval or a choice of production defaults.

The actual delivered-curation score-tier row is the ONE full Compute validated
for J47d, commit `c98070333ef4472aac0f6d6396f5fdc5ec97ead8`:
`D:/FamilyQuant-data/out-assay/j47d-01/actual-proof-01/before-after.md` and
`validation.json`. Its whole native family-share object exactly equals the
certified-hit rejoin onto that delivered curation. Metalloprotease changes from
38.598758% to 38.504225%, rather than the historical J38 row. Opening the delivered
project on master preserves its original headline; new score-tier credit applies
on explicit Compute. These distinct cohorts and the actual J47d row are retained
for the 2.1.1 notes.

For a labelled historical table, add `--reference-project <delivered-project-copy>`
after the comparer arguments. Both generated tables then print each project's
native membership SHA, leading-family UserID and assigned mean from its stored
results; the wording is not a numerical constant substituted into the output.
