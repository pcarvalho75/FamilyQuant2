# Quantitation panel

The Families side panel presents Full XIC first. PatternLab is an optional
secondary source for the separate Share of signal column. One shared-peptide
selector serves both sources. It keeps the existing enum indexes and callers.

| State | Visible choices |
|---|---|
| No quantitation | Full XIC next step and optional PatternLab load |
| Full XIC only | Full XIC denominator and shared-peptide policy |
| PatternLab only | PatternLab denominator, shared policy and Recalculate |
| Both sources | Both independent denominators, one shared policy and PatternLab Recalculate |

Full XIC changes apply immediately. PatternLab choices still require Recalculate.
An unavailable recorded divisor stays disabled with its reason; no item is
removed or renumbered. Missing TIC for any quantified run refuses the all-runs
TIC basis rather than narrowing it. The quantified-signal basis and the explicit
refusal remain visible. Full XIC details retain the quantity, shared bucket,
unattributed signal and conservation statement. PatternLab details retain its
recorded profile. Failures stay visible and clear when the quantitation detaches.

`CurationQuantitationPanel` keeps all named controls and its LoadRequested,
RecalculateRequested, ChoiceChanged and BasisChanged events. The curation view
continues to own file selection and forward choices. The panel listens weakly
to its presentation model and only changes visibility and source status.

Prepared peptides, cached homology hits, exact per-item grouping/annotation
snapshots, the latest-attach guard, FullXicReady and numerical/CSV service calls
are unchanged. Presentation state does not create a second quantitation route.

`--test-quantitation-panel [output-dir]` checks every source state, retained enum
indexes, native choice events, missing-TIC refusal, failure clearing and native
360-pixel captures in both themes. `--test-curation-quantitation` retains the
family share, annotation, cached regrouping and asynchronous-attach checks.
