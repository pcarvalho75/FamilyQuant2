# Families with no observed peptide

`FamilyUnobservedStudies` distinguishes an unobserved family in a completed native study from an unavailable computation. It uses only stored results, the membership fingerprint and the current panel family map. It does not open acquisitions or run peptide attribution.

The run-share and protein-count builders complete atomically. Their original persisted method contracts enumerate every family reached by an attributed peptide. For those recognized native contracts, an omitted, current, conflict-free quantitative panel family has no assigned peptide. `Read` adds a view of its zero rows; it does not mutate the manifest. Absent studies, unrecognized partial methods and stale or conflicting panel mappings do not establish a zero. Membership checks still refuse stale studies.

At native compute completion, `Store` persists the same zero rows. The new `NoPeptideObserved` share flag is omitted from serialization when false. Empty zero-signal subclass, domain, product and ranking rows retain the same study family universe. Existing observed rows and method text are unchanged.

The report exporter uses `View`, which clones the study containers and appendable lists before completion. The canonical Others partition, exports, figures and panel rows therefore consume the same zeros, while the original manifest and its unrelated stored evidence remain unchanged.

A no-peptide share is zero only for a positive available denominator. A missing or zero denominator remains unavailable; SD needs two available runs, and CV remains undefined for mean zero. Protein and observed-peptide counts are zero. The report says `no peptide observed`; this describes the completed assignment under its recorded search rules, and does not establish biological absence.

The compatibility path deliberately recognizes the exact original native method contracts rather than treating every missing result as zero. A future changed method must define its completeness semantics before adopting this path.

Focused fixture: `FamilyQuant.Tests.exe --test-family-unobserved`. The group is registered once in DefaultSuite. It covers old completed studies, persistence of new zero rows, unavailable studies and denominators, shared peptides with zero policy credit, unchanged existing rows and culture invariance.

For a storage-only check of an owned saved-project copy, run `FamilyQuant.Tests.exe --inspect-family-unobserved <copy.fqproj> <family-userid> <owned-output-folder>`. It writes native observations, panel section cells and share/count CSVs; verifies every pre-existing row and its CSV bytes; and checks that the project file remains byte-identical. It does not save a changed project, render figures, or run attribution.

J29 read-only investigation used an owned copy of the saved full-run project. UserID 40416 has 83 target members in one current, conflict-free quantitative panel family. The two completed native studies contain 1,022 peptide-reached families and no row for this family. No stored spectrum assignment reaches it, and none of the 168,132 stored quant rows is exactly contained in its member sequences with I/L folded. The spectrum table alone is not an independent full attribution replay: 890 distinct side/sequence keys are not represented in that join. The omission follows from the completed native builders' enumeration rule, not a report threshold, panel decision or merge exclusion. Evidence is retained in the worker's J29 output folder; no acquisition or homology replay was performed for this finding.
