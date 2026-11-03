# Compute homology performance

## J38c: reuse the current attribution

`FullXicCompletionStage` already computes the current curation attribution. The
family-world completion now consumes that map when its membership fingerprint
matches, rather than searching the same identified and de novo queries again.
The public entry point still builds a fresh map; a stale map takes the same
fallback. World attribution, errors, ordering and serialization are unchanged.

The matched full-data run used WORKHORSE, 32 logical processors, the same private
project/RAW/cache paths, and no concurrent FamilyQuant or Orion process. Both
builds used master basis `4d92658`; AFTER adds `78e87f5`. The separate envelope
experiment was disabled in both and is parked on `fq/assay-j38-envelope-parked`.
It was excluded from J38c; J38d reapplies it separately after the measured
stored-study allocation peak, as described below.

| Measurement | BEFORE | Reuse AFTER |
| --- | ---: | ---: |
| Compute wall seconds | 896.873628 | 527.032977 |
| Process CPU seconds | 23187.250000 | 12560.234375 |
| Quant rows | 168132 | 168132 |

Wall time decreased 41.24%; process CPU decreased 45.83%. The older DISCOTECH
1167-second run has a different code basis and is not this comparison's baseline.

Every project entry was compared after lossless gzip decompression. Only
`CreatedUtc` and `UpdatedUtc` were replaced in the manifest; no scientific field
was removed. Curation, quant, spectrum and the complete manifest (including all
studies, family world, sequence census, fingerprints and cutoffs) were byte equal.
The separately exported spectrum CSV was also byte equal.

| Payload | Bytes | Equal SHA-256 (BEFORE and AFTER) |
| --- | ---: | --- |
| curation.json | 789641601 | 9995511f99792f569d681a4e11bd8c82eac473c4bcb9b5a095ad9cb9c4261821 |
| quant.tsv | 36459571 | adfa1fe01566454d75ffa5044bf5e0d02709a0daf75c10eb29db5de0847900fa |
| spectrum.jsonl | 829968459 | f843a91a35bc5d0a521c74b8057f55bc46f3c41eb2d4be965df27ec044765994 |
| manifest.json (native compact snapshot, two clocks replaced) | 30758916 | d7aefa60cd78622bcd10cbbf79d93854129a9c95d058d9213d0ef87b4ce2d911 |
| spectrum.csv | 304979141 | e541ac1ae69f2d5f319f8966649798f00efa2488424f21ba5d17e1d7c5e9f9b5 |

Evidence root: `D:/FamilyQuant-data/out-assay/j38-measure-01`:
`workhorse-matched-before/matched-before`, `workhorse-reuse-after/reuse-after`,
and `j38c-reuse-parity.json`. The input copy SHA-256 is
`910454D9EF1974D340BDD62DFFE0D39855BCCDC495A909691D5E7269B210BD43`.

Focused native checks cover fresh-map reuse, complete world JSON and notes,
stale-membership fallback, and preservation of the public two-argument entry point.

## J38d: separate equivalent search engine

`HomologySearch` is the production entry point. `HomologyMapper` retains the old
algorithm as the deprecated equivalence reference. All production callers and
ordinary behavior checks use the new class; only explicit equivalence checks use
the old one. The progress-aware public search signature and batch reports match.

The identity gate derives the smallest passing integer `m` using the original
`m / (double)L < MinIdentity` comparison. A window is abandoned only after more
than `e = L - m` mismatches. Such a window cannot pass or improve a passing maximum.

Auto uses a positional q3 index when `T = L - 2 - 3e` is positive. Each mismatch
spoils at most three q-grams, so every passing window has at least T matches on its
own diagonal `(protein, proteinOffset - queryOffset)`. The index folds I to L;
strict I/L therefore only gains candidates. K/Q equivalence, nonpositive T, an
unindexed database and the small-database policy retain the brute path. A forced
legacy SeedIndex request retains the old k-mer candidate set, including its
documented lossy cases. Postings use a compact 16-bit protein/16-bit position only
when both ranges fit; otherwise each uses 32 bits in a 64-bit posting. The tests
cover both ranges above 65535. Each Search owns its index, so attribution does not
retain positional data after mapping. PAM scoring still visits every full window of every
protein that passes the exact identity gate.

Query scratch uses a small merge heap over the sorted positional posting lists.
Subtracting a fixed query offset preserves each list's order. Equal diagonal keys
arrive consecutively, so exact q-gram votes need only O(query length) scratch, with
no allocating dictionary, database-sized per-worker array or all-window list.
The first dictionary prototype completed the DISCOTECH run but reached a 22.163
GiB peak; it is retained as a memory failure, not accepted performance evidence.

For BestScoreOnly queries with exact containers, a matrix-derived guard requires
that each row's maximizers are exactly its identity-fold class and that integer
sums cannot overflow. Only then can the earliest folded exact occurrence replace
the PAM scan. Wildcard or incompatible-fold rows decline this shortcut. All
candidate proteins still receive the identity gate so both target-decoy suppression
flags remain exact, even when only a lower-scoring opposing hit exists. Duplicate
identifiers decline containment to preserve the reference sort's equal-key behavior.

`CandidatesExamined` on the Auto q3 route counts distinct candidate proteins with
a qualifying diagonal before the identity gate; `UsedSeedIndex` records that the
positional filter ran. These work diagnostics can differ. Query/rejection fields,
hit order, protein, offset, score, both identities, matched sequence, decoy status
and both competition flags participate in equivalence.

The focused command `--test-homology-search-equivalence` covers every length 1 to
40 across 192 IL/KQ/gate/best/strategy configurations, canonical adversarial and
wildcard queries, separate gate/PAM optima, tail windows, rounding boundaries,
duplicate ranking keys, and offsets/protein indices above 65535. It also runs the
ordinary homology behavior fixtures. Registered default suites run each fixture
once. At integrated commit `68b868f`: Release build has zero warnings/errors,
equivalence/behavior 170 passed, family-world 51 passed and J35 progress 42 passed
(English and Portuguese, including stored-byte comparisons). Full-data proof and
timing are recorded only after the remote runs finish.

The final J38d product includes approved J48 streaming curation read/write and the
separate ordered-bin envelope allocation fix (`cf4f7f2`). The latter preserves the
original per-bin summation order, excludes all references to the held-out object
as before, and evaluates the original pooled fallback only when it is needed.
It does not subtract a held-out value from a total. Its seven native oracle and
allocation checks pass, and J35's 42 progress/byte checks remain green. J48's 21
streaming checks pass. No GC policy or heap-cap setting is changed.

On DISCOTECH, product run 04 completed with 168132 rows in 79.966893 Compute wall
seconds and 193.218750 process CPU seconds. Exact OS process peak was 8509157376
bytes (7.925 GiB); peak before project save was 8160096256 bytes (7.600 GiB).
The whole-process peak includes opening and saving. This run was clean, with no
other FamilyQuant or Orion process in the before/after checks.

Every decompressed project entry and exported snapshot matches the original
matched BEFORE byte for byte; exactly the two compute clock values are replaced.
Hashes are the same as the J38c table above. This proves cross-machine data
identity; a same-machine performance/peak comparison is reported separately.
Evidence: `search04-discotech-parity.json` and
`discotech-search04/search-after-04` under the evidence root above.

Run 04 did not pass the final memory acceptance. Its attempted multi-file legacy
override was later rejected by strict data comparison because family studies were
missing; its 308.344-second time and 6313508864-byte peak are retracted. The product
was complete, but still peaked at 8489758720 bytes on WORKHORSE.
Large later-study provenance hashes materialized JSON strings and UTF-8 copies.
`9ef6880` streams those same fingerprints instead: length-prefixed JSON is counted
then hashed with the original prefix, and subclass source JSON is hashed directly.
The serialized bytes, digests, decisions and currentness rules are unchanged.
Eight byte/allocation oracle checks and 211 existing panel/subclass/composition
checks pass; the final full-data bytes and memory measurements are recorded below.

The replacement reference build changes only `HomologySearch.Search` to delegate
all arguments to the untouched mapper. Production callers stay identical. The
snapshot runner now writes its notes and rejects unavailable requested mapping or
family-world studies, before a timing can be treated as a complete result.

For complete hit-level proof, a generated recorder writes each actual completed
Compute search's ordered protein database, raw queries, options/index settings and
scientific results to gzip sidecars, each bound by SHA-256. The native
`--replay-homology-capture <capture> <fresh-output> <expected-call-count>` checks
every file hash, rebuilds the identical search inputs and runs the frozen mapper.
Scientific result JSON bytes must match, including both identities and both
competition flags. This separates the reference run from Compute's project graph;
running both engines inside Compute exceeded the memory guard and was rejected.
The recorder/replay smoke verified all 64 fixture calls. Full actual-call replay
05 completed with exit 0 and all five calls equal, using a 809017344-byte peak:

| Actual call | Queries | Proteins | Kept hits | Old/new scientific fields |
| --- | ---: | ---: | ---: | --- |
| Curation identified | 7579 | 63554 | 51409 | equal |
| Curation de novo | 16400 | 63554 | 85203 | equal |
| Residual world | 12824 | 12086 | 545 | equal |
| Known-answer holders | 2472 | 63554 | 20514 | equal |
| Leave-species-out credit | 6179 | 6366 | 2958 | equal |

Evidence: `discotech-capture05/search-captures-05` and
`workhorse-replay05/search-replay-05/validation.json`. Call recording is a worker
diagnostic build; production does not record or repeat searches.

## Final clean pair and acceptance

The accepted pair ran on DISCOTECH with the same private project/RAW/cache paths,
32 logical processors, no heap cap or forced collection, and no other FamilyQuant,
Orion or DIA-NN process listed before/during/after. Both runs completed with 168132
quant rows, required family mapping/world studies and native exit 0. The reference
changes only `HomologySearch.Search` to delegate all arguments to the unchanged
mapper; no production caller is rewritten. Both builds include the same J48 I/O,
ordered-bin envelope and streaming fingerprint changes.

| Measurement | Frozen mapper (clean 10) | Final search (clean 10) |
| --- | ---: | ---: |
| Compute wall seconds | 605.889871 | 80.210704 |
| Process CPU seconds during Compute | 8468.843750 | 191.718750 |
| OS process peak before save (bytes) | 5199642624 | 4844351488 |
| **Overall OS process peak, including save/snapshots (bytes)** | **6909427712** | **6409265152** |

Wall time decreased 86.76%. The deciding overall peak decreased 7.24%, from
6.435 GiB to 5.969 GiB. Both phases are reported; no save allocation is hidden.
The final search clears posting-buffer references after all synchronous workers
join, including the exception/cancellation path. Posting data is not retained by
the caller's attribution database. This is reference lifetime management, not a
GC policy change.

`clean10-before-parity.json` proves the complete reference output matches the
original accepted baseline. `clean10-matched-parity.json` proves every decompressed
archive entry and quant/manifest/spectrum JSONL/CSV snapshot matches the final
product byte for byte, with only the two computation clocks replaced. All SHA-256
values in the J38c table remain unchanged. Full projects and native peak files are
in `discotech-clean10-before/search-before-10` and
`discotech-clean10-after/search-after-10`; `clean10-performance.json` records the
unrounded measurements.

The final compact index was also recorded in a complete five-call Compute. Every
captured database/query/options file matches the certified native old-reference
packet, and every scientific result byte matches its old-engine digest:
`certified-capture08-validation.json` is all-equal for all five calls. The last
change only releases posting references after scoring has finished. Lengths 1-40,
wide-index/offset, duplicate-key and all option equivalence checks remain green.

Final current-master integration: Release build zero warnings/errors; native
equivalence/behavior 172 passed, streaming provenance hash 8 passed, suite-command
coverage audit 13 passed. Earlier focused checks also passed: envelope oracle 7,
family world 51, J35 progress 42, J48 streaming 21, panel evidence 64, panel
validation 7, family panel/subclasses 109 and composition 31. New fixture commands
are registered once in DefaultSuite and explicitly routed in the K8 inventory;
the selected-input replay command has a reasoned manual-proof exclusion.

Rejected/partial runs are retained: the allocating dictionary prototype, the
in-process double-execution audits, the invalid multi-file reference override and
the shared WORKHORSE timings. None supplies the accepted clean pair or weakens
the scientific byte comparison. These are validated cache-backed Compute results,
not fresh acquisition extraction reproduction.
