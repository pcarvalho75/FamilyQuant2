# Pfam values supplied in a family list

The unified workbook proposal publishes `PanelWorkbookPfamHint` source facts: the row number, actual Pfam column, family key and named accessions. The end-of-proposal enrichment publishes those hints once. The import path and hints share the existing Pfam parser; a hint never imports or confirms a choice.

Family-row Pfam values precede inferred stored-name suggestions. They are primary suggestions, marked `from your sheet`, with exact stored coverage including zero. The wizard and shared matching dialog start these explicit source statements ticked when there is no previous confirmation. Other inferred suggestions remain unticked.

Existing confirmed choices take precedence, including an empty selection and an unticked source domain. The user can untick or edit a source value. Back and Next preserve the current draft; changing the source domains invalidates the matching cache. The draft still requires explicit confirmation or skipping before Import.

The counted broad-label question remains active for source defaults as for any other selected label. Confirmed metadata remains separate from automatic protein grouping. Source text and the imported Pfam definitions are retained.

`--test-sheet-pfam-matches` exercises the typed workbook proposal, parser, both matching projections, saved unticks, the native broad question and dark/light screens from a synthetic template workbook. The default suite includes it.
