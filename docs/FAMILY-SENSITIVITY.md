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
