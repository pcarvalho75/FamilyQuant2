# Dependency audit, X11

The roadmap owns acceptance. This note records the removal decisions and the bounded checks.

| Dependency | Decision | Evidence |
|---|---|---|
| DocumentFormat.OpenXml 3.5.1 | Added in Core, J1b0 | One direct package for reading XLSX panels and writing DOCX reports. MIT license. Latest stable verified on [NuGet](https://www.nuget.org/packages/DocumentFormat.OpenXml/3.5.1) on 2026-11-02. The App and Tests receive it through ProjectReference; no Office installation is required. |
| Extended.Wpf.Toolkit 4.6.1 | Removed | All six numeric sites use the shared `NumericUpDown` implementation with typed integer/decimal adapters. Names, bounds, increments, defaults and bindings remain. |
| Newtonsoft.Json 13.0.3 | Removed | Only the extraction and MS1 cache serializers used it. Both now use `System.Text.Json` with the historical field names and cache versions. Accessor omissions apply only to those caches. |
| LiteDB 5.0.21 | Kept | `PatternLabRunQuants.FromXic2` reads the `QuantPackage` and `Quants` collections. It opens the file read-only. Removing it would remove a supported input. |
| OpenMcdf 3.2.0 | Kept | The installed RawFileReader 5.0.0.38 package declares OpenMcdf and OpenMcdf.Extensions 2.2.1.3. The direct pin prevents restoring the older OpenMcdf implementation. Absence of a direct source call does not prove it unused. |

`FamilyQuant.Tests.exe --test-dependencies` covers historical Json.NET cache shapes, exact IEEE
double round trips, stale-source and isotope-policy rejection, corrupt-cache recovery and repeated
byte-identical cache writes. It also checks removal of the assembly references, numeric bounds,
typed and spinner edits, two-way bindings and the numeric surface in both themes. The historical
cache compatibility checks passed before migration. The removal guards failed before each removal.

`--dependency-file-check <real.raw> <real.xic2> <new-report.json>` opens the RAW through the actual
Thermo reader, reads its first scan statistics and runs the production read-only `.xic2` projection.
It never extracts XICs or writes to the inputs. The report records the RAW scan limits and first TIC,
plus all per-run stored quant sums and recorded divisors. It refuses to overwrite a report.

On the Bjararaca run `_01` and `FamilyQuant_XIC.xic2` under the shared Desktop FamilyQuant folder,
the before, after and repeat reports were byte-identical:
`9C1F89B71BEBD6776C9635653FCDEDD0AE94B41C6A6A3B5793C20057C80A0389` (SHA256).
The RAW exposed scans 1 through 284107 and first-scan TIC 16588293; the `.xic2` projection returned
three named runs. Reports and logs are in `D:/FamilyQuant-data/out-assay/x11-*`.

This is an input-opening check, not a full extraction, a fresh scientific reproduction or an
installed-profile check. The fixture suite, stored-project golden comparison and local ClickOnce
publish rehearsal are separate gates. No version or publish profile changed, and no file was uploaded.

X11 validation: Release build had zero warnings/errors; focused tests were 23 passed, 0 failed,
0 skipped; the quick suite was 2361 passed, 0 failed, 17 skipped; frozen stored-project golden was
4 passed, 0 failed, 0 skipped. The .NET Framework MSBuild rehearsal produced `setup.exe` and
`FamilyQuant2.application` in `out-assay/x11-publish`. Its payload retains LiteDB, OpenMcdf and all
four Thermo assemblies, with no Newtonsoft, Xceed or test assembly.

## Open XML payload, J1b0

Core pins `DocumentFormat.OpenXml` 3.5.1. The package brings its matching Framework package
transitively. `--test-dependencies` exercises XLSX Unicode/line-break reading and DOCX writing
through that transitive reference. The first build failed without the package; the green Release
build had zero warnings/errors, the dependency checks passed 36/0/0 and quick passed 3328/0/17.
A pre-existing nullable fixture dereference in `Program.PauloRulingTests.cs` was annotated after its
existing guard so the solution builds without warnings.

The isolated, framework-dependent FileSystem publish at
`D:/FamilyQuant-data/out-assay/j1b0-publish` used `dotnet publish` with `--no-build --no-restore`,
`PublishProtocol=FileSystem`, `GenerateManifests=false`, `GenerateClickOnceManifests=false`,
`BootstrapperEnabled=false`, `SignManifests=false` and that explicit output folder. It uploads
nothing and does not change any publish profile. This is a local payload check, not an install.

| Published file | Bytes | SHA256 |
|---|---:|---|
| DocumentFormat.OpenXml.dll | 7985120 | EE2A31FF79DB570E2EB98EC7513B222F2B8C6BFCB638D66C2CEC8255B0BBE499 |
| DocumentFormat.OpenXml.Framework.dll | 478688 | FF3ED8318388D84FA5BBC5736DF6144ECFA3165C0B95F6B45B5330F463EDF7AE |
