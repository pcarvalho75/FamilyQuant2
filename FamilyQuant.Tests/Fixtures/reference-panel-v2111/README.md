# Recorded 2.1.11 reference panel and recipe

Recorded 2026-11-07 06:46:32 -0800 with the original fq/v2111-fetch build at db43dd70, without Z15a changes. Core SHA256: 9d3b9fe5e5d38d95c9d28285b4766d049ca7cfe38cd9ef93479a320fdfeac00e. The recorder's copied Core DLL hash was checked against that original build.

Invented target, reviewed reference and annotation only. The source, recipe and applied JSON are unedited serializer output. The new scope test opens the source and replays its recipe, comparing the applied JSON byte for byte. Coverage is absent in all recorded files.

Exact recording command: dotnet run --project D:/FamilyQuant-data/out-fetch/Z15a/legacy-recorder/record.csproj -c Release -- D:/FamilyQuant-wt/fetch-panel/FamilyQuant.Tests/Fixtures/reference-panel-v2111
