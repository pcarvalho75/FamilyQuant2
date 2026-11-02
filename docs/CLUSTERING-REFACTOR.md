# Clustering result contract and parity

Validation counts in this note are the recorded milestone results, not a live suite total. Use the [command catalog](INDEX.md) for current invocations and report the exact tested commit.

X3 keeps the existing family assignment algorithms. `USearchTools.Cluster`
returns a `ClusteringResult` with the document, requested and executed exhaustive
pass flags, and warnings. `GenerateFCF` remains a compatibility facade.
`RemoveRedundancyDetailed` returns counts, output path and identity;
`RemoveRedundancy` retains its tuple contract.

Both entry points accept the X10 operation context. Progress, native-process
cancellation, memo cancellation and atomic redundancy output use the existing
X10 path. No WPF dependency is introduced.

An empty or failed exhaustive alignment retains the fast families as before.
The result now reports that fallback and records the actual fast method in the
document. This corrects provenance without changing assignments. Cancellation
still propagates and never becomes a fallback.

## Authoritative comparison boundary

Orchestrator ruling M31 limits the Viperidae check to fast clustering at identity
85. Its 63,554 stored records include targets and their decoys. Nobody runs its
exhaustive pass: the unchanged 30-minute native timeout would not prove refined
family equivalence. Bjararaca is compared with the exhaustive pass at identity
85. The existing exhaustive memo and coverage/identity gate tests remain.

The shared C# helper `FamilyQuant.Tests/ClusteringSnapshot.cs` is compiled into
both the current test executable and `ReferenceGoldenHost`, which links the
frozen 1.5.0.6 assemblies. It calls the program's clustering API and writes one
ordinally sorted row per protein: identifier, user family, USEARCH family and
sequence SHA256. Equality is checked on the entire output, not family counts.

Inputs and outputs stay outside the checkout in
`D:/FamilyQuant-data/out-assay/x3-parity`. Original files and frozen binaries
are read only. Both hosts use the same embedded native executable through
`FAMILYQUANT_USEARCH`; its SHA256 is
`E05C852CE29155CEA546B271AA0B79A303EBB90BC8C87299A87303832662E764`.

| Input | Source | Bytes | SHA256 |
|---|---|---:|---|
| Bjararaca | Shared Desktop FamilyQuant `uniprotkb_taxonomy_id_8724_2026_06_25_ORDENADO_CONT123_NO-Redundancy.T-R`, copied to `x3-parity/bjararaca.T-R` | 254418 | `835FF172B58471AFA40DB488587E34EE1507B1227AA61C345E454A6BE78FE1D1` |
| Viperidae | All sequences stored in `D:/FamilyQuant-data/ana_source.fcf`, in stored order, exported once to `D:/FamilyQuant-data/out-assay/viperidae-from-fcf.T-R` | 32653472 | `133D0E0C9856A4FB1450AD6708343A268E32C6791894140FB18AA64EC69648F9` |

The C# verifier checked every exported Viperidae identifier and sequence, in
stored order, with no extra records: 63,554 proteins. The shared export is never
replaced and is reused by the C7 lane.

| Comparison | Proteins | Families | Entire snapshot SHA256 |
|---|---:|---:|---|
| Bjararaca fast 85, frozen and current | 584 | 452 | `FFA9786561B3FA610DEAAE15A1B4A5A9C062DB62784255BF53BD04B3BD19FCD7` |
| Bjararaca exhaustive 85, frozen and current | 584 | 441 | `DA19D1B4DC0101F45AD00CBD3E7205F891F96044F39ECBFEE196A6A24A0FD42D` |
| Viperidae fast 85, frozen and current | 63554 | 44659 | `56F978B419FE0937F15E73667A49F504CEB3F4A881A83ACE497A959DC4BEC375` |

Run `--clustering-snapshot <input> <output.tsv> 85 <fast|exhaustive>` on
each host. File names are `{bjararaca,viperidae}-{frozen,current}-{mode}.tsv`,
with console and error logs beside each. `--clustering-verify-fcf <fcf> <fasta>`
checks the shared export without rewriting it.

The focused command `--test-clustering-results` checks result provenance,
fallback preservation, duplicate survivor behavior and redundancy output.
The ordinary quick suite retains exhaustive memo and gate checks. This packet
does not claim a Viperidae exhaustive comparison or a fresh RAW extraction.

Release build: zero warnings and errors. Quick suite: 2007 passed, zero failed,
17 skipped. The six result checks pass, and all four stored-project golden
exports match frozen 1.5.0.6 byte for byte in `out-assay/x3-integrated-golden`.
