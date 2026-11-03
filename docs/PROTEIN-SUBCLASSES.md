# Protein subclass labels contract (J3a)

`FamilyQuant.Panels.ProteinSubclassLabel` is the per-target seam. Storage is
`FamilyClusterFile.ProteinSubclassLabels` in curation.json and the corresponding
snapshot `EvidenceRecipe.ProteinSubclassLabels`. Null lists are omitted in old
files. A computed list includes every target, including explicit unknown rows.

| Field | Meaning |
|---|---|
| ProteinIndex, Identifier | Both must match the target at that index |
| FamilyUserId | Current UserID at computation; null if not supplied |
| PanelFamilyKey | User panel key; null when outside the panel |
| SubclassKey | Reported subclass after following ReportAsSubclassKey; null when unknown |
| DefinitionKey | Original matched subclass definition, if any |
| Status | Unknown or Assigned; Assigned requires family and subclass keys |
| Reason | Evidence or an explicit unresolved reason |

Quantitation joins its protein mappings to these rows, retains null labels for
unknown parts and keeps incompatible labels unresolved. Peptide evidence must
still distinguish the parts before crediting subclass signal. A protein's label
alone does not make a shared peptide specific to that subclass.

This contract commit defines data only; J3a supplies the complete computed list.
