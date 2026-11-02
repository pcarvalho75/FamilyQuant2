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

Per orchestrator M3, the protected curation callers remain C1-owned:
PatternLab loading, visible protein-report CSV, composition CSV, and its message boxes.
They must adopt the same helper. S8 does not claim their adoption is already complete.
Lock management also refuses headless modal presentation.

`FilePresenter`, `FolderPresenter`, `ShellMessages.Presenter` and the `Reported` event
are test seams. Production keeps the presenters null and uses native Windows dialogs.
The existing Open/Save path overrides and unsaved-decision override remain available.
