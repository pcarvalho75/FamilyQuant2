# Panel subclass assignment

Subclass definitions are user data in the family panel. Each rule can specify required and absent
Pfam accessions, or source subfamily text. A source rank suffix can be omitted from the entered
text; matching otherwise uses a complete annotation component, not a substring. Multiple rules
that support different reported subclasses remain unresolved. An absent-domain rule cannot be
established from missing domain evidence, a recorded fragment, or unknown fragment status. A rule may report its
result under another subclass selected in the panel.

`SubclassAssignment.ComputeCurrent` requires current applied family-assignment evidence. It
creates one candidate row per target, including explicit Unknown rows, and does not change the
project. A reviewed-neighbour search uses the existing scope, identity, query-residue coverage,
domain compatibility and learned-marker gates. Equal-identity references are kept distinct by
source family, panel family, reported subclass and source definition; a conflicting tie is not
settled by accession order. A carried rule is refused when a target has a forbidden domain or a
recorded fragment cannot establish an absence. Current but conflicting family UserIDs are flagged,
not attributed to a panel family.

`CommitCandidate` verifies the family assignment source again, refuses a stale or altered
candidate, and stores the labels and fingerprints in the project and its recipe as one undoable
change. `IsCurrent` checks both family-assignment provenance and the subclass inputs and stored
labels. The App entry point is `SubclassAssignmentRunner.RecomputeSubclassesAsync(host)`; it uses
the existing Families busy/Cancel host and Undo stack. Call it after a previous host lease ends.
The default creates one Undo entry. For an automatic recomputation that is part of a preceding
action, pass `combineWithPreviousUndo: true`; the caller's existing Undo must capture panel and
evidence state. The operation refuses combined mode if no preceding Undo exists or it changes
while work is running. If family assignment evidence is not current, it refuses to publish labels.

`MeasureLeaveOneOutCurrent` measures reviewed targets whose source subfamily matches a user rule.
It masks each query's own subclass and excludes the same accession, then applies the reviewed
neighbour gate. The output lists right, wrong and no prediction separately. Prediction rate is
predictions divided by eligible targets; accuracy among predictions uses only right plus wrong.
Neither rate is defined when its denominator is zero. These numbers measure the selected cohort,
not an independently validated biological error rate.

For an existing project whose family assignments and panel rules are current, the native checker is:

```powershell
$testExe = 'FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe'
& $testExe --subclass-leave-one-out <project.fqproj> <new-output-directory>
```

It reads the project without modifying it and writes an invariant per-target CSV and summary JSON
to a new directory. Fixture evidence is run by `--test-family-panel`. A synthetic fixture result is
not a real-data measurement; the real panel definitions and assignment inputs are supplied during
the full project run.
