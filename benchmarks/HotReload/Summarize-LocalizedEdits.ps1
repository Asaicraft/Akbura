#requires -Version 7.2
param([Parameter(Mandatory = $true)][string]$ResultDirectory)

$ErrorActionPreference = 'Stop'
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
[Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
$encoding = [Text.UTF8Encoding]::new($false)
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Measured localized markup edit results')
$lines.Add('')
$lines.Add('Generated from completed groups, excluding the initial revision and two warm-ups. Each cell contains eight measured edits. Negative change means less elapsed time/allocation; positive means more. Inclusive stages are not additive. These are generator-driver measurements, not complete Hot Reload latency.')
$lines.Add('')
$lines.Add('## Executed verification')
$lines.Add('')
$lines.Add('| Suite | Executed | Passed | Failed |')
$lines.Add('| --- | ---: | ---: | ---: |')
foreach ($suite in @(
    @{ Name = 'Relevant DebugStats'; File = 'verification/debugstats/tests.trx' },
    @{ Name = 'SDK Roslyn host'; File = 'verification/sdk/sdk-invalidation.trx' },
    @{ Name = 'Relevant Release'; File = 'verification/release/tests.trx' },
    @{ Name = 'Complete Release Akbura.UnitTests'; File = 'verification/unit-full-release/tests.trx' }
)) {
    $path = Join-Path $ResultDirectory $suite.File
    if (-not (Test-Path -LiteralPath $path)) { continue }
    [xml]$result = Get-Content -LiteralPath $path -Raw
    $counts = $result.TestRun.ResultSummary.Counters
    if ([int]$counts.failed -ne 0 -or [int]$counts.executed -le 0) { throw "Unsuccessful verification: $($suite.Name)" }
    $lines.Add("| $($suite.Name) | $($counts.executed) | $($counts.passed) | $($counts.failed) |")
}
$lines.Add('')
$lines.Add('Suites overlap; these are not additive unique-test counts. Detailed TRXs and actual SDK identities are archived under verification/.')
$lines.Add('')
foreach ($profile in @(@{ Name = 'Instrumented'; Root = $ResultDirectory }, @{ Name = 'Production Release, no STATS call sites'; Root = (Join-Path $ResultDirectory 'production') })) {
    $data = @(Import-Csv -LiteralPath (Join-Path $profile.Root 'comparison.csv'))
    $parity = Get-Content -LiteralPath (Join-Path $profile.Root 'comparison-parity.json') -Raw | ConvertFrom-Json
    if (-not $parity.Equal) { throw 'Before/after parity has not been established.' }
    $lines.Add('## ' + $profile.Name)
    $lines.Add('')
    $lines.Add("Matching before/after source/diagnostic records: $($parity.Records). All measured revisions also passed fresh-driver parity.")
    $lines.Add('')
    $lines.Add('| Component / edit | Generator ms before | After | Change | Process bytes before | After | Probe compilations before | After |')
    $lines.Add('| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
    foreach ($group in $data | Group-Object Project,File,Scenario | Sort-Object Name) {
        $time = $group.Group | Where-Object Metric -eq 'GeneratorMs'
        $allocation = $group.Group | Where-Object Metric -eq 'ProcessAllocatedBytes'
        $probes = $group.Group | Where-Object Metric -eq 'ProbeCompilationCreated'
        $probeBefore = if ($probes) { $probes.Before } else { 'unavailable' }
        $probeAfter = if ($probes) { $probes.After } else { 'unavailable' }
        $lines.Add(('| {0} / {1} | {2:F2} | {3:F2} | {4:+0.00;-0.00;0.00}% | {5:F0} | {6:F0} | {7} | {8} |' -f
            $time.File, $time.Scenario, [double]$time.Before, [double]$time.After, [double]$time.ChangePercent,
            [double]$allocation.Before, [double]$allocation.After, $probeBefore, $probeAfter))
    }
    $lines.Add('')
    $lines.Add('Full ranges, initial generation, per-stage costs and post-GC retained heaps are in the adjacent before/after reports and comparison.csv. Do not infer a speedup from a probe-count reduction alone.')
    $lines.Add('')
}
$after = @(Import-Csv -LiteralPath (Join-Path $ResultDirectory 'after/invalidation-samples.csv') | Where-Object { $_.First -eq 'False' -and $_.Warmup -eq 'False' })
$lines.Add('## Current-owner cache observations')
$lines.Add('')
$lines.Add('| Component / edit | Requests (median) | Cache hits (median) | Misses (median) | Recovery/directive bypasses (median) |')
$lines.Add('| --- | ---: | ---: | ---: | ---: |')
function Median($values) {
    $ordered = @($values | ForEach-Object { [double]$_ } | Sort-Object)
    if ($ordered.Count -ne 8) { throw 'Incomplete cache observation group.' }
    return ($ordered[3] + $ordered[4]) / 2
}
foreach ($group in $after | Group-Object Project,File,Scenario | Sort-Object Name) {
    $first = $group.Group[0]
    $lines.Add("| $($first.File) / $($first.Scenario) | $(Median $group.Group.MarkupExpressionRequest) | $(Median $group.Group.MarkupExpressionCacheHit) | $(Median $group.Group.MarkupExpressionCacheMiss) | $(Median $group.Group.MarkupExpressionCacheDisallowed) |")
}
$diffs = [Collections.Generic.List[object]]::new()
$trxPath = Join-Path $ResultDirectory 'verification/debugstats/tests.trx'
if (Test-Path -LiteralPath $trxPath) {
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    foreach ($test in $trx.TestRun.Results.UnitTestResult | Where-Object testName -like '*GeneratedLiteralEdit_ChangesOnlyItsProperty*') {
        foreach ($line in ([string]$test.Output.StdOut).Split("`n")) {
            if ($line.StartsWith('GENERATED_LITERAL_DIFF ')) {
                $diffs.Add(($line.Substring('GENERATED_LITERAL_DIFF '.Length) | ConvertFrom-Json))
            }
        }
    }
}
if ($diffs.Count -ne 2) { throw 'Both generated-code comparison modes must have executed and recorded their differences.' }
$lines.Add('')
$lines.Add('## Observed generated C# difference for Margin="10" to Margin="20"')
$lines.Add('')
foreach ($diff in $diffs) {
    $lines.Add($diff.Mode + ':')
    $lines.Add('')
    $lines.Add('```diff')
    foreach ($change in $diff.Changes) { $lines.Add('-' + $change.Before); $lines.Add('+' + $change.After) }
    $lines.Add('```')
    $lines.Add('')
}
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'generated-code-diffs.json'), ($diffs.ToArray() | ConvertTo-Json -Depth 8) + "`r`n", $encoding)
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'measured-summary.md'), ($lines -join "`r`n") + "`r`n", $encoding)
Write-Host 'Wrote measured-summary.md and verified generated-code differences; manual review remains required.'
