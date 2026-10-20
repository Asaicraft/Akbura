#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$PreviousDirectory,
    [Parameter(Mandatory = $true)][string]$CurrentManifest
)

$ErrorActionPreference = 'Stop'
$verifier = Join-Path $PSScriptRoot 'Get-VerificationReuse.ps1'
$PreviousDirectory = [IO.Path]::GetFullPath($PreviousDirectory)
$CurrentManifest = [IO.Path]::GetFullPath($CurrentManifest)
$root = Join-Path $PSScriptRoot ('../../bin/VerificationReuseTests/' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$encoding = [Text.UTF8Encoding]::new($false)

function Save-Manifest($value, [string]$path) {
    $value.Fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($value.Files -join "`n"))))
    [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 6) + "`r`n", $encoding)
}

function Rejected([scriptblock]$action, [string]$message) {
    $failure = ''
    try { $null = & $action }
    catch { $failure = $_.Exception.Message }
    if (-not $failure.Contains($message)) { throw "Expected rejection containing '$message', got '$failure'." }
}

$verified = & $verifier -PreviousDirectory $PreviousDirectory -CurrentManifest $CurrentManifest
if ($verified.Suites.Count -ne 4) { throw 'Expected four previously successful suites.' }

$candidate = Join-Path $root 'previous'
$null = New-Item -ItemType Directory -Path (Join-Path $candidate 'quality')
$previousManifest = Join-Path $PreviousDirectory 'sources.json'
$candidateManifest = Join-Path $candidate 'sources.json'
$source = Get-Content -LiteralPath $previousManifest -Raw | ConvertFrom-Json
$source.Revision = 'invalid-revision'
Save-Manifest $source $candidateManifest
Rejected { & $verifier -PreviousDirectory $candidate -CurrentManifest $CurrentManifest } 'another repository revision'

$source = Get-Content -LiteralPath $previousManifest -Raw | ConvertFrom-Json
$entry = $source.Files | Where-Object { $_.StartsWith('src/Akbura.Generator/') } | Select-Object -First 1
$index = [Array]::IndexOf($source.Files, $entry)
$source.Files[$index] = $entry.Substring(0, $entry.Length - 64) + ('0' * 64)
Save-Manifest $source $candidateManifest
Rejected { & $verifier -PreviousDirectory $candidate -CurrentManifest $CurrentManifest } 'Test reuse does not cover this source change'

$current = Get-Content -LiteralPath $CurrentManifest -Raw | ConvertFrom-Json
$current.Files[0] = $current.Files[0].Substring(0, $current.Files[0].Length - 64) + ('0' * 64)
$tampered = Join-Path $root 'tampered-current.json'
Save-Manifest $current $tampered
Rejected { & $verifier -PreviousDirectory $PreviousDirectory -CurrentManifest $tampered } 'Current source differs from its manifest'

Copy-Item -LiteralPath $previousManifest -Destination $candidateManifest -Force
foreach ($name in @('source-manifest.json','debugstats.success.json','sdk.success.json','release.success.json')) {
    Copy-Item -LiteralPath (Join-Path $PreviousDirectory ('quality/' + $name)) -Destination (Join-Path $candidate 'quality')
}
$full = Get-Content -LiteralPath (Join-Path $PreviousDirectory 'unit-full-release.success.json') -Raw | ConvertFrom-Json
$full.Arguments += @('--filter','FullyQualifiedName~OneTest')
$fullMarker = Join-Path $candidate 'unit-full-release.success.json'
[IO.File]::WriteAllText($fullMarker, ($full | ConvertTo-Json -Depth 6) + "`r`n", $encoding)
Rejected { & $verifier -PreviousDirectory $candidate -CurrentManifest $CurrentManifest } 'not an unfiltered dotnet test run'

$full = Get-Content -LiteralPath (Join-Path $PreviousDirectory 'unit-full-release.success.json') -Raw | ConvertFrom-Json
$failedDirectory = Join-Path $root 'failed-tests'
$null = New-Item -ItemType Directory -Path $failedDirectory
[xml]$trx = Get-Content -LiteralPath (Join-Path $full.Directory 'tests.trx') -Raw
$trx.TestRun.ResultSummary.Counters.failed = '1'
$trx.TestRun.ResultSummary.Counters.passed = '0'
$trx.Save((Join-Path $failedDirectory 'tests.trx'))
$full.Directory = $failedDirectory
[IO.File]::WriteAllText($fullMarker, ($full | ConvertTo-Json -Depth 6) + "`r`n", $encoding)
Rejected { & $verifier -PreviousDirectory $candidate -CurrentManifest $CurrentManifest } 'Cannot reuse unsuccessful test results'

Write-Host "6 verification-reuse checks passed; preserved fixtures: $root"
