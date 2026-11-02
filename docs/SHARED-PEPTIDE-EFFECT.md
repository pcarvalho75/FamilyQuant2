# One-run shared-peptide effect

`--shared-peptide-effect` calls the production `ComputeFullXic` path, then the production
family attribution and composition exporters. It changes no extraction or attribution rule.
The dataset label is mandatory and appears in every JSON deliverable and every CSV record.

Inputs must be copies under the study folder. Before computation, the supplied FASTA and
curation project must contain exactly the same identifier/sequence multiset. Stored UserID
families are reused only after that match. No family definition is guessed. The command
requires one copied RAW and explicitly restricts computation to its normalized run name;
other runs in the package are reported and excluded. `--prepare-only` performs these checks
without extraction.

This measurement supplies no de novo input. The denominator is the deduplicated quantified
identified envelope, using the recorded extraction settings. Shared representative counts
refer to prepared run/sequence/charge representatives, not PSMs. The shared-signal fraction
comes from the production family result. Counting in every family overlaps: its excess is
the family-row sum plus the unattributed residual, minus the measured envelope. It is
separate from the fraction carried by shared peptides.

All three policy CSVs come from `CsvReport.BuildFamilyComposition`. The study adds a quoted
dataset-scope column without changing numerical bytes or quoted multiline records. The
JSON summary, family table and quant rows retain the same scope. Operational caches remain
in the explicitly supplied lane cache folder; the source folder is never a cache target.

Repeat with a fresh output and cache folder. Compare the policy CSVs and scientific JSON
byte-for-byte. The separate provenance JSON and console log retain execution dates, paths
and cache messages. This is a measurement of the supplied run and family space; it does
not establish a transferable fraction for another database or sample.

The A5b packet is `D:/FamilyQuant-data/out-evidence/a5b-species-one-run`. Its label is:
"One run, species database, not Ana's 8689 file. These numbers do not transfer to Ana's
8689 file." It uses the supplied `_01-003` RAW. The package also contains another run;
29,575 PSMs match the selected run and the other run is explicitly excluded. The 584
database identifier/sequence entries exactly match the stored curation project.

The program reports 28,824 quant rows, collapsed to 1,816 identified representatives,
with 36 shared representatives (1.9824%). Shared representatives carry 2.0287% of the
quantified identified envelope. Counting each in every reached family introduces 2.0649%
excess overlapping signal. Forty-two families are reached. These values describe the
stored UserID family definition, one run, six isotopes and a three-point peak minimum.
No de novo input is supplied, and the denominator is quantified identified envelope.

The three policy CSVs report family-row sums of 97.8261% for family-unique, 99.8548% for
equal splitting and 101.9196% for full counting. Shared and unattributed buckets must be
read as recorded in each CSV; the full-count rows overlap. Both fresh-cache computations
produce byte-identical policy CSVs, input check, quant rows, family table and numerical
summary: seven scientific artifacts. Provenance and logs retain their distinct run dates
and paths. Executing study source and binaries are retained under `executed`.
