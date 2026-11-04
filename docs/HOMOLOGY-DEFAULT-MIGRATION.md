# Score-tier default and saved projects (J47d)

Paulo's D43 enables the zero-margin de novo score tier for new full-XIC Compute
requests. Per-length calibration and tag rewrites remain OFF. The role-sensitive
policy is created at the Compute boundary by `HomologyDefaults.ForNewCompute`;
the basic `HomologyOptions` constructor and a missing saved `Science` field keep
their explicit legacy/OFF meaning. Identified best-score attribution is unchanged.

Compute records the selected options in optional `FamilyMappingProvenance.Science`
and a matching method description. Enabled science settings already distinguish
J28 cache keys. Compute writes/reads that separate native entry. A caller can
explicitly request legacy options for a historical replay.

Opening a 2.1.0 project does not apply the new default. The family screen and
report reconstruction derive their search options from recorded provenance;
absence means OFF. A matching old cache stays reusable. A missing cache can be
searched with the equivalent fast engine under those same old semantics. Stored
run shares and native report headline values remain unchanged until Compute.
New projects reopen with their recorded score tier and its corresponding cache.

The exact two Help sentences handed to FQEvidence are:

> Opening a saved project keeps its stored results and report values until you run Compute; opening alone does not apply the new best-score rule.
> When you compute in 2.1.1, de novo family credit uses the best protein-score tier and a separate cache entry, while identified-peptide rules stay the same.

`--test-homology-default-migration` exercises the real project codec, old/new J28
cache entries, identified/de novo family credit, spectrum rejoin, family view and
stored native headline export. It is enrolled once in the ordinary suite. Legacy
options and their old serialized fields retain their original meaning.

The selected-input `--j47d-rejoin-headlines` producer verifies the certified
measured hit payloads and protein sets, rejects incomplete query coverage or
invalid hit spans, then writes/reads new native compact entries for the approved
delivered-input copy. It never searches or performs RAW Compute. OFF must exactly
reproduce that copy's saved shares before the expected score-tier row is accepted.

The ONE authorized full Compute uses an owned copy of
`D:/FamilyQuant-data/out-opus/K14/input/final-input.fqproj`. The source is unchanged.
Its expected rows are in `D:/FamilyQuant-data/out-assay/j47d-01/expected-01/`;
the input copy SHA256 is
`3D9DDBBACCE4E62D0DAC4EC9F66DC34B08566B6E02B74E5D17973B6917906081`.
The actual native result and family-by-family before/after proof will be recorded
in the READY handoff. Scientific defaults are not inferred from an earlier curation.
