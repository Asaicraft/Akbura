#requires -Version 7.2
param([Parameter(Mandatory)][string]$ResultDirectory, [int]$Rounds = 5, [int]$Updates = 30, [int]$FallbackRounds = 3, [int]$FallbackUpdates = 20)
$ErrorActionPreference = 'Stop'
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$culture = [Globalization.CultureInfo]::InvariantCulture
function N($value) { [double]::Parse("$value",$culture) }
function Q([double[]]$values, [double]$fraction) {
    if (-not $values.Count) { throw 'Empty sample distribution.' }
    $sorted = [double[]]$values.Clone(); [array]::Sort($sorted)
    if ($fraction -eq 0.5 -and -not ($sorted.Length % 2)) { return ($sorted[$sorted.Length/2-1]+$sorted[$sorted.Length/2])/2 }
    return $sorted[[math]::Max(0,[int][math]::Ceiling($fraction*$sorted.Length)-1)]
}
function Csv([string]$name, $rows) {
    $values = foreach ($row in $rows) {
        $record = [ordered]@{}
        foreach ($property in $row.PSObject.Properties) { $record[$property.Name] = if ($property.Value -is [double]) { $property.Value.ToString('R',$culture) } else { $property.Value } }
        [pscustomobject]$record
    }
    [IO.File]::WriteAllText((Join-Path $ResultDirectory $name),((@($values | ConvertTo-Csv -NoTypeInformation) -join "`r`n")+"`r`n"),[Text.UTF8Encoding]::new($false))
}
$sessions = [Collections.Generic.List[object]]::new()
$samples = [Collections.Generic.List[object]]::new()
$distributions = @{}
foreach ($scenario in @('supported','fallback')) {
    $count = if ($scenario -eq 'supported') { $Rounds } else { $FallbackRounds }
    $expected = if ($scenario -eq 'supported') { $Updates } else { $FallbackUpdates }
    foreach ($round in 1..$count) {
        foreach ($variant in @('baseline','projection','binding','combined')) {
            $path = Join-Path $ResultDirectory ('watch/sidebar/{0}/round-{1:D2}-{2}' -f $scenario,$round,$variant)
            $acceptance = Get-Content -LiteralPath (Join-Path $path 'acceptance.json') -Raw | ConvertFrom-Json
            $result = Get-Content -LiteralPath (Join-Path $path 'result.json') -Raw | ConvertFrom-Json
            if ($acceptance.TimingContaminationObserved -or $acceptance.Profile -ne 'production' -or $result.Instrumented -or
                -not $result.Complete -or -not $result.RuntimeCorrectness -or $result.Restarts -ne 0 -or $result.Updates -ne $expected) { throw "Invalid primary timing acceptance: $path" }
            $rows = @(Get-Content -LiteralPath (Join-Path $path 'watch.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
            if (@($rows | Where-Object Kind -eq 'purity-critical-complete').Count -ne 1) { throw 'Incomplete watch session.' }
            $measured = @($rows | Where-Object { $_.Kind -eq 'purity-critical-sample' -and -not $_.Warmup })
            if ($measured.Count -ne $expected -or @($measured.Revision | Sort-Object -Unique).Count -ne $expected -or
                @($measured.Application.Pid | Sort-Object -Unique).Count -ne 1) { throw 'Missing, duplicate or restarted measured revisions.' }
            $times = foreach ($row in $measured) {
                foreach ($field in @('OwnerPreserved','ControlsPreserved','StatePreserved','CommandsValid')) { if (-not $row.Application.$field) { throw 'A live UI invariant failed.' } }
                $value = N $row.FileWriteToObservedUiMs
                if ($value -le 0) { throw 'Non-positive end-to-end latency.' }
                $samples.Add([pscustomobject]@{ Scenario=$scenario; Round=$round; Variant=$variant; Revision=$row.Revision; FileWriteToObservedUiMs=$value
                    FileWriteDurationMs=$row.FileWriteDurationMs; ApplicationPid=$row.Application.Pid; ApplicationValidationMs=$row.Application.ValidationMs
                    CompilerAllocatedBytes=$null; CompilerAllocationMeaning='Unavailable in primary production session'
                    GeneratorDriverMs=$null; EnCDeltaProductionMs=$null; StageMeaning='Separate instrumented evidence; no production per-update causal attribution' })
                $value
            }
            $median = Q $times 0.5
            $distributions["$scenario/$round/$variant"] = $median
            $sessions.Add([pscustomobject]@{ Scenario=$scenario; Round=$round; Variant=$variant; Samples=$times.Count; MedianMs=$median
                P95Ms=(Q $times 0.95); MinMs=($times | Measure-Object -Minimum).Minimum; MaxMs=($times | Measure-Object -Maximum).Maximum })
        }
    }
}
$random = [Random]::new(20261022)
$comparisons = foreach ($scenario in @('supported','fallback')) {
    $count = if ($scenario -eq 'supported') { $Rounds } else { $FallbackRounds }
    foreach ($pair in @(@('projection','baseline'),@('binding','baseline'),@('combined','baseline'),@('combined','binding'))) {
        $logs = [double[]]@(foreach ($round in 1..$count) { [math]::Log($distributions["$scenario/$round/$($pair[0])"]/$distributions["$scenario/$round/$($pair[1])"]) })
        $draws = [double[]]::new(10000)
        for ($iteration = 0; $iteration -lt $draws.Length; $iteration++) {
            $sum = 0.0
            for ($draw = 0; $draw -lt $count; $draw++) { $sum += $logs[$random.Next($count)] }
            $draws[$iteration] = 100*(1-[math]::Exp($sum/$count))
        }
        [pscustomobject]@{ Scenario=$scenario; Variant=$pair[0]; Reference=$pair[1]; SessionsPerVariant=$count
            ImprovementPct=100*(1-[math]::Exp(($logs | Measure-Object -Average).Average)); Low95Pct=(Q $draws 0.025); High95Pct=(Q $draws 0.975)
            Bootstrap='10000 deterministic paired-round session-cluster resamples of session medians; no independent-edit bootstrap' }
    }
}
Csv 'samples.csv' $samples
Csv 'session-summary.csv' $sessions
Csv 'comparison.csv' $comparisons
$stages = [Collections.Generic.List[object]]::new()
$diagnostics = Join-Path $ResultDirectory 'diagnostic'
if (Test-Path -LiteralPath $diagnostics) {
    foreach ($run in Get-ChildItem -LiteralPath $diagnostics -Directory) {
        $result = Get-Content -LiteralPath (Join-Path $run.FullName 'result.json') -Raw | ConvertFrom-Json
        if (-not $result.Complete -or -not $result.Instrumented) { throw 'Incomplete diagnostic evidence.' }
        $rows = @(Get-Content -LiteralPath (Join-Path $run.FullName 'watch.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
        $warmups = @($rows | Where-Object { $_.Kind -eq 'purity-critical-sample' -and $_.Warmup } | ForEach-Object Revision)
        foreach ($row in $rows | Where-Object Kind -eq 'purity-critical-causal') {
            if ($row.Revision -in $warmups) { continue }
            foreach ($property in $row.Record.PSObject.Properties | Where-Object { $_.Name -match '^(stageMs\.|stageCount\.|counter\.)|^Generated|^ChangedGenerated|^ScopeElapsedMs$|^ComponentPlansCreated$' }) {
                $stages.Add([pscustomobject]@{ Run=$run.Name; Variant=$result.Variant; Scenario=$result.Scenario; Revision=$row.Revision
                    Kind=$row.Record.Kind; Metric=$property.Name; Value=$property.Value; CompilerPid=$row.Record.Pid; SourceSha256=$row.Record.RevisionExpectedSha256 })
            }
        }
        $spanPath=Join-Path $run.FullName 'roslyn-spans.csv'
        if (-not (Test-Path -LiteralPath $spanPath)) { throw 'Missing direct Roslyn event attribution in a diagnostic session.' }
        foreach ($span in Import-Csv -LiteralPath $spanPath | Where-Object { $_.Revision -and $_.Warmup -eq 'False' }) {
            $stages.Add([pscustomobject]@{ Run=$run.Name; Variant=$result.Variant; Scenario=$result.Scenario; Revision=$span.Revision
                Kind='RoslynEventPipe'; Metric=$(if ($span.Scope -eq 'GeneratorDriverRunTime') { 'GeneratorDriverMs' } else { 'SingleGeneratorMs.'+$span.Generator })
                Value=$span.DurationMs; CompilerPid=$span.CompilerPid; SourceSha256=$null })
        }
    }
}
Csv 'stage-observations.csv' $stages
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Generated real Sidebar measurements'); $lines.Add('')
$lines.Add('Non-STATS production sessions only. Equal session weights. p95 below is descriptive pooled-edit p95, not an independent-edit confidence interval. Instrumented stages are a separate population. Missing allocation/EnC/memory/correctness-gate values are not substituted from historical experiments.')
$lines.Add(''); $lines.Add('| Scenario | Variant | Sessions | Median of session medians, ms | Pooled p95, ms |'); $lines.Add('| --- | --- | ---: | ---: | ---: |')
foreach ($scenario in @('supported','fallback')) {
    foreach ($variant in @('baseline','projection','binding','combined')) {
        $group = @($sessions | Where-Object { $_.Scenario -eq $scenario -and $_.Variant -eq $variant })
        $values = [double[]]@($samples | Where-Object { $_.Scenario -eq $scenario -and $_.Variant -eq $variant } | ForEach-Object FileWriteToObservedUiMs)
        $lines.Add("| $scenario | $variant | $($group.Count) | $((Q $group.MedianMs 0.5).ToString('F3',$culture)) | $((Q $values 0.95).ToString('F3',$culture)) |")
    }
}
$lines.Add(''); $lines.Add('| Scenario | Candidate/reference | Improvement, % | 95% session-cluster interval |'); $lines.Add('| --- | --- | ---: | ---: |')
foreach ($row in $comparisons) { $lines.Add("| $($row.Scenario) | $($row.Variant)/$($row.Reference) | $($row.ImprovementPct.ToString('F2',$culture)) | $($row.Low95Pct.ToString('F2',$culture)) to $($row.High95Pct.ToString('F2',$culture)) |") }
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'measured-summary.md'),(($lines -join "`r`n")+"`r`n"),[Text.UTF8Encoding]::new($false))
$statistics = @{ Complete=$true; PrimarySessions=$sessions.Count; PrimaryUpdates=$samples.Count; BootstrapResamples=10000
    BindingOnly='Restricted opt-in: separate correctness/GeneratorDriver/memory/default gates not completed by this benchmark-only stage'
    ProjectionOnly='Restricted'; Combined='Restricted opt-in'; WatchMemory='Not newly measured in this archive'
    Comparisons=@($comparisons); NoDefaultActivation=$true; CompletedUtc=[DateTime]::UtcNow.ToString('o') }
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'statistics.json'),(($statistics | ConvertTo-Json -Depth 8).ReplaceLineEndings("`r`n")+"`r`n"),[Text.UTF8Encoding]::new($false))
Write-Output "Summarized $($sessions.Count) independent primary sessions / $($samples.Count) live updates. Defaults unchanged; review stage attribution and separate gates."
