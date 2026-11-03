# File > Export report

The command requires stored Full XIC Results. A missing project or missing Results gives
the next action before opening an output chooser. Report details, input provenance, section
and figure choices, and the report threshold come from the open project.

Preparation uses the shared operation host and waits for family attribution. The production
`ReportProjectExporter` builds sections and pinned Light PNGs on an owned STA thread from a
curation snapshot and the captured Results. The checkbox screen edits a private draft;
closing it or cancelling the file chooser leaves the choices unchanged.

Accepted options mark the project changed. A changed threshold rebuilds the report from
the updated setting. The production Word writer prepares a file beside its destination;
the shell checks cancellation and the current project before one final paired publication.
The Word document and same-base `_supplement.xlsx` workbook are staged together. Cancel or
failure between renames rolls back both artifacts, retaining recovery copies if restoration
itself fails. A replaced project preserves previous reports. A Word report cannot replace
the open project. Export does not save the project.

The report menu is distinct from File > Export > Panel audit (CSV).

The saved mean-share threshold decides Others membership once for the report. Quantitative Word
rows, family observations and report figures reuse that membership. Complete member observations
and names remain in the companion workbook; the person's original panel-reference text stays in
Word. Legacy diagnostic plots retain their original signal values and denominators while showing
only individually reportable groups. Generic member/part labels in the pooled Others figures keep
their stored amounts; the named detail is supplementary. Conflicting assignments keep their own
flagged amounts and reasons, outside both reported families and Others.
Long legacy contribution plots use pages of native bars, without a second display-based Others rule.

Focused checks after a Release build:

```powershell
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-selection
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-command <fresh-output>
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --report-export-screens <fresh-output>
```

The native command fixture writes a real Word document through the menu and production
builder/writer, checks its stored details, sources, selected sections and all published workbook
cells, reopens the choices, and exercises file-chooser cancellation, shared Cancel between the
two publications, old-pair restoration and stale-project refusal. It uses
labelled synthetic stored Results and reads no RAW file. The section/figure checker also
compares selected Word packages and the retained original PNG bytes.
