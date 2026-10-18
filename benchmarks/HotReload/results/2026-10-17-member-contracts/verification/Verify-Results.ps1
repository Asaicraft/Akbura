#requires -Version 7.2
param([string]$ResultDirectory = (Join-Path $PSScriptRoot '..'))

$ErrorActionPreference = 'Stop'
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)

function Require([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function Read-Gzip([string]$name) {
    $stream = [IO.File]::OpenRead((Join-Path $ResultDirectory $name))
    $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionMode]::Decompress)
    $reader = [IO.StreamReader]::new($gzip)
    try {
        while (($line = $reader.ReadLine()) -ne $null) {
            if (-not [string]::IsNullOrWhiteSpace($line)) {
                $line | ConvertFrom-Json
            }
        }
    }
    finally {
        $reader.Dispose()
        $gzip.Dispose()
        $stream.Dispose()
    }
}

function Save-Csv($rows, [string]$name) {
    $invariantRows = foreach ($row in $rows) {
        $values = [ordered]@{}
        foreach ($property in $row.PSObject.Properties) {
            $values[$property.Name] = if ($property.Value -is [IFormattable]) {
                $property.Value.ToString($null, [Globalization.CultureInfo]::InvariantCulture)
            }
            else {
                $property.Value
            }
        }
        [pscustomobject]$values
    }
    $text = (@($invariantRows | ConvertTo-Csv -NoTypeInformation) -join "`r`n") + "`r`n"
    [IO.File]::WriteAllText((Join-Path $ResultDirectory $name), $text, [Text.UTF8Encoding]::new($false))
}

$tests = foreach ($suite in @(@{ File = 'debugstats.trx'; Count = 325 }, @{ File = 'sdk-invalidation.trx'; Count = 112 },
    @{ File = 'release.trx'; Count = 82 }, @{ File = 'guard.trx'; Count = 8 })) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $PSScriptRoot $suite.File) -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    Require ([int]$counts.executed -eq $suite.Count -and [int]$counts.passed -eq $suite.Count -and
        [int]$counts.failed -eq 0 -and [int]$counts.notExecuted -eq 0) "Unexpected TRX counts: $($suite.File)"
    if ($suite.File -in @('debugstats.trx', 'sdk-invalidation.trx')) {
        $longTests = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match 'MixedLongSession|OnlyNewestStoreAlive' })
        Require ($longTests.Count -eq 4 -and @($longTests | Where-Object outcome -ne 'Passed').Count -eq 0) 'Missing 100/500-revision lifetime results.'
    }
    [pscustomobject]@{ File = $suite.File; Passed = [int]$counts.passed; Failed = 0; Skipped = 0 }
}

$run = Get-Content -LiteralPath (Join-Path $ResultDirectory 'run.json') -Raw | ConvertFrom-Json
$groups = @{}
$parityGroups = @{}
$baselineErrors = @{}
$parityCount = 0
$provenanceCount = 0
foreach ($row in Read-Gzip 'invalidation-traces.jsonl.gz') {
    if ($row.Kind -eq 'invalidation-provenance') {
        foreach ($name in @('Akbura.BlackSilence.dll', 'Akbura.Generator.dll')) {
            $expected = ($run.ToolHashes | Where-Object Name -eq $name).Sha256
            $actual = if ($name -eq 'Akbura.BlackSilence.dll') { $row.BlackSilenceSha256 } else { $row.GeneratorSha256 }
            Require ($actual -eq $expected) 'Primary measurements mix compiler implementations.'
        }
        Require ($row.Profiling -eq 'stats' -and $row.VerifyEveryUpdate) 'Wrong primary measurement protocol.'
        $provenanceCount++
    }
    if ($row.Kind -in @('invalidation', 'invalidation-parity')) {
        $key = @($row.Project, $row.File, $row.Scenario, $row.Optimization) -join '|'
        $table = if ($row.Kind -eq 'invalidation') { $groups } else { $parityGroups }
        if (-not $table.ContainsKey($key)) {
            $table[$key] = [Collections.Generic.HashSet[int]]::new()
        }
        Require ($row.Index -in 0..10 -and $table[$key].Add([int]$row.Index)) "Repeated or invalid primary index: $key"
        if ($row.Kind -eq 'invalidation-parity') {
            Require ($row.EqualSources -and $row.EqualDiagnostics) 'Fresh-generation parity failed.'
            $parityCount++
        }
        else {
            Require ($row.GeneratorErrors -eq 0) 'A primary generator run reported errors.'
        }
    }
    if ($row.Kind -eq 'invalidation-validation' -and -not $baselineErrors.ContainsKey($row.Project)) {
        $baselineErrors[$row.Project] = @($row.Diagnostics | ForEach-Object { $_ | ConvertFrom-Json } |
            Where-Object Severity -eq 3 | Select-Object Id, Message)
    }
}
Require ($groups.Count -eq 44 -and $parityGroups.Count -eq 44 -and $parityCount -eq 484 -and $provenanceCount -eq 44) 'Incomplete primary matrix.'
foreach ($key in $groups.Keys) {
    Require ($groups[$key].Count -eq 11 -and $parityGroups[$key].Count -eq 11) "Incomplete primary group: $key"
}

$memory = @(Read-Gzip 'artifact-memory-traces.jsonl.gz')
$final = @($memory | Where-Object Kind -eq 'artifact-memory-result')
$samples = @($memory | Where-Object Kind -eq 'artifact-memory-sample')
$concurrent = @($memory | Where-Object Kind -eq 'artifact-concurrent')
Require ($final.Count -eq 4 -and $samples.Count -eq 206 -and $concurrent.Count -eq 32) 'Incomplete main lifetime/concurrency matrix.'
Require (($final.FreshParityChecks | Measure-Object -Sum).Sum -eq 270) 'Missing lifetime/fork fresh comparisons.'
foreach ($result in $final) {
    foreach ($entry in $result.Objects.PSObject.Properties) {
        $expected = if ($entry.Name -eq 'DetachedSnapshot' -and $result.Optimization -eq 'enabled') { 1 } else { 0 }
        Require ($entry.Value.Alive -eq $expected) "Unexpected owner-only live objects: $($result.Project) / $($entry.Name)"
    }
    $forks = @($concurrent | Where-Object { $_.Project -eq $result.Project -and $_.Optimization -eq $result.Optimization })
    Require ($forks.Count -eq 8 -and @($forks.Index | Sort-Object -Unique).Count -eq 8 -and
        @($forks | Where-Object { $_.Index -notin 0..7 -or $_.IncludesFreshOracle }).Count -eq 0) 'Incomplete or oracle-contaminated concurrent timings.'
}

$watchRows = @([IO.File]::ReadLines((Join-Path $ResultDirectory 'member-watch.jsonl')) | ForEach-Object { $_ | ConvertFrom-Json })
$watch = @($watchRows | Where-Object Kind -eq 'watch')
Require ($watch.Count -eq 9 -and @($watch.Index | Sort-Object -Unique).Count -eq 9 -and
    @($watch.ApplicationPid | Sort-Object -Unique).Count -eq 1) 'Live watch did not preserve the original process across nine edits.'
$probes = @($watchRows | Where-Object { $_.Kind -eq 'watch-log' -and $_.Text -match '^PROBE_VALUE (?:[2-9]|10) ' })
Require ($probes.Count -eq 9 -and @($probes | Where-Object { $_.Text -notmatch
    'same_control=True state_preserved=True input_preserved=True member_result=True' }).Count -eq 0) 'Live member/UI/state verification failed.'
$watchSamples = [Collections.Generic.List[object]]::new()
$detection = $null
foreach ($row in $watchRows) {
    if ($row.Kind -eq 'watch-log' -and $row.Ms -gt 0 -and $row.Text -match '(?i)File changed:|File change: Update') {
        if ($null -eq $detection -or $row.Ms -lt $detection) {
            $detection = $row.Ms
        }
    }
    if ($row.Kind -eq 'watch') {
        # The measured recorder recognized "File changed:" only. SDK 10.0.401
        # actually logged "File change: Update". Recover that observed boundary
        # from the untouched native transcript, without inventing EnC intervals.
        $watchSamples.Add([pscustomobject]@{ Index = $row.Index; Warmup = $row.Warmup; ApplicationPid = $row.ApplicationPid
            TotalMs = $row.TotalMs; SourceDetectionLogMs = $detection
            PostDeltaClearCacheCallbackMs = $row.PostDeltaClearCacheCallbackMs
            PostDeltaApplicationCallbackMs = $row.PostDeltaApplicationCallbackMs
            GeneratorProcessingMs = $row.GeneratorProcessingMs; DeltaSubmissionMs = $row.DeltaSubmissionMs })
        $detection = $null
    }
}
Require (@($watchSamples | Where-Object { $null -eq $_.SourceDetectionLogMs }).Count -eq 0) 'Missing raw source-detection observations.'
Save-Csv $watchSamples 'member-watch-samples.csv'
$watchSummary = foreach ($metric in @('TotalMs', 'SourceDetectionLogMs', 'PostDeltaClearCacheCallbackMs', 'PostDeltaApplicationCallbackMs')) {
    $values = @($watchSamples | Where-Object { -not $_.Warmup } | ForEach-Object { [double]$_.$metric } | Sort-Object)
    [pscustomobject]@{ Metric = $metric; Count = $values.Count; MedianMs = ($values[3] + $values[4]) / 2
        MinMs = $values[0]; MaxMs = $values[-1]; P95Ms = $values[-1] }
}
Save-Csv $watchSummary 'member-watch-summary.csv'
$audit = @{ VerifiedUtc = [DateTime]::UtcNow.ToString('o'); Tests = @($tests); PrimaryGroups = 44; PrimaryFreshParityChecks = 484
    MainLifetimeSamples = 206; MainConcurrentPairs = 32; MainLifetimeAndForkFreshParityChecks = 270
    WatchEdits = 9; WatchApplicationPid = $watch[0].ApplicationPid; CompilerBaselineErrors = $baselineErrors
    WatchLogSha256 = (Get-FileHash -LiteralPath (Join-Path $ResultDirectory 'member-watch.jsonl')).Hash }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'results-audit.json'), ($audit | ConvertTo-Json -Depth 8) + "`r`n", [Text.UTF8Encoding]::new($false))
Write-Output 'Verified TRXs, 484 primary parity checks, 270 main lifetime/fork comparisons and nine live watch edits.'
