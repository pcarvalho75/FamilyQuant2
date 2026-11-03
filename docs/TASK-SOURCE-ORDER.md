# Task source identity after curation

R4c fixes Results failing after loading PatternLab, editing a User ID, pressing Undo and batch annotating a protein. Curation can reorder proteins tied in a family. The previous task fingerprint hashed physical order, so the unchanged proteins appeared to be a different source.

New task fingerprints use `set-v2:` followed by SHA-256 of the complete original source tuples in ordinal order. Each tuple retains identifier, description, sequence and serialized evidence, with length prefixes. Duplicates remain in the multiset. User IDs, annotations and display order do not identify the task. Changed source content, membership, multiplicity or reviewed scope still refuses validation.

On reading an old project or FCF, the original ordered fingerprint must match before it is bound to the current multiset fingerprint. The optional curation-root `LegacyTaskSourceFingerprints` mapping persists that verified binding through later sorting and saving. The stored world's task, content hash and result fingerprint remain unchanged. Both stored-world and recipe tasks are covered. An already stale legacy task whose original hash cannot be verified is not silently approved.

Database gap contains an invalid-task refusal in its suggested-additions state. Its stored composition table and other Results remain available. The message explains that the task must be discovered and confirmed again.

Run the focused native regression with:

```text
FamilyQuant.Tests.exe --test-task-order <fresh-output> [<copied-legacy-project> <copied-plp>]
```

The native check opens the project in the shell, waits for initial attribution, loads PLP through the actual button, sends the actual grid edit event and binding, presses Undo, batch annotates and navigates to Results. It selects the project's stored tag length through the native selector, saves and reopens the reordered legacy task, and compares the exact stored world hash and serialized result table. A changed sequence must leave the refusal inside the mounted Database gap section. Windows remain offscreen without activation; native editors never receive focus. The file chooser presenter supplies only owned input copies. Core identity, scope, legacy-recipe and stale-source checks also run in the quick suite.

No quantitation algorithm, scientific cutoff or frozen export palette changes.
