# Native XIC message receipts

The J15 producer covers the assigned 21 frozen observations: 14 in
`FamilyQuant/DeNovo/Ms1EnvelopeDetector.cs`, six CSV outcome fragments and the
empty-run filename fallback in `FamilyQuant/FullXicComputer.cs`.
It proposes evidence and does not change the ledger.

```text
FamilyQuant.Tests.exe --ledger-xic-messages-runtime <frozen-source-root> <fresh-output> <date-timestamp> <stored-project> [--negative-control]
```

Take the timestamp from `date '+%F %T %z'` immediately before each invocation.
Use the full frozen N3 source tree: both original source files must match the
inventory SHA256. The stored project is opened read-only for its input fingerprint
dictionary; stored quantitation is not loaded. The first RAW path in ordinal order
must still match its recorded SHA256 before a detector pass can start.

The native cases execute:

- `DetectRun`'s empty-path refusal.
- Its validated cache branch, with a synthetic owned cache containing 1,234
  features. The complete status must retain invariant grouping under `fr-FR`.
- One uncached `DetectRun` pass on that read-only RAW, including the loading and
  completion messages, all four counts, and both frozen charge-evidence labels.
  The input hash, length and modification time must remain unchanged. The output
  detector cache belongs to the proof; nothing is written beside the acquisition.
- The public Full XIC CSV success and refusal paths, including exact complete
  messages, actual CSV creation and retained quantitation/table after an IO refusal.
- Public cache-path construction with an empty run, observing the frozen `run`
  fallback in the produced filename.

Only the owned output prefix is canonicalized after exact raw message/state
comparisons. Raw observations remain in `native-cases-raw.json`; canonical values,
the assertion log, input metadata, cache/CSV fixtures, frozen/current sources and
loaded application assemblies are retained and hashed. The register is
`J15-xic-runtime.json`, with one executed receipt for each of the two frozen kinds.
Every listed observation gets its own complete comparison, with exact cohort
coverage checked before receipt creation.

The negative control changes the synthetic cached feature count to 1,235 while
the frozen-contract expectation remains 1,234. Its register must be refused before
any import mutation. Run two normal fresh packets and compare their canonical
cases, assertion logs and authored fixture bytes. Never import a negative packet.

Proposed desk producer entry (the desk applies it):

```json
{
  "id": "j15-xic-messages-runtime",
  "registers": ["J15-xic-runtime"],
  "automatic": false,
  "reason": "executes one uncached read-only RAW detector pass from the stored project; requires --include-raw and an owned output folder",
  "args": ["--ledger-xic-messages-runtime", "{in:frozenN3}", "{out}", "{date}", "{in:storedProject}"],
  "doc": "docs/XIC-MESSAGES-LEDGER.md"
}
```

This is preservation evidence for messages, formatting, cache routing and CSV
outcomes. It is not full XIC extraction, fresh quantitation, validation of detector
accuracy, a complete scientific reproduction, or release acceptance. Independent
review and the integrated ledger/release gates remain required.

## Retained validation

Packets are in `D:/FamilyQuant-data/out-floater/J15-xic`. Both normal packets
passed 23/0/0 (21 observations and two kind receipts). The wrong-count control
failed 19/4/0 as intended, and both the original and stricter pinned-source
importers refused it without changing the ledger or creating its register.

Twenty-nine files repeated byte for byte: canonical cases, assertion logs,
authored fixtures, input metadata, retained sources and binaries. The only
exclusions are per-run receipt metadata and raw captures containing the different
owned output prefixes. Raw captures remain available for inspection. Both real
detector runs preserved the input SHA256, length and modification time.

The detached fresh validation tree `70aab88` combines Shell's `5d69a0c` receipt
baseline with producer `140151d`. Its native audit/import changed exactly the
assigned 21 rows from OPEN to VERIFIED: 5,439 VERIFIED / 21 OPEN became
5,460 VERIFIED / 0 OPEN; 296 approved DROP rows stayed unchanged. Fatal and ledger
problem lists were empty. Official master was subsequently merged into
`fq/floater`, with the same Core/App/test code tree. Native batch import there
also reported 5,460 VERIFIED / 0 OPEN / 296 approved DROP. No disposition was
edited by hand. These are candidate proof states, pending independent review.
