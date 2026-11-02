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
