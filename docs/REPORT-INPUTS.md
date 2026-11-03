# Stored report input facts

`FamilyClusterFile.ReportInputProvenance` is an optional read-only source snapshot beside
`ReportDetails`. An absent value is omitted from old curation JSON. The record contains
the original database/family-file basename, the identification-package basename, the search
parameter-file basename and original-byte SHA-256, and ordered key/value strings.

Opening a plain family file records its basename if none was recorded. Opening a project
preserves the stored facts. Successful Quantify stages input facts from the actual request
and attaches them only after the current-project guard passes. Failed, cancelled or stale
work cannot replace the previous facts.

Quantify accepts an optional search parameter file. Without an explicit choice, exactly one
`.params` beside the identification package is read; no candidate or multiple candidates
leave settings unavailable. Parsing reads `key = value` lines, removes `#` comments, retains
empty values and duplicate keys in order, and does not interpret decimal text or invent an
enzyme name. The original file bytes, including encoding and line endings, determine the hash.
Read-only facts appear in Report details under Input provenance; missing fields say Not recorded.

After a Release build:

```powershell
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-inputs
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --capture-report-inputs <database-file> <package.sepr2> <search.params> <fresh-output>
```

The capture writes a metadata-only project and JSON, verifies reopening and checks the source
parameter hash again. It produces no measured Results. The report writer reads these stored
facts without requiring the original files and names the missing fields for older projects.
