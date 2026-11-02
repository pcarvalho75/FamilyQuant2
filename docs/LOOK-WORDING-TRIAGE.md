# LK wording handoff under roadmap v4

N6m's complete 73-row App/Home cohort and N6n's complete 88-row Help cohort
are accounted for in `D:/FamilyQuant-data/reviews/wording-for-paulo.tsv`.
The lane checked membership against both explicit cohort ID sets: 73/73 and
88/88, with no missing ID. The snapshots are retained under
`D:/FamilyQuant-data/out-look/general-wording/n6m-cohort.json` and
`n6n-cohort.json`. These are shared-machine review artifacts, not product inputs.

Each review row identifies the lane, frozen observation, old text, current text
or replacement surface, and the unresolved reason. Named tooltip replacements
come from their current named declarations. Changed Help literals name the
current literal diff block. A blank new-text field means that no unique current
replacement was identified; it does not assert preservation. Unchanged fragments
are included when needed for complete assigned-cohort accounting, pending the
shared native text checker. No semantic proof or DROP is inferred from this list.

The App/Home execution packet is described in [LOOK-APP-HOME.md](LOOK-APP-HOME.md).
It checks native navigation, state and palette consumers in both themes. Removed
StepCard/StepNumber presentation and numbered-card wording remain explicit review
items. The gate still needs executed preservation evidence or Paulo's ruling
before changing ledger acceptance. N6m/N6n's accounting handoff does not claim a
green ledger release check.
