# Dependency audit, X11

The roadmap owns acceptance. This note records the removal decisions and the bounded checks.

| Dependency | Decision | Evidence |
|---|---|---|
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
