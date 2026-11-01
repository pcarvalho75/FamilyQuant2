# Core operation contract

X10 publishes these WPF-free types in `FamilyQuant.Operations`:

- `OperationContext`: one per operation, shared by nested phases; exposes the
  `CancellationToken`, `Report`, `ThrowIfCancellationRequested` and `Commit`.
- `OperationProgress`: immutable `Stage`, `Message`, optional `Completed` and
  `Total`. An unknown count stays null.
- `OperationStatus`: `Succeeded`, `Cancelled` or `Failed`.
- `OperationResult<T>`: `Status`, guarded `Value` and optional `Failure`.
- `OperationFailure`: operation name, message and the original exception `Cause`.
- `CoreOperation.Run` and `CoreOperation.RunAsync`: the shared result boundary.

The runner creates a fresh context. Cancellation is observed before work and
before returning an uncommitted result. A cancelled or failed operation has no
usable `Value`; reading it throws instead of silently returning a scientific zero.
Only cancellation requested through this operation's token receives `Cancelled`.
An unrelated cancellation exception remains a failure with its original context.

`RunAsync` awaits an async delegate with `ConfigureAwait(false)`. It does not move
blocking CPU work off the UI thread. The App operation host schedules synchronous
work on a worker thread and owns its busy state, result attachment and Cancel.

## Progress and cancellation

Report a stable stage name, a short user message, and counts when known. The
context observes cancellation before and after the report. `Messages(stage)`
adapts an existing `Action<string>` progress seam.

Core routines use `ThrowIfCancellationRequested` at bounded work boundaries and
before output publication. The optional operation context is passed to nested
work, not replaced by a fresh context. Existing callers without a context retain
their behavior. Network APIs can use `context.CancellationToken` directly.

## Final publication

Prepare and validate output before `Commit`. Its action must perform one atomic
final change, such as replacing a completed temporary file or attaching a fully
prepared result. A context permits one final commit. Intermediate cache writes do
not count as this user-visible commit.

Cancellation observed before the commit prevents publication. Once the atomic
commit succeeds, a late cancellation request does not relabel the operation as
cancelled after it changed the user's file. Exceptions in the commit action still
produce a failure, so the action must provide its own atomic I/O semantics.

## Adoption

The contract commit is `90d1a05`. The wiring commit adds an optional final
`OperationContext` parameter to these existing APIs:

| Area | API |
|---|---|
| Clustering/alignment | `USearchTools.Align`, `GenerateFCF`, `CountSequences` and its exhaustive pass |
| Identity selection | `FamilyClusterFile.ScanIdentities`, `DivideAndConquer` and detailed result |
| Curation files | `FamilyClusterFile.Load`, `Save`, `SetLookupDictionary`, `AttributeXICToGroups` |
| Redundancy | `RedundancyTools.RemoveRedundancy` |
| Homology | `HomologyDatabase.Build`, `HomologyMapper.Search` and `SearchOne` |
| Project files | `ProjectStore.ReadFqproj`, `WriteFqproj` and `ShellProjectOperations.Read` |

Existing callers retain their argument defaults. Cancellation during native work
stops the owned process tree; cancellation never becomes an exhaustive-pass
fallback. Failed/cancelled memo entries are removed. Save/redundancy output uses
a unique temporary file and one final replacement. Cancelled project writes also
restore the manifest fields prepared for writing. Attribution publishes displayed
shares only after all family totals are prepared.

Opaque vendor calls and synchronous JSON serialization are cancellation
boundaries: a request is observed before/after them, not by aborting their thread.
No partial result from those calls is attached or published after cancellation.

Per orchestrator M24, further adoption belongs to FQEvidence for compute during
X5, FQResults for WordCluster, and FQOpus for its global method. Their adoption
does not hold this X10 plumbing open. Evidence fetching already has a published
token/progress seam, which its owner can adapt. FQShell owns the S6 App consumer.

The focused test command is `FamilyQuant.Tests.exe --test-operations`. Use a
worktree-specific TEMP directory, as the quick-suite launcher does. The regular
quick suite runs the same contract checks.

Wiring verification: clean Release build; 28 focused checks pass, including a
controlled running native process, cancellation before final save replacement,
manifest restoration, and no partial homology/attribution result. The quick suite
passes 1906 checks with zero failures and 17 skips. The stored-project golden
comparison passes all four byte checks against frozen 1.5.0.6. The Opus-owned
global-search method body is unchanged.
