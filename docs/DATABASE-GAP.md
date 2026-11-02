# Database gap in Results

Results reads `QuantManifest.FamilyWorldResult`. Each family row shows the stored
identified envelope, de novo placed by the database, and de novo placed only by the
family world. Ranking uses the last amount. The view retains the Core row objects;
it performs no peptide attribution, family allocation or rate calculation.

The five global amounts remain separate: unexplained de novo, the three shared
buckets, and identified signal in no family. Unexplained signal never enters a
family bar. The basis is identified observed envelope plus de novo reference-calibrated
envelope, not RAW TIC. Curated groups without a UniProt family retain their supplied
name and group ID. The table and chart scroll without folding or dropping rows.

The minimum tag length, identity gate and in-scope credit rule appear beside the
result. Full stored rules, scope and provenance remain available in the details.
Known-answer right rates show true/credited counts; insufficient evidence replaces
the rate. Help defines a credit as right for a family when that family is among
the known answers, including when the peptide also received another family credit.
The decoy rate uses family-world credits, including shared credits, as
its denominator. Missing values say not recorded or retain the Core evidence text.

The Database gap CSV delegates directly to `BuildCompositionCsv`. Suggested additions
uses `FamilyWorldFetcher.Stored(file)` and exact accession/sequence entries. The
world is checked with Core's production `FamilyWorld.FromSnapshot` factory against
the result fingerprint, release and scope. That factory retains the verified B4
snapshot content hash; reconstructing a protein-list hash is a different identity.
Every supplied hit ID must resolve exactly. The export does not shorten accessions,
parse protein headers, substitute sequences, or publish a partial FASTA. It writes
stable accession-sorted records and a stored-world hash. The person decides which
candidates to add and searches again in PatternLab; there is no database-update action.

Missing and older composition tables are explicit empty states. A table can be read
and exported without re-extracting XIC. An absent, corrupt or mismatched stored world
leaves the CSV available and refuses FASTA with its reason on the result and export
button. The screen never creates an attribution merely because a result is absent.
Production computation/attachment of a new table remains the B5/B6 pipeline's job.

The original five analytics PNG exports and presentation evidence pack are unchanged.
The new chart follows the existing application palette and theme subscription lifetime.
Native tests cover both themes at 1024 pixels, direct Core CSV bytes, repeat/culture
independence, stored-project reopen, exact multi-line FASTA sequences, incomplete
evidence and the actual Results selectors and file-dialog routes.

For a retained project, run the program's checker with a fresh lane-owned output:

```text
FamilyQuant.Tests.exe --database-gap-screens <project.fqproj> <fresh-output>
```

It reads the supplied project without changing its bytes, captures both themes,
writes the stored composition CSV, and writes suggested FASTA only if its matching
world is available. Synthetic fixtures do not establish fresh RAW reproduction,
real-sample correctness or installation.

The retained B6 project `out-opus/b6/bothrops-jararaca-family-world.fqproj` was
checked on a byte-verified copy in `D:/FamilyQuant-data/out-results/B7-real`.
The original UI's protein-list fingerprint refused this valid snapshot. The
fixture now uses the production snapshot factory and failed before the UI fix;
the corrected native fixture passes 27 checks, including the new Help definition.
The real check passes two checks
per run, including an independent parser comparing FASTA membership and complete
sequences with the verified stored world.

The two real runs produce identical CSV, FASTA, both-theme PNGs, printed-value
transcripts and Help bytes. The CSV, with 49 family rows and separate global buckets, is byte-identical to Opus's composition
report. FASTA contains 1,049 exact stored proteins. Original and copied project
hashes remain `62259582C4E99C3F077C9FA7D583AE2F0F4A62659E38E5DE084FF2B1265AE94A`.
Both themes were inspected. Printed rules are nine residues, 80.00 percent
identity, stored-scope credits only and a minimum of ten known credits for a
right rate. This is stored-project validation, not fresh RAW extraction.
