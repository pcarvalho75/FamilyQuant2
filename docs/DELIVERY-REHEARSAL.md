# Delivery rehearsal

The test-side command opens a computed project in the real application window and invokes
File > Export report. It selects every section/figure and retains the saved report threshold.
It does not Compute full XIC, save the source project, or calculate scientific results.

```powershell
FamilyQuant.Tests.exe --delivery-rehearsal <computed.fqproj> <staging-folder> [--panel <workbook.xlsx>] [--allow-nonempty] [--protected-root <source-folder>]
```

The target must be a dedicated empty folder unless `--allow-nonempty` is intentional. The command
copies `project.fqproj` and `family-panel.xlsx`, publishes `report.docx` and
`report_supplement.xlsx`, runs the J18 numeric-token trace into `trace.csv`, writes a plain
English `READ-ME.txt`, and records checks in `delivery-checks.json`. The console summary gives
sizes, SHA-256 values, readable Office XML-part counts and the trace's `NOT FOUND` count. A
nonzero unmatched count gives exit code 1 and retains the files for review.

The panel must match the SHA-256 recorded in the project. The command resolves its recorded
absolute path, a same-folder basename or recorded workbook input paths. Use `--panel` when a
project records only a basename that is no longer beside it. A different workbook is refused.

All command-owned deliverables, diagnostic files, preferences and temporary work are within
the target. A fresh `.work/export-<guid>` directory allows an explicit non-empty rerun without
deleting prior work. Absolute/parent escapes, drive roots and junction/symbolic-link staging
paths are refused. The real program's normal per-user cache/state remains in its usual location
and is recorded separately, as required by the delivery boundary ruling.

For a final target under an `AnaG` ancestor, that source tree is protected automatically except
for the explicit target. `--protected-root` provides the same assertion for another source tree.
The before/after census includes directory paths and each file's size, modification time and
SHA-256. Project and panel input hashes are also rechecked independently. This is read-only
checking of the source tree; RAW files are never copied by this command.

`delivery-checks.json` lists every command-created file present at audit time, including scratch.
Its own final size/hash is printed after writing, avoiding a self-referential hash. The six
recipient files are listed separately. A numeric match remains a trace aid, not a biological
validation or proof of a particular figure's semantic provenance.

Focused small computed-project check:

```powershell
FamilyQuant.Tests.exe --test-delivery-rehearsal <fresh-fixture-output>
```

This check is in `DefaultSuite`. It covers the real command, all selected sections, stored
inputs, zero unmatched trace tokens, Office parts, original input identity, staging refusal,
explicit rerun, and protected files/directories. It is synthetic, with no RAW extraction.
