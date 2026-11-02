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

A row closes only when all three checks hold (N6a-2, after FQAssay's review of 330afa5):

1. **The frozen literal is compiled into the program, live.** For a C# literal, the frozen source is read around
   it. A run of constant literals joined by `+` compiles to one string, so the run's value must be one `ldstr`
   of a method. An interpolated literal must have its constant pieces, in order, in one method. Text alone
   identifies a fact only when it is prose: two or more words of two or more letters. A format string,
   separator, single word or identifier never closes by text, nor a dotted identifier path (a binding or
   member path) or a numeric or date format operand, quoted unit words included (N6a-3). A literal stored into a field or property is
   skipped when the same member is stored again later in the same method, directly or through a method it
   calls afterwards (a dead store).
   A XAML attribute must be held by its own screen's compiled XAML (BAML) on an element of the same name, or,
   unnamed, of the same type with a prose value, with that property and value. A control row needs an element
   of that type and name. There is no fallback to code: a XAML fact keeps its element and property. Other kinds
   record no literal and stay OPEN.
2. **The holder is the row's own member.** The frozen literal's containing member is read from the frozen
   source. The compiled holder must be that member of the same class (a state machine, closure or lambda
   counts as the member it was written in), or a member that valid receipts of other steps name for rows of
   the same frozen member. A constructor qualifies for a field or property initializer only when the literal is
   stored straight into that member. A XAML row's screen is its relocated XAML file, or a XAML file valid
   receipts name for rows of the same frozen XAML file.
3. **The holder is reachable and shown.** The call graph follows calls, delegate targets, XAML `Connect`
   handlers and XAML loads. A virtual method is reachable only on a type that is constructed (a reachable
   constructor, a XAML load, or a value type passed as a generic argument). It must also be a slot the framework
   can call (an override of an outside method, or an explicit interface implementation), or be called by name
   from reachable code. Naming or touching a type reaches only its static constructor. For XAML, neither the
   element nor any element around it may set its Visibility in markup to anything but Visible. No method may
   set a Visibility while reading a field of that name either, reached or not, because the shell also sets
   properties through reflection. Otherwise its being shown is not established.

Each closed row records the assembly, method, source file, entry (the nearest XAML handler, screen or
delegate on the path), the number of steps from the entry point, and the frozen literal (shortened, with
its SHA256; the full text is in `text.json`). A row that fails stays OPEN and names the check that failed.
Closed rows share one kind receipt per kind (D13), so the reviewer's sample covers them, and one failure
reopens the kind.

Before any row, the log shows the controls in the test assembly. A literal reached from the control entry
closes. These stay OPEN: a literal held only by dead code, a virtual method of a class only named by `typeof`,
a caption overwritten later in the same constructor, a sentence held by another member of the right class, a
format token in its own member, a dotted binding path kept only as a caption (review-field-path), and a numeric
format with a quoted unit (review-format-operand). After the rows, a check confirms that the ten rows FQAssay found unsupported in
330afa5 stay OPEN. The rule never reads that list.

What it does not claim: behaviour, numbers, layout or a render. Text reached only through data binding
(a property getter read by WPF) is not followed, so such rows stay OPEN.
