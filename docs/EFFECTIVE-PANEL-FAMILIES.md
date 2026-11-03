# Effective panel-family study contract

`QuantManifest.EffectivePanelFamilies` is optional and absent when never computed. It contains native studies after candidate source UserIDs are mapped to effective panel keys and duplicate keys removed **before** the shared-peptide policy. A peptide shared by two UserIDs under one effective key can therefore be unique to that panel family. The original UserID studies are preserved for compatibility; they cannot be summed to reconstruct this result.

The bundle records source membership and panel-map provenance. Its `Families` table maps deterministic local `StudyFamilyId` slots to effective keys and original source UserIDs. The nested reusable native study types use these local slots in their existing `FamilyUserId` fields. They never renumber or replace a source project's UserIDs. Unmapped, excluded and conflicting source groups retain separate slots so removing a competitor cannot make a shared peptide unique.

The native producer reuses run-share statistics, protein counts, pooled family quantitation, subclass allocation and position allocation. It stores per-run denominators, means and sample SD, pooled named parts and explicit part availability. Unmeasured named parts have null values and a stored reason. Names and definitions come only from the panel and recorded annotations. Merge aliases remain reference text, with no second family total.

Report consumers must use stored effective-key rows and their recorded basis rather than reporting arithmetic over source UserID summaries. Missing, stale or unavailable parts are not zero. A positive observed component cannot become a family-wide no-peptide claim.

The supplied full-run project's structural check found zero effective keys with multiple UserIDs, so the new multi-UserID path is not a gate for that report. This is a structural observation, not a biological accuracy claim.
