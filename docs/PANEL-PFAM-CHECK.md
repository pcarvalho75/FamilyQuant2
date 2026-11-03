# Checking panel domain names (J1c)

`FamilyQuant.Panels.PanelPfamChecker.CheckAsync` snapshots the user panel and
checks its accessions against the InterPro Pfam entry API. It compares each
claimed name with both the current full name and short name, ignoring case and
outer whitespace. A different name or a missing claimed name is reported.

Every original row remains in the report, including repeated accessions with
different claimed names. Each distinct accession is requested once. The checker
never replaces a name or accession in the panel.

Status is Match, NameMismatch, NotFound (including invalid accession syntax),
or CouldNotCheck. Offline/network failures, provider refusals, malformed replies
and replies for another accession/database are CouldNotCheck. Cancellation
propagates. A report is complete only if no row is CouldNotCheck; completion
does not mean every accession/name is valid. The source URL is recorded per row.

The native program command is:

```text
FamilyQuant.Tests.exe --check-panel-pfam <project.fqproj-or-fcf> <new-output-directory>
```

The project must contain an imported panel. The command writes `pfam-check.csv`
(UTF-8 with BOM, fixed English columns) and `pfam-check.json`. It refuses to
overwrite an existing output directory. The input and panel remain unchanged.

Fixture command: `FamilyQuant.Tests.exe --test-panel-pfam`. Fixtures also run in
the ordinary quick suite through the evidence checks. Live results require a
separate run on the user's imported panel; fixture success is not a live check.
