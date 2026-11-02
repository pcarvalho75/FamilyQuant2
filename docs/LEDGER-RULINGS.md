# Recorded Paulo wording rulings

`--ledger-apply-rulings <fresh-output> <date>` reads the shared wording TSV and
the four recorded rules under `D:/FamilyQuant-data/reviews`. It writes one
`Paulo-rulings` register. Import it with the ordinary native evidence importer.
The rule source is pinned to SHA256
`3C6B1EC5979F0B593A609D0555AB84BC5EC05FCC60B370A647E11030993A8605`.
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
application/Core strings, compiled WPF resources or resolved current Help: the
Families Help page for every mode and shared-peptide policy, and the De novo Help
page for each preset source with the default filters, as its Help button shows it.
Test-assembly strings cannot qualify. The witness records its assembly, symbol
and whole-string hash. Rule 3 applies to an old `Layer N` reference. These are
recorded wording decisions, not evidence that a handler or export still works.
Compiled text alone does not prove that the old fact survived. The independent
fact verdict is required as well; the reviewer must still sample accepted rows.

Rule 2 accepts only an exact frozen XAML Style/Setter anchor whose setters use
the supported visual properties. Event setters and unsupported properties stay
open. Export/storage and format/export observations never qualify. Identical
old and new wording closes under Rule 1 only with a kept verdict, a wording kind
and a live compiled witness; otherwise it belongs to the text comparison tool.
Placeholders, empty values, missing compiled text and unsupported behavior also
stay open with a reason.

Rule 4 reads Paulo's group ruling, `D:/FamilyQuant-data/reviews/paulo-group-ruling.tsv`
(`row_id`, `group`, `note`), pinned in the packet when present. A row that rules 1
to 3 leave open closes under rule 4 when listed as `fragment` (no user fact of its
own), `format` (a number format where the same quantity shows the same precision
today) or `superseded` (the feature changed and the row only concerns wording or
layout). The reason is recorded as `Paulo group ruling: fragment, no user fact of
its own`, `Paulo group ruling: format, same precision today` followed by the note,
or `Paulo group ruling: superseded, ` followed by the note. An approved DROP stays
a DROP, export/storage and format/export rows never close this way, and a row Rule
1 closes stays Rule 1. A missing or empty file closes nothing. Paulo fills the file;
a superseded row that removes something the user could do stays open and goes to
him one by one.

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
