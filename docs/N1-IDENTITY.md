# FamilyQuant 2.0 identity checks

The 2.0 solution is `FamilyQuant2.sln`. The app project is
`FamilyQuant/FamilyQuant.App.csproj`, its assembly is `FamilyQuant2`, and its
resource and C# namespace stays `FamilyQuant`. The test project and executable
remain `FamilyQuant.Tests`.

The build, assembly, file and ClickOnce deployment versions start at `2.0.0.0`.
The installed product is `FamilyQuant 2.0`. Installation and foreground automatic
updates use `http://patternlabforproteomics.org/familyquant2/`. The deployment
manifest is `FamilyQuant2.application`, with application identity
`FamilyQuant2.exe`. The 1.x install address and identity remain separate.

USEARCH is extracted into `%LOCALAPPDATA%\FamilyQuant2`. Its alignment scratch
files use `%TEMP%\FamilyQuant2`. The `FAMILYQUANT_USEARCH` override is unchanged.
Project-local scientific caches retain their existing names and formats.

## Local verification

`dotnet build FamilyQuant2.sln -c Release -v q` succeeds. The suite checks the
assembly name, resource namespace, title, version agreement, install and update
addresses, foreground update setting, product name and effective USEARCH path.
The new deployment checks fail against the previous profile and pass after N1.

The .NET Framework MSBuild publish rehearsal from `CLAUDE.md` succeeded with
`PublishDir` and `PublishUrl` set to `D:/FamilyQuant-data/out-assay/n1-publish/`.
This writes local files only. The generated deployment manifest names
`FamilyQuant2.application`, version `2.0.0.0`, product `FamilyQuant 2.0` and the
`familyquant2/FamilyQuant2.application` update provider. Its payload includes the
app, PatternTools, SEPro, MAligner, CSMSL, alglibnet2 and all four Thermo assemblies.
The test executable is absent from the payload.
Both deployment and application manifests are byte-identical across two local
publish runs.

## Installed-profile check for Paulo

The rehearsal establishes the generated identity and update address. It does not
establish successful installation or an actual update. Once Paulo hosts 2.0 at
its own address, he checks the complete N1 acceptance condition:

1. Keep 1.5.0.6 installed and install 2.0 on the same Windows profile.
2. Launch each from its own Start menu entry and open an existing file in each.
3. Publish a later 2.0 revision and confirm that 2.0 updates and still opens.
4. Launch 1.5.0.6 again and confirm that its version, files and settings remain
   unchanged. Confirm the corresponding independence when updating 1.x.

N1 does not upload, install either product, change the frozen reference, or claim
that this installed-profile check has passed.
