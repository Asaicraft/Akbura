#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$PreviousDirectory,
    [Parameter(Mandatory = $true)][string]$CurrentManifest
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$PreviousDirectory = [IO.Path]::GetFullPath($PreviousDirectory)
$previous = Get-Content -LiteralPath (Join-Path $PreviousDirectory 'sources.json') -Raw | ConvertFrom-Json
$current = Get-Content -LiteralPath $CurrentManifest -Raw | ConvertFrom-Json
if ($previous.Revision -ne $current.Revision -or $current.Revision -ne (& git -C $repository rev-parse HEAD)) {
    throw 'Cannot reuse tests from another repository revision.'
}

function Source-Map($manifest) {
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($manifest.Files -join "`n"))))
    if ($digest -cne $manifest.Fingerprint) { throw 'Invalid source manifest fingerprint.' }
    $map = @{}
    foreach ($line in $manifest.Files) {
        if ($line -notmatch '^(?<Path>.+) (?<Digest>[A-F0-9]{64})$') { throw 'Invalid source manifest entry.' }
        if ($map.ContainsKey($Matches.Path)) { throw 'Duplicate source manifest entry.' }
        $map[$Matches.Path] = $Matches.Digest
    }
    return $map
}

$oldMap = Source-Map $previous
$newMap = Source-Map $current
foreach ($path in $newMap.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $repository $path)).Hash -cne $newMap[$path]) {
        throw "Current source differs from its manifest: $path"
    }
}
$allowed = @('Program.cs','Program.Invalidation.cs','Program.ArtifactRetention.cs','Program.Statistics.cs',
    'Verify-LocalizedEdits.ps1','Start-ContractBenchmarks.ps1','Get-VerificationReuse.ps1',
    'Test-StatisticsCompatibility.ps1','Test-VerificationReuse.ps1','BenchmarkCellOverrides.ps1',
    'Test-LocalizedInputs.ps1','Test-ContractRunner.ps1') | ForEach-Object { 'benchmarks/HotReload/' + $_ }
$changed = @(@($oldMap.Keys) + @($newMap.Keys) | Sort-Object -Unique | Where-Object { $oldMap[$_] -cne $newMap[$_] })
foreach ($path in $changed) {
    if ($path -notin $allowed) { throw "Test reuse does not cover this source change: $path" }
}
$dirty = @(& git -C $repository diff --name-only HEAD) + @(& git -C $repository ls-files --others --exclude-standard)
foreach ($path in $dirty | Where-Object { -not $_.StartsWith('benchmarks/HotReload/') }) {
    if (-not $oldMap.ContainsKey($path) -or $oldMap[$path] -cne $newMap[$path]) {
        throw "Unverified change outside the benchmark harness: $path"
    }
}

$qualityManifest = Get-Content -LiteralPath (Join-Path $PreviousDirectory 'quality/source-manifest.json') -Raw | ConvertFrom-Json
if ($qualityManifest.Fingerprint -cne $previous.Fingerprint -or $qualityManifest.VerificationMode -ne 'LocalizedEdits') {
    throw 'Previous quality verification does not match the source manifest or requested suites.'
}
$suites = foreach ($suite in @(
    @{ Name = 'debugstats'; Marker = 'quality/debugstats.success.json'; Configuration = 'DebugStats' },
    @{ Name = 'sdk'; Marker = 'quality/sdk.success.json'; Configuration = '' },
    @{ Name = 'release'; Marker = 'quality/release.success.json'; Configuration = 'Release' },
    @{ Name = 'unit-full-release'; Marker = 'unit-full-release.success.json'; Configuration = 'Release' }
)) {
    $markerPath = Join-Path $PreviousDirectory $suite.Marker
    $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
    if ($marker.Fingerprint -cne $previous.Fingerprint -or -not $marker.CompletedUtc) {
        throw "Previous test marker does not match verified sources: $markerPath"
    }
    if ($suite.Name -eq 'unit-full-release' -and ($marker.Command -ne 'dotnet' -or
        $marker.Arguments[0] -ne 'test' -or $marker.Arguments -contains '--filter')) {
        throw 'The previous full-suite evidence was not an unfiltered dotnet test run.'
    }
    $files = @(Get-ChildItem -LiteralPath $marker.Directory -Filter '*.trx' -File -Recurse)
    if ($files.Count -ne 1) { throw "Expected one test result for $($suite.Name)." }
    [xml]$trx = Get-Content -LiteralPath $files[0].FullName -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.executed -le 0 -or [int]$counts.failed -ne 0 -or [int]$counts.passed -ne [int]$counts.executed) {
        throw "Cannot reuse unsuccessful test results: $($suite.Name)"
    }
    $binaries = @()
    if ($suite.Configuration) {
        $started = [DateTime]::Parse($trx.TestRun.Times.start).ToUniversalTime()
        $binaryRoot = Join-Path $repository "src/Akbura.UnitTests/bin/$($suite.Configuration)/net10.0"
        $binaries = @(foreach ($file in Get-ChildItem -LiteralPath $binaryRoot -Filter '*.dll' -File) {
            if ($file.LastWriteTimeUtc -gt $started) { throw "Test binary changed after verification: $($file.FullName)" }
            @{ Name = $file.Name; Sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash; LastWriteUtc = $file.LastWriteTimeUtc.ToString('o') }
        })
        if (-not ($binaries | Where-Object Name -eq 'Akbura.UnitTests.dll')) { throw 'Verified test assembly is missing.' }
    }
    [pscustomobject]@{ Name = $suite.Name; Directory = $marker.Directory; Trx = $files[0].FullName
        TrxSha256 = (Get-FileHash -LiteralPath $files[0].FullName).Hash; Executed = [int]$counts.executed
        Passed = [int]$counts.passed; CompletedUtc = $marker.CompletedUtc; Binaries = $binaries }
}

[pscustomobject]@{ PreviousDirectory = $PreviousDirectory; PreviousFingerprint = $previous.Fingerprint
    CurrentFingerprint = $current.Fingerprint; ChangedHarnessFiles = $changed; Suites = @($suites)
    Basis = 'Same commit and exact compiler/test source hashes; only explicitly allowed harness changes. Existing binaries predate successful test runs. Binary hashes recorded at reuse validation, not claimed as historical captures.' }
