# File > Export report

The command requires stored Full XIC Results. A missing project or missing Results gives
the next action before opening an output chooser. Report details, input provenance, section
and figure choices, and the report threshold come from the open project.
File commands refresh when a shared operation begins or ends, including automatic family
attribution after opening a project. They become available without visiting another menu.

Preparation uses the shared operation host and waits for family attribution. The production
`ReportProjectExporter` builds sections and pinned Light PNGs on an owned STA thread from a
curation snapshot and the captured Results. The checkbox screen edits a private draft;
closing it or cancelling the file chooser leaves the choices unchanged.

The shared wait screen counts sections and figures by name while preparing, sections while
writing Word, and sheets while writing the supplement. The phase bar has a known total;
the elapsed time covers the whole export, and the time since the last step keeps advancing
while an item runs. Cancel remains available until publication finishes. Operation log opens
the timestamped steps recorded so far. Each export also retains a UTF-8 log in
`%LOCALAPPDATA%/FamilyQuant2/operation-logs/report-<id>.log`; `MainWindow.ReportExportLogPath`
exposes its exact path to a native driver. Failures retain the exception type, inner exceptions
and full stack there, while the dialog keeps the short message.

Accepted options mark the project changed. A changed threshold rebuilds the report from
the updated setting. The production Word writer prepares a file beside its destination;
the shell checks cancellation and the current project before one final paired publication.
The Word document and same-base `_supplement.xlsx` workbook are staged together. Cancel or
failure between renames rolls back both artifacts, retaining recovery copies if restoration
itself fails. A replaced project preserves previous reports. A Word report cannot replace
the open project. Export does not save the project.

The report menu is distinct from File > Export > Panel audit (CSV).

Focused checks after a Release build:

```powershell
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-selection
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-command <fresh-output>
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-progress <fresh-output>
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-project-command-ready <fresh-output>
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --report-export-screens <fresh-output>
```

The native command fixture writes a real Word document through the menu and production
builder/writer, checks its stored details, sources, selected sections and all published workbook
cells, reopens the choices, and exercises file-chooser cancellation, shared Cancel between the
two publications, old-pair restoration and stale-project refusal. It uses
labelled synthetic stored Results and reads no RAW file. The section/figure checker also
compares selected Word packages and the retained original PNG bytes.

The progress fixture checks every counted section, figure and sheet, the live wait screen,
timestamped logs, byte-identical output, cancellation preserving both previous files, and
diagnostics from a failing stager. It is also registered once in `DefaultSuite`.
