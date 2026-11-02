# Shell messages and browse folders

`ShellDialogs.ShowFile(origin, id, dialog)` and `ShowFolder(origin, id, dialog)`
are the native file/folder boundary. They apply the last valid folder before showing,
use the originating window as native owner, and record a folder only after acceptance.
Owned secondary windows resolve the same `MainWindow` preference through `Owner`.
Standalone windows use the application preference file. Disconnected visuals refuse
the native presentation even when a test presenter is installed.

`ShellMessages.Report` records the full message in the shell status/Help path before
an optional modal presentation. Its parameters preserve text, title, buttons, icon and
default answer. A local status callback can preserve a detached view's status too.
Headless questions return No or Cancel; an OK-only report returns OK.
Build information reports keep their existing status-only behavior.
De novo retains its existing view-model refusal messages and raises no MessageBox.

`--test-shell-dialogs` drives these non-curation browse paths with synthetic inputs.
The native presenters decline every inventory browse, recording its initial folder.
Separate accepted file/folder cases check persistence and the input value.
The same handler paths are driven disconnected to check that no native presenter runs.
`--after-busy` also runs the busy-operation group first to check application setup order.

| Browse IDs | Caller |
|---|---|
| `project.open`, `project.save` | Shell Open and Save as |
| `build.database` | Build database chooser |
| `quantify.raw`, `quantify.sepro`, `quantify.denovo` | Quantify's three inputs |
| `denovo.import`, `denovo.sepro`, `denovo.curation` | De novo inspection inputs |
| `denovo.export.FamilyQuant_DeNovo_Summary.csv` | De novo verdict summary |
| `denovo.export.FamilyQuant_DeNovo_ReviewQueue.csv` | De novo review queue |
| `denovo.export.FamilyQuant_DeNovo_OverlapStudy.csv` | De novo overlap study |
| `results.Summary`, `results.Peptides`, `results.Homology` | Results CSV exports |
| `results.Figures`, `results.EvidencePack` | Results folder exports |
| `families.export`, `sepro.reannotate` | Shell FCF and trusted SEPro inputs |
| `identity.figure` | Detached identity scan figure |
| `spectrum.csv`, `spectrum.score-plot` | Detached spectrum comparison exports |
| `partition.csv` | Retained three-way-window compatibility export |

Curation uses the same boundaries for PatternLab loading, visible protein-report CSV,
composition CSV and its messages. Its native screen proof exercises chooser cancellation,
accepted synthetic loading, the actual CSV writes and preservation of an existing file
after a write refusal.
Lock management also refuses headless modal presentation.

`FilePresenter`, `FolderPresenter`, `ShellMessages.Presenter` and the `Reported` event
are test seams. Production keeps the presenters null and uses native Windows dialogs.
The existing Open/Save path overrides and unsaved-decision override remain available.

`--prove-shell-main <frozen-root> <fresh-output> <date>` executes 31 native shell
scenarios and emits the 145-row `SH-main-routes` register. Its additional cases cover
missing-project refusals, optional spectrum CSV success/refusal, a stale Save and
the spectrum menu's caught Build failure. The compatibility completion uses an
explicitly seeded typed result and never shows its window. It does not compute RAW
results or prove secondary-window content. Every shown proof window is positioned
at (-30000, -30000), uses manual placement, and disables activation and taskbar entry.

The stale-save fixture queues a real empty attach on a detached control's own busy
host. The shell host stays idle, so Save can enter and wait for that control's actual
`FullXicReady`. The existing raw Open compatibility seam then replaces the project;
releasing the detached host must make Save refuse without changing the prior file.
No readiness task is replaced by reflection. The spectrum failure fixture first
checks that its malformed synthetic evidence throws in Build, before any secondary
window can be reached, then drives the enabled native menu and checks the refusal.

The two screen runs supported 115 observations and left 30 OPEN. Their five comparison
outputs were byte-identical after fixture-root canonicalization; raw captures remain
beside them. This is fixture determinism, not a scientific-data claim. The producer
also writes `wording-for-paulo.tsv`: unchanged comparisons still need executed evidence,
while changed wording and unsupported inner expressions stay OPEN for Paulo's ruling.

`--prove-curation-actions` takes the frozen source, this worktree's frozen host, a fixed
native-audit JSON, a fresh output and a timestamp. The retained cohort has 449 curation
observations and ten CSV observations. One native screen run covers filters, native sort
events, row numbering, selection availability, batch curation and Undo, User ID editing,
PatternLab loading, pending normalization and Recalculate, both exports, Full XIC basis,
and word/keyword grouping with Undo. The edit test emits the actual grid event and updates
the column's binding on a detached editor; it does not call `BeginEdit` or request focus.
Every action also checks actual HWND placement and nonactivation. Secondary Help,
alignment-result and modal lock windows require their own safe presentation boundary.

The shared export helper executes twenty complete CSV fixtures: two cultures, every
normalization mode plus no declared mode, both Full XIC headings, null/empty fields,
commas, quotes, CR/LF, and fractional values. The frozen and current output must match
exactly and the current helper must repeat byte for byte. The frozen ReportView adapter
selects its explicit User branch; sorting and counters are also compared. ReportView's
wording is outside this cohort. The optional negative control plants different CSV
comparison bytes and must fail. Plain-text and unexecuted observations remain OPEN;
an action does not supply a semantic proof for changed copy.
