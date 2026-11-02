# Text-preserved ledger rows (N6a)

The text tool closes OPEN ledger rows whose frozen 1.5 text is in the compiled 2.0 program. It reads the
assemblies, not the source. Run it from a built checkout, with a timestamp taken from `date`:

```text
FamilyQuant.Tests.exe --ledger-text-preserved <frozen-source-root> <fresh-output> <date-timestamp>
FamilyQuant.Tests.exe --feature-ledger-evidence-import <fresh-output>/N6a-text.json <report-output> [--replace-existing]
```

The frozen root is the clean source export the ledger was scanned from (`frozenN3` in the refresh map). Every
frozen file must match the scan's SHA256. The cohort is every retained row declared OPEN, plus the rows this
tool closed before, so a refresh proves them again. Rows of the kinds `export/storage fact` and
`format/export` are never considered: anything that can change a number or a file stays on strict checks.
The tool never drops a row.

A row closes only when all three checks hold:

1. **The frozen literal is compiled into the program.** For a C# literal, the frozen source is read around
   it. A run of constant literals joined by `+` compiles to one string, so the run's value must be one
   `ldstr` of a method. An interpolated literal must have its constant pieces, in order, in one method.
   For a XAML attribute, a compiled XAML resource (BAML) must hold an element of the same name (or,
   unnamed, of the same type) with that property and value. For a control row, it must hold an element of
   that type and name. A text property (ToolTip, Header, Content, Text) may also have moved into code. Other
   kinds record no literal and stay OPEN.
2. **The holder belongs to the row's feature.** The holder's source file comes from the portable PDB. The
   feature is the relocated path of the frozen file, its code-behind, and every current file a register
   maps rows of the same frozen file to. It also includes the top-level classes those files declare,
   partial files included.
3. **The holder is reachable from the application's entry point.** The call graph follows calls, delegate
   targets and constructors. A type touched by reachable code has its virtual methods and static
   constructor reachable. A reachable reference to a XAML resource loads it, which constructs its class and
   every type its elements name. XAML event handlers are reached through the generated `Connect` method.
   Dead code stays unreachable.

Each closed row records the assembly, method, source file, entry (the nearest XAML handler, screen or
delegate on the path), the number of steps from the entry point, and the frozen literal (shortened, with
its SHA256; the full text is in `text.json`). A row that fails stays OPEN and names the check that failed.
Closed rows share one kind receipt per kind (D13), so the reviewer's sample covers them, and one failure
reopens the kind.

Before any row, the log shows the controls in the test assembly: a literal reached from the control entry
is reachable and its row closes, and a literal held only by dead code is not reachable and its row stays
OPEN, naming check 3.

What it does not claim: behaviour, numbers, layout or a render. Text reached only through data binding
(a property getter read by WPF) is not followed, so such rows stay OPEN.
