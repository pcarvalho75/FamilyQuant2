# Build one unsigned Setup asset using ONLY the installed SDK/local NuGet cache. Never runs it.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PublishFolder,
    [Parameter(Mandatory=$true)][string]$OutputFolder,
    [string]$PayloadZip
)
$ErrorActionPreference='Stop'
$env:DOTNET_PROCESSOR_COUNT='4'
$publish=[IO.Path]::GetFullPath($PublishFolder).TrimEnd('\','/')
$output=[IO.Path]::GetFullPath($OutputFolder).TrimEnd('\','/')
if (-not (Test-Path -LiteralPath $publish -PathType Container)) { throw 'PublishFolder must be the self-contained app folder.' }
if ($output.Equals($publish,[StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($publish+'\',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputFolder must be outside PublishFolder.'
}
$app=Join-Path $publish 'FamilyQuant2.exe'
$assembly=Join-Path $publish 'FamilyQuant2.dll'
foreach ($name in @('FamilyQuant2.exe','FamilyQuant2.dll','FamilyQuant2.runtimeconfig.json','coreclr.dll','hostfxr.dll','PresentationFramework.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf)) { throw ('Self-contained payload file missing: '+$name) }
}
$info=[Diagnostics.FileVersionInfo]::GetVersionInfo($app)
$version=[Version]$info.FileVersion
if ($info.ProductName -ne 'FamilyQuant2' -or -not $info.ProductVersion.StartsWith($version.ToString(),[StringComparison]::Ordinal) -or
    [Reflection.AssemblyName]::GetAssemblyName($assembly).Version -ne $version) { throw 'Payload EXE/DLL version metadata differs.' }
$tag=('{0}.{1}.{2}' -f $version.Major,$version.Minor,$version.Build)
if ($version.Revision -gt 0) { $tag+='.'+$version.Revision }
$asset=Join-Path $output ('FamilyQuant2-'+$tag+'-Setup.exe')
if (Test-Path -LiteralPath $asset) { throw ('Refusing to replace an existing Setup asset: '+$asset) }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$work=Join-Path $output ('.setup-build-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$files=New-Object 'System.Collections.Generic.List[object]'
$pending=New-Object 'System.Collections.Generic.Stack[string]'
$pending.Push($publish)
while($pending.Count -gt 0) {
    $folder=$pending.Pop()
    foreach($entry in Get-ChildItem -LiteralPath $folder -Force) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Publish payload contains a link: '+$entry.FullName) }
        if ($entry.PSIsContainer) { $pending.Push($entry.FullName); continue }
        $relative=$entry.FullName.Substring($publish.Length).TrimStart('\','/').Replace('\','/')
        if ($relative -in @('.familyquant-install.json','FamilyQuant2-Uninstall.exe')) { throw 'PublishFolder contains reserved installer files.' }
        $files.Add([ordered]@{path=$relative;length=[long]$entry.Length;sha256=(Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash})
    }
}
$fileArray=@($files.ToArray() | Sort-Object { $_.path })
$zip=if($PayloadZip) { [IO.Path]::GetFullPath($PayloadZip) } else { Join-Path $work 'payload.zip' }
if(-not $PayloadZip) { [IO.Compression.ZipFile]::CreateFromDirectory($publish,$zip,[IO.Compression.CompressionLevel]::Optimal,$false) }
# An existing release ZIP is consumed byte-for-byte. Its paths, sizes and hashes must match the app.
$expected=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
foreach($file in $fileArray) { $expected.Add($file.path,$file) }
$seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach($entry in $archive.Entries) {
        $name=$entry.FullName
        if($name.EndsWith('/')) { continue }
        if($name.StartsWith('/') -or $name.Contains('\') -or $name.Contains(':') -or ($name.Split('/') -contains '..') -or
            -not $seen.Add($name) -or -not $expected.ContainsKey($name)) { throw ('Unexpected ZIP path: '+$name) }
        $file=$expected[$name]
        $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try { $hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $sha.Dispose();$stream.Dispose() }
        if($entry.Length -ne $file.length -or $hash -ne $file.sha256) { throw ('ZIP file differs from publish folder: '+$name) }
    }
} finally { $archive.Dispose() }
if($seen.Count -ne $expected.Count) { throw 'The ZIP is missing published files.' }
$metadata=Join-Path $work 'payload.json'
$manifest=[ordered]@{schema=1;productId='FamilyQuant2';version=$version.ToString();zipSha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash;files=$fileArray}
[IO.File]::WriteAllText($metadata,($manifest | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
$emptyFeed=Join-Path $work 'empty-feed';New-Item -ItemType Directory -Path $emptyFeed | Out-Null
$project=Join-Path $PSScriptRoot 'FamilyQuant.Setup.csproj'
$restoreLog=Join-Path $output 'setup-restore.log'
$publishLog=Join-Path $output 'setup-publish.log'
# Empty local source plus existing cache means a missing pack fails instead of downloading anything.
& dotnet restore $project -r win-x64 --source $emptyFeed -p:NuGetAudit=false -p:EnableSingleFileAnalyzer=false -p:EnableTrimAnalyzer=false *> $restoreLog
if($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $restoreLog -Tail 15; throw 'Offline Setup restore failed; required runtime packs must already be cached.' }
$binary=Join-Path $work 'publish'
& dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $binary `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
    -p:EnableSingleFileAnalyzer=false -p:EnableTrimAnalyzer=false -p:DebugType=None -p:DebugSymbols=false `
    "-p:Version=$version" "-p:SetupPayloadZip=$zip" "-p:SetupPayloadManifest=$metadata" *> $publishLog
if($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $publishLog -Tail 20; throw 'Setup publish failed.' }
$built=Join-Path $binary 'FamilyQuant2-Setup.exe'
if(-not (Test-Path -LiteralPath $built -PathType Leaf)) { throw 'Setup publish did not produce its executable.' }
Copy-Item -LiteralPath $built -Destination $asset
$checksum=(Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($asset+'.sha256'),$checksum+'  '+[IO.Path]::GetFileName($asset)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $output 'setup-package-proof.json'),([ordered]@{asset=$asset;sha256=$checksum;payloadZip=$zip;
    payloadSha256=$manifest.zipSha256;fileVersion=$info.FileVersion;productVersion=$info.ProductVersion;
    assemblyVersion=[Reflection.AssemblyName]::GetAssemblyName($assembly).Version.ToString();fileCount=$fileArray.Count;installedHere=$false} | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
Write-Output ('Setup: '+$asset)
Write-Output ('SHA256: '+$checksum)
Write-Output 'Built only. Setup was not executed and nothing was installed.'
