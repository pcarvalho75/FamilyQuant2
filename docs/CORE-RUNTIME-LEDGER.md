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

## XIC source and stage observations

```text
FamilyQuant.Tests.exe --ledger-xic-runtime <frozen-source-root> <fresh-output> <date-timestamp> <stored-project> <validated-cache-folder> [--negative-control]
```

The manifest supplies source selections, cutoffs, extraction parameters and readonly
input paths. The checker reads those settings without loading stored quantitation
or spectrum populations. Existing stage methods are called by reflection for
targeted branch checks. These establish the stated stage contract; they do not
replace the separate full-pipeline parity checks from X5.

The synthetic checks execute public missing-input refusals, top-level RAW discovery,
import progress, default/overridden cache names, source-specific confidence/filter
labels, exact database-export discovery and source-identity refusal. Completion
checks cover shared family membership, mapping failure with rows/curation preserved,
and under-sampled counts, area, TIC share and estimate wording under a decimal-comma
culture. A synthetic invalid acquisition executes the metadata warning and remains
refused at TIC reading; a separate source-only spectrum table verifies explicit
fallback metadata. That failed acquisition never becomes a completed measurement.

The manifest-key check locates the actual compiler-generated key selector referenced
by `RebuildObservedInputManifests`. It requires that parent to call `GroupBy`, and a
unique callback with the expected input/output signature and calls to SourceId,
FilePath, full-path resolution and three-string concatenation. It then executes the
callback on two source IDs and a null source, comparing complete keys with the
frozen separator recipe. Missing or ambiguous linkage leaves the row OPEN. The
check does not choose a callback by finding matching string text.

The bounded stage replay takes at most 4,096 identified PSMs and 4,096 retained
de novo calls from one readonly acquisition. Only lane-owned validated caches are
copied into its fresh output. Before replay, the checker verifies the current RAW
fingerprint, every extraction request against the production cache loader, and the
detector cache against its production provenance validator. A missing request or
invalid detector cache refuses the replay before fresh extraction/detection is
launched. Status counts, row sides, detector wording/area/share and explicit cache
filenames are checked. An injected diagnostic-path refusal must preserve exactly
the same peptide rows and TIC. All measured rows come from program APIs.

The negative control removes the eligible RAW extension and alters one manifest-key
source ID. The positive runs must repeat proof values/logs and bounded peptide rows
byte-identically. Cache replay is explicitly recorded in `readonly-inputs.json`;
it is not fresh RAW extraction or a new full-sample result. All stage and de novo
helper sources, checker source, loaded DLLs, copied caches and outputs are retained
and hashed. No product source or input data is changed.

The retained lane data for these commands is
`D:/FamilyQuant-data/out-floater/ledger`. The supplied replay cache is
`D:/FamilyQuant-data/out-floater/X5/cache-validated`; the readonly project is the
mission's stored `scientific_results.fqproj`. This path is a test input, not a
family-specific rule. Independent review and the desk's integrated gate remain
required before accepting proposed ledger closure.

The completed lane checks propose coverage for the full assigned cohorts:

| Frozen cohort | Earlier evidence | New runtime observations | Total evidenced |
|---|---:|---:|---:|
| X5, FullXicComputer | 67 complete declarations | 80 | 147 |
| X7, ProjectStore | 43 N5 native checks plus 1 complete declaration | 90 | 134 |

X5's final negative control failed 46/4/32; its two positive runs each passed
82/0/0. All 71 retained files outside run-specific receipt metadata repeated
byte-identically, including source/DLL snapshots, copied caches, actual-value JSON,
assertion logs and bounded peptide rows. Accepted X5 packets are `xic-ready-1` and
`xic-ready-2`; `xic-ready-negative` is deliberately ineligible for import.
X7's accepted packets are `storage-ready-1` and `storage-ready-2`. The native
importer rejects each negative register before mutation. Author-proposed coverage
still needs independent review; global N6 and release readiness remain separate.
