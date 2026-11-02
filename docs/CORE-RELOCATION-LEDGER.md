# Core relocation ledger

The X5 and X7 pass proposes source declaration proofs for independent review. It
does not close N6 or establish that a refactored branch executes. Runtime checks
from X5/X7 remain separate evidence with their original limits.

Run the native test executable, with a timestamp obtained immediately from `date`:

```text
FamilyQuant.Tests.exe --ledger-core-relocation <frozen-source-root> <fresh-output> X5 <date-timestamp> [member-mappings.json]
FamilyQuant.Tests.exe --ledger-core-relocation <frozen-source-root> <fresh-output> X7 <date-timestamp> [member-mappings.json]
```

The frozen root contains `FamilyQuant/FullXicComputer.cs`,
`FamilyQuant/FamilyEnvelopeQuant.cs` and `FamilyQuant/ProjectStore.cs`. Each required
file must match the frozen scan's SHA256 before comparison. The checker does not
regenerate that scan. It limits current candidates to the matching facade and
top-level `Quantitation` or `Storage` files. It never borrows a literal from another
subsystem. Optional mappings explicitly identify the old member, new owner and
new file; they do not relax complete token equality.

For each observation the checker requires a unique frozen literal on its recorded
physical line, a unique containing member, an unambiguous current member, exact
literal syntax and multiplicity, and identical complete member tokens excluding
whitespace/comments. Changed signatures, accessibility, bodies or initializers
remain unproved. A declaration proof makes no runtime or reachability claim.

Outputs include `comparisons.json`, `comparisons.log`, a schema-1 register and
`executed/` snapshots of scoped source, frozen source, checker source and loaded
checker/Core assemblies. Every retained artifact has a hash in the register.
Each row names its actual frozen/current member and member-token hashes, or its
specific reason for remaining open. Existing independently valid receipts stay
authoritative, even when this pass also matches their declarations. Importing a
register uses the ordinary evidence validation and whole-kind review rules.

The first native pass found:

| Cohort | Scanned | Exact declarations | Already proved elsewhere | New proposed closures | Assigned rows still open |
|---|---:|---:|---:|---:|---:|
| X5 | 147 | 67 | 0 | 67 | 80 |
| X7 | 134 | 21 | 43 | 1 | 90 |

X7's 21 exact matches include 20 existing N5 proofs. Its assignment was the other
91 rows. Existing runtime storage assertions keep their N5 references.

The remaining rows cover extracted/refactored full-XIC orchestration and source
selection, storage facade/codec delegation, and schema constants whose public
facade now forwards to internal declarations. Source-token equality cannot prove
those changes. They need observation-specific runtime, branch or resolved-value
checks; unchanged text elsewhere and aggregate parity do not close them.

The retained packet is `D:/FamilyQuant-data/out-floater/ledger`. Original audits
and reviewed owner mappings are preserved beside the READY receipts. Repeated
comparison arrays must be byte-identical. Receipt timestamps and output paths
describe their individual executions and are not compared as scientific output.
The native receipt import prints current global ledger counts; the table above
describes this lane's scope and does not claim release readiness.
