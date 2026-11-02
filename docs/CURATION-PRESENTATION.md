# Curation presentation seam

C1a separates the curation presentation from its stored document. C1 adds the
shared operation host and dialog integration. N5/N6 own preservation acceptance.

`ClusterGenerationControl.ViewModel` is the `CurationViewModel` over the same
document exposed by `fcf`. It owns filtered rows, grouping choices, batch edits,
locks and the bounded Undo history. Display filters raise no document change.

`CurationViewModel.Quantitation` is the `CurationQuantitationViewModel` assigned
to `CurationQuantitationPanel.DataContext`. It keeps the prepared homology hits,
exact grouping snapshot, attribution, denominator state and existing CSV service
calls. PatternLab preparation uses a copy and publishes completed shares only.
The view retains its existing full-XIC APIs and private compatibility entry
points for the shell, golden checker and retained interaction tests.

C5 owns the inside of `CurationQuantitationPanel` and its presentation model
after C1a merges. Its current events are `LoadRequested`, `RecalculateRequested`,
`ChoiceChanged` and `BasisChanged`. The containing curation view handles file
selection and forwards current choices to the model. C1's remaining operation
adapters live in separate partial files and preserve these caller APIs.

The protein table stays outside the panel. Search IDs and decoys start hidden;
the search-ID toggle reveals the stored column. Locus displays the accession,
while its tooltip and document retain the full database identifier. Grid copy
and UniProt actions keep their existing independent identifier adapters.

`--test-curation-view` exercises a displayed native table, including annotation
editing and Undo. `--test-curation-layout` runs retained chrome and tooltip checks.
`--curation-screens <new-output-dir> <input.fcf>` writes four native client captures
at 96 DPI, both themes and both viewport sizes, with an input-hash manifest. It
does not extract RAW data or recompute quantitation.

`ClusterGenerationControl.Operations` resolves the window's shared host. Full-XIC
attribution waits behind a parent operation and exposes its pending completion
immediately. The queued operation starts in the host's idle notification so Save
cannot acquire the host while waiting for work that needs it. Replaced requests
use generation checks; cancellation never aborts a vendor thread.

Word grouping prepares its groups before one accepted commit. PatternLab load
and recalculation prepare on a document copy and reject stale membership before
publication. Online evidence also prepares on a copy at the curation boundary;
S9 owns the shared evidence-session implementation. Native alignment receives
the same Core context. CSV export uses `AppOperationFiles` staged publication.

The three browse IDs are `curation.patternlab`, `curation.proteins-csv` and
`curation.composition-csv`. `ShellMessages` keeps their operation results in the
status/Help record. Disconnected controls never open a native dialog.

`--test-curation-operations` covers parent-operation queuing, cancellation,
staged PatternLab publication, stale membership, native browse preferences,
headless refusal and one shared standalone overlay. These fixture checks do not
replace fresh RAW validation or prove every ledger observation.

## Family overview

C2 projects the current families beside the protein table. Selecting a family
shows its currently filtered protein rows; All proteins removes that selection.
These display choices raise no document-change event. The protein count is the
whole family's target count; its tooltip also gives the visible row count.

Names use `FamilyNaming`, with the same membership order and annotation rule as
the production exports. Editing a family name annotates all its target members,
including filtered members, in one Undo step. Lock changes are bound to the
stored lock set, so mouse, keyboard and accessibility toggles share one operation.

Family shares use the existing per-family values once, rather than summing the
same share repeated on its protein rows. PatternLab's column appears only after
its displayed calculation has a known mode. Both column headings follow the
protein table's actual denominator. This view introduces no quantitation rule.

The family/member dictionaries are rebuilt after document changes or display
filters; selection looks up the already indexed rows. `--test-family-view`
exercises the native actions and scrolls/selects the last of 39,996 fixture families
while checking recycling virtualization and the realized row count.
