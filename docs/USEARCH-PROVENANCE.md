# Bundled USEARCH provenance and parity, X15

The pinned artifact is `usearch11.0.667_win32.exe` from the author's
[`rcedgar/usearch_old_binaries` repository at commit 699ee4c](https://github.com/rcedgar/usearch_old_binaries/tree/699ee4c996cfd9f164035c61821ca191197d7bf7).
The author's [README at that commit](https://github.com/rcedgar/usearch_old_binaries/blob/699ee4c996cfd9f164035c61821ca191197d7bf7/README.md)
identifies these binaries as CC0 and supplies the Windows 32-bit MD5 checksum. The fresh download
used the immutable [raw artifact URL](https://raw.githubusercontent.com/rcedgar/usearch_old_binaries/699ee4c996cfd9f164035c61821ca191197d7bf7/bin/usearch11.0.667_win32.exe).
The original CC0 notice is retained as `FamilyQuant/DLLs/USEARCH-CC0.txt`.

| Artifact | SHA256 | MD5 |
|---|---|---|
| Author's upstream file | `CFB6A77FF0CABCF9D7334EE298A1DF22B2351A54C65418F0EB93F3913A10ACD3` | `6EEAC51DA9576DD240CD08E4662B5BBF` |
| Previous bundled file, origin unproven | `E05C852CE29155CEA546B271AA0B79A303EBB90BC8C87299A87303832662E764` | Not used for provenance |

The old file's different hash is not evidence of a particular modification. X15 replaces it with
the pinned upstream bytes only after the following full assignment comparisons pass.
Both files are 1547264 bytes, so size alone cannot distinguish them. The existing extractor compares
the cached executable's SHA256 with the embedded payload. Each payload uses its full SHA256 in
the extracted filename, so older running copies retain their own executable. A unique staged file
and atomic move publish complete bytes. A named mutex serializes publishers of the same path across
threads and processes; ownership is followed by another hash check. A concurrent winner is accepted
only after validation.
The override remains available. A regression holds older same-size bytes open, extracts a different
payload, reuses identical locked bytes and checks concurrent callers and staging cleanup.

All runs use identity 85 and the unchanged production clustering API. Each upstream case ran twice
in fresh test processes with `FAMILYQUANT_USEARCH` pointing directly to the verified download.
This avoids both native-table memo reuse across binaries and another worktree's shared extracted
executable. The `.T-R` inputs and frozen comparison packet are the ones documented in
[the clustering comparison](CLUSTERING-REFACTOR.md).

| Input and mode | Records, including decoys | Families | Entire assignment TSV SHA256 |
|---|---:|---:|---|
| Bjararaca fast | 584 | 452 | `FFA9786561B3FA610DEAAE15A1B4A5A9C062DB62784255BF53BD04B3BD19FCD7` |
| Bjararaca exhaustive | 584 | 441 | `DA19D1B4DC0101F45AD00CBD3E7205F891F96044F39ECBFEE196A6A24A0FD42D` |
| Viperidae fast | 63554 | 44659 | `56F978B419FE0937F15E73667A49F504CEB3F4A881A83ACE497A959DC4BEC375` |

Both outputs in every row are byte-identical to each other and to the frozen/before packet. These
are per-record identifier, user family, USEARCH family and sequence-hash comparisons, not counts
alone. Viperidae exhaustive was not run. This does not establish parity on every possible database.
Native logs, input hashes and TSVs are under `D:/FamilyQuant-data/out-assay/x15-*`.

`--test-usearch-binary [published-Core-assembly]` checks the vendored and embedded bytes, the CC0
notice and command registration. Its optional assembly argument loads a separate publish output
in an isolated assembly context and checks the actual embedded payload there. The normal quick
suite includes the checkout and running-Core checks through the dependency section. This does not
change the existing four-file golden snapshot, a scientific rule, the app version or publish profile.

Validation: old checkout/resource/notice/publish checks were red at 1 passed, 4 failed. The locked-old
payload regression was red at 6 passed, 1 failed. A quick run also exposed an identical-publication
race, fixed by serializing publishers. Final Release build had zero warnings/errors; focused checks
were 10/0/0; quick suite 2594/0/17; the actual published Core resource check 11/0/0; and fresh stored-
project golden 4/0/0. A native smoke through the extracted executable, with no override, passed
12/0/0 and its extracted SHA256 matched the pinned artifact.

The .NET Framework MSBuild rehearsal produced local `setup.exe` and `FamilyQuant2.application` in
`out-assay/x15-publish`. No upload, app version change or publish-profile edit occurred. These checks
do not constitute an installed-profile test, the full suite or fresh RAW quantitation validation.
