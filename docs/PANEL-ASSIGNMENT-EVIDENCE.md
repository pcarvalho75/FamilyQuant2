# Per-target family assignment evidence contract (J2a)

`FamilyQuant.Panels.PanelProteinAssignment` is one target's family assignment
and evidence. Its Reason concerns family assignment. `ProteinSubclassLabel.Reason`
concerns the subclass and is independent.

Lists live at `FamilyClusterFile.PanelProteinAssignments` and
`EvidenceRecipe.PanelProteinAssignments`, with matching optional
`PanelProteinAssignmentsFingerprint` strings. All four properties are omitted
when null so old projects and recipes keep their existing JSON shape.

| Field | Meaning |
|---|---|
| ProteinIndex, Identifier | Both identify the target at the current file position |
| FamilyUserId | Current UserID when computed |
| PanelFamilyKey | Applied panel key; null outside the panel or when not selected |
| IsApplied | True only for an applied assignment |
| EvidenceKind | Flags: None, UniProtFamily, NearestReviewed, DomainArchitecture, ConfirmedLink |
| SourceFamilies | Source family memberships used in this route |
| NeighbourAccession, IdentityPercent, QueryCoveragePercent | Reviewed alignment considered, including refused candidates when available |
| ObservedPfam, ReferencePfam | Observed domains of the query and reviewed reference |
| LinkKeys | Exact source/architecture links used to connect to the panel |
| Reason | Human-readable evidence or why no panel assignment was applied |

The computed list includes every target, including explicit outside/unselected
rows. Decoys are absent. Do not interpret a neighbour fact as an accepted
assignment without the family key, IsApplied and reason.

This contract defines data only. J2a supplies the fingerprint validation helper
and computed rows. Consumers must verify currentness before presenting rows as
current assignment evidence; missing/stale data are unavailable, not guessed.
