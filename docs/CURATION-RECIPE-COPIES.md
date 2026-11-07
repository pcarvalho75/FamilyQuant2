# Recipe copies in curation format 2

Z15b3 registers `recipe-copies`, order 30, through its own `CurationFormatPart` attribute.
It changes only the format-2 wire representation. Canonical JSON, hashes, the editable
document and every classification or replay rule remain unchanged.

The part compares these current and historical values:

| Current value | Recipe value |
|---|---|
| `PanelProteinAssignments` | `EvidenceRecipe.PanelProteinAssignments` |
| `ProteinSubclassLabels` | `EvidenceRecipe.ProteinSubclassLabels` |
| `EvidenceSource.NearestReviewed` | `EvidenceRecipe.SessionSearch` |
| `FamilyPanel` | `EvidenceRecipe.FamilyPanel` |

Only non-null, byte-identical default-JSON values are shared. The comparison includes every
serialized field and list order; it does not substitute membership fingerprints or selected
measurements. `JsonStreamCopy.SameSerializedBytes` uses a bounded store and compares actual
bytes. Larger values spill to the same delete-on-close TEMP implementation used by other
JSON copies.

`Sections.recipe-copies` is a sorted string array of referenced recipe property names.
Those properties are omitted from the recipe body by `ModifyType`. Unequal copies remain
fully stored. Null and explicit empty values retain their original meaning.

After reading the full body, `CompleteRead` reconstructs each reference through
`JsonStreamCopy.Clone`. The historical recipe gets detached objects: later edits to the
current panel, lists or search cannot rewrite the recipe. Unknown or duplicate reference
names, a missing source/table, or a value both stored and referenced are refused.
Files written without this part keep their original full copies and remain readable.

`--test-recipe-copies [output-directory]` checks all four pairs, measured wire reduction,
exact canonical fingerprints, unchanged originals, deterministic writes, unequal snapshots,
detached history, null/empty distinctions, malformed references, native recipe replay and
the production project route. The frame's `--test-curation-format2` also exercises this part
through the automatically discovered registry.
