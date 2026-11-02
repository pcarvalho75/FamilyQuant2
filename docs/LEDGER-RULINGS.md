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
policy again. Schema 2 also pins the independent `rulings-fact-check.tsv`
(`row_id`, `verdict`, `note`). Rules 1 and 3 require `kept` in both the retained
file and the current canonical file under `D:/FamilyQuant-data/reviews`.
A missing or `lost` verdict leaves the row open. Old packets without the fact
file no longer qualify. Existing test and render requirements remain in force.

Rules 1 and 3 require changed, nonempty current text witnessed in compiled
application/Core strings, compiled WPF resources or resolved current Help.
Test-assembly strings cannot qualify. The witness records its assembly, symbol
and whole-string hash. Rule 3 applies to an old `Layer N` reference. These are
recorded wording decisions, not evidence that a handler or export still works.
Compiled text alone does not prove that the old fact survived. The independent
fact verdict is required as well; the reviewer must still sample accepted rows.

Rule 2 accepts only an exact frozen XAML Style/Setter anchor whose setters use
the supported visual properties. Event setters and unsupported properties stay
open. Export/storage and format/export observations never qualify. Identical
wording belongs to the text comparison tool. Placeholders, empty values, missing
compiled text and unsupported behavior also stay open with a reason.

Duplicate TSV entries are evaluated by observation ID in stable ordinal order.
One eligible decision closes that observation; otherwise one reasoned decision
remains. `decisions.json` and the remaining TSV are deterministic outputs; the
register additionally records the run date and artifact paths. The producer
retains inputs, rules, fact verdicts, executable assemblies, checker sources and frozen style
sources. It runs policy/receipt negative controls, including an empty-new-text
control against every input row, missing/lost verdict controls and a private
forged `kept` receipt against a canonical `lost` verdict. Failed guards stop
publication. Lost facts retain their independent review note in the remaining
list. The list is copied atomically to
`D:/FamilyQuant-data/reviews/wording-left-for-paulo.tsv`; the shared input is
unchanged.
