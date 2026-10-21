#requires -Version 7.2
param(
    [Parameter(Mandatory)][string]$InputDirectory,
    [Parameter(Mandatory)][string]$ResultDirectory,
    [ValidateRange(2, 5)][int]$Rounds = 2
)

$ErrorActionPreference = 'Stop'
$encoding = [Text.UTF8Encoding]::new($false)
function Median($values) {
    $ordered = @($values | Sort-Object)
    if (-not $ordered.Count) { throw 'No measured samples.' }
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if ($ordered.Count % 2) { return [double]$ordered[$middle] }
    return ([double]$ordered[$middle - 1] + [double]$ordered[$middle]) / 2
}
function Write-Text([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, ([regex]::Replace($text, '\r?\n', "`r`n").TrimEnd() + "`r`n"), $encoding)
}
function Export-Invariant($rows, [string]$path) {
    $rows | ForEach-Object {
        $values = [ordered]@{}
        foreach ($property in $_.PSObject.Properties) {
            $value = $property.Value
            if ($value -is [IFormattable]) {
                $value = $value.ToString($null, [Globalization.CultureInfo]::InvariantCulture)
            }
            $values[$property.Name] = $value
        }
        [pscustomobject]$values
    } | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding utf8
}
$cells = [Collections.Generic.List[object]]::new()
$stages = [Collections.Generic.List[object]]::new()
$samples = [Collections.Generic.List[object]]::new()
$runs = @(Get-ChildItem -LiteralPath (Join-Path $InputDirectory 'runs') -Directory)
if ($runs.Count -ne (2 + 2 * $Rounds) * 3) { throw 'The complete baseline/projection matrix is required.' }
foreach ($run in $runs) {
    $state = Get-Content -LiteralPath (Join-Path $run.FullName 'status.json') -Raw | ConvertFrom-Json
    if ($state.Phase -ne 'Complete') { throw "Incomplete matrix: $($run.Name)" }
    if ($run.Name -notmatch '^(stats|production-(\d+))-(baseline|projection)-(literal|binding|focused)$') { throw 'Unexpected matrix name.' }
    $profile = if ($Matches[1] -eq 'stats') { 'stats' } else { 'production' }
    $round = if ($profile -eq 'stats') { 0 } else { [int]$Matches[2] }
    $variant = $Matches[3]
    $category = $Matches[4]
    $expectedCells = switch ($category) { literal { 4 } binding { 2 } focused { 1 } }
    $files = @(Get-ChildItem -LiteralPath $run.FullName -Filter 'invalidation-*.jsonl' -File)
    if ($files.Count -ne $expectedCells) { throw "Unexpected cell count: $($run.Name)" }
    foreach ($file in $files) {
        $rows = @(Get-Content -LiteralPath $file.FullName | ForEach-Object { $_ | ConvertFrom-Json })
        $updates = @($rows | Where-Object Kind -eq 'invalidation')
        $parity = @($rows | Where-Object Kind -eq 'invalidation-parity')
        $heaps = @($rows | Where-Object Kind -eq 'artifact-heap')
        if ($updates.Count -ne 11 -or $parity.Count -ne 11 -or $heaps.Count -ne 11 -or
            @($parity | Where-Object { -not $_.EqualSources -or -not $_.EqualDiagnostics }).Count -or
            @($updates.Index | Sort-Object -Unique).Count -ne 11 -or @($heaps.Index | Sort-Object -Unique).Count -ne 11 -or
            @($parity.Index | Sort-Object -Unique).Count -ne 11) { throw "Invalid or incomplete raw evidence: $file" }
        $measured = @($updates | Where-Object { -not $_.First -and -not $_.Warmup })
        if ($measured.Count -ne 8) { throw 'Eight measured updates are required.' }
        $component = $measured[0].Project + '/' + $measured[0].File
        $heapSamples = @($heaps | Where-Object { $_.Index -in $measured.Index })
        $wall = @($measured.GeneratorMs | Sort-Object)
        $cells.Add([pscustomobject]@{ Profile = $profile; Round = $round; Variant = $variant; Component = $component
            Scenario = $measured[0].Scenario; Samples = $measured.Count; FreshParityChecks = $parity.Count
            GeneratorMedianMs = (Median $wall); GeneratorMinMs = $wall[0]; GeneratorMaxMs = $wall[-1]
            AllocatedMedianBytes = (Median $measured.ProcessAllocatedBytes); PostGcHeapMedianBytes = (Median $heapSamples.PostGcHeapBytes)
            Gen0Total = ($measured | ForEach-Object { $_.Collections[0] } | Measure-Object -Sum).Sum
            Gen1Total = ($measured | ForEach-Object { $_.Collections[1] } | Measure-Object -Sum).Sum })
        foreach ($update in $measured) {
            $samples.Add([pscustomobject]@{ Profile = $profile; Round = $round; Variant = $variant; Component = $component
                Index = $update.Index; GeneratorMs = $update.GeneratorMs; AllocatedBytes = $update.ProcessAllocatedBytes
                PostGcHeapBytes = @($heaps | Where-Object Index -eq $update.Index)[0].PostGcHeapBytes })
        }
        if ($profile -eq 'stats') {
            foreach ($name in @('ExpressionProjectionConstruction', 'ExpressionFactoryBinding', 'CrossRevisionRegionMatching',
                'CrossRevisionProjectionLookup', 'CompilationBoundProbePublication')) {
                $values = @($measured | ForEach-Object { $_.Stages | Where-Object Stage -eq $name })
                $stages.Add([pscustomobject]@{ Variant = $variant; Component = $component; Kind = 'Stage'; Name = $name
                    Count = ($values | Measure-Object -Property Count -Sum).Sum
                    InclusiveMs = ($values | Measure-Object -Property InclusiveMs -Sum).Sum })
            }
            foreach ($name in @('CrossRevisionRegionCandidate', 'CrossRevisionRegionGreenIdentityMatch', 'CrossRevisionRegionContextMatch',
                'CrossRevisionRegionContextRejected', 'CrossRevisionProjectionHit', 'CrossRevisionProjectionMiss',
                'CrossRevisionProjectionConstructionSkipped', 'CrossRevisionProjectionAdmissionRejectedSize', 'ProbeCompilationCreated',
                'ProbeSemanticModelAcquired', 'CompilationBoundProbeEntriesPublished', 'CompilationBoundProjectionCharactersPublished')) {
                $sum = ($measured | ForEach-Object { if ($_.Counters.PSObject.Properties[$name]) { $_.Counters.$name } else { 0 } } | Measure-Object -Sum).Sum
                $stages.Add([pscustomobject]@{ Variant = $variant; Component = $component; Kind = 'Counter'; Name = $name; Count = $sum; InclusiveMs = $null })
            }
        }
    }
}
$null = New-Item -ItemType Directory -Path $ResultDirectory -Force
Export-Invariant $cells (Join-Path $ResultDirectory 'projection-cells.csv')
Export-Invariant $stages (Join-Path $ResultDirectory 'projection-stages.csv')
Export-Invariant $samples (Join-Path $ResultDirectory 'projection-samples.csv')
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Projection reuse: generated measurements')
$lines.Add('')
$lines.Add('This is projection-only evidence, not acceptance of cross-revision semantic binding. Production timings are non-STATS Release. Stage timings are inclusive and must not be added together. Both variants retain the existing current-owner expression cache. Each cell has 2 warmup updates, 8 measured updates and 11 fresh-source/diagnostic parity checks.')
$lines.Add('')
$lines.Add('| Round | Component | Baseline median, ms | Projection median, ms | Reduction | Allocation change | Post-GC heap change |')
$lines.Add('| ---: | --- | ---: | ---: | ---: | ---: | ---: |')
foreach ($candidate in $cells | Where-Object { $_.Profile -eq 'production' -and $_.Variant -eq 'projection' } | Sort-Object Round, Component) {
    $baseline = @($cells | Where-Object { $_.Profile -eq 'production' -and $_.Variant -eq 'baseline' -and $_.Round -eq $candidate.Round -and $_.Component -eq $candidate.Component })
    if ($baseline.Count -ne 1) { throw 'Every production candidate requires one paired control.' }
    $reduction = 100 * (1 - $candidate.GeneratorMedianMs / $baseline[0].GeneratorMedianMs)
    $allocation = 100 * ($candidate.AllocatedMedianBytes / $baseline[0].AllocatedMedianBytes - 1)
    $heap = 100 * ($candidate.PostGcHeapMedianBytes / $baseline[0].PostGcHeapMedianBytes - 1)
    $lines.Add([string]::Format([Globalization.CultureInfo]::InvariantCulture,
        '| {0} | {1} | {2:F3} | {3:F3} | {4:F2}% | {5:F2}% | {6:F2}% |',
        $candidate.Round, $candidate.Component, $baseline[0].GeneratorMedianMs, $candidate.GeneratorMedianMs, $reduction, $allocation, $heap))
}
$lines.Add('')
$lines.Add('Positive reduction means lower GeneratorDriver elapsed time. Positive allocation/heap change means greater cost. Full distributions, GC activity and causal counters are in the CSV files. Entry/character counters are publication totals over measured updates, not a single latest-store gauge. Character counts are UTF-16 text lengths, not complete retained object-graph byte sizes. No first semantic binding, probe compilation or probe SemanticModel is intentionally bypassed by this candidate.')
Write-Text (Join-Path $ResultDirectory 'projection-measured-summary.md') ($lines -join "`n")
Write-Host "Validated $($cells.Count) cells and $($samples.Count) measured updates. Engineering acceptance still requires review."
