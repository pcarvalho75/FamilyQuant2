# App operation host

S6 provides `AppOperationHost`, the shell's `Operations` property and `BusyOverlay`.
One lease owns progress and cancellation until the actual operation ends.
The host blocks navigation, editing, keyboard commands and close while busy.
Close requests cancellation and leaves the window open until work has ended.
Escape and the overlay's Cancel button use the same token.
Unknown progress stays indeterminate. Old queued progress cannot update a new run.

Views obtain the inherited host through `AppOperationHost.For(view)`.
View models accept the shell host through their `Operations` property.
Use `using var lease = host.Begin(title)`, pass `lease.Context` into Core,
and check it immediately before attaching prepared results.
`AppOperationHost.OnUi` starts async view commands with a scoped dispatcher context,
including when a headless caller has no WPF synchronization context.
It restores the caller's original context before returning.

Only the final publication uses `OperationContext.Commit`.
An atomic commit wins a late cancellation request. Intermediate cache/staging files
are not the user's final result. Opaque vendor loading and synchronous serialization
observe cancellation at their boundaries; their threads are never aborted.

| Operation path | Adoption and proof |
|---|---|
| Build, identity scan/search, redundancy | Core context reaches native work. `BusyBuildCancellationTest` cancels running Build before save and compares prior bytes. `BusyDocumentTests` pre-cancels each Core path; the native-child test cancels its exact running process. |
| Open and verified Save | File reading and verified staging run off the UI thread. Cancel refuses attachment or final replacement. `BusyDocumentTests` cancels after staging and checks prior bytes, manifest fields, save check and temporary cleanup. |
| FCF export and SEPro reannotation | FCF uses its atomic save. Reannotation stages the separate regrouped copy before one publication. The original package is read only. Shared file-export cancellation checks retain prior bytes. |
| Quantify | Keeps its request/result contract, uses the host token and rejects late completion after cancellation. `QuantifyViewModelTests` exercises the running command and prior result behavior. |
| De novo import, reconciliation, homology | Stages outcomes before applying. Homology forwards context to the index/search. `BusyDeNovoTests` cancels running import and reconciliation/search before I/O. Retired extraction/partition compatibility methods also use the host. |
| Results preparation and exports | Preparation runs the existing model construction off the UI thread. CSV/figure writers prepare output before publication. Figure generation uses an owned STA thread. Existing synchronous model exports remain available to programmatic callers. |
| De novo and secondary-window exports | Use the same staged-file path. Secondary windows host the same overlay control in their own window, with close guarded until work ends. |
| Clustering diagnostic | Analysis runs the native diagnostic off the UI thread with the shared progress/Cancel context. It reads a saved unchanged FCF/FQPROJ and writes a fresh child folder. The adapter reads the returned report path and commits completed shell publication before opening the text and folder. Native fixture checks preserve input bytes/grouping/locks and compare two packets per format; injected presenters avoid launching desktop windows in the checks. |
| Curation and online evidence fetch | C1 routes curation through the shared host. S9 stages protein evidence and the reviewed panel together; the curation adapter passes the shared Cancel token into both network phases and keeps the shell's final document Commit. `--test-shell-evidence-cancel` exercises active requests, unchanged document bytes/objects on cancellation or panel failure, single publication and late Cancel. |

`--test-busy` runs focused host and cancellation checks.
`--busy-screens <output-dir>` captures unknown progress, counted progress and cancellation
in both themes at the minimum shell size, using labelled synthetic fixtures.
These checks do not claim a new RAW extraction, installed-product test or full scientific validation.

## Combined evidence fetch

`EvidenceFetchSession.FetchAsync(file, operationContext, handler)` is the S9 Core seam for the
online action. Protein evidence and the reviewed panel are prepared on detached records and
provenance. The context token and progress reach both fetch phases. A panel failure or a release
change refuses the complete transaction. The original protein objects, IDs, locks and quantitation
are retained; only fetched evidence, domains and their provenance attach at the final commit.
The commit also refuses a file changed while the requests were running.

The original progress overload remains available and accepts an optional cancellation token.
`--test-evidence-fetch-atomic` covers cancellation before starting, during either request phase,
between phases, after the panel, and before publication. It also checks failures, a source edit,
the panel-only path and the rule that a completed commit wins a later cancellation.
Connecting the shell's Cancel button to this Core seam is FQShell's follow-on integration.
