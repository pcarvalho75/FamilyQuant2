# Panel rule measurement (J2b)

`PanelEvidenceValidation.LeaveOneOut` measures one reviewed entry per accession
(isoforms grouped), in the selected user scope. Each fold removes the same entry
and its aliases from reviewed link support and masks its own source family in
the classifier. The native search excludes self entries and records all equally
close references. The query's scope and observed domains remain available.

Expectations come from source annotations interpreted through the user's panel
and confirmed links. They are not independent biological truth. Right, wrong,
abstained and unresolved expectations are all separate. RightRate uses every
scorable entry, including abstentions; RightOverAllReviewed also exposes the full
cohort denominator. A zero denominator is unavailable, never a measured zero.

Draft comparison uses the draft's stored annotations and explicit or uniquely
matching panel keys/names. Undeclared labels stay unresolved. Every addition,
removal, changed family, unknown draft label and absent source entry has the
program's reason. No draft class is copied into the automatic assignment rule.

```text
--test-panel-evidence-validation
--panel-evidence-audit <program-project> <draft.fcf> <scope> <new-output-directory> [draft-label-map.json] [link-choices.json]
```

The native command writes invariant LOO and draft-difference CSV/JSON plus input,
binary and rule provenance. It reads its project and comparison draft unchanged.
An optional draft map is user data; a null target means outside the panel.
These checks measure the selected rule and cohort, not fresh RAW quantitation.
Approved link-choice input can be supplied without mutating the source project.
Its path/hash are recorded. This permits measuring the rule while a separate
downstream quantitation or subclass operation is pending.

The native run at `D:/FamilyQuant-data/out-evidence/J2-20261102-215129/audit/`
measured 978 reviewed entries: 859 right, 12 wrong, 86 abstained and 21 unresolved
expectations. RightRate is 859/957 scorable entries (0.8975966562173459);
RightOverAllReviewed is 859/978 (0.878323108384458). Scope was the recorded user
choice Toxin, with 2,352 reviewed references available before each own-entry
removal. This is not the earlier Python experiment's cohort or result.

The final draft comparison is under `draft-final/`: 191 rows (165 removed,
13 changed, 13 added), all with a reason and no unresolved draft-label mapping.
Blank draft annotations mean outside/unassigned. Comparison aliases are explicit
input data from the panel's prior-name column; BPP/natriuretic peptide map to the
single NP row under the user's precursor rule. No comparison label enters the
assignment rule. The earlier uncorrected draft report remains under `audit/`
and is superseded by `draft-final/`.
