# N6h: USearch screen

The 224-row cohort is the frozen `m48-plans/usearch.json` selection. This packet
adds ten native handler/path proofs to the twelve existing control/default
proofs. The resulting cohort has 22 VERIFIED rows, 35 OPEN rows on Paulo's
wording/flow list, and 167 other OPEN text rows. M71 explicitly leaves unchanged
text to the N6a tool; these rows are not claimed by this packet.

`AS-build-workflow` executes the actual named routed clicks in one native USearch
screen. Its fixture window is configured before Show with Left/Top -30000,
ShowActivated false and ShowInTaskbar false. Native file and message presenters
are intercepted; no real picker, message box, clipboard or user setting is used.

The workflow checks defaults; missing database refusals; chooser cancel/accept;
the separate selection/build flow; overwrite No; the busy gate; build cancellation,
failure and success; saved FCF reload; the FDR action; redundancy output; and
duplicate-identifier failures through the search and scan buttons. Scan/search
success windows are not exercised. Their handler receipts prove actual named
wiring and the recorded refusal/error branches, not successful sweep completion.

An identical linked fixture executes native clustering and redundancy against the
read-only frozen assemblies and the current assemblies. Both return the same
memberships and redundancy FASTA. The actual screen then saves/loads the matching
FCF and writes the matching FASTA. The frozen UI handlers are not clicked because
their direct modal dialogs lack the current interception boundary.

The ten new rows are four retained named handlers, their four definitions, and
the `.fcf` and `_NO_REDUNDANCY.fasta` output suffixes. The old Input handler is
listed for Paulo with the new two-action flow. Consolidated chooser filters and
changed instructions are also listed with old/new text. No prose equivalence or
Paulo approval is inferred.

Validation:

- Release solution and dedicated frozen-host builds passed without warnings.
- Focused receipt guards: red 4/1/0, then green 5/0/0.
- Native positives: 34/0/202 twice. Negative: 21/13/202, rejecting all ten
  observation proofs and three kind guards while the workflow assertions pass.
- The two accepted runs have byte-identical proof logs, actual values, captured
  workflow values, saved FCF, redundancy FASTA and frozen/current native snapshots.
- Native ledger check passes after import. No quick or golden run under M64.

Accepted packets are `D:/FamilyQuant-data/out-assay/n6h-native-2` and
`n6h-native-3`; the register uses run 3. The earlier run 1 used a stale reference
host and stopped in its argument guard. It supplies no accepted proof.

Producer: `FamilyQuant.Tests.exe --ledger-assay-build-workflow <frozen-root>
<fresh-output> <date> [--negative-control]`. Build the dedicated
`FamilyQuant.Tests/ReferenceGoldenHost/ReferenceGoldenHost.csproj` first.
