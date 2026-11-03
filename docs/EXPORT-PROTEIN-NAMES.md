# Names in new outputs, J12

`ProteinNameFormatting.ForNewOutput(string?)` is the shared formatter for new report, supplement
and audit protein-name text. It stops at the exact ` OS=` header marker. Already-clean strings stay
exact, including whitespace, quotes and fragment qualifiers; null becomes empty. Stored descriptions,
annotations, name-selection rules and numerical values are unchanged.

The existing four-argument CsvReport.BuildFamilyComposition path retains its signature and historical
bytes. A new formatter overload lets new outputs opt in. ReportCurrentResults passes the shared helper
for its Word tables and companion data. Supplement tables consume those same formatted report cells.
PanelAuditCsv calls the same helper. Newly added direct protein-name columns in report/supplement
builders should call ForNewOutput; authored panel text is kept verbatim.

Revised checks: a missing-helper red build, then Release zero warnings/errors and
`--test-export-protein-names` 25/0/0. The checks cover legacy compatibility under all three policies,
new-output formatting, exact clean names, immutable source, culture-independent output, Word/companion
table cells, the audit, and existing CSV formatting. Existing `--test-report-draft` passed 11/0/0,
including the native saved-project Word command.

The required frozen golden is 4/0/0: analytics_summary.csv, composition.csv, evidence_pack_numbers.csv
and families.csv are byte-identical. Log: `D:/FamilyQuant-data/assay-j12-new-output-golden.log`.
The earlier formatting-only verification packet is retained at
`D:/FamilyQuant-data/out-assay/j12-golden/name-cleanup-verification.json`: it proves the helper removes
33 organism tails while leaving every other byte and 27 clean names unchanged. It is a helper proof;
the frozen baseline was never changed.
