# Family sensitivity contract, J5e

`QuantManifest.FamilySensitivity` is an optional, null-omitted FamilySensitivityResult. It contains
actual computed cutoff variants with source fingerprints and source/cutoff metadata, each holding
the three SharedPeptidePolicy results. Each family's signal and ShareOfMeasuredSignal are copied
from the existing production family route. The denominator is measured peptide signal on the primary
calibrated basis, counted once. Overlapping full counts can exceed 100% and must not be summed.
This is separate from a normalized composition of credited family signal.

The two additive policy results may also carry FL's FamilyRunSharesResult. Full-count composition
is undefined, so that policy does not pretend to have a normalized per-run composition.
Missing variants are absent. A table must show them as unavailable; only a successfully computed
variant can establish a zero signal for a family absent from its complete result.

The study consumes current stored quantitation and an explicitly selected saved comparison project.
It never fabricates a lower-cutoff extraction by filtering a project that omitted those precursors.
Database/grouping, scientific input fingerprints and extraction settings must agree. Sources and
native cutoffs come from each computed project's own DeNovoInputs. Original files are read-only.
The existing primary quantitation and family results are unchanged by storing this optional study.

## Program table and computation

Results has a Sensitivity expander. Calculate current result derives all policies from the current
stored quantitation; Compare saved project reads a computed `.fqproj` with another recorded cutoff.
The operation stages the entire new study and publishes it once. Cancellation, incompatible inputs,
source edits and a file read failure retain the previous result. Export table writes exact stored
signal, denominator, fraction, policy, cutoff/source label and source hash as invariant CSV.
The project is marked changed when a study is stored, so saving keeps the table offline.

The shared-policy route is the one used by the existing SHARED-PEPTIDE-EFFECT study:
FamilyEnvelopeQuant.PreparePeptides, AttributePeptides and BuildFromAttributedPeptides. Preparation
and reference calibration happen once per variant. FL's exact
FamilyCompositionStudy.BuildRunShares computes per-run additive-policy results from those same
prepared peptides. It deliberately refuses overlapping full counts, so that policy stores no
normalized per-run composition. The pooled sensitivity table uses measured-peptide shares for all
policies, with full-count overlap stated explicitly. Family-world-only credits are never added to a
family numerator; measured peptide signal is the common basis, including unattributed signal.

Compatibility checks use the exact protein/group/name fingerprint, recorded scientific input hashes
(RAW hashes remain paired to normalized run names), caller/source file hashes, source priority and
score field, isotope count, minimum points, extraction tolerances and recorded run TIC values. The
recorded family identity gate must also agree; the actual attribution provenance is stored beside
each variant. If no gate was recorded, the program's current default is used and recorded. The
cutoffs themselves may differ. Paths can differ when input copies have identical source identities.
Missing provenance refuses comparison; other unrecorded settings cannot be verified. No lower-cutoff
precursors are invented by refiltering a project that did not extract them.

FamilySensitivityTable reads the optional stored result without recomputing it. Unknown conditions
stay unavailable; an absent family can be zero only inside a complete computed variant. A stale
grouping or current-quant fingerprint hides the saved study until it is recalculated. Signal/share
disagreement in a corrupt study is refused, including after offline reopen. Study storage is
null-omitted so existing projects acquire no invented sensitivity result.

`FamilyQuant.Tests.exe --test-family-sensitivity` covers policy equality against the production
family table, two actual variant inputs, missing/corrupt/stale data, cancellation, input preservation,
locale-independent CSV, exact project round trip and native table/comparison behavior in both themes.
These are synthetic fixtures and saved-project checks, not a new RAW extraction or biological claim.

J5e validation before the policy-label correction: Release build had zero warnings/errors; focused/native checks passed 20/0/0,
including both themes, actual saved-project loading, exact study reopen, no row mutation, source/gate
incompatibility and preservation of the previous table on failure. Stored-project golden passed
4/0/0 with byte-identical analytics_summary.csv, composition.csv, evidence_pack_numbers.csv and
families.csv. Logs: `D:/FamilyQuant-data/assay-j5e-final-build.log` and
`D:/FamilyQuant-data/assay-j5e-golden.log`; fixture projects are under the isolated assay TEMP
`FamilyQuant.Tests/family-sensitivity` directory. No builder quick suite was run under desk M12.

Review correction: exported policy values use the same SharedPeptidePolicy.DisplayName wording as
the Results grid. The added assertion failed first at 20/1/0 with enum identifiers, then passed at
21/0/0 with the interface labels. Release remained zero warnings/errors and the numeric CSV locale
comparison passed. Logs: `D:/FamilyQuant-data/assay-j5e-policy-red.log`,
`assay-j5e-policy-green.log` and the matching build logs.

Gate correction: sensitivity content/actions live in the section's own ControlTemplate and name
scope. The unchanged Results four-control inventory failed red at layout 171/1/0, then passed
172/0/0. Results model checks passed 6/0/0 and sensitivity checks 25/0/0, including both themes,
template ownership, the real calculate-button event, saved comparison and atomic failure behavior.
Release remained zero warnings/errors. The layout assertion was not changed.
