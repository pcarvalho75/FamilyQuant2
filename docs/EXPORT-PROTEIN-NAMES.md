# Exported composition names, J12

CsvReport.BuildFamilyComposition stops a source protein-name description at the exact ` OS=`
header marker. It leaves already-clean names unchanged, including their whitespace, quotes and
fragment qualifiers. Stored descriptions, user annotations and the detailed description CSV are
unchanged. No family-name selection, grouping or numeric calculation changes.

`--test-export-protein-names` covers all three sharing policies, preservation of clean labels and
stored data, locale-independent export, and the existing CSV formatting checks. Red: 16/3/0;
green: 19/0/0; Release zero warnings/errors.

The existing golden corpus contains organism tails in composition.csv. Consequently J12 gives
3/1/0 there: analytics_summary.csv, evidence_pack_numbers.csv and families.csv are byte-identical;
composition.csv has the requested name cleanup. The native command
`--verify-export-name-cleanup <baseline.csv> <current.csv> <new-report.json>` verifies that every
byte outside precisely those tails is identical, including all numeric/basis/ID bytes and clean
labels. It passed 2/0/0 on the actual golden outputs. Evidence:
`D:/FamilyQuant-data/out-assay/j12-golden/name-cleanup-verification.json`.
The frozen corpus was not updated; the desk owns acceptance of this intended label difference.
