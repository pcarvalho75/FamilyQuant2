# Assignment access and mapping contract (J2a, desk M23/M24)

The App command signature is unchanged:
`Task FamilyQuant.ClusterGenerationControl.ReviewEvidenceAsync()`.
Store the panel on that control's current document, close the editor, then await
the command. J2a's body selects panel-aware review automatically for a stored panel.

Core access is `FamilyQuant.Evidence.PanelAssignmentEvidenceStore`:

- `Current(file)` returns every target's current assignment facts, or null for
  missing/stale source, evidence/provenance, panel rules, recipe or grouping.
- `RequireCurrent(file)` returns those facts or throws a short general refusal.
- `GetCurrentFamilyMap(file)` verifies currentness, then returns the mapping.
- `BuildFamilyMap(panel, assignments)` is the pure mapper for already verified
  data or a stand-in; production consumers use GetCurrentFamilyMap.

`PanelFamilyMap.IsCurrent` must be true before quantities are attributed.
`UserFamilies` maps each UserID to its effective PanelFamilyKey and the target
row's Decision. Outside members have no applied panel key. Two keys in one
UserID, or a mixture of a panel key and outside members, flag HasConflict and
ConflictingUserIds; no single key is guessed. Stale data return no usable map,
and may include a diagnostic about conflicting stored labels in current UserIDs.

`PanelRows` includes every original row, including NotAFamily and
MergeIntoAnotherFamily. It preserves the original decision and merge target,
resolves the effective target key and lists that target's UserIDs. A merged row
is an alias of its measured target and must not be counted as a second quantity.
Excluded rows have no measured target or UserIDs. ReportUnderOthers is retained
as a distinct decision on its measured target.

Currentness binds database identity, every target identifier/sequence/evidence,
current membership, source/reference provenance, panel rules, the recipe and
the facts themselves. Display names and printed report text do not decide
membership. The helper checks saved evidence; it never fetches, guesses, applies
or silently refreshes an assignment.
