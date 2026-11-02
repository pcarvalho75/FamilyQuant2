# Measured improvement candidates

X12 measures candidates before changing product rules. The program's Core diagnostic creates
these tables. Paulo approved D14: clustering defaults stay unchanged, with no automatic merge
on shared peptides and no production reviewed-family veto. D15 approves one common contaminant
predicate. Quantitation and golden snapshots remain unchanged.

## Coverage in the first clustering pass

The fast pass accepts centroid joins using identity alone. The current exhaustive pass starts
with those families and can merge them further; its coverage gate cannot undo an earlier join.
Consequently, adding a gate only to the second pass does not enforce that gate on every join.

The checker distinguishes these policies:

| Policy | Operation |
|---|---|
| before-fast | Existing native `cluster_fast`, with identity alone |
| native-terminal-coverage | Fresh `cluster_fast` with `query_cov` and `target_cov` equal to the identity fraction |
| posthoc-paired | Remove failing edges from the observed fast join tree; do not search again |
| current-exhaustive | Existing first-pass families plus the existing all-pairs gate and union-find |
| allpairs-paired | Start from singletons; join every all-pairs edge meeting identity and paired-letter coverage |
| paired-reviewed-veto | Same diagnostic graph with a component-level reviewed-family conflict veto |
| overmerge-negative-control | Put every target in one family and every decoy in another |

For the paired-letter diagnostic, coverage is `pairs / max(query length, target length)`.
The identity percentage is also the required coverage percentage, matching the current
exhaustive gate's threshold relationship. This diagnostic is separate from the evidence
neighbour's configured query coverage and does not change that rule.

USEARCH documents `alnlen` as alignment columns and `pairs` as letter-pair columns. Internal
gaps can make them differ. Its native coverage accept options discard terminal gaps, which
does not establish equivalence to the paired-letter rule. The checker reports remaining
paired-letter failures after native coverage rather than assuming equivalence.

The checker reads the actual fast alignments, not fresh alignments of only the rejected pairs.
It reconciles every UC hit against the user table. A UC hit missing from userout is recovered
from its compressed alignment and marked `uc`; overlapping rows must agree on target, paired
letters and nonterminal alignment columns. `M` counts paired letters, so the I/D direction is
irrelevant to this calculation. Exact-match shorthand also requires identical input sequences.

Primary specifications: [accept options](https://www.drive5.com/usearch/manual/accept_options.html),
[user fields](https://www.drive5.com/usearch/manual/userfields.html),
[UC records](https://www.drive5.com/usearch/manual/opt_uc.html).

### Bjararaca, 85 percent identity

This is the existing frozen-parity control input, containing 584 records: 292 targets and
292 decoys. The native diagnostic reproduces the current exhaustive result of 441 families.
The first-pass tree contains 132 joins; 43 fail both the current column gate and the paired
gate. Of 66 joins whose query is a target, 20 fail each gate.

| Policy | All families | Target-only | Decoy-only | Mixed | Exact identified peptides shared across families |
|---|---:|---:|---:|---:|---:|
| before-fast | 452 | 226 | 226 | 0 | 684 |
| native-terminal-coverage | 489 | 244 | 245 | 0 | 805 |
| posthoc-paired | 495 | 246 | 249 | 0 | 913 |
| current-exhaustive | 441 | 221 | 220 | 0 | 595 |
| allpairs-paired | 482 | 241 | 241 | 0 | 789 |

The stored project supplies 4,734 distinct PTM-stripped identified peptide sequences, with
I/L retained as different letters. Exact sequence containment maps 4,310 to at least one
noncontaminant target. The shared count's denominator is those 4,310 mapped sequences.
It is a sequence ambiguity count, with no intensity weighting and no fresh RAW extraction.
The same identified sequence is counted once across runs, charges and modification states.

Native coverage and posthoc splitting give different results. A rejected query may join a
different centroid during a fresh native search. Neither operation reproduces an all-pairs
graph with a coverage requirement on every accepted edge.

The input project contains no directly reviewed source-family labels. Therefore this cohort
does not test the proposed family veto. Its zero reported conflicts is an empty evidence
denominator, not evidence that every family is biologically coherent.

Independent native packets: `D:/FamilyQuant-data/out-assay/x12-bjar-03/` and
`D:/FamilyQuant-data/out-assay/x12-final-bjar-a/`. All five raw native files are byte-identical,
including the complete 170,236-row all-pairs table. `summary.json`, `fast-joins.tsv`,
`small-allpairs-gated.tsv`, `assignments.tsv` and `identified-peptides.tsv` contain the measured
counts and the per-record differences. The report's repeat uses recorded-alignment replay;
this is a second report calculation, not another native alignment execution.
`x12-final-bjar-b/` reproduces all 15 report files byte-for-byte. The independent-native and
report-replay receipts are `x12-bjar-native-repeat-check.log` and `x12-bjar-replay-check.log`.

### Ana's stored database

The original file records 70 percent clustering identity. Its diagnostic uses the shared,
unchanged FASTA exported by the program and the searched evidence file supplied to this lane.
It verifies every identifier and exact sequence before invoking USEARCH. The FASTA's stored
order is retained. Ana's audit runs the fast policies only; all-pairs is refused above 1,000
records.

The pilot contains 63,554 records, including 31,777 targets. Of 23,155 fast joins, 4,734 fail
the current column gate and 4,877 fail paired-letter coverage. For the 11,583 target-query
joins, the respective counts are 2,324 and 2,393. Eight fast joins occur in UC but not userout;
their compressed native alignments are retained with an explicit origin field.

| Policy | All families | Reviewed-family conflict components | Proteins whose family membership changes |
|---|---:|---:|---:|
| before-fast | 40,399 | 6 | 0 |
| native-terminal-coverage | 43,402 | 3 | 14,144 |
| posthoc-paired | 45,276 | 3 | 13,940 |
| posthoc-paired-reviewed-veto | 45,282 | 0 | 13,948 |
| overmerge-negative-control | 2 | 1 | 63,554 |

There are 1,179 targets with eligible directly reviewed family labels. Native coverage still
accepts 70 joins below paired-letter coverage. Zero mixed target/decoy families occur in every
policy, including the deliberately collapsed negative control. The searched `.fcf` has no
identified peptide table, so peptide-quality counts are unavailable for this cohort.

The conflict denominator is components with at least two eligible labelled proteins: 154 for
the current fast pass, 165 for native coverage, 131 for posthoc paired coverage and 130 for
the experimental veto. Source labels remain sparse observations, not a complete truth set.

The roadmap's historical 2,329 of 11,584 target joins was not reproduced by this fresh run.
The checker records its actual inputs, order and executable fingerprint; no threshold or input
was changed to recover the historical count. Current and frozen 1.5.0.6 fast snapshots at
70 percent are byte-identical for all 63,554 records, producing 40,399 families. Their SHA256
is `55F01923100740722B0ED7CC8A6584D520D141761E0DA0804EB13EF24FE7EFD5`.
The diagnostic's before-pass IDs also match every current default ID. The historical
difference is not a frozen/current regression on this exported input; its original cause
has not been established.

Final packets: `D:/FamilyQuant-data/out-assay/x12-final-ana-a/` and `x12-final-ana-b/`.
Two fresh native runs produce all 13 files byte-identically, including the raw native tables
and UC files. Verification logs are `x12-ana-repeat-check.log`, `x12-default-baseline-check.log`
and the frozen/current snapshots beside these directories.

## Identified peptide sharing and over-merging

An identified peptide found identically in two families establishes ambiguous attribution
for that peptide. It does not establish that two complete proteins belong to one family.
Conserved sequence, shared domains and fragments can all produce this observation.

Minimizing between-family peptide sharing alone is a degenerate objective: a single target
family has no between-family shared peptide. The negative control demonstrates the same
limitation in the measured data while keeping targets separate from decoys. Target/decoy
mixing also remains zero in that control. Neither measure independently detects over-merging.

Use sharing as a reported ambiguity diagnostic, paired with independent sequence and reviewed
annotation evidence. Do not automatically merge families merely to reduce the shared count.
The checker never uses a known family answer, protein name or domain accession as a rule.

## Reviewed evidence as a component veto

The diagnostic admits a label only from a directly reviewed, nondecoy, noncontaminant source
record with one unambiguous family. Missing, unreviewed, inherited or multiple-membership
labels are not veto truth. Family names come entirely from the stored evidence.

A veto compares all eligible labels in both components, so an unlabelled protein cannot
silently bridge two conflicting labelled components. The checker sorts edges by query and
target identifier for reproducibility. A different order can yield a different admissible
partition; this ordering has not been selected as a product rule.

Different source labels are an annotation conflict diagnostic. Source hierarchy, annotation
age and biological multi-domain architecture still require scientific review. A lack of
eligible evidence cannot certify coherence. Paulo's D14 leaves this experimental veto out
of production grouping; its measurements remain available in the diagnostic.

## One contaminant predicate, decisions D5 and D15

The previous product contained three predicate semantics: substring ignoring case in display
filtering, prefix ignoring case in the noncontaminant target-family counter, and ordinal
prefix in evidence. The approved common predicate applies the database's exact `contaminant_` prefix after
unwrapping one exact `Reverse_` decoy prefix. It changes no identifier and stores no exception
list. `SequenceLabels.IsContaminant` now serves filtering, scoring and evidence predicates.

On the 584-record Bjararaca input, display filtering marks 242 records, while each prefix-only
predicate marks 121. The proposed predicate marks 242, with zero target classification changes
against the display filter. The additional 121 versus the existing prefix predicates are
decoys. Ana's respective counts are 762, 381 and 762, also with zero target changes against
the previous display filter. The extra decoys do not enter the scoring counter's target
denominator, because that counter excludes decoys first. The final packets report old and
new noncontaminant target-family denominators for each policy. Protected presentation
producers retain their frozen behavior.

The measured current-fast noncontaminant target-family denominators are 19,941 before and
after D15 for Ana, and 122 before and after for Bjararaca. Bjararaca's current exhaustive
denominator is 117 in both cases. Every other reported diagnostic policy also retains its
denominator. The differently cased synthetic label does change classification as D15 requires;
this corrects the rule without claiming it changes the supplied real datasets.

Four behavioral checks were red before this change: exact case/prefix in filtering, the two
wrapped evidence predicates and a differently cased target in the scoring denominator.
They passed after the common predicate was applied. The complete focused guard run adds
generated-input report equality, source preservation, cancellation, overwrite refusal and
large all-pairs refusal.
The diagnostic/API/replay run passes 35 checks. The final focused command also runs the nine
existing canonical-prefix checks, for 44 passed and zero failed. It rejects replay with a different document, FASTA,
identity or native executable. The red D15 log is `x12-d15-red.log`; final focused checks are
in `x12-final-focused-green.log` under the lane output folder. The first quick suite exposed
a duplicate prefix literal in explanatory report text; it is now composed from the shared
constants, preserving the report bytes. No guard was weakened.

## Commands and current claim boundary

Run `--test-clustering-improvements` for the diagnostic guards. Run:

```text
FamilyQuant.Tests.exe --clustering-improvements <document> <matching-FASTA> <fresh-output> <identity-percent> [--small-exhaustive]
```

The installed program can call `FamilyQuant.Diagnostics.ClusteringDiagnostics.WriteReport`.
It reads a saved `.fcf` or `.fqproj` and writes a separate report packet. Omit `matchingFasta`
to generate the native input from the saved document in its stored order. The result exposes
`Folder`, `SummaryFile` and `TextReport`; FQShell owns the visible Analysis action for D14.
Progress and cancellation use the existing Core operation context.

`ReplayReport` recomputes the same report from a recorded native packet, checking the exact
document, FASTA, identity and executable fingerprints first. The test command is
`--clustering-improvements-replay <document> <FASTA> <packet> <output> <identity> [--small-exhaustive]`.
Replay logs identify the recorded source explicitly. Fresh-native checks and replayed report
checks are different validation levels. The native-producing source and compiled assemblies
are retained at `D:/FamilyQuant-data/out-assay/x12-executed-native/`.

The optional exhaustive mode is explicitly limited to 1,000 records. Every data table uses
invariant formatting, fixed row order and explicit entities. Summary provenance includes
the document, FASTA and executable hashes. Compare two fresh runs byte-for-byte before READY.

This work has not reproduced Ana's identification or quantitation, fetched new evidence,
changed production clustering policy, updated a golden snapshot or validated a new biological
grouping. D14 and D15 have Paulo's approval; independent science/code review remains required.

The READY checkpoint builds with zero warnings/errors. The quick suite is 2,836 passed,
zero failed and 17 skipped after fixing the existing canonical-prefix guard failure.
The final shared-tooltip clarification was rebuilt and passes the 44 focused checks.
Stored-project golden is 4/0/0, with all four files byte-identical; it is not fresh RAW
reproduction. FQShell's visible Analysis action and its interaction/render checks are not
part of AS's validation and remain required for the integrated D14 feature.
