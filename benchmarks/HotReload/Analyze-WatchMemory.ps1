#requires -Version 7.2
param([Parameter(Mandatory)][string]$ResultDirectory, [Parameter(Mandatory)][string]$SummaryDirectory)
$ErrorActionPreference = 'Stop'
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$SummaryDirectory = [IO.Path]::GetFullPath($SummaryDirectory)
$culture = [Globalization.CultureInfo]::InvariantCulture
function N($value) { [double]::Parse("$value",$culture) }
function Median($values) {
    $sorted = @($values | Sort-Object)
    if (-not $sorted.Count) { return $null }
    if ($sorted.Count % 2) { return $sorted[[int][math]::Floor($sorted.Count / 2)] }
    return ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2
}
function Save-Csv([string]$name, $rows) {
    $values = foreach ($row in $rows) {
        $record = [ordered]@{}
        foreach ($property in $row.PSObject.Properties) { $record[$property.Name] = if ($property.Value -is [double]) { $property.Value.ToString('R',$culture) } else { $property.Value } }
        [pscustomobject]$record
    }
    [IO.File]::WriteAllText((Join-Path $SummaryDirectory $name),((@($values | ConvertTo-Csv -NoTypeInformation) -join "`r`n") + "`r`n"),[Text.UTF8Encoding]::new($false))
}
$status = Get-Content -LiteralPath (Join-Path $SummaryDirectory 'status.json') -Raw | ConvertFrom-Json
if ($status.Phase -ne 'SummaryCompleteNeedsReview' -or -not $status.RawInputsUnchanged -or $status.Sessions -ne 36) { throw 'A complete, verified 36-session summary is required.' }
$summary = @(Import-Csv -LiteralPath (Join-Path $SummaryDirectory 'memory-summary.csv'))
$aggregate = foreach ($group in $summary | Group-Object { $_.Mode + '/' + ($_.Run -replace '^(natural|forced)-\d+-','') + '/' + $_.Role }) {
    if ($group.Count -ne 3) { throw 'Expected three independent sessions for every mode/configuration/process.' }
    $growth = @($group.Group | ForEach-Object { N $_.IncreaseBytes })
    $end = @($group.Group | ForEach-Object { N $_.Revision100Bytes })
    [pscustomobject]@{ Mode=$group.Group[0].Mode; Configuration=($group.Group[0].Run -replace '^(natural|forced)-\d+-',''); Role=$group.Group[0].Role
        Sessions=$group.Count; MedianWarmupBytes=(Median @($group.Group | ForEach-Object { N $_.WarmupBytes })); MedianRevision100Bytes=(Median $end)
        MedianIncreaseBytes=(Median $growth); MinimumIncreaseBytes=($growth | Measure-Object -Minimum).Minimum; MaximumIncreaseBytes=($growth | Measure-Object -Maximum).Maximum
        MinimumRevision100Bytes=($end | Measure-Object -Minimum).Minimum; MaximumRevision100Bytes=($end | Measure-Object -Maximum).Maximum
        MedianEarlySlopeBytesPerRevision=(Median @($group.Group | ForEach-Object { N $_.EarlySlopeBytesPerRevision }))
        MedianLateSlopeBytesPerRevision=(Median @($group.Group | ForEach-Object { N $_.LateSlopeBytesPerRevision })) }
}
Save-Csv 'configuration-summary.csv' $aggregate
$pairs = foreach ($mode in @('natural','forced')) {
    foreach ($configuration in @('observer','release','stats-no-observer')) {
        foreach ($round in 1..3) {
            $baseline = @($summary | Where-Object { $_.Run -eq "$mode-$round-baseline-$configuration" -and $_.Role -eq 'compilerWatch' })
            $combined = @($summary | Where-Object { $_.Run -eq "$mode-$round-combined-$configuration" -and $_.Role -eq 'compilerWatch' })
            if ($baseline.Count -ne 1 -or $combined.Count -ne 1) { throw 'Missing round-matched compiler/watch comparison.' }
            [pscustomobject]@{ Mode=$mode; Configuration=$configuration; Round=$round
                CombinedMinusBaselineEndBytes=(N $combined[0].Revision100Bytes)-(N $baseline[0].Revision100Bytes)
                CombinedMinusBaselineGrowthBytes=(N $combined[0].IncreaseBytes)-(N $baseline[0].IncreaseBytes)
                Meaning='Independent diagnostic sessions paired by round; not an exclusive retained-size or production latency effect' }
        }
    }
}
Save-Csv 'paired-compiler-differences.csv' $pairs
$focusTypes = @('Microsoft.CodeAnalysis.CSharp.CSharpCompilation','Microsoft.CodeAnalysis.Emit.EmitBaseline',
    'Microsoft.CodeAnalysis.EditAndContinue.ProjectBaseline','Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree+ParsedSyntaxTree',
    'Microsoft.CodeAnalysis.CSharp.SyntaxTreeSemanticModel','Microsoft.CodeAnalysis.CSharp.MethodBodySemanticModel',
    'Akbura.Language.Binder.CompilationBoundProbeStore','Akbura.Language.Binder.CompilationBoundProbeStore+Entry',
    'Akbura.Language.Binder.CompilationBoundProbeStore+DocumentGeneration','Akbura.Language.Binder.ReusableProbeBinding',
    'Akbura.Language.Binder.CSharpProbeContext','Akbura.Language.Binder.CSharpBindingResult',
    'Akbura.BlackSilence.BlackSilenceWatchObservation','Akbura.Language.CodeGeneration.GenerationStatisticsSession',
    'System.Threading.AsyncLocal<Akbura.Language.CodeGeneration.GenerationStatisticsSession>','System.String','System.Char[]','System.Byte[]')
$focus = [Collections.Generic.List[object]]::new()
$topGrowth = [Collections.Generic.List[object]]::new()
$totals = [Collections.Generic.List[object]]::new()
foreach ($configuration in @('baseline-observer','combined-observer','baseline-release','combined-release','baseline-stats-no-observer','combined-stats-no-observer')) {
    $maps = @{}
    foreach ($checkpoint in @('02-warmup-0','06-revision-50','08-revision-100')) {
        $profile = Join-Path $ResultDirectory "memory/runs/forced-1-$configuration/$checkpoint-compilerWatch-analysis"
        $map = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        $akburaCount = 0L; $akburaBytes = 0L
        foreach ($type in Import-Csv -LiteralPath (Join-Path $profile 'retained-types.csv')) {
            $map.Add($type.Type,[pscustomobject]@{ Count=[long]$type.RootReachableCount; Bytes=[long]$type.ShallowBytes })
            if ($type.Type.Contains('Akbura.')) { $akburaCount += [long]$type.RootReachableCount; $akburaBytes += [long]$type.ShallowBytes }
        }
        $maps[$checkpoint] = $map
        $graph = Get-Content -LiteralPath (Join-Path $profile 'summary.json') -Raw | ConvertFrom-Json
        $totals.Add([pscustomobject]@{ Configuration=$configuration; Checkpoint=$checkpoint; RootReachableObjects=$graph.RootReachableObjects
            RootReachableShallowBytes=$graph.RootReachableShallowBytes; ManagedModules=$graph.ManagedModules
            AkburaNamedObjects=$akburaCount; AkburaNamedShallowBytes=$akburaBytes
            Meaning='Type-name containment, including generic containers. Shallow bytes only; not transitive or exclusive ownership.' })
        foreach ($name in $focusTypes) {
            $type = if ($map.ContainsKey($name)) { $map[$name] } else { [pscustomobject]@{ Count=0L; Bytes=0L } }
            $focus.Add([pscustomobject]@{ Configuration=$configuration; Checkpoint=$checkpoint; Type=$name; RootReachableCount=$type.Count; ShallowBytes=$type.Bytes })
        }
    }
    $warm = $maps['02-warmup-0']; $end = $maps['08-revision-100']
    $names = [Collections.Generic.HashSet[string]]::new($warm.Keys,[StringComparer]::Ordinal)
    $names.UnionWith($end.Keys)
    $deltas = foreach ($name in $names) {
        $startType = if ($warm.ContainsKey($name)) { $warm[$name] } else { [pscustomobject]@{ Count=0L; Bytes=0L } }
        $endType = if ($end.ContainsKey($name)) { $end[$name] } else { [pscustomobject]@{ Count=0L; Bytes=0L } }
        [pscustomobject]@{ Configuration=$configuration; Type=$name; WarmupCount=$startType.Count; Revision100Count=$endType.Count
            CountIncrease=$endType.Count-$startType.Count; ShallowBytesIncrease=$endType.Bytes-$startType.Bytes }
    }
    foreach ($delta in $deltas | Sort-Object ShallowBytesIncrease -Descending | Select-Object -First 100) { $topGrowth.Add($delta) }
}
Save-Csv 'focused-type-counts.csv' $focus
Save-Csv 'top-type-growth.csv' $topGrowth
Save-Csv 'profile-totals.csv' $totals
$observerBounds = foreach ($run in Get-ChildItem -LiteralPath (Join-Path $ResultDirectory 'memory/runs') -Directory -Filter '*-observer') {
    if ($run.Name.EndsWith('-no-observer')) { continue }
    $records = @([IO.File]::ReadLines((Join-Path $run.FullName 'watch.jsonl')) | ForEach-Object {
        $row = $_ | ConvertFrom-Json
        if ($row.Kind -eq 'semantic-watch-causal' -and $row.Record.Kind -eq 'Generation') { $row.Record }
    })
    if (-not $records.Count) { throw "Missing analyzer-side generation observations: $($run.Name)" }
    $last = $records | Sort-Object { [long]$_.Version } | Select-Object -Last 1
    [pscustomobject]@{ Run=$run.Name; GenerationObservations=$records.Count; LastGenerationVersion=$last.Version
        MaximumSemanticEntries=($records.PublishedSemanticEntries | ForEach-Object { [long]$_ } | Measure-Object -Maximum).Maximum
        MaximumProjectionEntries=($records.PublishedProjectionEntries | ForEach-Object { [long]$_ } | Measure-Object -Maximum).Maximum
        LastOperationRoots=$last.LiveOperationRoots; LastProbeModels=$last.LiveProbeModels; LastProbeCompilations=$last.LiveProbeCompilations; LastProbeTrees=$last.LiveProbeSyntaxTrees
        RecorderRequestedGc=@($records | Where-Object HeapCollectionRequested -eq 'True').Count
        Meaning='Scalar current-snapshot observations, not a substitute for whole-heap retaining paths.' }
}
Save-Csv 'observer-bounds.csv' $observerBounds
$paths = foreach ($configuration in @('baseline-release','combined-release')) {
    $path = Join-Path $ResultDirectory "memory/runs/forced-1-$configuration/08-revision-100-compilerWatch-analysis/root-paths.json"
    foreach ($entry in (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json | Where-Object { $_.Type -in @('Microsoft.CodeAnalysis.Emit.EmitBaseline','Microsoft.CodeAnalysis.CSharp.CSharpCompilation') })) {
        [pscustomobject]@{ Configuration=$configuration; Checkpoint='08-revision-100'; Type=$entry.Type; Chain=$entry.Chain; Truncated=$entry.Truncated
            Meaning='Representative shortest strong-root path from the archived heap traversal. Fields and exclusive retained size are unresolved.' }
    }
}
[IO.File]::WriteAllText((Join-Path $SummaryDirectory 'retaining-path-excerpts.json'),(($paths | ConvertTo-Json -Depth 8).ReplaceLineEndings("`r`n") + "`r`n"),[Text.UTF8Encoding]::new($false))
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Generated memory statistics')
$lines.Add('')
$lines.Add('Three independent 100-edit sessions per row. Values are MiB (1,048,576 bytes). Growth is the median of per-session differences, not the difference between two medians. Ranges are observed minima/maxima, not confidence intervals. These are diagnostic memory measurements, not production latency results.')
foreach ($mode in @('forced','natural')) {
    $lines.Add(''); $lines.Add("## Compiler/watch: $mode GC"); $lines.Add('')
    $lines.Add('| Configuration | Warmup median | Revision 100 median | Growth median | Growth range |')
    $lines.Add('| --- | ---: | ---: | ---: | ---: |')
    foreach ($row in $aggregate | Where-Object { $_.Role -eq 'compilerWatch' -and $_.Mode -eq $mode } | Sort-Object Configuration) {
        $values = @($row.MedianWarmupBytes,$row.MedianRevision100Bytes,$row.MedianIncreaseBytes,$row.MinimumIncreaseBytes,$row.MaximumIncreaseBytes) | ForEach-Object { ($_/1MB).ToString('F2',$culture) }
        $lines.Add("| $($row.Configuration) | $($values[0]) | $($values[1]) | $($values[2]) | $($values[3])-$($values[4]) |")
    }
}
$lines.Add(''); $lines.Add('Forced values are gcdump live-graph bytes. Natural values are last-GC heap less fragmentation and may lag current allocations. Do not pool the two modes. Detailed process curves, independent rounds, early/late slopes and diagnostic counters remain in the CSV files. Type counts are measured only in the first forced-GC round at warmup, revision 50 and revision 100; their absence in another round is not a repeated object-graph validation.')
[IO.File]::WriteAllText((Join-Path $SummaryDirectory 'statistics.md'),(($lines -join "`r`n") + "`r`n"),[Text.UTF8Encoding]::new($false))
$provenance = @{ GeneratedUtc=[DateTime]::UtcNow.ToString('o'); AnalysisSourceSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
    SummaryProvenanceSha256=(Get-FileHash -LiteralPath (Join-Path $SummaryDirectory 'provenance.json')).Hash
    Meaning='Derived statistics and type deltas only; no new measurement, source rewrite or automatic leak attribution.' }
[IO.File]::WriteAllText((Join-Path $SummaryDirectory 'analysis-provenance.json'),(($provenance | ConvertTo-Json).ReplaceLineEndings("`r`n") + "`r`n"),[Text.UTF8Encoding]::new($false))
Write-Output 'Generated matched configuration statistics, round comparisons, focused object counts and top shallow type deltas. Ownership conclusions require reading actual retaining paths.'
