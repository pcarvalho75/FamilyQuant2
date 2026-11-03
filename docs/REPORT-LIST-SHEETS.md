# List cells in report supplements

Joined lists are displayed as an item count and a stable sheet reference. Their items are retained in long-format sheets with the source sheet/table, original data-row key, owning family, item order and exact item text. CSV exports keep their existing names and bytes. Scalar prose is not split because it contains punctuation; unsupported scalar/item text still receives the existing Excel-limit refusal instead of truncation.

`ReportListColumn` carries exact typed items and optional original row keys. `ReportCsvTable` attaches the schemas below; builders can provide typed items directly when an item itself contains the display separator. Panel subclass definitions use their original collections. J26 row selection projects both items and row keys. Word tables and compact family briefs print counts/references. Long sheets inherit the parent table and section exclusions. J23's original table sheets, names, non-list cells and native rounding sources remain intact.

The complete report-builder inventory is:

| Source column | Items |
|---|---|
| `selected_protein_identifiers`, `unexplained_sequences` | Protein explanation selections and distinct normalized unexplained peptide sequences |
| `panel_keys` | Conflicting panel-family keys |
| `curated_user_ids` | Database-gap source UserIDs, separated by spaces in the original export |
| `source_user_ids` | Effective-panel source UserIDs |
| Effective-panel `unexplained_peptides` | Unexplained sequences; the original UserID export's same-named count column stays numeric |
| `Family evidence used`, `Evidence recorded` | Explicit audit fact clauses, separated by the source's ` | ` delimiter |
| `Required domains`, `Absent domains`, `Source subfamilies` | Original structured panel subclass requirements |
| `Recorded source cutoffs` | Recorded per-source sensitivity gates |
| `Source User IDs` | Source identities in effective-panel briefs |

Basis, definition, reason, variant labels, part keys, user panel notes and supplied text remain scalar. In particular, a JSON-encoded structured part identifier is one key, rather than a list. Other report exports have no joined list columns. Source IDs/data rows are metadata; no signal or classification is recomputed by this projection.

Focused fixture: `FamilyQuant.Tests.exe --test-report-list-sheets [owned-output]`.

Stored native-table confirmation without attribution, RAW access or figure rendering:
`FamilyQuant.Tests.exe --write-report-list-workbook <project-copy.fqproj> <native-csv-folder> <owned-output>`.
It writes methods and every supplied native CSV table, streams every list sheet back against all exact source item/owner rows, and verifies the project and CSV file hashes are unchanged. It is a workbook proof, not a complete figure/Word regeneration or a fresh scientific computation.
