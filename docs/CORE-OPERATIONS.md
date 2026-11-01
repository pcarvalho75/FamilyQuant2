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

This first X10 commit publishes the common contract and tests. Long-operation
wiring is the next X10 commit; this contract alone does not make an existing
synchronous API interruptible. Evidence fetching already has its own published
token/progress seam, which its owner can adapt. FQShell owns the S6 App consumer.

The focused test command is `FamilyQuant.Tests.exe --test-operations`. Use a
worktree-specific TEMP directory, as the quick-suite launcher does. The regular
quick suite runs the same contract checks.
