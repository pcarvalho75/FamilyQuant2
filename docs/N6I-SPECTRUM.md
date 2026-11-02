# N6i spectrum preservation

The assigned v4 box contains 195 frozen observations. The existing native column packet covers
130 on this pre-C13 checkout. C13 intentionally changes two of those tooltip fragments; the
row-isolated producer leaves those two OPEN and preserves the other 128 at the gate.

After the M72 sync, all four FQAssay Spectrum producers were re-executed twice
against the changed implementation mappings. The current 195-row cohort has
183 VERIFIED and 12 OPEN; all twelve OPEN rows are on Paulo's wording list.
Both C13 fragments remain OPEN. Native values/proof logs and successful CSV/PNG
exports are byte-identical in each refreshed pair. Other lanes' registers were
not edited. These packets are under `D:/FamilyQuant-data/out-assay/m72-spectrum-*`.

The additional native packets cover 18 named control/property and routed-refusal observations,
25 actual export dialog/status/format observations, and 12 complete view-detail observations.
The ten other wording observations and the two C13 tooltip fragments are on
`D:/FamilyQuant-data/reviews/wording-for-paulo.tsv` with old/new text and a reason. They are
awaiting Paulo's decision, with no semantic preservation or DROP claim.

The actual product CSV and PNG actions run on synthetic data in an owned off-screen window:
Left/Top -30000, no activation and no taskbar entry. Native file dialogs are intercepted at
the existing presenter seam. No real clipboard, deck file or real presentation inputs are used.
The existing PNG producer is executed unchanged under M61's scratch-only authorization.

The eight export cases check dialog cancellation, success, real filesystem failure and operation
cancellation for CSV and PNG/CSV. The successful CSV equals the program's table output; the PNG
is 1920x1080 and its companion CSV equals the program's score-study output. Cancelled operations
preserve prior bytes. Source-expression anchors compare complete native status strings, including
the N0 display of 1,234 rows and 1,233 matches; only the dynamic exception message is a wildcard.

Focused fixtures pass 20/0/0, and row-isolation/window-configuration fixtures pass 4/0/0.
Native screen/action, export and detail runs report 21/0/47, 43/0/40 and 13/0/53 before the
other packets are imported. These summaries include shared kind checks and the export probe's
16 checks. Every row's compared property/action is recorded in its register.

Native snapshots, actual-value reports and proof logs repeat byte for byte. Successful CSV,
companion CSV and PNG bytes repeat exactly. Negative controls leave altered targets unproved.
No product code was changed. The native ledger check passes; quick, golden and master sync
are deliberately omitted for this tests/receipts/docs step under M64.

Registers and native producers:

```text
AS-spectrum-properties.json: --ledger-assay-spectrum-properties <frozen-root> <out> <date>
AS-spectrum-actions.json: --ledger-assay-spectrum-actions <frozen-root> <out> <date>
AS-spectrum-exports.json: --ledger-assay-spectrum-exports <frozen-root> <out> <date>
AS-spectrum-details.json: --ledger-assay-spectrum-details <frozen-root> <out> <date>
```

All commands use `FamilyQuant.Tests.exe`. The frozen root is
`D:/FamilyQuant-data/out-shell/N3/source`; the linked frozen host must be built first. The
registers retain executed sources, native assemblies and outputs with raw artifact hashes.

An initial native no-output investigation accidentally entered the test harness recursively.
Its exact owned PIDs were cleaned up, the run was rejected, and no result from it is evidence.
The replacement uses a dedicated environment-guarded return in ReferenceGoldenHost, smoke-tested
before the native missing-file check. No other lane process was targeted.
