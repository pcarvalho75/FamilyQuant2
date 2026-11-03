# Reviewed panel bulk retrieval

`ReferencePanelFetcher.FetchAsync` uses UniProt's `uniprotkb/stream` endpoint. It sorts and
case-folds the families found in the file, combines the previous `family:"..."` clauses with
OR, and splits when adding a clause would take the complete escaped URL above 7,000
characters or exceed UniProt's 100-condition query limit. No family, taxon or protein list is built into the rule. An empty selection makes
no requests.

The requested fields are unchanged from the paged route. Every entry goes through the same
`EvidenceFetcher.Parse` and exact, case-insensitive family-membership filter. Text-search
matches without the requested membership are dropped. Entries remain unique and sorted by
accession; similarity prose is dropped and tissue specificity is kept, as before.

Stream replies do not supply `X-Total-Results`. Before each stream, a `search` request with
`size=0`, `fields=accession` and the identical query supplies the completeness count. A missing
count, missing/changed release, unexpected pagination, incomplete body, malformed entry,
unreviewed entry, refused request or cancellation prevents publication. All count and stream
replies must use one release. The combined evidence session also retains its cross-phase
release check and one final commit. The existing retry and timeout policy is unchanged.

Progress still counts completed families. A completed stream may advance several families
together, with a cancellation checkpoint at each reported family. All downloaded entries
remain detached until the entire fetch succeeds. Provider, release, families and retrieval
time retain the existing stored schema. Projects continue to answer offline.

UniProt documents the shared query/field semantics of search and stream in its
[API paper](https://doi.org/10.1093/nar/gkaf394). The stream is not resumable; a failed request
is retried from its start, then the operation fails without a partial panel.

## Checks

`--test-reference-panel-stream` runs stream batching, entry-level parity with the former
route, release/completeness and cancellation fixtures, the retained paged-panel checks and
the operation/commit checks. `--test-evidence-fetch-atomic` exercises the real combined
session and Fetch evidence button. Both are feature-owned commands; the default suite also
registers the stream checks once in `DefaultSuite`.

`--reference-panel-parity-live <input.fcf> <new-output-directory>` fetches evidence once,
then times streamed and old paged panel retrieval independently for every discovered family.
The old route is retained behind `usePagedComparison: true` for this comparison only.
The checker reads its own supplied source and never consults another running project.
Ctrl+C requests cancellation.

Add `--resume` to reuse that command's evidence and streamed-panel checkpoints after an
interrupted paged comparison. The checker verifies the source identifiers/sequences/grouping,
source hash, requested families and release, then pins the checkpoint hashes. Successful
paged replies are retained verbatim with their headers and SHA256, so further interruptions
can replay completed requests without network access. Changed checkpoint or cached bytes are
refused. Only this comparison transport honors a provider `Retry-After` before retrying a
429 (30 seconds when omitted); the production retry policy is unchanged. The final paged
time includes active attempts and retry waits, including any replay processing, and excludes
time spent with the command stopped. Earlier attempts that predate these resumable captures
remain separate diagnostics and are not presented as a complete baseline timing.

The packet manifests record each response URL, release, total when supplied, body SHA256,
size and elapsed seconds. `entry-parity.csv` compares the full serialized entry, including
sequence and every evidence field, by accession. `validation.json` reports the timings,
request counts, release and mismatch count. Independent panel retrieval timestamps are
recorded separately; they are the only field excluded from exact panel parity. The fetched
protein evidence and its provenance timestamps are hashed before and after both routes.
A successful comparison saves `streamed.fqproj` and reopens it offline, comparing the panel
and protein evidence again. The original input hash must remain unchanged. These checks do
not claim new RAW extraction or biological accuracy.
