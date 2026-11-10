[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedCommit,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Version]$ExpectedVersion = [Version]'2.1.15.0',
    [ValidateRange(1,600)][int]$TimeoutSeconds = 120,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$utf8 = [Text.UTF8Encoding]::new($false)
function Normalize([string]$Path) { return [IO.Path]::GetFullPath($Path).Replace([char]92,[char]47).TrimEnd('/') }
function Digest([string]$Path) { return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }
function Get-VerifiedBundleNativeFiles([string]$BundleRoot, [string]$AppDirectory) {
    if (-not (Test-Path -LiteralPath $BundleRoot -PathType Container)) { throw 'The .NET host did not use the required test-root/.bundle extraction folder.' }
    $bundlePrefix = [IO.Path]::GetFullPath($BundleRoot).TrimEnd([char]92, [char]47) + [IO.Path]::DirectorySeparatorChar
    # This single-file WPF bundle extracts these native libraries, not coreclr.dll.
    # The app payload's own runtime is checked separately after ZIP verification.
    $names = @('D3DCompiler_47_cor3.dll', 'PenImc_cor3.dll', 'PresentationNative_cor3.dll', 'vcruntime140_cor3.dll', 'wpfgfx_cor3.dll')
    foreach ($name in $names) {
        $payload = Join-Path $AppDirectory $name
        if (-not (Test-Path -LiteralPath $payload -PathType Leaf)) { throw "Native WPF payload file missing: $name" }
        $expected = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
        $files = @(Get-ChildItem -LiteralPath $BundleRoot -Recurse -File -Filter $name)
        if (-not $files.Count) { throw "Native WPF bundle extraction missing: $name" }
        foreach ($file in $files) {
            if (-not $file.FullName.StartsWith($bundlePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Native WPF extraction escaped its bundle folder.' }
            $actual = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -cne $expected) { throw "Native WPF bundle bytes differ from payload: $name" }
            [pscustomobject]@{ Name=$name; BundlePath=$file.FullName; PayloadPath=$payload; Sha256=$actual }
        }
    }
}
$package = Normalize $PackageDirectory
$output = Normalize $OutputDirectory
if ($output -eq $package -or $output.StartsWith($package + '/', [StringComparison]::OrdinalIgnoreCase)) { throw 'Proof output must be separate from package assets.' }
if (Test-Path -LiteralPath $output) {
    if (-not (Test-Path -LiteralPath $output -PathType Container) -or @(Get-ChildItem -LiteralPath $output -Force).Count) { throw 'Use a fresh proof output folder.' }
}
$build = Get-Content -LiteralPath (Join-Path $package 'release-build.json') -Raw | ConvertFrom-Json
if ($build.Commit -cne $ExpectedCommit.ToLowerInvariant() -or $build.Version -cne $ExpectedVersion.ToString(4) -or -not $build.SelfContained) { throw 'Package provenance/version/self-contained check failed.' }
$releaseVersion = '{0}.{1}.{2}' -f $ExpectedVersion.Major,$ExpectedVersion.Minor,$ExpectedVersion.Build
if ($ExpectedVersion.Revision -gt 0) { $releaseVersion += '.' + $ExpectedVersion.Revision }
$zipName = 'FamilyQuant-' + $releaseVersion + '.zip'
$setupName = 'FamilyQuant2-' + $releaseVersion + '-Setup.exe'
$paths = @{}
foreach ($name in @($zipName,$setupName)) {
    $entries = @($build.Assets | Where-Object { $_.Name -ceq $name })
    if ($entries.Count -ne 1) { throw "Exactly one $name asset is required." }
    $path = Join-Path $package $name
    $hash = Digest $path
    $sidecar = (Get-Content -LiteralPath ($path + '.sha256') -Raw).Trim()
    if ($hash -cne $entries[0].Sha256 -or $sidecar -cne ($hash + '  ' + $name) -or (Get-Item -LiteralPath $path).Length -ne $entries[0].Bytes) { throw "Asset digest/size/sidecar mismatch: $name" }
    $paths[$name] = $path
}
if ($ValidateOnly) {
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $preflight = [ordered]@{ Commit=$build.Commit; Version=$build.Version; AssetDigestsVerified=$true; InstallerExecuted=$false; AppStarted=$false; Mode='Asset preflight only' }
    [IO.File]::WriteAllText((Join-Path $output 'asset-preflight.json'), ($preflight | ConvertTo-Json) + "`r`n", $utf8)
    $preflight | ConvertTo-Json
    return
}
$tempBase = Normalize (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Temp')
$testRoot = Normalize (Join-Path $tempBase ('FamilyQuant-SetupProof-' + [Guid]::NewGuid().ToString('N')))
$app = Normalize (Join-Path $testRoot 'app')
if (-not $testRoot.StartsWith($tempBase + '/', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $testRoot) -or
    -not $app.StartsWith($testRoot + '/', [StringComparison]::OrdinalIgnoreCase)) { throw 'Test extraction requires a fresh child of the OS temp folder.' }
if ($testRoot.Contains('"') -or $app.Contains('"')) { throw 'Quotes are not supported in the temp path.' }
$ancestor = [IO.DirectoryInfo]::new($tempBase)
while ($ancestor) {
    if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Test temp ancestors must not be directory links.' }
    $ancestor = $ancestor.Parent
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
New-Item -ItemType Directory -Path $testRoot | Out-Null
# Mandatory reviewed test mode. Native runtime bundle extraction is confined here too.
$nativeResult = Join-Path $testRoot 'installer-result.json'
$arguments = '/test-mode /silent /test-root="' + $testRoot + '" /dir="' + $app + '" /result="' + $nativeResult + '"'
$bundleRoot = Join-Path $testRoot '.bundle'
$previousBundleRoot = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
try {
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $bundleRoot
    $process = Start-Process -FilePath $paths[$setupName] -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $output 'setup.stdout.log') -RedirectStandardError (Join-Path $output 'setup.stderr.log')
    try {
        $null = $process.Handle
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill(); $process.WaitForExit()
            throw 'Installer test-mode timed out; the exact owned process was terminated.'
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw ('Installer test-mode exit ' + $process.ExitCode) }
    } finally { $process.Dispose() }
} finally { $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousBundleRoot }
if (-not (Test-Path -LiteralPath $app -PathType Container)) { throw 'Test-mode app folder missing.' }
$result = Get-Content -LiteralPath $nativeResult -Raw | ConvertFrom-Json
if (-not $result.Success -or $result.ExitCode -ne 0 -or (Normalize $result.Directory) -ne $app) { throw 'Native test result does not confirm the requested temp app folder.' }
$fakeState = Get-Content -LiteralPath (Join-Path $testRoot 'setup-test-state.json') -Raw | ConvertFrom-Json
if ($fakeState.Launched -or -not $fakeState.StartMenu -or -not $fakeState.Registration -or (Normalize $fakeState.Registration.Directory) -ne $app) { throw 'Fake integration state is missing or the quiet installer launched the app.' }
$zip = [IO.Compression.ZipFile]::OpenRead($paths[$zipName])
$verified = @()
try {
    foreach ($entry in $zip.Entries) {
        $relative = $entry.FullName.Replace([char]92,[char]47)
        if ($relative.EndsWith('/')) { continue }
        $target = Normalize (Join-Path $app $relative)
        if (-not $target.StartsWith($app + '/', [StringComparison]::OrdinalIgnoreCase)) { throw 'ZIP entry escapes the test app folder.' }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Extracted payload missing: $relative" }
        $sha = [Security.Cryptography.SHA256]::Create(); $stream = $entry.Open()
        try { $expected = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ((Digest $target) -cne $expected) { throw "Extracted payload bytes differ: $relative" }
        $verified += $relative
    }
} finally { $zip.Dispose() }
if ($verified.Count -ne $build.PayloadFiles) { throw 'Verified payload count differs from package manifest.' }
$exe = Join-Path $app 'FamilyQuant2.exe'; $dll = Join-Path $app 'FamilyQuant2.dll'
$exeInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
$assembly = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(4)
$wanted = $ExpectedVersion.ToString(4)
if ($exeInfo.FileVersion -cne $wanted -or -not $exeInfo.ProductVersion.StartsWith($wanted,[StringComparison]::Ordinal) -or $assembly -cne $wanted) { throw "Extracted app metadata does not match $wanted." }
$config = Get-Content -LiteralPath (Join-Path $app 'FamilyQuant2.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $config.runtimeOptions.includedFrameworks -or -not (Test-Path -LiteralPath (Join-Path $app 'coreclr.dll'))) { throw 'Extracted app is not self-contained.' }
$bundleFiles = @(Get-VerifiedBundleNativeFiles $bundleRoot $app)
$extra = @(Get-ChildItem -LiteralPath $app -Recurse -File | ForEach-Object { $_.FullName.Substring($app.Length + 1).Replace([char]92,[char]47) } | Where-Object { $_ -notin $verified })
$proof = [ordered]@{ Commit=$build.Commit; Version=$wanted; InstallerSha256=(Digest $paths[$setupName]); ZipSha256=(Digest $paths[$zipName]);
    TestRoot=$testRoot; AppDirectory=$app; Arguments=$arguments; VerifiedPayloadFiles=$verified.Count; ExtraInstallerFiles=$extra;
    ExeFileVersion=$exeInfo.FileVersion; ExeProductVersion=$exeInfo.ProductVersion; AssemblyVersion=$assembly;
    SelfContained=$true; AppStarted=$false; NativeResult=$nativeResult; BundleExtractionRoot=$bundleRoot;
    BundleRuntimeFiles=@($bundleFiles.BundlePath); BundleNativeFiles=$bundleFiles; FakeStartMenu=$fakeState.StartMenu;
    FakeRegistration=$fakeState.Registration; Mode='Reviewed installer test-mode; fake machine integration; OS-temp payload verification' }
[IO.File]::WriteAllText((Join-Path $output 'setup-test-proof.json'), ($proof | ConvertTo-Json -Depth 8) + "`r`n", $utf8)
$proof | ConvertTo-Json -Depth 8
