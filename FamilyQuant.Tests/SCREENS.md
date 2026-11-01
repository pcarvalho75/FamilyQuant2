# Screen review harness

Build the solution, then run the test executable in its own process:

```powershell
$env:DOTNET_PROCESSOR_COUNT = '4'
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --screens D:/FamilyQuant-data/out-look/F3 --screen-fcf D:/FamilyQuant-data/ana_source.fcf --screen-project C:/Users/paulo/Desktop/FamilyQuant/BrProt_Talk/all+DeepNovo/scientific_results.fqproj
```

`--screens <dir>` is isolated from the regular suite and all scientific export
commands. Optional `--screen-fcf` and `--screen-project` paths are read only.
Without them, captures use a synthetic curation fixture and empty Analytics.
The manifest labels those states explicitly. No RAW file is opened.

The harness loads `App.InitializeComponent()`, selects the .NET 10 Fluent theme
through `ThemeMode`, shows the real windows off screen, drains layout work, then
draws each client area through a `VisualBrush`. Product styles remain installed,
including legacy styles until the theme migration removes them. Native title
bars, dialogs and operating system chrome are outside the image.

Output has `light/1366x820`, `light/1024x768`, `dark/1366x820` and
`dark/1024x768` folders. Dimensions are client viewports at 96 DPI. Each folder
contains Home, Build, Curation controls and browser, De novo and advanced De novo,
Analytics, Compute, Super Cluster preview, Lock manager, Spectrum table, Identity
scan, Text report and Help. The identity scan shows stored scan rows when the
project has them; otherwise it truthfully shows the empty state.

Add `--screen-system` to capture the current Windows app appearance as well. It
adds `system/1366x820` and `system/1024x768` folders for a total of 84 PNGs. Each
manifest capture records both the selected and effective theme. Capturing a theme
does not save a profile preference.

Add `--screen-shell-status` to hide curation's inline status banner through the
same `UseShellStatusBar` property the shell uses. The manifest records this flag.

`screens.json` lists all 56 PNGs, input paths and SHA-256 hashes, runtime and OS.
A failed capture or changed input returns a nonzero exit code and does not write
a successful manifest. Run into a fresh step directory for review provenance.
The quick suite checks command isolation, the matrix, missing arguments and an
encoded PNG's exact dimensions. It does not run the real-data capture matrix.
