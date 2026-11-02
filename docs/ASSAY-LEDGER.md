# AS preservation receipt

M22 assigned the OPEN `EV/X6`, `AS grouping / LK review` and `AS/X14/X15` rows from
`D:/FamilyQuant-data/out-shell/N5-final-20261101-224706/audit-a/mapping.json`.
The runner retains exactly those 1,146 observations, including every nonmatch.
It does not scan new product facts into the frozen inventory or edit product code.

The shared `LedgerMemberFactComparison` requires an exact literal in a unique frozen member,
unchanged literal multiplicity and identical complete member tokens. Type context and current
destination must be unambiguous. Source discovery narrows candidates by declaring type; it
provides no proof itself. This receipt proves C# declarations, not control reachability,
handler execution, numerical correctness or fresh RAW reproduction.

| Assigned cohort | Proposed matches | Still OPEN |
|---|---:|---:|
| EV/X6 | 991 | 82 |
| AS grouping / LK review | 0 | 68 |
| AS/X14/X15 | 0 | 5 |
| Total | 991 | 155 |

The proposed kind receipts cover 950 export/storage facts, 29 format/export facts and 12 code
facts. The native comparison run reports 996 passed, zero failed and 155 skipped; the five
additional checks verify the assignment, three kind groups and complete row coverage.
All skipped rows remain in the register with a reason and receive no closure proof.

Nonmatch reasons: 88 missing/ambiguous current members, 25 ambiguous frozen-line literals,
20 kinds requiring dedicated behavior/control checks, 11 changed enclosing type contexts,
nine changed member bodies and two changed literal multiplicities. These are not declarations
of feature loss. They identify where this particular proof does not apply.

The 68 grouping-window rows require their view-model/control/interaction evidence or the
look owner's inspected renders. The five USEARCH observations require a scoped native-output
proof after its refactor. The other 82 EV/X6 observations include cache/serialization changes,
charts and repeated literal tokens; their existing functional checks or an independently
reviewed relocation need to be tied to the individual rows before closure.

## Inputs and executed checks

Use `D:/FamilyQuant-data/out-shell/N3/source` as the frozen export. Every selected source hash
is validated against the committed inventory before comparison. A first attempt against the
old repository's working copy was refused because one file had mixed line endings. Its uniform
CRLF bytes match the inventory, but the runner did not normalize or silently accept that copy.
The existing immutable N3 export passes the hash guard without any source alteration.

```text
FamilyQuant.Tests.exe --ledger-assay-compare <frozen-source> <M22-owner-map> <fresh-output> <step> <timestamp-from-date>
```

Two runs, `D:/FamilyQuant-data/out-assay/n5-assay-b` and `n5-assay-c`, produce byte-identical
`comparisons.json`, `counts-by-source.tsv` and `unproven.tsv`. Receipt run timestamps and output
paths differ intentionally. The `c` register was imported through the native evidence tool.
Together with N5's previous 72 proposed rows, the local ledger reports 1,063 VERIFIED,
4,397 OPEN and 296 APPROVED_DROP. These are proposed states pending independent review.

The native `--feature-ledger-check` passes, including re-execution of the existing N5 storage
cohort. The frozen scan and DROP dispositions are unchanged. The release gate remains open;
this work does not claim N6 completion or approve its own 200-row sample.
Release build has zero warnings/errors; the READY quick suite is 2,836/0/17 and the fresh
stored-project golden is 4/0/0. No Core/product source changed in this receipt step.

The schema-1 register is `docs/feature-ledger-evidence/AS-retained.json`. It includes the exact
comparison for every assigned row, per-kind executed assertions and current implementation
hashes. The output's `executed` directory retains the checker sources, shared comparer and
compiled test assembly because the receipt was generated before this helper's commit.
The receipt links its retained logs and the original ownership map with exact SHA256 values.

Later source edits, including formatting, can invalidate implementation hashes. Re-execute
the comparison and use the explicit replacement import after such an edit; retain the old
register in the lane output as required by the evidence protocol.
