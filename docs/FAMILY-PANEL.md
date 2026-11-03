# FamilyPanel contract (J1a)

Types live in `FamilyQuant.Panels` in `FamilyQuant.Core/Panel/FamilyPanel.cs`.
`FamilyClusterFile.FamilyPanel` is the editable panel in `.fqproj` curation.json
and `.fcf`. `EvidenceRecipe.FamilyPanel` is the snapshot used by a recipe. Both
are optional and omitted when null. Existing projects acquire no default panel.

- `FamilyPanel.Families` preserves user order. A family's `Key` is user supplied,
  stable, nonempty and case sensitive. `DisplayName` is the primary name;
  `Names` contains additional named text, such as names in other languages.
- `Decision` is Quantify, ReportUnderOthers, NotAFamily or MergeIntoAnotherFamily.
  A merge uses `MergeTargetFamilyKey`. `SourceDecision` preserves the user's
  original label. Importers require an explicit mapping for every distinct
  source value; `DecisionMappings` stores those mappings for replay.
- `Pfam` holds accessions and the user's claimed names. Checking them against
  InterPro reports disagreements and never edits this list silently.
- `Subclasses` holds keys, names, required and absent Pfam accessions, and exact
  UniProt subfamily text matches. Domain rules require all required domains and
  none of the absent domains. `ReportAsSubclassKey` can report a fragment or
  subcategory inside a broader subclass. Ambiguous rules stay unresolved.
- `TextFields` is ordered named text. Importers retain literature abundance,
  notes, sources, occurrence, level labels and any other columns here. Reports
  print the original labels and text. Literature text is never a measured value.
- `Levels` retains named definitions from the user's legend. `Import` records
  the source SHA256, chosen sheets and chosen headers, with no fixed workbook
  vocabulary or translated biological rules.

The first contract commit defines data and storage locations only. Validation,
transactions, domain checking, assignment and subclass computation follow in
their own steps. No family, organism, domain or keyword list is built in.
