# FamilyQuant identity

Paulo selected the three-bar mark on 2026-11-01. The reference images are kept
unchanged in `D:/FamilyQuant-data/logos/chosen`. This redraw keeps the rounded
teal tile, two pale bars and cyan centre, with flat surfaces and transparent space
outside the tile. It contains no cream paper background.

[Branding.xaml](../../Themes/Branding.xaml) is the geometry source. The three logo brushes live in
[Tokens.xaml](../../Themes/Tokens.xaml). `StartPageLogo` is a vector `ImageSource` with a 256 by 256
canvas, suitable for the start page's 52 by 52 image. `AppWindowIcon` loads the
multi-size ICO resource. App startup applies it to the main window; the shared
window style supplies it to other windows.

Files:

- `FamilyQuant-mark.svg`: transparent master symbol, no font dependency.
- `FamilyQuant-mark-{size}.png`: 16, 20, 24, 32, 40, 48, 64, 128, 256, 512 and 1024 pixels.
- `FamilyQuant.ico`: 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel PNG frames with alpha.
- `FamilyQuant-lockup-dark/light.png`: 1024 by 512 transparent wordmarks.
- `FamilyQuant-lockup-dark/light.svg`: matching editable vector compositions,
  using Segoe UI Variable Display with Segoe UI and sans-serif fallbacks.

The app's `ApplicationIcon` points to the ICO. Standard MSBuild also selects
`ApplicationIcon` for the ClickOnce application manifest's icon file, as shown in
[Microsoft.Common.CurrentVersion.targets](https://github.com/dotnet/msbuild/blob/main/src/Tasks/Microsoft.Common.CurrentVersion.targets).
The installer appearance remains part of the orchestrator's publish rehearsal;
no publish profile is edited by this lane.

Regenerate the derived assets after building:

```powershell
./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --logo-assets ./FamilyQuant/Assets/Brand --preview=D:/FamilyQuant-data/out-look/L1/preview.png
```

The exporter reads the compiled vector and token resources. It does not read a
scientific project or write a profile preference. The PNG wordmark uses the native
Windows font, while the symbol itself is entirely geometric.

Setup uses `FamilyQuant-Setup.ico` and `FamilyQuant-setup-128.png`, with a download
badge composed from the same mark and logo tokens. Its editable master is
`FamilyQuant-setup.svg`. Regenerate only those assets with:

```powershell
./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --setup-logo-assets ./FamilyQuant/Assets/Brand
```
