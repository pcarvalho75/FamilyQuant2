# Screen review harness

Build the solution, then run the test executable in its own process:

```powershell
$env:DOTNET_PROCESSOR_COUNT = '4'
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --screens D:/FamilyQuant-data/out-look/F3 --screen-fcf D:/FamilyQuant-data/ana_source.fcf --screen-project C:/Users/paulo/Desktop/FamilyQuant/BrProt_Talk/all+DeepNovo/scientific_results.fqproj
```

`--screens <dir>` is isolated from the regular suite and all scientific export
commands. Optional `--screen-fcf` and `--screen-project` paths are read only.
Without them, captures use a synthetic curation fixture and empty Results.
The manifest labels those states explicitly. No RAW file is opened.

Add `--screen-incomplete-panel` with synthetic inputs to capture retained protein evidence,
the incomplete-panel status and the Retry panel button. The manifest records this fixture.

The harness loads `App.InitializeComponent()`, selects the .NET 10 Fluent theme
through `ThemeMode`, shows the real windows off screen, drains layout work, then
draws each client area through a `VisualBrush`. Product styles remain installed,
including compatibility views until their owning steps remove them. Native title
bars, dialogs and operating system chrome are outside the image.

Output has `light/1366x820`, `light/1024x768`, `dark/1366x820` and
`dark/1024x768` folders. Dimensions are client viewports at 96 DPI. Each folder
contains Home, Build, Curation controls and browser, De novo and advanced De novo,
Results, the Compute compatibility wrapper, Super Cluster preview, Lock manager, Spectrum table, Identity
scan, Text report and Help. The identity scan shows stored scan rows when the
project has them; otherwise it truthfully shows the empty state.

Add `--screen-system` to capture the current Windows app appearance as well. It
adds `system/1366x820` and `system/1024x768` folders for a total of 84 PNGs. Each
manifest capture records both the selected and effective theme. Capturing a theme
does not save a profile preference.

Add `--screen-shell-status` to hide curation's inline status banner through the
same `UseShellStatusBar` property the shell uses. The manifest records this flag.

Add `--screen-update-complete` to show a synthetic once-only update completion
notice on Home through the normal marker consumer. No application is installed.

Add `--screen-close-project` to open and close a synthetic project before the Home
capture. This checks the post-close screen and dependent sidebar states.

`screens.json` lists all 56 PNGs, input paths and SHA-256 hashes, runtime and OS.
A failed capture or changed input returns a nonzero exit code and does not write
a successful manifest. Run into a fresh step directory for review provenance.
The quick suite checks command isolation, the matrix, missing arguments and an
encoded PNG's exact dimensions. It does not run the real-data capture matrix.

Focused screen producers and their exact arguments are listed in [the command catalog](../docs/INDEX.md). Legacy capture names are stable review identifiers; they are not the main menu order.

## C14 scale matrix

Add `--screen-finish --screen-shell-status` to `--screens <fresh-output>` for the
C14 finishing matrix. It captures 18 client views in both themes at WPF root scales 1,
1.25 and 1.5, at 900x600 and 1366x820 DIPs. A secondary window with a larger
declared minimum uses that minimum instead. The four extra views are the actual
Quantify screen, selected-family member view, and B10's stored Database gap
length/result panel at its top and bottom. This extends C14a's original 16-view,
192-PNG packet to 216 native PNGs.

The harness calls `VisualTreeHelper.SetRootDpi` on each real WPF window and checks
that `GetDpi` on both window and content reports the requested scale. PNG pixel
dimensions and resolution follow that scale. `FinishCaptures` records DIP and
pixel sizes, minimum dimensions, native DPI values, layout checks and native Tab
candidates. All windows stay off screen and refuse activation or keyboard focus.
Software rendering and a dispatcher-driven animation settling period keep repeated
captures stable. The default C14 Results data is a labelled, populated synthetic
fixture; the ordinary harness retains its original empty default.

These are native WPF client renders at 96, 120 and 144 DPI. Windows monitor DPI
settings stay unchanged. Native chrome, actual Tab traversal and live keyboard
focus visibility are separate C14 checks and Paulo's sign-off. A rendered scale
matrix does not close a red small-window/layout check; the manifest retains each
frame's `LayoutAccepted` value and the command returns failure for layout gaps.
