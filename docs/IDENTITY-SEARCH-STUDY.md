# Identity scan and Divide comparison

Validation counts in this note are the recorded milestone results, not a live suite total. Use the [command catalog](INDEX.md) for current invocations and report the exact tested commit.

C7 keeps both identity-selection flows. Bjararaca alone disproves equivalence:
at the default acceptable target/decoy mixing score of zero, the lowest acceptable
point in Scan's five-percent grid is 75%, while Divide returns 73%.

Scan displays a curve for the user to inspect; it does not choose a threshold.
This comparison defines its candidate as the lowest measured grid point whose
mixing score meets the limit. Divide searches at one-percent resolution. A grid
candidate and a finer probe answer therefore need not be the same number.
Neither result is a biological recommendation.

The compatibility `CalculateFDR` API and the reported target/decoy mixing values
remain. The score is mixed families divided by all families, and the separately
reported target-relative score uses families containing targets. This is not a
statistical false discovery rate and cannot detect unrelated target families
being fused.

## Method and accepted boundary

`FamilyQuant.Tests/IdentitySearchStudy.cs` calls the current program's
`ScanIdentities` and `DivideAndConquerDetailed` APIs. The test-only flag is:

```text
FamilyQuant.Tests.exe --identity-search-study <input> <empty output directory> <fast|exhaustive>
```

The scan is exactly 95, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45, 40, 35, 30,
25, 20, 15, 10. Divide's acceptable mixing score is zero. Each file is studied
once in one process so the exhaustive alignment memo can be reused. The full
probe console log is retained. Probe TSV ratios come from the program's integer
counts, avoiding loss of precision in its percentage display.

M31 and M33 authorize Bjararaca exhaustive and Viperidae fast. No Viperidae
exhaustive run is made. Input provenance, byte sizes and hashes are recorded in
[the X3 parity note](CLUSTERING-REFACTOR.md). The study uses the same copied
Bjararaca file and the one shared Viperidae export from the source `.fcf` in
stored order. Input SHA256 is checked before and after each study. Runs use
normal priority and `DOTNET_PROCESSOR_COUNT=4`.

## Measured cases

| File and mode | Lowest acceptable Scan point | Divide answer | Divide probes in order | All-pairs runs |
|---|---:|---:|---|---:|
| Bjararaca exhaustive | 75% | 73% | 100, 50, 75, 62, 68, 71, 73, 72 | 1 |
| Viperidae fast | 70% | 70% | 100, 50, 75, 62, 68, 71, 69, 70 | 0 |

Bjararaca at 73% has 339 groups, 169 with targets and zero mixed. At 72%,
327 groups include 162 with targets and one mixed. Thus the rejected point's
score is `1/327`, and its target-relative score is `1/162`.

Viperidae at 70% has 40,399 groups, 20,194 with targets and zero mixed. At 69%,
40,107 groups include 20,049 with targets and one mixed. The rejected point's
score is `1/40107`; its target-relative score is `1/20049`. The all-family
percentage display rounds that nonzero score to `0.00%`, so it must not be read
as zero. The helper retains the integer counts and full-precision ratios.

Viperidae's agreement does not remove the Bjararaca counterexample. Both flows
remain available, with their existing score reporting and numerical rules.

Packets are under `D:/FamilyQuant-data/out-assay/c7-study/{bjararaca,viperidae}`:
`scan.tsv`, `divide-probes.tsv`, `divide-full.log` and `summary.json`.
The full outer logs sit beside the packet directories. These are program-made
measurements, not Python results. No RAW extraction or numerical rule changed.
Positive mixing allowances and other input files have not been compared.
Each agreed case was run once under M33; the full study was not repeated.

| Packet file | SHA256 |
|---|---|
| Bjararaca `scan.tsv` | `0CDAE5A4D49E02D387F38E06DA414F9223C8DD571BBB0C95E38B465657EFD30F` |
| Bjararaca `divide-probes.tsv` | `B0C1310D7B9A1E81DD713726CDC588DBCFAD47BCB7DAEA5B9624682630F01CF9` |
| Viperidae `scan.tsv` | `D1CDF6F39DDE4FC959AE2BD5CFF1689A6ABD208D52F6978CA0D76A337C3391FA` |
| Viperidae `divide-probes.tsv` | `3760E1C37572F3A8CF87097A80BF059AD447C3F88EEEC56D81768ACB278DB666` |

Three quick checks protect precise probe-count parsing, refusal of a missing
probe log and the distinction between grid and probe answers.
They were red before the helper existed and pass in the ordinary quick suite:
2049 passed, zero failed, 17 skipped. Release build has zero warnings and errors.
No full RAW suite was run by this lane.
