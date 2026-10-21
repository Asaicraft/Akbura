#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('AkburaProjectionSummary-' + [Guid]::NewGuid().ToString('N'))
$encoding = [Text.UTF8Encoding]::new($false)
$null = New-Item -ItemType Directory -Path $temporary
function Json([string]$path, $value) {
    [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 10 -Compress) + "`r`n", $encoding)
}
function Expect-Failure([scriptblock]$action) {
    $failed = $false
    try { & $action } catch { $failed = $true }
    if (-not $failed) { throw 'Expected invalid synthetic evidence to be rejected.' }
}
$summarize = Join-Path $PSScriptRoot 'Summarize-ProjectionReuse.ps1'
$result = Join-Path $temporary 'report'
$firstFile = $null
foreach ($profile in @('stats', 'production-1', 'production-2')) {
    foreach ($variant in @('baseline', 'projection')) {
        foreach ($category in @('literal', 'binding', 'focused')) {
            $directory = Join-Path $temporary "runs/$profile-$variant-$category"
            $null = New-Item -ItemType Directory -Path $directory -Force
            Json (Join-Path $directory 'status.json') @{ Phase = 'Complete' }
            $count = switch ($category) { literal { 4 } binding { 2 } focused { 1 } }
            for ($cell = 0; $cell -lt $count; $cell++) {
                $rows = [Collections.Generic.List[string]]::new()
                for ($index = 0; $index -le 10; $index++) {
                    $sample = @{ Kind = 'invalidation'; Project = 'SyntheticSummaryTest'; File = "$category-$cell.akbura"
                        Scenario = 'localized-literal'; Index = $index; First = $index -eq 0; Warmup = $index -in 1..2
                        GeneratorMs = $(if ($variant -eq 'baseline') { 10 } else { 9.5 }); ProcessAllocatedBytes = 1000
                        Collections = @(1, 0, 0); Counters = @{ ProbeCompilationCreated = 7 }
                        Stages = @(@{ Stage = 'ExpressionProjectionConstruction'; Count = 23; InclusiveMs = 0.25 }) }
                    $rows.Add(($sample | ConvertTo-Json -Depth 8 -Compress))
                    $rows.Add((@{ Kind = 'invalidation-parity'; Index = $index; EqualSources = $true; EqualDiagnostics = $true } | ConvertTo-Json -Compress))
                    $rows.Add((@{ Kind = 'artifact-heap'; Index = $index; PostGcHeapBytes = 5000 } | ConvertTo-Json -Compress))
                }
                $file = Join-Path $directory "invalidation-$cell.jsonl"
                [IO.File]::WriteAllText($file, ($rows -join "`r`n") + "`r`n", $encoding)
                if (-not $firstFile) { $firstFile = $file }
            }
        }
    }
}
& $summarize -InputDirectory $temporary -ResultDirectory $result
$cells = @(Import-Csv -LiteralPath (Join-Path $result 'projection-cells.csv'))
$stages = @(Import-Csv -LiteralPath (Join-Path $result 'projection-stages.csv'))
if ($cells.Count -ne 42 -or @($cells | Where-Object Samples -ne 8).Count) { throw 'Wrong sample count.' }
if (@($cells | Where-Object Variant -eq 'projection' | Where-Object { [double]$_.GeneratorMedianMs -ne 9.5 }).Count) { throw 'Wrong median or culture-dependent CSV.' }
if (@($stages | Where-Object Name -eq 'ExpressionProjectionConstruction' | Where-Object { [double]$_.Count -ne 184 -or [double]$_.InclusiveMs -ne 2 }).Count) {
    throw 'Stage Count values must be summed, not confused with array cardinality.'
}
$original = [IO.File]::ReadAllText($firstFile)
$previousCulture = [Globalization.CultureInfo]::CurrentCulture
try {
    foreach ($culture in @('en-US', 'ru-RU')) {
        [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo($culture)
        $cultureResult = Join-Path $temporary $culture
        & $summarize -InputDirectory $temporary -ResultDirectory $cultureResult
        foreach ($name in @('projection-cells.csv', 'projection-stages.csv', 'projection-samples.csv', 'projection-measured-summary.md')) {
            if ([IO.File]::ReadAllText((Join-Path $result $name)) -cne [IO.File]::ReadAllText((Join-Path $cultureResult $name))) {
                throw "Locale-dependent report: $culture/$name"
            }
        }
    }
}
finally { [Globalization.CultureInfo]::CurrentCulture = $previousCulture }
[IO.File]::WriteAllText($firstFile, $original.Replace('"EqualSources":true', '"EqualSources":false'), $encoding)
Expect-Failure { & $summarize -InputDirectory $temporary -ResultDirectory $result }
[IO.File]::WriteAllText($firstFile, $original, $encoding)
$statePath = Join-Path ([IO.Path]::GetDirectoryName($firstFile)) 'status.json'
Json $statePath @{ Phase = 'Running' }
Expect-Failure { & $summarize -InputDirectory $temporary -ResultDirectory $result }
Json $statePath @{ Phase = 'Complete' }
$rows = @($original -split '\r?\n' | Where-Object { $_ })
[IO.File]::WriteAllText($firstFile, ($rows[0..($rows.Length - 2)] -join "`r`n") + "`r`n", $encoding)
Expect-Failure { & $summarize -InputDirectory $temporary -ResultDirectory $result }
Write-Host "PASS: complete matrix, medians, nontrivial stage sums, identical en-US/ru-RU reports, parity rejection, incomplete-run rejection and missing-heap rejection. Synthetic fixtures: $temporary"
