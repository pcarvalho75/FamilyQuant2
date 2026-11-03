# Spectrum fallback facts after the progress signature change

The frozen nine-argument `SpectrumComparisonTable.Build` no longer has an exact
current containing-member match: J35c added a trailing `OperationContext` and
progress statements. A relocation cannot establish complete member-token equality.
`LedgerMemberFactComparison` remains unchanged and still rejects that declaration.

The assay producer supplies executed evidence for only these three observations:

| Observation | Embedded PatternLab claim property | Exact value |
|---|---|---|
| FQ15-AB0D120CA773 | ScoreField | XCorr |
| FQ15-ABD9B82A59A3 | SourceId | patternlab |
| FQ15-CE9FF8A983EE | SourceDisplayName | PatternLab |

`ProveAssaySpectrumFacts` anchors the three frozen source tokens and executes the
pinned frozen assembly's nine-argument Build and the current ten-argument Build.
Synthetic persisted identified rows exercise the fallback, including positive,
negative and zero scores; empty rows exercise the absence of a fallback claim.
Both cultures and absent/present progress are checked.

An observer copies the compiled Build instructions using the existing injector.
Exactly one BuildClusters call is redirected to a same-signature observer that
reads the actual Claim objects and then calls original clustering unchanged.
Unmodified and observed frozen/current complete CSVs must all agree. The observed
claim tuples must also agree, retain the input scan/score, and contain all three
exact fields. Original IL hashes, inputs, tuples, complete CSVs and assemblies are
retained as artifacts. No loaded assembly or product source is patched.

The three rows receive direct executed proofs, never a declaration-match claim.
All other assay comparisons retain their original strict member/token rules.
Product code, ledger declarations and receipt files are not edited by this change.
Evidence is bounded fixture preservation, not fresh RAW or scientific validation.

The existing `--ledger-assay-compare` accepts a final pinned reference-app folder.
The desk producer map appends its existing `{in:referenceApp}` input for
assay-retained, as K9a did for the shell producer. Without that explicit argument
the historical declaration-only invocation remains available.

Refresh validation uses `python D:/FamilyQuant-wt/desk/refresh_ledger.py --tree
<built-master-copy-with-candidate> --parallel 1 --processors 4 --also AS-retained
--out-root <owned-output>`. Imports are performed by the native refresher in the
isolated copy; no negative-control option is used.

Validation on the isolated master-plus-candidate copy at `5f8cc19` restored all
three observations from OPEN to VERIFIED. The automatic refresh restored 2,121
of 2,222 stale rows; its remaining 101 rows were the two intentionally deferred
RAW producers. Their already executed registers from the supplied K9 run were
then accepted by the unchanged native batch importer, giving 5,460 VERIFIED,
zero OPEN. This carry-forward is not a new RAW run. No receipt was edited by hand.

Evidence is under `D:/FamilyQuant-data/out-shell/K9b`: `target-refresh-proof.json`,
`refresh/20261103-134111-master-copy`, and `carry-forward-raw`. The native assay
producer passed 1,012 checks with zero failures and 153 unrelated declaration
skips; all three scoped facts passed. The unchanged declaration guards passed
67 checks with zero failures. Both isolated and worker builds had zero warnings
and errors. Generated imports remain in the isolated copy, outside the source
commit.
