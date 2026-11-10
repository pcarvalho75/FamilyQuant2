$ErrorActionPreference = 'Stop'
$verifier = Join-Path $PSScriptRoot 'Test-ReleaseSetup.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('FamilyQuant-VersionGuards-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$commit = 'e' * 40
$utf8 = [Text.UTF8Encoding]::new($false)
$passed = 0
function Check([string]$Name, [bool]$Passed) {
    if (-not $Passed) { throw "FAIL $Name" }
    $script:passed++
    Write-Output "PASS $Name"
}
function Write-Text([string]$Path, [string]$Text) { [IO.File]::WriteAllText($Path, $Text, $utf8) }
function Assets([string]$Version, [string]$Tag) {
    $folder = Join-Path $root ('assets-' + $Tag)
    New-Item -ItemType Directory -Path $folder | Out-Null
    $files = @(('FamilyQuant-' + $Tag + '.zip'), ('FamilyQuant2-' + $Tag + '-Setup.exe'))
    $entries = foreach ($name in $files) {
        $path = Join-Path $folder $name
        # Inert files. ValidateOnly must never execute or extract these fixtures.
        Write-Text $path ('inert asset for version ' + $Version + ': ' + $name)
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Text ($path + '.sha256') ($hash + '  ' + $name + "`r`n")
        [ordered]@{Name=$name; Sha256=$hash; Bytes=(Get-Item -LiteralPath $path).Length}
    }
    Write-Text (Join-Path $folder 'release-build.json') ([ordered]@{Commit=$commit; Version=$Version; SelfContained=$true; PayloadFiles=0; Assets=@($entries)} | ConvertTo-Json -Depth 8)
    return $folder
}
function Invoke-Preflight([string]$Folder, [string]$Output, [string]$ExpectedVersion) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $verifier,
        '-PackageDirectory', $Folder, '-ExpectedCommit', $commit, '-OutputDirectory', $Output, '-ValidateOnly')
    if ($ExpectedVersion) { $arguments += @('-ExpectedVersion', $ExpectedVersion) }
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell @arguments *> ($Output + '.log')
        return $LASTEXITCODE
    } finally { $ErrorActionPreference = $prior }
}
$current = Assets '2.1.14.0' '2.1.14'
$currentOutput = Join-Path $root 'current-default'
$code = Invoke-Preflight $current $currentOutput ''
Check 'Default version accepts current 2.1.14 assets without ExpectedVersion' ($code -eq 0)
$proof = Get-Content -LiteralPath (Join-Path $currentOutput 'asset-preflight.json') -Raw | ConvertFrom-Json
Check 'Default preflight records 2.1.14.0 and no execution' ($proof.Version -ceq '2.1.14.0' -and -not $proof.InstallerExecuted -and -not $proof.AppStarted)
$previous = Assets '2.1.3.0' '2.1.3'
$previousOutput = Join-Path $root 'previous-default'
$code = Invoke-Preflight $previous $previousOutput ''
Check 'Default version refuses previous 2.1.3 assets before execution' ($code -ne 0 -and -not (Test-Path -LiteralPath $previousOutput))
$explicitOutput = Join-Path $root 'previous-explicit'
$code = Invoke-Preflight $previous $explicitOutput '2.1.3.0'
Check 'Explicit historical version remains supported for package verification' ($code -eq 0)
$proof = Get-Content -LiteralPath (Join-Path $explicitOutput 'asset-preflight.json') -Raw | ConvertFrom-Json
Check 'Historical override also records no installer or app execution' ($proof.Version -ceq '2.1.3.0' -and -not $proof.InstallerExecuted -and -not $proof.AppStarted)
[ordered]@{Passed=$passed; Failed=0; Root=$root; InstallerExecuted=$false; AppStarted=$false} | ConvertTo-Json
exit 0
