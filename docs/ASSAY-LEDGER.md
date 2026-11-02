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

## V3 refresh, N6-AS-2

M47 requested re-execution after V3 `e03ddc8` changed the bytes of `ThreeWayWindow.cs`.
The refreshed run at `D:/FamilyQuant-data/out-assay/n6-as2-refresh` still reports 996/0/155:
991 declarations match and the same 155 observations remain OPEN. Covered members did not
change, but the containing source-file hashes were refreshed rather than reused.
The explicit `--replace-existing` import retains the old register in `n6-as2-import`.
The native ledger check passes at the merged dependency checkpoint `bb981a8`.
The combined local ledger now reports 1,432 VERIFIED, 4,028 OPEN and 296 APPROVED_DROP,
including other lanes' merged registers. This is a narrow approved-receipt refresh, not a
new product change, broad suite run or independent review claim.

## M48 first batch: remaining declarations, cache wire and protected evidence pack

The new scoped runner uses an explicit selection plan of frozen IDs and current source paths.
The shared member comparer may resolve repeated literals on one frozen line only when the
recorded ID identifies exactly one existing token offset. Whole-member tokens, literal
multiplicity and unique current membership still have to agree. The two new offset checks
failed before the change; the complete declaration suite now passes 52/0/0.

Of the remaining 87 rows, 24 declarations match. Five of those are the CSV utility's inspected
internal-to-public visibility change; both parsed type headers are pinned and complete member
tokens still agree. The other 19 use the ordinary strict comparer. No grouping-window rows
are included. The native source check reports 27/0/63, including its three guard/kind checks.

The cache receipt compares the frozen and current product serializers on the same synthetic
fixture. Both native products write and reopen their caches. Every recorded top-level JSON
key is compared with its full fixture value, including numeric arrays, quote/markup text and
a subnormal double. Canonical JSON sorts object keys and preserves array order; this does not
claim identical raw serializer whitespace or escaping. A deliberately blocked final rename
also leaves each complete native payload at its actual `.tmp` path.

All 28 cache rows pass. The negative control fails all 28 and the kind receipt (0/29/0), while
the ordinary run passes 29/0/0. The frozen host's product assembly must match the read-only
reference assembly hash. Executed sources, assemblies, raw cache files, canonical payloads,
staging witnesses and observation-specific values are retained with hashes in the packet.

The protected `PresentationEvidencePack.cs` source is byte-identical to its frozen source.
All 289 assigned declaration rows pass the same complete-member comparison, 293/0/0 including
guards and two kind assertions. The file was read and compared only. No presentation producer,
deck, product source or scientific number was changed or generated by these comparisons.

These three receipts propose 341 additional rows: 24 declarations, 28 cache-wire facts and
289 protected evidence-pack declarations. They do not approve their own review. The remaining
35 rows from the first cohort need dedicated checks: five partition-format rows, ten chart
rows, fifteen window rows and five USEARCH rows. They stay OPEN. A declaration proves neither
handler execution nor control reachability.

```text
FamilyQuant.Tests.exe --ledger-assay-cohort <frozen-source> <selection.json> <fresh-output> <step> <date-timestamp>
FamilyQuant.Tests.exe --ledger-assay-cache <frozen-source> <fresh-output> <date-timestamp> [--negative-control]
FamilyQuant.Tests.exe --assay-cache-snapshot <fresh-output>
```

The cache command needs a Release build of `FamilyQuant.Tests/ReferenceGoldenHost` against
the frozen reference. Both commands require fresh output outside read-only inputs. The
snapshot flag is a shared native fixture linked into that host, not a scientific analysis.
The selection-plan runner records missing/unsupported facts as OPEN and never proposes DROP.

Two executions produce identical comparison JSON/logs/counts/open rows and identical native
cache fixture bytes for each implementation. Receipt timestamps, run paths and register
provenance intentionally differ. Packets are under `D:/FamilyQuant-data/out-assay/m48-*`.

Current implementation hashes accept LF/CRLF checkout differences only, following the separate
`ledger-eol` guard fix. Frozen-source and retained-artifact hashes remain byte-exact. A changed
character still invalidates a receipt. No other lane's register was edited for checkout EOLs.

READY validation after master V3 merge `48ebb71`: Release build clean, quick suite 3,001/0/17,
stored-project golden 4/0/0. The golden checks four native stored-project exports; no fresh RAW
reproduction, full suite, presentation run or independent review was performed by this lane.
Registers: `AS-remaining.json`, `AS-cache-wire.json` and `AS-presentation-pack.json` under
`docs/feature-ledger-evidence`. The 341 proposed rows remain subject to the merged review gate.

## M48 second batch: protected Figures declarations

The shared complete-member comparison proves 466 of 471 `PresentationFigures.cs` observations.
Two native runs report 470/0/5. Comparison JSON, logs, per-source counts and OPEN-row lists
are byte-identical. Only declaration facts are proposed; the source and its outputs were not
edited, and no presentation render was run.

Five rows stay OPEN. Two prefix literals in `ReferenceComponentSummary.FromCuration` now refer
to `SequenceLabels` constants. Three inventory rows classified as shortcuts are dictionary
`g.Key` metadata accesses in `DeNovoConsolidationChart.OnRender`, not keyboard actions.
They need dedicated checks and make no shortcut-preservation claim. M55 agreed to ship the
466 exact declarations while retaining those five gaps. No DROP is proposed.

The register is `docs/feature-ledger-evidence/AS-presentation-figures.json`, with packets at
`D:/FamilyQuant-data/out-assay/m48-figures-1` and `m48-figures-2`. On master merge `80b35a7`,
the Release build is clean, quick suite is 3,013/0/17, stored-project golden is 4/0/0, and the
native ledger check passes. The local audit proposes 2,718 VERIFIED, 2,742 OPEN and 296 DROP
rows, including other lanes' merged work. No full suite or fresh RAW validation was run.

## M48 runtime spectrum properties

The strict source-only passes close no additional USEARCH or spectrum declarations: 224 and
195 observations respectively remain outside that proof. The dedicated spectrum fixture
constructs the actual frozen and current windows without showing them. A shared source file
captures each column's binding, header, tooltip setter and format from the product objects.

The checker binds each frozen observation ID to its exact literal offset inside a supported
column argument. It then requires the complete source argument, frozen native property and
current native property to agree at that same binding path. It never searches another column
for matching text. Six fixtures cover exact values, repeated same-line literals, a missing
binding, a changed character, a mismatched frozen object and rejection of handler claims.
They were red at 4/2/0 before implementation and green at 6/0/0 afterwards.

This proves 130 code facts. Native positive runs report 131/0/65; deliberately altered current
values produce 0/131/65. The extra assertion is the kind receipt. The remaining 65 spectrum
facts stay OPEN, including actions and exports beyond these constructed column properties.
No CSV write, displayed-window interaction, app navigation, scientific result or render is
inferred from this packet. The shared fixture also captures build-view defaults for later
checks, but no USEARCH row closes merely because its snapshot exists.

The complete runs at `D:/FamilyQuant-data/out-assay/m48-spectrum-properties-green-4` and
`m48-spectrum-properties-green-5` produce byte-identical property snapshots, source anchors, actual values and proof
logs. An earlier run passed its comparisons but refused an incorrect dependency source path
while archiving; no register from that failed run was imported. The corrected packet pins
the actual `SecondaryWindows.ViewModels.cs` and `SecondaryWindowView.cs` dependencies,
alongside the window parts, executed checker sources and both product assemblies.

The imported register is `docs/feature-ledger-evidence/AS-spectrum-properties.json`. READY
validation on master `9f135c2`: clean Release build, focused fixtures 6/0/0, quick suite
3,021/0/17, stored-project golden 4/0/0 and native ledger check passed. The combined local
audit reports 3,039 VERIFIED, 2,421 OPEN and 296 DROP rows. These remain proposed states
pending independent review. No full suite, fresh RAW, displayed window or producer run
was performed for this packet.
