# Panel families by evidence (J2a)

PanelEvidenceClassifier uses the user's stored family rows and decisions. It
learns links from reviewed in-scope proteins' source memberships and Pfam
combinations to the panel's domain lists. Accompanying domains distinguish
shared domains; equally supported candidates require a user choice. Source
and exact architecture contexts are distinct. A single-domain choice cannot
catch an architecture with additional domains. Confirmed links and their source
attribution are stored in the project and recipe as data.

An annotated source family is used first. Otherwise a reviewed in-scope neighbour
must meet the annotated/unannotated identity gates and recorded query-residue
coverage. Unannotated queries additionally require compatible observed domains
without conflicting panel-domain evidence. There is no domain-only rescue.
All equally close reference identities are retained, including references with
the same source family but different architectures. Conflicting panel labels
remain outside. Historical searches without this tie provenance must be rerun.

The per-protein panel audit explains when a protein's own source family maps to a
panel family but its keywords and tissue annotations do not match any selected
scope phrase. It names the phrases and retains the reviewed-neighbour reason. A
saved confirmed link with the current panel definition can support this explanation
even when the current scope contains no reference for that source. Stale choices
are not claimed. This is an audit explanation only: it does not change the proposal,
grouping, saved assignment reasons or recipe replay.

The existing assignment review now has a Panel links tab. No ambiguous choice is
preselected. Applying choices updates the proposal; applying the reviewed
selection stores one locked family change. The subclass runner then runs after
the family host lease ends and folds into the same Undo entry. The original
snapshot restores groups, locks, panel, recipe, assignment facts and labels.

Current/RequireCurrent refuse changed database, evidence/provenance, panel-family
rules, recipe, or membership. A missing target sequence is an explicit refusal.
Family-only fingerprints exclude subclass definitions; subclass edits rerun only
the subclass runner. Current mapping and mapping conflicts are separate states.
See PANEL-ASSIGNMENT-ACCESS.md for the public accessor/resolver signatures.

Native proof commands use explicit inputs and new output directories:

```text
--panel-evidence-propose <copied-evidence-project> <panel-project> <original-database.fcf> <scope> <new-output-directory>
--panel-evidence-apply <source-panel.fqproj> <explicit-link-choices.json> <scope> <new-output-directory>
```

The proposal verifies the original database hash and every identifier/sequence,
runs the current tie-aware search, saves its prepared project, and exports every
target proposal and link with provenance. Apply requires an explicit property
PanelFamilyKey on every supplied choice (null means leave outside); unresolved
links are refused. It writes the assigned project, exact recipe-replay check,
current assignment facts/map, per-family database-protein counts and every
outside target with its reason. Database-protein counts are not toxin diversity.

The completed native packet is
`D:/FamilyQuant-data/out-evidence/J2-20261102-215129/applied-final/`.
It records 2,562 applied database proteins and 29,215 outside targets. Current
family evidence, current subclass labels and exact recipe replay passed (3/0/0).
All 13 ambiguous link choices are input data attributed to Ana's consensus,
tab 4. The imported panel currently has no executable subclass definitions,
so the subclass runner stores explicit Unknown labels without an empty search.
The full analysis supplies those definitions separately through the panel.

`family-assignment-counts.csv` lists every panel row; merged rows alias their
effective target and are not added twice. `outside-panel.csv` lists every
outside protein and reason. The earlier interrupted run is not a completed
acceptance packet.
