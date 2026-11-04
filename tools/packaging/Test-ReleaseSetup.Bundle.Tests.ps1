$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Test-ReleaseSetup.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
if (@($errors).Count) { throw 'The release verifier must parse without errors.' }
$guard = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-VerifiedBundleNativeFiles'
}, $false)
# Load only the production filesystem guard. The installer runner is never invoked.
if (-not $guard) { throw 'FAIL: native WPF bundle hash guard is absent.' }
. ([ScriptBlock]::Create($guard.Extent.Text))
$root = Join-Path ([IO.Path]::GetTempPath()) ('FamilyQuant-BundleGuards-' + [Guid]::NewGuid().ToString('N'))
$names = @('D3DCompiler_47_cor3.dll', 'PenImc_cor3.dll', 'PresentationNative_cor3.dll', 'vcruntime140_cor3.dll', 'wpfgfx_cor3.dll')
$passed = 0
function Check([string]$Name, [bool]$ExpectedSuccess, [ScriptBlock]$Action) {
    $failure = $null
    try { $null = & $Action } catch { $failure = $_ }
    if (($null -eq $failure) -ne $ExpectedSuccess) { throw "FAIL $Name : $failure" }
    $script:passed++
    Write-Output "PASS $Name"
}
function Fixture([string]$Name) {
    $caseRoot = Join-Path $root $Name
    $app = Join-Path $caseRoot 'app'
    $bundle = Join-Path $caseRoot '.bundle'
    $native = Join-Path $bundle 'Setup/content-hash'
    New-Item -ItemType Directory -Path $app,$native -Force | Out-Null
    foreach ($name in $names) {
        [IO.File]::WriteAllText((Join-Path $app $name), ('native fixture ' + $name))
        [IO.File]::WriteAllText((Join-Path $native $name), ('native fixture ' + $name))
    }
    return @{ App=$app; Bundle=$bundle; Native=$native }
}
$fixture = Fixture 'matching'
Check 'Five matching WPF DLLs pass without bundle/coreclr.dll' $true {
    $files = @(Get-VerifiedBundleNativeFiles $fixture.Bundle $fixture.App)
    if ($files.Count -ne 5 -or (Test-Path -LiteralPath (Join-Path $fixture.Bundle 'coreclr.dll'))) { throw 'Wrong bundle evidence.' }
    foreach ($file in $files) {
        if ($file.Sha256 -cne (Get-FileHash -LiteralPath $file.PayloadPath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Hash proof missing.' }
    }
}
Check 'Missing bundle folder is refused' $false { Get-VerifiedBundleNativeFiles (Join-Path $root 'absent') $fixture.App }
$empty = Join-Path $root 'empty'; New-Item -ItemType Directory -Path $empty | Out-Null
Check 'Empty bundle folder is refused' $false { Get-VerifiedBundleNativeFiles $empty $fixture.App }
[IO.File]::WriteAllText((Join-Path $empty 'coreclr.dll'), 'runtime alone is insufficient')
Check 'Bundle coreclr alone does not replace native WPF evidence' $false { Get-VerifiedBundleNativeFiles $empty $fixture.App }
foreach ($name in $names) {
    $fixture = Fixture ('missing-' + $name)
    # Move a single fixture file aside; retain all temporary evidence.
    Move-Item -LiteralPath (Join-Path $fixture.Native $name) -Destination (Join-Path $fixture.Native ($name + '.absent'))
    Check ('Missing native DLL is refused: ' + $name) $false { Get-VerifiedBundleNativeFiles $fixture.Bundle $fixture.App }
    $fixture = Fixture ('tampered-' + $name)
    [IO.File]::WriteAllText((Join-Path $fixture.Native $name), 'tampered native bytes')
    Check ('Native payload mismatch is refused: ' + $name) $false { Get-VerifiedBundleNativeFiles $fixture.Bundle $fixture.App }
}
$fixture = Fixture 'missing-payload'
Move-Item -LiteralPath (Join-Path $fixture.App $names[0]) -Destination (Join-Path $fixture.App ($names[0] + '.absent'))
Check 'Missing payload counterpart is refused' $false { Get-VerifiedBundleNativeFiles $fixture.Bundle $fixture.App }
$fixture = Fixture 'bad-duplicate'
$duplicate = Join-Path $fixture.Bundle 'other-hash'; New-Item -ItemType Directory -Path $duplicate | Out-Null
[IO.File]::WriteAllText((Join-Path $duplicate $names[0]), 'mismatching second extraction')
Check 'Every extracted copy must match the payload' $false { Get-VerifiedBundleNativeFiles $fixture.Bundle $fixture.App }
$proof = [ordered]@{ Passed=$passed; Failed=0; Root=$root; InstallerExecuted=$false; AppStarted=$false }
$proof | ConvertTo-Json
exit 0
