# Feature-ledger rows to verify again after 2.1.16

Under D116 (lean gates) the 2.1.16 refresh ran only the producers whose receipts went stale: 815 of 820 stale rows came back on the first pass and 7 more on the second. Four rows stay OPEN. All four are the Families counter on Curate families, whose text changed on purpose in SF4 (slide 4 of the user's 11-10 review: "19,948 visible · 40,407 in file" did not say what was hidden). The counter now reads "N shown of M in file" followed by the filters that are active. Both counts and their number format are kept; the mapping is FQ2-FAMILY-COUNTER-FILTERS in the ledger's Added contracts. No function is lost.

| ID | Source |
|---|---|
| FQ15-B0E9E05EC8E3 | LabelNumberOfGroups.Content (ClusterGenerationControl.xaml:107) |
| FQ15-A46A1D04B820 | "N0" (ReportView.cs:28) |
| FQ15-8B7D63ADF6DA | " visible · " (ReportView.cs:28) |
| FQ15-919C85EFE603 | "N0" (ReportView.cs:29) |
