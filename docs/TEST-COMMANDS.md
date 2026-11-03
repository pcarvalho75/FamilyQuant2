# Test command registration

Add a flag in a feature-owned `FamilyQuant.Tests/Program.Commands.<Area>.cs` file. Do not add a branch
to `Main`. The shared dispatcher discovers attributed tables in the partial `Program` class. Each
flag has one table row; predicates and handlers live with that feature. Different lanes use different
provider files and method names, so adding a flag does not edit a shared block.

```csharp
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ExampleCommandTable() =>
    [
        new("--example-check", 10000, TestCommandPhase.Raw, ExampleRequested, RunExample),
    ];

    private static bool ExampleRequested(string[] args) =>
        args.Length == 1 && args[0] == "--example-check";

    private static int RunExample(string[] args, string workDir)
    {
        // Call the feature's real checks here and return their exit status.
        return 0;
    }
}
```

`Raw` commands run before the original dot-to-empty positional normalization. `Normalized` commands
run afterwards and receive the original suite work directory. A handler receives the same argument
array and work directory, and its exit code or exception passes through. Keep an existing flag's
phase, predicate, priority and handler behavior. Lower priority numbers win within a phase. New
independent flags can use a feature-specific range without renumbering old rows.

The metadata flag must occur in the arguments, case-insensitively, and its predicate must also pass.
The predicate preserves the command's actual case, position and arity rules. Thus a case-sensitive
predicate stays case-sensitive. A missing or unknown flag falls through to the ordinary suite, as
before. Duplicate flags, including case-only duplicates, fail catalog construction. Removing a
preserved flag also fails construction; a negative-control fixture removes one required registration
and verifies the same guard rejects it.

`--list-test-commands` prints the deterministic phase, priority, flag and handler table without
executing any handler. `--test-dispatch` checks the preserved flag inventory, phases, arity, case,
precedence, aliases, argument forwarding, duplicate rejection and culture-independent catalog bytes.
Those checks also run in the quick suite. They guard against flag literals returning to `Main` and
against `merge=union` returning for `FamilyQuant.Tests/Program.cs`.

X16 moved the previous predicates and handlers with a C# syntax audit: tokens and syntax-node kinds
were identical for every moved body and predicate, and for the ordinary suite. The audit covered
66 blocks and 74 registrations, including the new regression route. The catalog-list route was added
after that move. Scratch audit and build/test logs are under `D:/FamilyQuant-data/out-assay/x16-*`.
No scientific producer was edited or executed by the migration utility. Heavy scientific flags retain
their handlers; listing or resolving them does not run them.

`Program.cs` uses normal Git conflict detection. The removed union driver had silently deleted common
closing lines when two lanes inserted similar dispatch blocks. Resolve an actual conflict and build
after a merge; never re-enable union merging to hide it.

X16 validation at the C3 integration: Release build zero warnings/errors; focused dispatch 21 passed,
0 failed, 0 skipped; quick suite 2398 passed, 0 failed, 17 skipped. The earlier validated quick count
was 2372 before C3 and the new routing checks. Two native catalog outputs were byte-identical.
The X11 and X14 focused flags passed 23/0/0 and 12/0/0 through the registry; frozen exports already
produced for X14 passed `--golden-verify` at 4/0/0. This did not regenerate the pack, run the full
suite or execute the heavy scientific commands.

The S6/B3 integration adds four flags in Evidence and Shell providers. Its syntax audit preserved
67 incoming predicates, handlers, phases and precedence rules, plus 221 ordinary-suite statements
in order. Build had zero warnings/errors; dispatch passed 21/0/0, B3 18/0/0, S6 38/0/0 and the
integrated quick suite 2454/0/17. The existing `--shell-checks` also retains S6's new busy check.

## The default run as a table, and shards (J8)

The ordinary suite is an ordered table built by `DefaultSuite` in `Program.cs`: one entry per former call,
under the former section headings, each call keeping the `EnsureApplication()` that preceded it
(`AddWithApplication`). A run without `--shard` executes exactly the former calls in the former order and
prints the same headings. A lane adds a check as before, by adding one `suite.Add(...)` line in place.

`--test-report-command [fresh-output]` also runs once in `DefaultSuite`, with a unique temporary output
folder. It exercises the real File > Export report command, Word/workbook publication, selection,
cancellation and rollback without RAW data. Its flag registration alone previously left it outside both
ordinary quick and full gates, so J41's two list sheets and empty-list references went unchecked there.
The fixture now compares every completed native sheet/cell and pins those two sheet names and references.

`--test-suite-command-coverage [output.tsv] [--negative-control]` audits the command registry against
`DefaultSuite` without executing command handlers or suite bodies. It also runs once in `DefaultSuite`.
Its deterministic listing includes every registered flag, its declared fixture methods or individual
exclusion reason, compiled fixture entry-point references and any routing problem. With an output path,
the exact suite-method inventory is also written to `<output.tsv>.suite-fixtures.txt`.

Every flag requires a literal entry in `SuiteCommandInventory.cs`; there is no flag-prefix or wildcard
exemption. A covered entry names exact method signatures present in the compiled suite and reached from
the command handler. Missing/changed fixture routes, new undeclared flags, duplicate declarations, empty
reasons and stale exclusions fail. Shared assertion/bootstrap helpers cannot prove coverage, and a
covered subfixture cannot conceal another missing assertion fixture. This checks static enrollment,
not whether every assertion or data-dependent branch ran with a particular invocation.

Exclusions distinguish selected-data/live checks, artifact/proof producers, and existing focused-only
enrollment gaps. Each gap is named explicitly; it is not reported as gated coverage. K8b does not silently
add those unrelated packs to this release's execution set. To enroll one, add its fixture to `DefaultSuite`
and replace its exclusion with the exact fixture signatures from the audit. For a new no-RAW test, add
the suite call and covered declaration, or supply an explicit per-command reason for keeping it opt-in.
The negative-control switch adds one registered synthetic test without a suite call or declaration and
must exit nonzero; its handler is never invoked. Ordinary checks also plant missing/partial routes and
invalid exclusions to verify those failures.

`--shard k/N` (0 <= k < N) may appear anywhere in the arguments. It is removed, with its value, after the dot
normalization and both command dispatches and before any positional data slot is read, so
`. . <deNovoRoot> <acquisitionRoot> --shard 2/6` and `--shard 2/6` both work. Shard k runs the entries whose
index mod N is k. A group key keeps entries together when a later one reads what an earlier one left in a
static field (the run 01 de novo extraction read by the identified-envelope section, and the reconciliation,
three-way and homology censuses read by the De novo screen on real data); the group follows its first entry.
In a shard, an entry that came after the serial run's first `EnsureApplication()` gets the Application created
first if its shard has none yet; nothing else is added. Every shard ends with the usual summary line and exits
0 only when nothing failed, and names its three longest entries just before that line.

Each shard needs its own `TEMP` and `TMP`; the suite's fixture folder lives under the temp path. The shards
also share the per-user `%LOCALAPPDATA%/FamilyQuant2` folder: the extracted USEARCH executable (read, and
written only when missing) and the real `shell.json` and `appearance.json`, which the suite's shell and theme
checks redirect to their own fixture paths and only read for the unchanged-profile check.

The feature-ledger checks are thirteen entries in the table (base validation, six planted-change validations, the
receipt guard, the receipt fixtures, the import batch, shell metadata, message frames and the live proofs), so
shards share them; `FeatureLedgerTests()` still runs them all, in the same order, for `--feature-ledger-check`.
With `FAMILYQUANT_LEDGER_RECEIPTS=release-only`, the ordinary suite skips the receipt-currency checks: validation
leaves out the "Verified row lacks a current executed receipt" test and the receipt load it needs, and two
SKIP lines name what was left out. Every other ledger guard runs. `--feature-ledger-release-check` always
checks receipts, whatever the variable says. Unset, nothing changes.
