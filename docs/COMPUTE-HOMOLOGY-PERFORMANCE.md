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
