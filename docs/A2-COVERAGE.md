# A2 coverage audit

The original evidence gate uses alignment columns divided by full query length. Query gaps can
make this ratio exceed 100%. Both neighbour selection and classification use that same gate.
The counts-only audit label now reports alignment columns and full query length; it does not call
their ratio query coverage and does not clamp it. The gate itself is unchanged in this step.

The native checker `--evidence-coverage-check` captured all 31,777 queries against the exact 2,721
scoped references of A1. It first reproduced the stored 2,696 neighbours and the actual recipe's
2,644 default-selected proteins exactly. Otherwise it refuses to issue an impact conclusion.

The capture retains native `qrow`, `trow`, `qs`, `qcov` and coordinates. Aligned query residues
are counted as non-gap letters in `qrow`. This build's `qs` and coordinate spans sometimes include
unaligned flanks, so they are retained as observations rather than used as the coverage numerator.
Native `qcov` agrees with row-letter coverage within integer rounding for every returned hit.
The aligned-row fields are defined in the
[USEARCH user-field documentation](https://m.drive5.com/usearch/manual/userfields.html).

| Reading at the same 70% threshold | Neighbours | Changed neighbours | Changed proposed families | Changed tiers | Default selected |
|---|---:|---:|---:|---:|---:|
| Original alignment columns | 2,696 | 0 | 0 | 0 | 2,644 |
| Actual aligned query letters | 2,695 | 2 | 0 | 0 | 2,644 |
| Letter pairs only | 2,693 | 5 | 0 | 0 | 2,644 |

Neither alternative loses or gains a default-selected protein. The checker reproduces the 208
selected audit rows whose old ratio exceeded 100%. These findings apply to this captured cohort;
they do not establish that changing the gate cannot change another dataset's families.

Artifacts are under `D:/FamilyQuant-data/out-evidence/`: `a2-coverage-first/capture` contains the
native tables and executable hash. `a2-coverage-replay-2` and `a2-coverage-replay-3` contain identical
reports and inspectable neighbour, family/selection and outlier detail. The primary report SHA256
is `E223B34EE8C9450E98DDFCAD7C4A601E15F1489CD62A2F3DBD09112C2D875F71`.

`a1-preview1-count-labels/delivery` is the regenerated counts-label packet. Its native independent
app-session replay and app Open passed, and `--evidence-delivery verify-families` proves membership,
annotations, sequences, locks and stored quantitative values unchanged from the original A1 file.
The actual query-letter gate and its visible explanation are the separately approved A6 step.
