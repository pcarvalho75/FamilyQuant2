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
the shell checks cancellation and the current project before one final publication.
The Word document, same-base `_supplement.xlsx` workbook and `.html` page are staged together.
For `report.docx`, the companion files are `report_supplement.xlsx` and `report.html`. Cancel or
failure between any of the three renames rolls back all artifacts, retaining recovery copies if restoration
itself fails. A replaced project preserves previous reports. A Word report cannot replace
the open project. Export does not save the project.

The HTML page is an offline graphical summary of the selected prepared report, with short
pop-ups and links to its sibling Word/workbook files. Section and figure exclusions pass to
the native payload builder, and a changed Others threshold rebuilds the same prepared report
used by all three outputs. No new analysis runs in the HTML writer. The writer loads the
embedded template and inserts one safely encoded JSON payload; user strings cannot close
the data script or become markup. Fixed property/field order, LF newlines and UTF-8 without
a byte-order mark give identical bytes for the same payload, independent of display culture.

The report menu is distinct from File > Export > Panel audit (CSV).

Methods includes a table of saved panel link choices when present, including each
user-entered note exactly as saved. Empty notes remain empty. A choice whose panel
definition changed is marked as such; the table does not claim that a saved choice
was applied. The table stays in Word and also has its own supplement sheet.

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
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-report-progress <fresh-output>
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-html-report-export <fresh-output>
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-project-command-ready <fresh-output>
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --report-export-screens <fresh-output>
```

The native command fixture writes a real Word document through the menu and production
builder/writer, checks its stored details, sources, selected sections and all published workbook
cells, reopens the choices, and exercises file-chooser cancellation, shared Cancel between the
publications, restoration and stale-project refusal. The HTML checks cancel and fail after
each of the three publication boundaries, and cover escaping, repeatability and selection. It uses
labelled synthetic stored Results and reads no RAW file. The section/figure checker also
compares selected Word packages and the retained original PNG bytes.

The progress fixture checks every counted section, figure and sheet, the live wait screen,
timestamped logs, byte-identical output, cancellation preserving previous files, and
diagnostics from a failing stager. It is also registered once in `DefaultSuite`.
