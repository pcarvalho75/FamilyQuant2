# Y6 retained workflow facts

The desk's `reviews/rulings-fact-check.tsv` contains 125 `lost` observations.
Y6 restores 123 through current Start tooltips, Build Help, family/file Help,
Quantify Help and refusal/cancel feedback, de novo Help and tooltips, and the
lock/spectrum window explanations. No scientific formula, setting, export rule,
new screen or computation mechanism is added.

`HelpWorkflowFacts` centralizes text only. Existing Help builders and Quantify's
existing details surface publish it. The database chooser's existing All files
filter option is restored. Quantify names missing paths, explains that nothing
was computed, and keeps cancelled chooser input paths unchanged while reporting
the next step. The source-specific score, point-count, isotope, cache, CSV and
trusted-input explanations remain explicit.

Two observations stay OPEN for Paulo:

| Observation | Reason |
|---|---|
| FQ15-BEF6F613076C | The former Recalculate colour-change cue was replaced by the new action/status design; the current load location and unavailable state are explained. |
| FQ15-903BC9DCDC73 | Quantify now runs the full project pipeline, so the old claim that no derived result is computed here no longer applies; result storage and file-name matching are explained. |

No DROP or ledger acceptance is inferred. The complete 125-ID disposition
table is retained at `D:/FamilyQuant-data/out-look/Y6/dispositions.tsv` for the
desk and Paulo.

Eight native screen-group checks exercise the current text surfaces; the new
checks were red at 129/8/0 before the wording restoration. They run within
`--test-ui-wording` and the ordinary suite, including the no-organism guard.
Additional checks exercise the live missing-RAW and missing-SEPro refusals.
These are UI and stored-project checks, not a fresh RAW validation.
