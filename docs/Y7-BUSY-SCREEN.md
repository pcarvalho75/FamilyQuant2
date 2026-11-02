# Shared animated wait screen

The shared `BusyOverlay` now shows the vector mark from
`FamilyQuant/images/FamilyQuant_logo.svg`. Stable family centres anchor the
surrounding nodes as they assemble in a slow wave. The three quantity bars follow
the same wave. One six-second loop uses twelve opacity/render-transform
animations capped at 30 frames per second. There is no timer, polling loop,
worker or layout animation in the product control.

The overlay uses the existing Canvas, Surface, Accent, Text and Muted theme
brushes, plus a 65% `BusyScrimBrush` in both token sets. The central box remains
opaque; the screen behind it stays faintly visible and cannot receive input.
This is Paulo's requested change after approving the initial design. The logo stays vector at native
100%, 125% and 150% DPI. The existing Title, Message, progress and Cancel bindings
remain below the mark; the mark yields height to those controls in a short window.

`BusyLogo` starts only when loaded, visible and busy. It follows Windows'
`SystemParameters.ClientAreaAnimation` setting and listens for changes only while
loaded. A disabled animation preference shows the fully assembled still mark.
Completion, hiding and unloading remove the controllable storyboard and its
property clocks. The Windows preference subscription is detached on unload.
Escape requests cancellation through the window's shared host while work is
active, without closing its review. That keyboard hook is detached on unload.
The instance preference override and storyboard seek helper are internal native
proof seams, unused by the product.

The implementation follows Microsoft's [client animation setting](https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemparameters.clientareaanimation?view=windowsdesktop-10.0),
[controllable storyboard lifetime](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/storyboards-overview)
and [render-transform guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/transforms-overview).

## Long-operation audit

| Desktop work | Shared path |
|---|---|
| Build families, exhaustive clustering, identity scan/search and redundancy | `USearchLib.BeginRun` / `AppOperationHost.For` |
| Full XIC, including the stored family-world analysis | `ComputeXicViewModel.Operations`; the dedicated window retains one explicit overlay and now declares the same window scope |
| Evidence and reviewed-panel fetch | `ClusterGenerationControl.RunCurationAsync` and the operation cancellation token |
| PatternLab load/recalculation, full-XIC family projection, alignments, keyword/evidence grouping preparation | Curation's shared lease |
| De novo load/filter, reconciliation, extraction, homology and partition | `DeNovoViewModel.Operations` |
| Results CSV/FASTA/figures, curation exports and de novo exports | Existing shared leases and staged file/folder writers |
| Spectrum CSV/score figure and identity figure exports | The secondary window's shared host |
| Open, Save, SEPro reannotation, clustering diagnostic, Results and spectrum preparation | Main shell's shared host |
| Grouping-review discovery, proposal and motif work | **Gap fixed:** the secondary window owns the shared overlay, forwards status, and connects Cancel to discovery and late-result rejection |
| Standalone De novo and Quantify controls | **Gap fixed:** loading into a standalone window binds their existing operation host and attaches the shared overlay; shell hosting reuses its inherited host |
| Desktop family-world fetch | No desktop action existed at the audit. FQEvidence adds Y8 using the shared curation host. The existing native headless fetch has no desktop window |

The source audit found no separate product XAML progress bar outside
`BusyOverlay`. Synchronous report views and headless Core APIs are not given
independent animations. New secondary wrappers retain their operation lease until
the worker actually ends. Cancel refuses late proposal/motif results and keeps
the previous review. Closing a busy wrapper requests cancellation through that
same host. Event forwarding and window-host subscriptions are detached at the end
of their lifetime.

## Native proof

`--test-busy-logo <fresh-output>` creates an offscreen native window in each theme,
checks actual WPF DPI at 100/125/150%, exercises Cancel through its native button
automation peer, tests reduced motion and the real OS setting, and checks that no
proof window takes foreground focus. It verifies clock removal and preference
event cleanup, then exercises all newly covered secondary routes with bounded
synthetic workers. The older `--test-busy` regressions retain their real handler,
staging and cancellation checks.

The command captures 48 phases per theme from the actual paused/seeked storyboard,
plus scale and still frames, and writes a local `preview.html`. These are labelled
synthetic operation fixtures, with no scientific results. Product animation is
30fps; the captured preview samples its six-second loop at 8fps.

`performance.json` retains three interleaved still/animated samples of identical
background CPU work and the rendering tier. This is a bounded offscreen smoke
probe, not visible-window profiling or a RAW extraction benchmark. The short
initial samples were noisy, so the final probe uses longer work without changing
the acceptance threshold. Scientific export parity remains the golden gate.

The initial focused proof passed 51 checks; existing busy regressions passed 38,
and the colour guard passed 6. Captures in both themes were inspected at all
three scales. All PNGs and the preview HTML matched byte for byte between
`D:/FamilyQuant-data/out-results/Y7/final1` and `final2`; timing JSON is run data
and is not compared for determinism. The longer final probe recorded median
still/animated worker times of 1.189/1.142 seconds. This shows no observed slowdown
in that bounded probe and is not a claim that animation accelerates computation.

The first quick run found two review-action checks selecting the hidden overlay
Cancel, plus stale source receipts. The review check now selects a visible action
and verifies that Cancel really closes the idle review; the busy proof also
checks Escape against the active operation. The scrim proof uses the real shared
window attachment over a background form and checks hit testing and disablement.
It passed 80 checks, including the original review-interaction checks that first
found the hidden Cancel. The final native cost probe recorded still/animated
medians of 1.195/1.139 seconds on the scrim revision.
The repeat measured 1.079/1.083 seconds. All 106 native PNGs and the preview HTML
matched between the two scrim runs. The final quick suite passed
3,228/0/17 and the single golden comparison passed 4/0/0.

The generated ledger was reconciled using a rerun of this lane's strict ruling
producer. Other lanes' receipt payloads were not refreshed. UI-source changes
leave stale receipts OPEN for the orchestrator's gate to rerun; the reconciled
audit is 3,206 VERIFIED, 2,254 OPEN and 296 APPROVED_DROP.

Open `D:/FamilyQuant-data/out-results/Y7/final3/preview.html` to view the final loop and
the native scale/still captures. Paulo approved the original layout; this preview
includes his requested translucent surround.
