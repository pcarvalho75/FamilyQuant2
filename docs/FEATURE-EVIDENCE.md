# Feature preservation evidence

The frozen scan remains unchanged. Source movement identifies a destination, not proof
that a function works. An arbitrary sentence, a search and a green build cannot close a row.
Worker commits propose VERIFIED states for review; acceptance still requires the independent
reviewer and the desk's merged green gate. Authors never record their own review approval.

Each step supplies `docs/feature-ledger-evidence/<step>.json`, schema version 1:

```json
{
  "schemaVersion": 1,
  "step": "C1",
  "frozenCommit": "e9b73064d8ff19b2fde3e3231240b636a64c0a91",
  "runs": [{
    "id": "unique-run-label",
    "command": "exact command actually executed",
    "executedAt": "timestamp obtained from date at execution",
    "commit": "HEAD at execution",
    "workingDirectory": "execution directory, when recorded",
    "artifacts": [{"path": "retained log or PNG path", "sha256": "SHA256 of exact bytes"}]
  }],
  "rows": [{
    "observationId": "exact FQ15 ID from the frozen scan",
    "implementation": [{"path": "repository-relative source path", "symbol": "current member", "sha256": "SHA256 of exact source bytes"}],
    "proofs": [{
      "runId": "unique-run-label",
      "kind": "test",
      "target": "ExactProgramTestMethod",
      "assertion": "exact PASS assertion text",
      "artifact": "one of the run's hashed artifacts",
      "inspectedBy": ""
    }],
    "note": "optional limitation"
  }]
}
```

Paulo's D13 permits one executed receipt per kind. A register may add
`kindReceipts: [{id, kind, proofs}]`. A covered row sets `kindReceiptId` to that ID
and records its exact frozen/current comparison in `compared`; its own `proofs`
may be empty. The shared receipt must match the row's frozen kind, identify a green
execution and retained artifacts, and every row still carries its current implementation
mapping/hash. Rows not covered by a kind check need their own proof or remain OPEN.
The checker expands shared proofs for validation; it never treats a kind name as evidence.

A test proof must identify an actual assertion in a zero-failure execution log.
The assertion must establish this observation's behavior, not merely that a broad method ran.
All declared artifacts and mapped implementation hashes must still match.
Keep the source and compiled checker snapshots when a proof is produced before its commit.
Source paths stay within this worktree. Artifact paths may name retained lane data folders.
Record a mapping with `proofs: []` while its behavior is unproven; it stays OPEN.

A render uses `kind: "render"`, a PNG target/path, the specific visible fact in `assertion`,
and the actual inspector in `inspectedBy`. Inspect both themes when the fact depends on theme.
A screenshot alone does not prove a handler, shortcut, export or numerical result.
The tool validates receipt structure and provenance; the reviewer checks whether the cited
assertion or visible fact actually supports the observation.

Run the built test executable with these flags and explicit lane-owned outputs:

| Command | Effect |
|---|---|
| `--test-ledger-evidence` | Adversarial receipt-validation fixtures |
| `--feature-ledger-prove <fresh-output> <date-timestamp>` | Native storage/source tests and N5 register; no ledger edit |
| `--feature-ledger-evidence-import <register> <output>` | Validate and import a step register; apply only supported states |
| Same import plus `--replace-existing` | Explicit replacement after re-execution, preserving the old register in output |
| `--feature-ledger-evidence <output>` | Read-only counts, complete mapping and deterministic review sample |
| `--feature-ledger-kind-review <kind> REOPENED/ACCEPTED <reviewer> <reason> <date> <output>` | Record an independent kind verdict and reapply supported states |
| `--test-ledger-declarations` | Exact XAML declaration and scoped C# member comparison fixtures |
| `--ledger-declaration-compare <frozen-root> <current-root> <fresh-output> <step> <source-prefix> <date>` | Compare named explicit XAML properties and emit kind receipts |
| `--ledger-shell-compare <frozen-root> <fresh-output> <date>` | Compare SH/C1 declarations and run native curation interactions; emit separate candidate registers |
| `--ledger-core-relocation <frozen-root> <fresh-output> X5/X7 <date> [member-mappings.json]` | Compare complete scoped Core member declarations and retain per-row gaps; see [Core relocation ledger](CORE-RELOCATION-LEDGER.md) |

For example, obtain the timestamp with `date '+%F %T %z'` immediately before the proof command.
Neither the proof runner nor the audit invokes the frozen-source generator.
An import reopens a stale VERIFIED row rather than retaining unsupported closure.
The ordinary and release ledger guards require a current receipt for every VERIFIED row;
the release guard additionally requires zero retained OPEN rows.
The ordinary ledger check also re-executes the native preservation cohort, so a changed
helper cannot hide behind a matching facade-file hash. Its current case IDs must cover
the registered native cohort exactly.

The audit emits `report.json`, `counts-by-kind.tsv`, `mapping.json`, `source-files.json`
and `review-sample-200.json`, plus suggested routing in `open-by-owner.tsv`.
It retains `review-sample-50.json` for the historical N5 packet.
The desk remains the authority for assigning work. D13's final sample orders closed IDs
by SHA256 of the frozen inventory fingerprint, a separator and the ID, taking the first 200.
Its seed is recorded for reproduction. One failed sampled row reopens its whole kind.
Kind review history is append-only in `docs/feature-ledger-kind-reviews.json`; a REOPENED
kind is excluded from closure and cannot be silently reclosed by another import.
An independent ACCEPTED verdict can release that block after the corrected check is reviewed.
An audit introduces no current timestamp, so identical inputs produce byte-identical reports.
Source-file movement categories describe physical file locations only. Per-row implementation
records identify command/view-model replacements when the old file still exists as a wrapper.

`LedgerDeclarationComparison.Compare` requires a unique named element, the same local type
and exactly equal explicit property value. Markup expressions, missing or ambiguous anchors,
and operation facts stay unverified. Its logical-module boundary prevents borrowing copy
from an unrelated view. A declaration receipt does not establish reachability or a handler.

`LedgerMemberFactComparison.Compare` supports C# literals inside a unique containing member.
It requires exact literal syntax/multiplicity and identical complete member tokens after
removing whitespace/comments. Explicitly reviewed `MemberMapping` values can identify a
relocated owner/file. Changed member bodies, ambiguous tokens/destinations and unsupported
syntax remain unverified. It is a declaration comparison, not a runtime result calculation.
Focused tests cover wrong-member borrowing, changed branches and changed multiplicity.

The shell producer checks the frozen hashes before comparing and writes separate `SH.json`
and `C1.json` candidates, complete comparison results and execution snapshots. Its source
scope follows the assigned SH cohorts and the 557 curation observations. XAML properties
cannot borrow a same-named control from another window. The three inspected individual curation
literal relocations are explicit: `FullXicQuantity`, `PrepareFullXic` and `UpdateLockSummary`.
Each still requires equal complete member tokens. Changed copy, unsupported syntax,
handlers, shortcuts and control reachability need their own proof and remain OPEN.
Same-type partial splits in MainWindow, curation, Build, lock manager and spectrum views
also require the parsed frozen/primary/destination type declarations. A changed primary
base or a changed complete member cannot pass as a neutral partial relocation.
Candidate generation does not edit the ledger; import only after validating the receipts
against the final source at READY. The final N6 gate still needs C2, C13 and independent review.
The same execution runs `CurationScreenTests`: ten explicitly mapped table, identifier,
annotation-binding and Undo observations get individual interaction receipts. Their
implementation maps name the actual view/model classes and hash the executed test source.
`curation-runtime.json` separates those observations from exact declaration matches.

The N5 first pass executes synthetic project write/read, native ZIP and quant-column checks,
source discovery, descriptor/cutoff checks and refusal paths. Each closed row has its own
assertion and recorded actual value. All retained observations from sources moved to Core and
the 13 displaced handlers have a mapping; the native proofs cover two Core source files.
Untested rows remain OPEN. These are preservation fixtures, not fresh RAW,
Ana-file reproduction, installation or a claim that every function is already proven.
