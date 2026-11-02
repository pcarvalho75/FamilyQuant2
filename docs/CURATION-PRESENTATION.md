# Curation presentation seam

C1a separates the curation presentation from its stored document. C1 remains open
for the shared operation host, dialog integration and preservation evidence.

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
adapters will live in separate partial files and preserve these caller APIs.

The protein table stays outside the panel. Search IDs and decoys start hidden;
the search-ID toggle reveals the stored column. Locus displays the accession,
while its tooltip and document retain the full database identifier. Grid copy
and UniProt actions keep their existing independent identifier adapters.

`--test-curation-view` exercises a displayed native table, including annotation
editing and Undo. `--test-curation-layout` runs retained chrome and tooltip checks.
`--curation-screens <new-output-dir> <input.fcf>` writes four native client captures
at 96 DPI, both themes and both viewport sizes, with an input-hash manifest. It
does not extract RAW data or recompute quantitation.
