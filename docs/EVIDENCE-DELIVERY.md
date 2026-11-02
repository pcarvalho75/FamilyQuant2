# Native evidence delivery

The compiled native delivery command automates the ordinary Core and app review calls. It contains
no family-name rule, answer-key lookup or second classifier. A preparation uses `EvidenceFetcher`,
then fetches a current panel for the explicitly supplied scope through `ReferencePanelFetcher`.
The panel keeps tissue specificity. A stored search is reused only if `NearestReviewed.IsCurrent`
accepts its complete query and scoped-reference fingerprints; otherwise the program searches again.

Preparation writes a separate `prepared.fcf`, its source and prepared byte hashes, the complete
review packet, and the review model's default selection. It never saves over the input. Failed
preparation produces no delivery. No script converts a probe into a delivered result.

Use the built `FamilyQuant.Tests.exe` from this worktree:

```text
--evidence-delivery prepare <source.fcf> <new-preparation-directory> <scope> <stored-search.fcf>
--evidence-delivery split <preparation-directory> <family-from-review> <architecture-key-from-review>
--evidence-delivery create <preparation-directory> <new-output-directory>
```

The scope is an explicit reviewed choice. Quote a multiword phrase in the scope syntax. The `split`
command records one explicitly selected architecture key from the native review packet. It names
no biological class in code. This supports a single group apart while all other groups stay in
their family, as the Core per-key choice contract permits.

Create requires exactly the default protein selection and default names: unflagged proposals are
selected, flagged or unassigned rows are not. Only an explicit architecture split can differ. It
uses `EvidenceGroupingSession.Apply`, writes a recipe-bearing FCF, and uses `EvidenceExports` and
`LeftOutList` for the ordinary CSVs. `review.json` retains every proposed tier, reason and flag.
`delivery-note.txt` names the explicit split. `provenance.json` records the input byte hash,
database source hash, program version, evidence and panel releases, scope and recipe hash.

The command reopens the prepared source and reconstructs the selection from the saved recipe
through another `EvidenceGroupingSession`. The resulting FCF and every export must be
byte-identical. Both copies run `SavedFileCheck`. The delivery copy must also open through
`MainWindow.OpenProjectPath` with the same curation. App settings for this check are isolated in
the output directory. `--test-evidence-delivery` exercises this path with synthetic proteins.

The `delivery` folder is the packet for review and delivery. The `replay` folder and app-check
settings are verification artifacts. The FCF holds curation and evidence; this path computes no
XIC and makes no abundance claim. An installed-preview or fresh-RAW check is a separate gate.
