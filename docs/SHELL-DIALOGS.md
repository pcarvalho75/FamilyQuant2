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
and word/keyword grouping with Undo. The annotation-only batch runs with PatternLab loaded,
compares every family ID and share before/after recalculation, saves and reopens the .fcf,
checks both quoted CSV annotations, and restores the exact pre-edit document through Undo.
The export actions capture chooser filters/default filenames before the presenter supplies
a path. Both CSV filters and filenames equal frozen literals; the accepted PLP chooser
retains the frozen *.plp pattern, with the shorter current description recorded explicitly.
These six additional storage/format observations bring this own cohort to 43 supported and
416 OPEN rows. The edit test emits the actual grid event and updates
the column's binding on a detached editor; it does not call `BeginEdit` or request focus.
Every action also checks actual HWND placement and nonactivation. Secondary Help,
alignment-result and modal lock windows require their own safe presentation boundary.

The shared export helper executes twenty complete CSV fixtures: two cultures, every
normalization mode plus no declared mode, both Full XIC headings, null/empty fields,
commas, quotes, CR/LF, and fractional values. The frozen and current output must match
exactly and the current helper must repeat byte for byte. The frozen ReportView adapter
selects its explicit User branch; sorting and counters are also compared. ReportView's
wording is outside this cohort. The optional negative control plants different CSV
comparison bytes and a different captured chooser filter; both assertions must fail.
The final M22 packet is `D:/FamilyQuant-data/out-shell/final-leftrows`: two 19/0/0 runs,
a 17/2/0 negative control and ten byte-identical data files. The unshown De novo control
also supplies actual default binding values and constructor-replaced tooltips for the
manual review, alongside resolved curation Help. This capture is no semantic proof.
The separate 70-row manual
fact review and 42-row execution report are `reviews/factcheck2-FQShell.tsv` and
`reviews/execution2-FQShell.tsv` under `D:/FamilyQuant-data`. The execution report supplies
specific inability reasons for 36 rows and references the six new native receipts.
Manual wording verdicts supply no behavioral proof or disposition change.
Plain-text and unexecuted observations remain OPEN;
an action does not supply a semantic proof for changed copy.

The review-list producer captures current text from unshown compiled controls,
MainWindow Help/status methods, curation Help, context-menu strings and the text-report
view. Retained native MainWindow report captures are accepted only when both executing
product assemblies match the captured bytes. A value that cannot be captured is written
as exactly `no current equivalent`, with its reason. Source literals and XAML declarations
are not substituted for current user text. These entries request a decision; they never
mark a feature VERIFIED or approve a retirement. The list validator also requires every
ID in the independent review's gap packet, including ReportView, GridCopy and text-report
rows outside the original ten-row CSV cohort.

`--prove-shell-execution3` creates the own `SH-execution3` register for M24's fixed
twenty-row execution JSON. The existing native De novo packet runs both versions'
enabled Import, Reconcile and Homology buttons, all three actual native choosers and
all three chosen-file CSV exports, with cancellation/failure checks and complete
frozen/current data comparisons. Its eighteen handler receipts are retained under
this own register; no wording verdict is used as a handler proof. The same run executes
both versions' contaminant accession stripping/decoy exclusion on five identifiers,
and both versions' actual identity search on a two-target FASTA with no decoys.
The latter must refuse with the same complete message and preserve the input bytes.

All UI runs on the existing never-selected private desktop, with offscreen windows,
nonactivation and native-dialog message automation. The packet under
`D:/FamilyQuant-data/out-shell/execution3` has two 235/0/0 native runs and a 234/1/0
planted label-comparison rejection. Six complete CSV files, native accession/refusal
values and canonical command data are byte-identical between runs. Canonical data
normalizes the owned output directory and excludes only UI status text, measured
ElapsedSeconds and diagnostic UIA control listings; all scientific data values remain.
Original status, timing and dialog traces remain in the full native packet.
No product edit, new retirement, golden run or full-suite claim is made.

`--prove-shell-behaviour` takes the fixed M29 JSON and emits one own `SH-behaviour`
register covering every requested row. Tests compare actual frozen/current parent
lock removal, curation denominator Help, native alignment data and unchanged grouping,
recorded HTTP domain fetch and current atomic cancellation, real review edits/selectors,
readonly report selection/scrolling and Copy command availability, Build busy/reentry
and no-family guards, a real two-point target/decoy curve with its lowest-clean report,
the empty-import reconciliation refusal, missing-run formatting and detached Browse.
Copy availability is checked without writing the user's clipboard. Alignment comparison
excludes the executable/temp-file command preamble; the complete alignment is retained.
Broad harness failures do not overwrite completed independent per-row comparisons.

The authoritative M29 packet is `D:/FamilyQuant-data/out-shell/behaviour/checked2`;
checked1/checked2 have 24 supported and 15 OPEN rows, 14 passed/0 failed/2 skipped.
Normalized complete compared values agree for all 39 rows. Earlier overlapping build/run
outputs under final1/final2 are discarded and supply no receipt. The initial retained-log
footer defect was caught by the importer and fixed before the authoritative runs.
`D:/FamilyQuant-data/reviews/behaviour-FQShell-results.tsv` lists all proofs and specific
reasons for every remaining gap, including the three unexecuted 1001-count cases, missing
Start-page next-step guidance, actual USEARCH phase cancellation and shell Help adapters.
No product edit or DROP is supplied by this batch.

The final `--prove-shell-behaviour2` scope contains sixteen rows. The native harness
invokes `MenuItem.OnClick`, which executes command bindings as well as Click handlers.
It compares Families and De novo Help, workflow Help and the F1 replacement route;
uses the named grouping confirmation button; executes both no-reconciliation writer
guards; writes empty-run native cache artifacts through both real serializers; and
executes actual redundancy removal before checking restored controls, headers and order.
The cache check compares the complete JSON schema and exact numeric double bits, allowing
only equivalent numeric spellings such as 5 and 5.0. No tolerance is introduced.
Frozen redundancy's combined duplicate headers are retained and compared exactly.
The authoritative packet is `D:/FamilyQuant-data/out-shell/behaviour2`: final1/final2
each have ten passed/zero failed/zero skipped, nine supported/seven OPEN rows. All sixteen
fact records and four cache/FASTA data artifacts repeat byte-identically. Every remaining
gap is listed in `D:/FamilyQuant-data/reviews/behaviour2-FQShell-results.tsv`.
Only requested case groups run; unrelated approved execution/input packets are not replayed.
