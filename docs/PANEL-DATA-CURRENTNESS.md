# Panel data currentness contract (J2a, desk M14)

Call `FamilyQuant.Panels.PanelDerivedDataCurrentness.Check(file)` after a panel
edit. The result has FamilyAssignments and SubclassLabels statuses (NotComputed,
Current, OutOfDate), IsOutOfDate and a short general Message.

`Fingerprint(panel)` covers classification inputs: family keys, decisions,
merge targets, domain accessions, subclass definitions and confirmed links.
`FamilyFingerprint(panel)` excludes subclass definitions and is the stamp for
family assignments. A subclass-only edit invalidates subclass labels only.
Names, literature, notes and other printed text do not change membership and do
not invalidate scientific assignments. List order does not change domain rules.

The computed fingerprints are stored separately:

- `FamilyClusterFile.PanelProteinAssignmentsPanelFingerprint`
- `FamilyClusterFile.ProteinSubclassLabelsPanelFingerprint`
- The corresponding snapshots on EvidenceRecipe.

An existing list with a missing/mismatched panel fingerprint is OutOfDate.
A missing list is NotComputed. No list is silently refreshed or guessed.
The per-target evidence helper additionally validates source/provenance and
group membership; this one check specifically answers whether panel rules changed.

`ProteinSubclassLabelsFingerprint` is reserved for the subclass source/provenance
check supplied by J3a. All fingerprint slots are optional and null-omitted.
GroupingSnapshot.CaptureEvidence captures both lists and fingerprints in J2a,
so undo of a panel edit restores the exact earlier currentness state.
