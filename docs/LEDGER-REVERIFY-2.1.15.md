# Feature-ledger rows to verify again after 2.1.15

Paulo, 2026-11-09 (D106): rows whose proofs went stale because of the intended 2.1.15 changes and that the refresh could not bring back are OPEN, to verify again after release. Here every one is a native Results chart of 1.x that this release redrew on purpose: recovery by mass range replaces the percent of ion current by mass chart (U6), and the isotope count chart is drawn from the shared native data (U3b). Twelve fail the 1.x raster comparison for that reason; eight (the peptide mass chart) pass their own comparison but share the results runtime register, which imports only when every row in it passes; six lost the member they were read from; two tooltip rows are approved drops, which the import does not accept as behavioural proof. 2.1.15 ships with the full suite green. 28 rows.

| ID | Source |
|---|---|
| FQ15-0A23F2CBFDA6 | "share of TIC" |
| FQ15-0A90D9115B97 | "split each bar by peptide mass." |
| FQ15-0BF65773FF2C | "0" |
| FQ15-1001355FDE8B | "credited peptide signal and add up to 100%; green bars, when the run's total ion " |
| FQ15-15B84190A3F4 | "current is known, are its share of all ion current." |
| FQ15-183961BF94B3 | "P0" |
| FQ15-19228B26B200 | "-isotope ceiling. An isotope is credited only where signal was found. The colours " |
| FQ15-25B8151A1126 | "share of TIC" |
| FQ15-2F326D517A18 | "N0" |
| FQ15-2FE77BAEBF4C | "N0" |
| FQ15-372A1D7D876F | "Percent of ion current by mass" |
| FQ15-393E11835299 | "Nothing to show: no peptide carried a positive envelope yet." |
| FQ15-45C4F2A2837C | "are the de novo additions." |
| FQ15-4CE2050A0999 | "Segoe UI" |
| FQ15-4DECEB0D8774 | "One dot per peptide: its extracted isotope envelope (log10 area, up the side) " |
| FQ15-54519B6879D6 | " (ceiling)" |
| FQ15-54F5DE5562BE | "Segoe UI" |
| FQ15-60554875A19A | "against its mass (along the bottom). Blue dots are identified peptides; green dots " |
| FQ15-727F643D4187 | "1e" |
| FQ15-A05988D6E225 | "Segoe UI" |
| FQ15-A405B2C741B9 | "share of credited envelope" |
| FQ15-B25597DA07D1 | "N0" |
| FQ15-D106481D7895 | "over 3000 Da" |
| FQ15-D16E461B873F | "monoisotopic mass (Da), upper edge of each bin" |
| FQ15-D659F7855A37 | "Peptide mass is binned along the bottom. Blue bars are each mass band's share of the " |
| FQ15-EAAD8BBA27D5 | "under 1500 Da" |
| FQ15-FD0073FA4998 | "1500 to 3000 Da" |
| FQ15-FF8E3AB54443 | "N0" |
