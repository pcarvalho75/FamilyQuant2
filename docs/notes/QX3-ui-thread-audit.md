# QX3 UI thread audit

Synthetic inputs only. Every named proof attaches a handler that reads a WPF
`Grid.Children` collection created on the dispatcher thread. HTTP fixtures run
on `Task.Run`; no network or acquisition file is read.

| Path and notification | Producer and listener | Safe boundary | Worker proof |
|---|---|---|---|
| Build engine and file-save progress | `USearch.Actions.cs:101,104` to `AppOperationHost.cs:170`; shell and overlay listeners at `MainWindow.Operations.cs:14`, `BusyOverlay.xaml.cs:65` | QX2 invokes display PropertyChanged and ProgressReported together on the host dispatcher, outside its lifetime lock. | `UiProgressBuildAudit`, `UiProgressLifecycleAudit` |
| Build completion PropertyChanged and ShellStatusChanged | `USearch.xaml.cs:35,48,59`, `USearch.Presentation.cs:25` to `MainWindow.Status.cs:55` | `USearch.Actions.cs:60` installs the dispatcher context with OnUi. Notifications after both worker awaits resume there. The other long Build actions use the same boundary at lines 179,245,289. | `UiProgressBuildAudit` |
| Fetch evidence progress | `CurationViewModel.Evidence.cs:113` and Core operation progress to host | QX2 covers the progress adapter and operation reports. | `UiProgressEvidenceAudit` |
| Early domain preview, panel selection and preview clearing | `CurationViewModel.Evidence.cs:40,59,80` to curation bindings | `OnPreviewContext` at line 100 posts to the captured UI context before publishing. | `UiProgressEvidenceAudit` observes a nonempty preview and its clearing after worker HTTP. |
| Evidence document and grouping PropertyChanged | `CurationViewModel.Evidence.cs:74` to `ClusterGenerationControl.Grouping.cs:7,8` and Families handler at `ClusterGenerationControl.Families.cs:25` | Screen entry at `ClusterGenerationControl.xaml.cs:684,687` and `RunCurationAsync` at `ClusterGenerationControl.Operations.cs:65` preserve the dispatcher context across awaits. | `UiProgressEvidenceAudit` observes DocumentChanged and all model properties. |
| Family-world task and fetch progress | `ClusterGenerationControl.FamilyWorld.cs:97` and Core fetch to host | QX2 covers task adapter and world operation reports. | `UiProgressFamilyWorldAudit` invokes the real task adapter from a worker, then fetches through worker HTTP. |
| Family-world completion properties and DocumentChanged | `CurationViewModel.FamilyWorld.cs:42,44` to grouping screen | `ClusterGenerationControl.FamilyWorld.cs:54` and `RunCurationAsync` install the dispatcher context. | `UiProgressFamilyWorldAudit` |
| Review grouping discovery progress and completion properties | `SuperClusterPreviewWindow.Operations.cs:61`, `GroupingReviewViewModel.cs:202,204` to window handlers at `SuperClusterPreviewWindow.cs:60` and operations line 39 | QX2 covers taxonomy progress; operations line 34 uses OnUi before awaiting discovery. | `UiProgressReviewAudit`; existing `--test-grouping-discovery-safety` retains thrown-progress and cancellation checks. |
| Quantify missing-run warning and main-window guide | `FullXicInputStage.cs:108`, `ComputeXicViewModel.cs:104`, `MainWindow.Quantify.cs:27` to `MainWindow.Status.cs:70` | QX2 delivers the warning subscriber on the host dispatcher. | `QuantifyWarningDispatcherTests` |
| Quantify detail status, completion properties and Completed event | `ComputeXicViewModel.cs:108,121,147`, `QuantifyControl.xaml.cs:20`, `MainWindow.Quantify.cs:35` | Progress captures the UI context; the compute command awaits on that context. | `QuantifyWarningDispatcherTests` now touches UI for every property, including worker detail status. |
| Quantify family-attribution progress and completion | `CurationQuantitationViewModel.Operations.cs:34`, `ClusterGenerationControl.Operations.cs:91,145` | QX2 covers reports; OnUi and the awaited continuation publish the completed attribution. | Existing `FullXicColumnUiTests`, command `--test-curation-quantitation`, observes PENDING in the progress callback and READY after completion. |
| Results file/folder export progress and completion status | `AppOperationFiles.cs:20,35,52`, `AnalyticsViewModel.cs:162` to `AnalyticsControl.xaml.cs:35` | QX2 covers STA worker reports; the UI-awaited export resumes on the captured dispatcher context. Writer detail callbacks only assign local text. | `UiProgressExportAudit` exercises STA publication and actual Results export completion. |
| Report snapshot, figure, Word, workbook, HTML and publication progress | `MainWindow.ReportExport.cs:73,130,147,202` to shell and overlay | Entry at line 30 uses OnUi; all worker progress uses the host fixed in QX2. | `ReportProgressTests` (`--test-report-progress`), `HtmlReportExportTests` (`--test-html-report-export`), plus shared host lifecycle proof. |
| Host end PropertyChanged, including worker disposal | `AppOperationHost.cs:200,201` to shell, overlay and queued-attribution handler | Begin/Cancel verify dispatcher access; worker End queues Notify to the dispatcher. | `UiProgressLifecycleAudit` |

`--test-ui-progress-audit` runs all six `UiProgress*Audit` proofs and is in
DefaultSuite. The Quantify and existing subscriber fixtures are also in
DefaultSuite. No additional production defect was found; no cross-lane edit is
needed. The audit concerns screen entry points, not arbitrary off-thread calls
to UI view models without their OnUi boundary.

Synchronous Invoke preserves report order, propagates subscriber failures to the
operation and lets cancellation take effect before Report returns. BeginInvoke
would deliver later. Production UI code awaits these workers; it must continue
to avoid synchronous waits on a worker that reports progress. The two timing
fixtures now pause their injected worker after forwarding progress, rather than
blocking the UI event. Measured values and the warning text are unchanged.
