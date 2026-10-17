#requires -Version 7.2
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../bin/CSharpInvalidationInvestigation/report-tests'))

$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([Guid]::NewGuid().ToString('N'))
$raw = Join-Path $root 'raw'
$result = Join-Path $root 'result'
$null = New-Item -ItemType Directory -Path $raw -Force
$summarizer = Join-Path $PSScriptRoot 'Summarize-Invalidation.ps1'
$encoding = [Text.UTF8Encoding]::new($false)
$checks = 0
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
    $script:checks++
}

foreach ($configuration in @('production/off', 'stats/off', 'stats/trace')) {
    $flavor, $mode = $configuration.Split('/')
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($index in 0..10) {
        $row = @{
            Kind = 'invalidation'; Project = 'Synthetic'; File = 'Page.akbura'; Scenario = 'unrelated-body'; Profiling = $mode
            BinaryFlavor = $flavor
            Index = $index; First = $index -eq 0; Warmup = $index -in @(1, 2); GeneratorMs = $index + 0.25
            ProcessCpuMs = $index; Collections = @(0, 0, 0); Sources = 1; ChangedSources = @()
            CurrentThreadAllocatedBytes = 100; ProcessAllocatedBytes = 200; ManagedHeapBytes = 300; WorkingSetBytes = 400
            Counters = if ($mode -eq 'trace') { @{ ComponentGenerated = 1; ComponentReused = 0; AkcssGenerated = 0; DiagnosticDocumentEvaluated = 1 } } else { $null }
            Stages = @(); Events = @()
            Steps = @{ 'BlackSilence.GeneratedComponents' = if ($index % 2 -eq 0) { @{ Cached = 1 } } else { @{ Unchanged = 1 } } }
        }
        $lines.Add(($row | ConvertTo-Json -Depth 10 -Compress))
    }
    foreach ($index in @(9, 10)) {
        $lines.Add((@{
            Kind = 'invalidation-parity'; Project = 'Synthetic'; File = 'Page.akbura'; Scenario = 'unrelated-body'; Profiling = $mode
            BinaryFlavor = $flavor
            Index = $index; EqualSources = $true; EqualDiagnostics = $true
        } | ConvertTo-Json -Compress))
    }
    [IO.File]::WriteAllText((Join-Path $raw "invalidation-$flavor-$mode.jsonl"), ($lines -join "`r`n") + "`r`n", $encoding)
}
& $summarizer -InputDirectory $raw -ResultDirectory $result
$summary = @(Import-Csv -LiteralPath (Join-Path $result 'invalidation-summary.csv'))
Check ($summary.Count -eq 3) 'Expected separate production/off, stats/off and stats/trace mode groups.'
Check (@($summary | Where-Object { $_.Count -ne '8' }).Count -eq 0) 'Warm-ups/initial leaked into measured sample count.'
$trace = $summary | Where-Object Profiling -eq 'trace'
$off = $summary | Where-Object Profiling -eq 'off' | Select-Object -First 1
Check ($trace.MedianMs -eq '6.75' -and $trace.P95Ms -eq '10.25') 'Median or nearest-rank p95 is incorrect.'
Check ([string]::IsNullOrEmpty($off.MedianComponentsGenerated)) 'Disabled instrumentation was reported as zero work.'
$samples = @(Import-Csv -LiteralPath (Join-Path $result 'invalidation-samples.csv'))
Check (($samples | Where-Object Profiling -eq 'trace' | Select-Object -First 1).ComponentGenerated -eq '1') 'CSV lost counter columns after an off-mode first row.'
$transition = Import-Csv -LiteralPath (Join-Path $result 'invalidation-transitions.csv') | Select-Object -First 1
Check ($transition.TransformExecution -eq 'Mixed' -and $transition.CachedMin -eq '0' -and $transition.CachedMax -eq '1' -and
    $transition.UnchangedMin -eq '0' -and $transition.UnchangedMax -eq '1') 'Compact transitions lost mixed callback execution or zero-count states.'
$reportPath = Join-Path $result 'README.md'
$expectedLink = [IO.Path]::GetRelativePath($result,
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../For Contributors/csharp-invalidation-investigation.md'))).Replace('\', '/').Replace(' ', '%20')
Check ([IO.File]::ReadAllText($reportPath).Contains("[engineering investigation]($expectedLink)")) 'Investigation link does not resolve from a custom output directory.'
$manual = "Manual interpretation stays intact.`r`n`r`n"
[IO.File]::WriteAllText($reportPath, $manual + [IO.File]::ReadAllText($reportPath), $encoding)
& $summarizer -InputDirectory $raw -ResultDirectory $result
Check ([IO.File]::ReadAllText($reportPath).StartsWith($manual)) 'Manual report text was replaced.'
$before = (Get-FileHash -LiteralPath $reportPath).Hash
& $summarizer -InputDirectory $raw -ResultDirectory $result
Check ((Get-FileHash -LiteralPath $reportPath).Hash -eq $before) 'Report generation is not deterministic.'
$reportText = [IO.File]::ReadAllText($reportPath)
Check ($reportText.EndsWith("`r`n") -and $reportText -notmatch '(?<!\r)\n') 'Report must use CRLF including its final newline.'
$path = Join-Path $raw 'invalidation-stats-trace.jsonl'
$original = [IO.File]::ReadAllText($path)
$filtered = @([IO.File]::ReadLines($path) | Where-Object { ($_ | ConvertFrom-Json).Index -ne 10 })
[IO.File]::WriteAllText($path, ($filtered -join "`r`n") + "`r`n", $encoding)
$failed = $false
try {
    & $summarizer -InputDirectory $raw -ResultDirectory $result
}
catch {
    $failed = $true
}
Check $failed 'Incomplete sampling must fail.'
Check ((Get-FileHash -LiteralPath $reportPath).Hash -eq $before) 'A failed run replaced the report.'
[IO.File]::WriteAllText($path, $original, $encoding)
Write-Host "$checks invalidation-report checks passed. Synthetic fixtures: $root"
