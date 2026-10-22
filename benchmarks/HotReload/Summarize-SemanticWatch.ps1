#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$ResultDirectory,
    [int]$Rounds = 3,
    [int]$Updates = 20
)
$ErrorActionPreference = 'Stop'
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$culture = [Globalization.CultureInfo]::InvariantCulture
function Number($value) { [Convert]::ToDouble($value, $culture) }
function Quantile([double[]]$values, [double]$fraction) {
    if (-not $values.Count) { throw 'Empty distribution' }
    $sorted = [double[]]$values.Clone()
    [array]::Sort($sorted)
    if ($fraction -eq 0.5) {
        $middle = [int][math]::Floor($sorted.Length / 2)
        if ($sorted.Length % 2) { return $sorted[$middle] }
        return ($sorted[$middle - 1] + $sorted[$middle]) / 2
    }
    $sorted[[math]::Max(0, [int][math]::Ceiling($fraction * $sorted.Length) - 1)]
}
function Csv([string]$name, [object[]]$values) {
    $rows = foreach ($value in $values) {
        $record = [ordered]@{}
        foreach ($property in $value.PSObject.Properties) {
            $record[$property.Name] = if ($property.Value -is [double]) { $property.Value.ToString('R', $culture) } else { $property.Value }
        }
        [pscustomobject]$record
    }
    $text = (@($rows | ConvertTo-Csv -NoTypeInformation) -join "`r`n") + "`r`n"
    [IO.File]::WriteAllText((Join-Path $ResultDirectory $name), $text, [Text.UTF8Encoding]::new($false))
}
$samples = [Collections.Generic.List[object]]::new()
$summary = [Collections.Generic.List[object]]::new()
$stages = [Collections.Generic.List[object]]::new()
$distributions = @{}
foreach ($round in 1..$Rounds) {
    foreach ($scenario in @('margin','text')) {
        foreach ($variant in @('baseline','projection','binding','combined')) {
            $directory = Join-Path $ResultDirectory "runs/production-$round-$variant-$scenario"
            $acceptance = Get-Content -LiteralPath (Join-Path $directory 'acceptance.json') -Raw | ConvertFrom-Json
            if ($acceptance.TimingContaminationObserved -or $acceptance.Profile -ne 'production') { throw 'Invalid timing acceptance.' }
            $rows = @(Get-Content -LiteralPath (Join-Path $directory 'watch.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
            if (@($rows | Where-Object Kind -eq 'semantic-watch-complete').Count -ne 1) { throw 'Incomplete watch run.' }
            $measured = @($rows | Where-Object { $_.Kind -eq 'semantic-watch-sample' -and $_.Phase -eq $scenario -and -not $_.Warmup })
            if ($measured.Count -ne $Updates -or @($measured.Revision | Sort-Object -Unique).Count -ne $Updates) { throw 'Missing or duplicate samples.' }
            foreach ($row in $measured) {
                if (-not $row.Application.ControlsPreserved -or -not $row.Application.StatePreserved -or -not $row.Application.InputPreserved -or
                    -not $row.Application.BindingsValid -or $row.Application.Subscriptions -ne 1 -or $row.FileWriteToObservedUiMs -le 0) { throw 'Invalid UI acknowledgment.' }
                $samples.Add([pscustomobject]@{ Round = $round; Scenario = $scenario; Variant = $variant; Revision = $row.Revision
                    FileWriteToObservedUiMs = Number $row.FileWriteToObservedUiMs; FileWriteDurationMs = Number $row.FileWriteDurationMs
                    ApplicationPid = $row.Application.Pid; ApplicationHeapBytes = $row.Application.ManagedHeapBytes
                    ApplicationAllocatedBytes = $row.Application.AllocatedBytes; ApplicationWorkingSetBytes = $row.Application.WorkingSetBytes
                    CausalEvidence = 'Unverified per production update; separate STATS runs provide workload-level attribution.' })
            }
            if (@($measured.Application.Pid | Sort-Object -Unique).Count -ne 1) { throw 'Application restart detected.' }
            $times = [double[]]@($measured | ForEach-Object { Number $_.FileWriteToObservedUiMs })
            $distributions["$round/$scenario/$variant"] = $times
            $mean = ($times | Measure-Object -Average).Average
            $variance = ($times | ForEach-Object { [math]::Pow($_ - $mean, 2) } | Measure-Object -Sum).Sum / ($times.Count - 1)
            $memory = @($rows | Where-Object { $_.Kind -eq 'semantic-watch-sample' -and $_.Phase -eq 'memory' })
            if ($memory.Count -ne 1 -or -not $memory[0].Application.PostGc) { throw 'Missing application post-GC observation.' }
            $summary.Add([pscustomobject]@{ Round = $round; Scenario = $scenario; Variant = $variant; Samples = $times.Count
                MedianMs = Quantile $times 0.5; MeanMs = [double]$mean; P90Ms = Quantile $times 0.9; P95Ms = Quantile $times 0.95
                MinMs = ($times | Measure-Object -Minimum).Minimum; MaxMs = ($times | Measure-Object -Maximum).Maximum
                StandardDeviationMs = [math]::Sqrt($variance); ApplicationPostGcHeapBytes = $memory[0].Application.ManagedHeapBytes
                Restarts = 0; SubscriptionFailures = 0 })
        }
    }
}
foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $ResultDirectory 'runs') -Directory | Where-Object Name -NotLike 'production-*') {
    $rows = @(Get-Content -LiteralPath (Join-Path $directory.FullName 'watch.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
    foreach ($row in $rows | Where-Object Kind -eq 'semantic-watch-causal') {
        $record = $row.Record
        $stages.Add([pscustomobject]@{ Run = $directory.Name; Revision = $row.Revision; Phase = $row.Phase; Kind = $record.Kind
            CompilerPid = $record.Pid; Observer = $record.Observer; InputCompilation = $record.InputCompilation; Compilation = $record.Compilation
            ProjectState = $record.ProjectState; ProbeStore = $record.ProbeStore; PreviousGeneration = $record.PreviousGenerationVersion
            PublishedGeneration = $record.PublishedGenerationVersion; SemanticEntries = $record.PublishedSemanticEntries
            OperationRoots = $record.LiveOperationRoots; ProbeModels = $record.LiveProbeModels; ProbeCompilations = $record.LiveProbeCompilations
            ProbeSyntaxTrees = $record.LiveProbeSyntaxTrees; FirstBindingsSkipped = $record.'counter.CrossRevisionFirstBindingSkipped'
            SemanticHits = $record.'counter.CrossRevisionBindingHit'; Imported = $record.'counter.CrossRevisionSemanticContributionImported'
            FactoryInvocations = $record.'stageCount.ExpressionFactoryBinding'; CompilerHeapBytes = $record.ManagedHeapBytes
            PostGc = $record.HeapCollectionRequested; CompilerWorkingSetBytes = $record.WorkingSetBytes })
    }
}
$comparisons = [Collections.Generic.List[object]]::new()
$random = [Random]::new(20261021)
foreach ($scenario in @('margin','text')) {
    foreach ($variant in @('projection','binding','combined')) {
        $observed = @(foreach ($round in 1..$Rounds) { [math]::Log((Quantile $distributions["$round/$scenario/$variant"] 0.5) / (Quantile $distributions["$round/$scenario/baseline"] 0.5)) })
        $bootstrap = [double[]]::new(2000)
        for ($iteration = 0; $iteration -lt $bootstrap.Length; $iteration++) {
            $logRatio = 0.0
            for ($draw = 0; $draw -lt $Rounds; $draw++) {
                $round = 1 + $random.Next($Rounds)
                $candidate = $distributions["$round/$scenario/$variant"]
                $baseline = $distributions["$round/$scenario/baseline"]
                $a = [double[]]::new($Updates); $b = [double[]]::new($Updates)
                for ($index = 0; $index -lt $Updates; $index++) { $a[$index] = $candidate[$random.Next($Updates)]; $b[$index] = $baseline[$random.Next($Updates)] }
                $logRatio += [math]::Log((Quantile $a 0.5) / (Quantile $b 0.5))
            }
            $bootstrap[$iteration] = 100 * (1 - [math]::Exp($logRatio / $Rounds))
        }
        $comparisons.Add([pscustomobject]@{ Scenario = $scenario; Variant = $variant
            GeometricMedianReductionPct = 100 * (1 - [math]::Exp(($observed | Measure-Object -Average).Average))
            BootstrapLowPct = Quantile $bootstrap 0.025; BootstrapHighPct = Quantile $bootstrap 0.975
            Bootstrap = '2000 deterministic hierarchical resamples: independent sessions/rounds, then independent samples within selected sessions; not paired edits.' })
    }
}
Csv 'samples.csv' $samples.ToArray()
Csv 'summary.csv' $summary.ToArray()
Csv 'stage-observations.csv' $stages.ToArray()
Csv 'comparisons.csv' $comparisons.ToArray()
$lines = @('# Live semantic watch: generated measurements','',
    'Production results use non-STATS Release analyzers. Latency starts after the file write completes and ends when the host receives the validated UI acknowledgment. Startup, deliberate delays, diagnostic repair and explicit full collections are excluded. Per-production-update semantic hits are unavailable; causal runs are separate.', '',
    '| Round | Scenario | Variant | Samples | Median ms | Mean ms | p95 ms | Min–max ms |',
    '| ---: | --- | --- | ---: | ---: | ---: | ---: | ---: |')
foreach ($row in $summary) {
    $lines += '| ' + $row.Round + ' | ' + $row.Scenario + ' | ' + $row.Variant + ' | ' + $row.Samples + ' | ' +
        $row.MedianMs.ToString('F3',$culture) + ' | ' + $row.MeanMs.ToString('F3',$culture) + ' | ' + $row.P95Ms.ToString('F3',$culture) + ' | ' +
        $row.MinMs.ToString('F3',$culture) + '–' + $row.MaxMs.ToString('F3',$culture) + ' |'
}
$lines += @('', 'Equal-weight round-median ratios and hierarchical bootstrap intervals are descriptive. Three sessions per variant provide limited uncertainty resolution; do not infer a general speedup, default activation or a cache hit in an uninstrumented update. Compiler allocations/post-GC heap are not observable in production here; application metrics must not be substituted for compiler metrics.')
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'measured-summary.md'), ($lines -join "`r`n") + "`r`n", [Text.UTF8Encoding]::new($false))
