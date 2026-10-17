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
foreach ($optimization in @('disabled', 'enabled')) {
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($index in 0..10) {
        $row = ([IO.File]::ReadLines($path) | Select-Object -First 1) | ConvertFrom-Json
        $row | Add-Member -NotePropertyName Optimization -NotePropertyValue $optimization
        $row | Add-Member -NotePropertyName VerifyEveryUpdate -NotePropertyValue $true
        $row.Index = $index; $row.First = $index -eq 0; $row.Warmup = $index -in @(1, 2)
        $lines.Add(($row | ConvertTo-Json -Depth 10 -Compress))
        $lines.Add((@{ Kind = 'invalidation-parity'; Project = $row.Project; File = $row.File; Scenario = $row.Scenario
            Profiling = $row.Profiling; BinaryFlavor = $row.BinaryFlavor; Optimization = $optimization
            Index = $index; EqualSources = $true; EqualDiagnostics = $true } | ConvertTo-Json -Compress))
        $lines.Add((@{ Kind = 'artifact-heap'; Project = $row.Project; File = $row.File; Scenario = $row.Scenario
            Profiling = $row.Profiling; BinaryFlavor = $row.BinaryFlavor; Optimization = $optimization
            Index = $index; PostGcHeapBytes = 1234; WorkingSetBytes = 5678; Entries = 2; IdentitySlots = 4 } | ConvertTo-Json -Compress))
    }
    [IO.File]::WriteAllText((Join-Path $raw "invalidation-artifacts-$optimization.jsonl"), ($lines -join "`r`n") + "`r`n", $encoding)
}
& $summarizer -InputDirectory $raw -ResultDirectory $result
$summary = @(Import-Csv -LiteralPath (Join-Path $result 'invalidation-summary.csv'))
Check ($summary.Count -eq 5) 'Enabled and disabled artifacts must not be merged with each other or legacy runs.'
Check (@($summary | Where-Object { $_.Optimization -ne 'legacy' -and $_.MedianPostGcHeapBytes -eq '1234' }).Count -eq 2) 'Post-GC heap rows were not joined to the correct samples.'
$path = Join-Path $raw 'invalidation-artifacts-enabled.jsonl'
$original = [IO.File]::ReadAllText($path)
foreach ($kind in @('invalidation-parity', 'artifact-heap')) {
    $filtered = @($original.Split("`n", [StringSplitOptions]::RemoveEmptyEntries) | Where-Object {
        $row = $_ | ConvertFrom-Json
        -not ($row.Kind -eq $kind -and $row.Index -eq 4)
    })
    [IO.File]::WriteAllText($path, ($filtered -join "`r`n") + "`r`n", $encoding)
    $failed = $false
    try {
        & $summarizer -InputDirectory $raw -ResultDirectory $result
    }
    catch {
        $failed = $true
    }
    Check $failed "Missing intermediate $kind must fail, even if the final two parity checks exist."
    [IO.File]::WriteAllText($path, $original, $encoding)
}
$memoryPath = Join-Path $raw 'artifact-memory-synthetic-enabled.jsonl'
$memory = @(
    @{ Kind = 'artifact-memory-sample'; Project = 'Synthetic'; Optimization = 'enabled'; Index = 0
        CumulativeCollections = @(1, 2, 3); CollectionsSinceUpdateStartIncludingOracle = @(1, 2, 3) }
    @{ Kind = 'artifact-memory-sample'; Project = 'Synthetic'; Optimization = 'enabled'; Index = 1
        CumulativeCollections = @(4, 5, 6); CollectionsSinceUpdateStartIncludingOracle = @(3, 3, 3) }
    @{ Kind = 'artifact-concurrent'; Project = 'Synthetic'; Optimization = 'enabled'; BranchCompletionMs = @(10, 11)
        BranchGeneratorMs = @(9, 10); Collections = @(1, 2, 3)
        Counters = @{ ProbeCompilationCreated = 0 }; Stages = @(@{ Stage = 'SemanticBinding'; Count = 0; InclusiveMs = 0 }) }
    @{ Kind = 'artifact-memory-result'; Project = 'Synthetic'; Optimization = 'enabled'; Revisions = 1
        Objects = @{ CSharpCompilation = @{ Observed = 4; Alive = 0 }; DetachedSnapshot = @{ Observed = 4; Alive = 1 } } }
)
function Save-MemoryFixture {
    $lines = @($memory | ForEach-Object { $_ | ConvertTo-Json -Depth 12 -Compress })
    [IO.File]::WriteAllText($memoryPath, ($lines -join "`r`n") + "`r`n", $encoding)
}
Save-MemoryFixture
& $summarizer -InputDirectory $raw -ResultDirectory $result
$fork = Import-Csv -LiteralPath (Join-Path $result 'artifact-concurrent.csv') | Select-Object -First 1
Check ($fork.ProbeCompilationCreated -eq '0' -and $fork.BranchCompletionMs -eq '10/11') 'Concurrent counters or timing arrays were lost.'
$forkStage = Import-Csv -LiteralPath (Join-Path $result 'artifact-concurrent-stages.csv') | Select-Object -First 1
Check ($forkStage.Stage -eq 'SemanticBinding' -and $forkStage.Count -eq '0') 'Concurrent inclusive stages were lost.'
$memory[-1].Objects.CSharpCompilation.Alive = 1
Save-MemoryFixture
$failed = $false
try {
    & $summarizer -InputDirectory $raw -ResultDirectory $result
}
catch {
    $failed = $true
}
Check $failed 'A surviving compilation in an isolated-cache proof must fail.'
Write-Host "$checks invalidation-report checks passed. Synthetic fixtures: $root"
