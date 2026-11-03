# Report text and export choices

`FamilyQuant.Report.ReportDetails` holds Title, Authors, SampleDescription, Workflow,
DatabaseKind and WrittenInterpretation. The interpretation is plain text and preserves
paragraph breaks. `FamilyClusterFile.ReportDetails` stores it in `curation.json` inside
the project. An absent value is omitted, so old curation text is unchanged.

File > Report details opens a private draft. Save details applies every field as one
curation Undo step and marks the project changed. Cancel discards the draft. File > Save
persists it. The report writer owns printing each field in its corresponding section.

The export checkbox draft uses the report document's stable section and figure ids.
All parts start selected. Unknown exclusions remain available for a later export.

Focused checks after a Release build:

```powershell
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-details
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --report-details-screens <fresh-output>
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-selection
```

The first command checks both cultures, multi-paragraph round trip, old curation bytes,
isolated metadata Undo and the native File command. Captures use fixture text, both themes
and the default and minimum editor sizes; they do not represent measured results.
