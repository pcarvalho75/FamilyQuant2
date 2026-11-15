# Feature-ledger rows to verify again after 2.1.20

Under D116 (lean gates) the 2.1.20 refresh ran only the producers whose receipts went stale: 1,035 of 1,043 stale rows came back on the first pass and 6 more on the second, for 1,950 rows proved. Eight rows stay OPEN. All eight pin the old Export > Families save dialog and completion message in MainWindow.Documents.cs, which FS1 (File > Save families as (.fcf), D134) replaced on purpose: Export > Families and the new File menu entry now share one families-only writer, so the dialog is titled "Save families as (.fcf)" and the message says the curated families, evidence and family list were saved without quantitation, names the file, and says the open results project, its save path and unsaved changes are unchanged. The old message's claim that percentages come back when a PatternLab project is loaded again no longer described the program. The saved path, what the file holds and what it leaves out are all kept; the mapping is FQ2-SAVE-FAMILIES-SEPARATE in the ledger's Added contracts. No function is lost.

| ID | Old source text |
|---|---|
| FQ15-896FD56FF1F3 | "Save the current families as a Family Cluster File" (dialog title) |
| FQ15-29EB32BD76B6 | "The families were saved to\n" |
| FQ15-8D6A1F9ED763 | "\n\n" |
| FQ15-91DF62AD5796 | "The file holds the grouping, your User IDs and your annotations. It does not hold the " |
| FQ15-1DA6DACF8438 | "quantitation: reopening it restores the families, and the percentages come back when " |
| FQ15-63177F1FB3F4 | "you load a PatternLab project again.\n\n" |
| FQ15-4EF7F04BBFA8 | "Next step: nothing. Carry on working, or open this file later to return to exactly " |
| FQ15-5165C52E6F76 | "this grouping." |

The four Families counter rows listed in [LEDGER-REVERIFY-2.1.16.md](LEDGER-REVERIFY-2.1.16.md) stay open as well.
