# Native Core runtime ledger checks

These native test commands add executed, observation-specific evidence where the
strict declaration comparer cannot cover refactored code. They change tests and
receipts only. A kind receipt covers only the listed rows whose checks actually
ran and passed. Existing independently valid receipts remain authoritative.

```text
FamilyQuant.Tests.exe --ledger-storage-runtime <frozen-source-root> <fresh-output> <date-timestamp> [--negative-control]
```

Obtain the timestamp from `date '+%F %T %z'` immediately before execution. The
frozen `FamilyQuant/ProjectStore.cs` must match its inventory SHA256. Expected
literal values and complete message fragments come from those frozen observations.
The checker retains its source, loaded assemblies, frozen source, scoped current
storage source, fixtures, actual values and assertion log with artifact hashes.
Every current storage file is bound in each proposed row, including the split
codecs and schema, so a helper edit cannot hide behind an unchanged facade hash.

The X7 pass covers 90 observations left open after N5 and the declaration pass:

- The twelve legacy header slots plus a reversed legacy-header read, including
  missing modern fields and the four-isotope contract.
- Reversed modern-header reads for every named field and all six isotope areas.
- Tab delimiters, invariant round-trip doubles and both boolean wire values, with
  parsing also tested under a decimal-comma culture.
- Each missing structural column, checked by its complete refusal message.
- Future write/read formats, isotope overflow, null spectrum rows, missing ZIP
  entries, a JSON-null manifest and stale shared-scan concordance.
- Missing, negative, absent-sidecar and mismatched spectrum counts, plus null and
  malformed JSONL rows with the exact physical line number.
- A real staged GUID temporary file before atomic publication, then cancellation,
  target preservation and temporary-file cleanup.

The negative control swaps run and sequence cells in the reversed-header fixture.
It must fail both field observations and the shared kind assertion. Its register
must never be imported. The normal fixture then runs twice; actual-value JSON,
assertion logs and all authored fixture files must be byte-identical. Synthetic ZIP
entry timestamps are fixed before the probes read them. Manifest timestamps are
fixed in the synthetic input. Actual values and error messages are not rewritten.
Receipt command paths and execution timestamps describe their individual runs.

These checks establish the specific synthetic value and refusal contracts. They
are not fresh RAW extraction, Ana-file reproduction, installation or release
readiness. The ordinary receipt validator and independent kind review still apply.
