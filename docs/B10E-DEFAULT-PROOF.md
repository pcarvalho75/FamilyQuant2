# B10e default-length proof

The real Bothrops jararaca project opens at minimum tag length 8 without a stored
length override or a picker action. Its top-level computed table is also at 8.
The input is B10b's native computed project:
`D:/FamilyQuant-data/out-opus/b10b/computed12/computed.fqproj`, SHA256
`0F39525A3B25FC2222998B3395570BE69BBFD27DC5B9EC9E3A81A2FA554E1FF4`.

The existing screen producer now has a `--require-default` option. It captures
the initial Results screen and lower family-rate panel in Dark and Light before
any export status can add a run-specific path. It exercises the real Results
export selector and button, checks the CSV against Core's composition writer,
and verifies every FASTA accession, sequence and snapshot hash against the stored
world. It leaves the input bytes unchanged.

The native default values are:

| Value | Stored result |
|---|---:|
| Minimum tag length | 8 |
| World proteins | 4,203 |
| Residual de novo envelope | 269071929058.21497 |
| Credited envelope | 55264469830.691635 |
| Landed fraction of residual | 0.2053892058682008 |
| Decoy fraction of credited envelope | 8.352170549939114E-05 |
| Known answers right / landed | 1902 / 1954 |
| Families with a displayed right rate | 9 |
| Suggested FASTA records | 1,213 |

The older project stored at top-level length 9 fails the new guard (3 passed,
1 failed). The approved default-8 project passes twice (4 passed, 0 failed each).
The CSVs, FASTAs, four native PNGs, values JSON and printed values are
byte-identical between the two final runs. All four captures were inspected:
the length choices, amounts, rate headers and FASTA state are readable in both
themes. Packets are under `D:/FamilyQuant-data/out-results/B10e/final1` and
`final2`. `repeated-sha256.txt` records representative output hashes.

Reproduce with the Release harness:

```text
FamilyQuant.Tests.exe --database-gap-lengths-screens D:/FamilyQuant-data/out-opus/b10b/computed12/computed.fqproj <fresh-output> --require-default
```

This proof reads B10b's stored native Compute output and does not repeat RAW
extraction or the family-world search. Product code and scientific rules are
unchanged by this proof.
