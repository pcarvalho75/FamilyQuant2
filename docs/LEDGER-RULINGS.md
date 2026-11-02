# Recorded Paulo wording rulings

`--ledger-apply-rulings <fresh-output> <date>` reads the shared wording TSV and
the three recorded rules under `D:/FamilyQuant-data/reviews`. It writes one
`Paulo-rulings` register. Import it with the ordinary native evidence importer.
The rule source is pinned to SHA256
`D3C632398BF48334E08AC2FF31879E4D814865BB72D0AEE781E8EF029C70090B`.
Changing the rules requires reviewing this producer before another application.

The dedicated `ruling` proof includes the full rule, old wording and new wording.
The validator accepts it only in that register, for an observation/text pair in
the retained TSV. It checks the pinned rules/input, current source hashes and the
policy again. Existing test and render receipt requirements remain in force.

Rules 1 and 3 require changed, nonempty current text witnessed in compiled
application/Core strings, compiled WPF resources or resolved current Help.
Test-assembly strings cannot qualify. The witness records its assembly, symbol
and whole-string hash. Rule 3 applies to an old `Layer N` reference. These are
recorded wording decisions, not evidence that a handler or export still works.
The independent reviewer must sample whether the old fact still reaches users.

Rule 2 accepts only an exact frozen XAML Style/Setter anchor whose setters use
the supported visual properties. Event setters and unsupported properties stay
open. Export/storage and format/export observations never qualify. Identical
wording belongs to the text comparison tool. Placeholders, empty values, missing
compiled text and unsupported behavior also stay open with a reason.

Duplicate TSV entries are evaluated by observation ID in stable ordinal order.
One eligible decision closes that observation; otherwise one reasoned decision
remains. `decisions.json` and the remaining TSV are deterministic outputs; the
register additionally records the run date and artifact paths. The producer
retains inputs, rules, executable assemblies, checker sources and frozen style
sources. It runs policy/receipt negative controls, including an empty-new-text
control against every input row. The remaining list is copied atomically to
`D:/FamilyQuant-data/reviews/wording-left-for-paulo.tsv`; the shared input is
unchanged.
