# Checking panel domain names (J1c)

`FamilyQuant.Panels.PanelPfamChecker.CheckAsync` snapshots the user panel and
checks its accessions against the InterPro Pfam entry API. It compares each
claimed name with both the current full name and short name, ignoring case and
outer whitespace. A different name or a missing claimed name is reported.

Every original row remains in the report, including repeated accessions with
different claimed names. Each distinct accession is requested once. The checker
never replaces a name or accession in the panel. Requests are serial and spaced
one second apart, with a 60-second response timeout. HTTP 429 retries at most twice, respecting Retry-After or using
5/10 second backoff. A request for a wait longer than a minute stays unavailable
so the user can retry later; it is never retried early.

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

The native Ana run imported 41 rows and preserved all 615 source cells through
save/replay (5/0/0). Workbook SHA256 is
`E0D12300FBDDE0E69A31482904F9679AB39D410C7ABA1E75457FD90D076CD3B4`.
The completed live check at `D:/FamilyQuant-data/out-evidence/J1c-20261102-212419/interpro-final/`
reports all 49 domain rows: 48 matches, one name mismatch, no unavailable rows.
The mismatch is row key `3FTx`, accession `PF00087`: panel name `Toxin_cobra-type`,
provider name `Snake toxin and toxin-like protein`, short name `Toxin_TOLIP`.
No panel name or accession was changed. The earlier rate-limited and one-timeout
runs are retained beside the final report and are not claimed as complete.
