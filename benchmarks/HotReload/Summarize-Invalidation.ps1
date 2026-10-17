#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$ResultDirectory
)

$ErrorActionPreference = 'Stop'
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
if ($InputDirectory -eq $ResultDirectory) {
    throw 'Keep raw runs and portable summaries in separate directories.'
}
$null = New-Item -ItemType Directory -Path $ResultDirectory -Force
$culture = [Globalization.CultureInfo]::InvariantCulture
$samples = [Collections.Generic.List[object]]::new()
$stages = [Collections.Generic.List[object]]::new()
$steps = [Collections.Generic.List[object]]::new()
$causes = [Collections.Generic.List[object]]::new()
$parity = [Collections.Generic.List[object]]::new()
$provenance = [Collections.Generic.List[object]]::new()
$rawPath = Join-Path $ResultDirectory 'invalidation-traces.jsonl.gz'
$stream = [IO.File]::Create($rawPath)
$gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionLevel]::SmallestSize)
$writer = [IO.StreamWriter]::new($gzip, [Text.UTF8Encoding]::new($false))
try {
    foreach ($file in Get-ChildItem -LiteralPath $InputDirectory -Filter 'invalidation-*.jsonl' -File | Sort-Object Name) {
        foreach ($line in [IO.File]::ReadLines($file.FullName)) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }
            $row = $line | ConvertFrom-Json
            $writer.WriteLine($line)
            if ($row.Kind -eq 'invalidation-provenance') {
                $provenance.Add($row)
            }
            if ($row.Kind -eq 'invalidation-parity') {
                if (-not $row.EqualSources -or -not $row.EqualDiagnostics) {
                    throw "Failed parity in $($file.Name)."
                }
                $parity.Add($row)
            }
            if ($row.Kind -ne 'invalidation') {
                continue
            }
            $sample = [ordered]@{
                Project = $row.Project; File = $row.File; Scenario = $row.Scenario; Profiling = $row.Profiling
                BinaryFlavor = $row.BinaryFlavor
                Index = $row.Index; First = $row.First; Warmup = $row.Warmup; GeneratorMs = $row.GeneratorMs
                OutsideDocumentBatchMs = $row.OutsideDocumentBatchMs
                ExpectedIncompleteInput = $row.ExpectedIncompleteInput; GeneratorErrors = $row.GeneratorErrors
                ProcessCpuMs = $row.ProcessCpuMs; Gen0Collections = $row.Collections[0]; Gen1Collections = $row.Collections[1]; Gen2Collections = $row.Collections[2]
                CurrentThreadAllocatedBytes = $row.CurrentThreadAllocatedBytes; ProcessAllocatedBytes = $row.ProcessAllocatedBytes
                ManagedHeapBytes = $row.ManagedHeapBytes; WorkingSetBytes = $row.WorkingSetBytes
                Sources = $row.Sources; ChangedSources = @($row.ChangedSources).Count
                InputFingerprintsChanged = @($row.InputDeltas | Where-Object { $_.PreviousObserved -and @($_.Changed).Count -gt 0 }).Count
                SourceReadFingerprintsChanged = @($row.SourceReadDeltas | Where-Object Changed -eq $true).Count
                DocumentVersionFingerprintsChanged = @($row.DocumentVersionDeltas | Where-Object { $_.PreviousObserved -and @($_.Changed).Count -gt 0 }).Count
                DependencyFingerprintsChanged = @($row.DependencyDeltas | Where-Object DependenciesChanged -eq $true).Count
                InvalidAllocationSamples = $row.InvalidAllocationSamples
                StateChanged = $row.ContextDeltas.State
                PreviousSnapshotUnavailable = @($row.Events | Where-Object { $_.Kind -eq 'ProjectContext' -and $_.Decision -eq 'NoPreviousSnapshot' }).Count
                EqualOutputComparisons = @($row.Events | Where-Object { $_.Kind -eq 'OutputComparison' -and $_.Decision -eq 'Equal' }).Count
            }
            if ($row.Counters) {
                foreach ($counter in $row.Counters.PSObject.Properties) {
                    $sample[$counter.Name] = $counter.Value
                }
            }
            $samples.Add([PSCustomObject]$sample)
            foreach ($stage in $row.Stages) {
                $stages.Add([PSCustomObject]@{
                    Project = $row.Project; File = $row.File; Scenario = $row.Scenario; Profiling = $row.Profiling
                    BinaryFlavor = $row.BinaryFlavor
                    Index = $row.Index; First = $row.First; Warmup = $row.Warmup; Stage = $stage.Stage
                    Count = $stage.Count; InclusiveMs = $stage.InclusiveMs; InclusiveCurrentThreadBytes = $stage.InclusiveCurrentThreadBytes
                })
            }
            foreach ($step in $row.Steps.PSObject.Properties) {
                foreach ($reason in $step.Value.PSObject.Properties) {
                    $steps.Add([PSCustomObject]@{
                        Project = $row.Project; File = $row.File; Scenario = $row.Scenario; Profiling = $row.Profiling
                        BinaryFlavor = $row.BinaryFlavor
                        Index = $row.Index; Stage = $step.Name; Reason = $reason.Name; Count = $reason.Value
                    })
                }
            }
            foreach ($event in $row.Events | Where-Object Kind -eq 'CSharpEnvironmentComparison') {
                foreach ($reason in $event.Reasons) {
                    $causes.Add([PSCustomObject]@{
                        Project = $row.Project; File = $row.File; Scenario = $row.Scenario; Profiling = $row.Profiling
                        BinaryFlavor = $row.BinaryFlavor
                        Index = $row.Index; Reason = $reason
                    })
                }
            }
        }
    }
}
finally {
    $writer.Dispose()
    $gzip.Dispose()
    $stream.Dispose()
}
if ($samples.Count -eq 0) {
    throw 'No invalidation measurements found.'
}

function Median($Values) {
    $ordered = @($Values | Where-Object { $null -ne $_ } | Sort-Object)
    if ($ordered.Count -eq 0) {
        return $null
    }
    return ($ordered[[int][Math]::Floor(($ordered.Count - 1) / 2)] + $ordered[[int][Math]::Floor($ordered.Count / 2)]) / 2
}

function Save-Csv($Rows, [string]$Name) {
    $columns = @($Rows | ForEach-Object { $_.PSObject.Properties.Name } | Select-Object -Unique)
    $normalized = foreach ($row in $Rows) {
        $values = [ordered]@{}
        foreach ($column in $columns) {
            $value = $row.$column
            $values[$column] = if ($value -is [double] -or $value -is [single] -or $value -is [decimal]) {
                [Convert]::ToString($value, $culture)
            }
            else {
                $value
            }
        }
        [PSCustomObject]$values
    }
    $csv = @($normalized | ConvertTo-Csv -NoTypeInformation) -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $ResultDirectory $Name), $csv + "`r`n", [Text.UTF8Encoding]::new($false))
}

function Count-Cell($Value) {
    if ($null -eq $Value) {
        return 'not measured'
    }
    return [Convert]::ToString($Value, $culture)
}

$summaries = foreach ($group in $samples | Group-Object Project, File, Scenario, Profiling, BinaryFlavor | Sort-Object Name) {
    $measured = @($group.Group | Where-Object { -not $_.First -and -not $_.Warmup })
    $initial = @($group.Group | Where-Object First)
    $warmups = @($group.Group | Where-Object Warmup)
    if ($measured.Count -ne 8 -or $initial.Count -ne 1 -or $warmups.Count -ne 2) {
        throw "Incomplete sampling in $($group.Name): expected 1 initial + 2 warm-ups + 8 measured."
    }
    $first = $group.Group[0]
    $checks = @($parity | Where-Object { $_.Project -eq $first.Project -and $_.File -eq $first.File -and $_.Scenario -eq $first.Scenario -and $_.Profiling -eq $first.Profiling -and $_.BinaryFlavor -eq $first.BinaryFlavor })
    if ($checks.Count -ne 2 -or @($checks.Index | Sort-Object -Unique).Count -ne 2 -or
        @($checks | Where-Object { $_.Index -notin @(9, 10) }).Count -gt 0) {
        throw "Expected two final fresh-driver parity checks for $($group.Name)."
    }
    $values = @($measured.GeneratorMs | Sort-Object)
    [PSCustomObject]@{
        Project = $first.Project; File = $first.File; Scenario = $first.Scenario; Profiling = $first.Profiling
        BinaryFlavor = $first.BinaryFlavor
        Count = $values.Count; InitialMs = $initial[0].GeneratorMs; MedianMs = Median $values
        MinMs = $values[0]; MaxMs = $values[-1]; P95Ms = $values[[int][Math]::Ceiling(0.95 * $values.Count) - 1]
        MedianProcessAllocatedBytes = Median $measured.ProcessAllocatedBytes
        MedianCurrentThreadAllocatedBytes = Median $measured.CurrentThreadAllocatedBytes
        MedianComponentsGenerated = Median $measured.ComponentGenerated
        MedianComponentsReused = Median $measured.ComponentReused
        MedianAkcssGenerated = Median $measured.AkcssGenerated
        MedianDiagnosticsEvaluated = Median $measured.DiagnosticDocumentEvaluated
        MedianChangedSources = Median $measured.ChangedSources
    }
}
Save-Csv $samples 'invalidation-samples.csv'
Save-Csv $stages 'invalidation-stages.csv'
Save-Csv $steps 'invalidation-steps.csv'
Save-Csv $causes 'invalidation-causes.csv'
Save-Csv $summaries 'invalidation-summary.csv'
$trackedBoundaries = @('BlackSilence.CSharpEnvironment', 'BlackSilence.ProjectState', 'BlackSilence.GenerationRequests',
    'BlackSilence.GeneratedBatch', 'BlackSilence.GeneratedComponents', 'BlackSilence.GeneratedExternalAkcss',
    'BlackSilence.GeneratedInlineAkcss', 'BlackSilence.GeneratedProjectSources', 'BlackSilence.Diagnostics', 'SourceOutput')
$transitions = foreach ($group in $steps | Where-Object { $_.Index -in 3..10 -and $_.Stage -in $trackedBoundaries } |
    Group-Object Project, File, Scenario, Profiling, BinaryFlavor, Stage | Sort-Object Name) {
    $first = $group.Group[0]
    $values = [ordered]@{
        Project = $first.Project; File = $first.File; Scenario = $first.Scenario; Profiling = $first.Profiling
        BinaryFlavor = $first.BinaryFlavor; Stage = $first.Stage
    }
    foreach ($reason in @('Cached', 'Unchanged', 'Modified', 'New', 'Removed')) {
        $counts = foreach ($index in 3..10) {
            [long]($group.Group | Where-Object { $_.Index -eq $index -and $_.Reason -eq $reason } | Measure-Object Count -Sum).Sum
        }
        $range = $counts | Measure-Object -Minimum -Maximum
        $values["${reason}Min"] = $range.Minimum
        $values["${reason}Max"] = $range.Maximum
    }
    $values['TransformExecution'] = if ($values.UnchangedMax + $values.ModifiedMax + $values.NewMax -gt 0) {
        if ($values.CachedMax -gt 0) { 'Mixed' } else { 'Executed' }
    }
    elseif ($values.CachedMax -gt 0) { 'Skipped' }
    else { 'No retained output' }
    [PSCustomObject]$values
}
Save-Csv $transitions 'invalidation-transitions.csv'
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'invalidation-provenance.json'), ($provenance | ConvertTo-Json -Depth 10) + "`r`n", [Text.UTF8Encoding]::new($false))
if (Test-Path -LiteralPath (Join-Path $InputDirectory 'invalidation-environment.json')) {
    Copy-Item -LiteralPath (Join-Path $InputDirectory 'invalidation-environment.json') -Destination $ResultDirectory -Force
}
foreach ($name in @('harness-update.json', 'control-order.json')) {
    if (Test-Path -LiteralPath (Join-Path $InputDirectory $name)) {
        Copy-Item -LiteralPath (Join-Path $InputDirectory $name) -Destination $ResultDirectory -Force
    }
}
if (Test-Path -LiteralPath (Join-Path $InputDirectory 'purity-source.json')) {
    $tutorial = Get-Content -LiteralPath (Join-Path $InputDirectory 'purity-source.json') -Raw | ConvertFrom-Json
    $portableTutorial = @{ Repository = $tutorial.Repository; Revision = $tutorial.Revision }
    [IO.File]::WriteAllText((Join-Path $ResultDirectory 'purity-source.json'),
        ($portableTutorial | ConvertTo-Json) + "`r`n", [Text.UTF8Encoding]::new($false))
}

$table = @(
    '| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |'
    '| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |'
)
foreach ($row in $summaries) {
    $table += '| ' + $row.Project + ' / ' + $row.File + ' | ' + $row.Scenario + ' | ' + $row.BinaryFlavor + ' / ' + $row.Profiling + ' | ' +
        $row.InitialMs.ToString('F2', $culture) + ' | ' + $row.MedianMs.ToString('F2', $culture) + ' | ' +
        $row.MinMs.ToString('F2', $culture) + ' - ' + $row.MaxMs.ToString('F2', $culture) + ' | ' +
        $row.P95Ms.ToString('F2', $culture) + ' | ' + (Count-Cell $row.MedianComponentsGenerated) + ' / ' + (Count-Cell $row.MedianComponentsReused) + ' | ' +
        (Count-Cell $row.MedianDiagnosticsEvaluated) + ' | ' + $row.MedianChangedSources + ' |'
}
$content = @(
    '<!-- BEGIN GENERATED: invalidation -->'
    "Recorded $($samples.Count) driver runs; $($summaries.Count) workload/mode groups; $($parity.Count) fresh-driver parity checks. Each group has two warm-ups and eight measured samples."
    ''
    ($table -join "`r`n")
    ''
    'All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.'
    '<!-- END GENERATED: invalidation -->'
) -join "`r`n"
$report = Join-Path $ResultDirectory 'README.md'
if (Test-Path -LiteralPath $report) {
    $text = [IO.File]::ReadAllText($report)
    $pattern = '(?s)<!-- BEGIN GENERATED: invalidation -->.*?<!-- END GENERATED: invalidation -->'
    if ([regex]::Matches($text, $pattern).Count -ne 1) {
        throw 'Expected exactly one existing invalidation report block; manual text was not replaced.'
    }
    $text = [regex]::Replace($text, $pattern, [Text.RegularExpressions.MatchEvaluator]{ param($match) $content })
}
else {
    $investigation = [IO.Path]::GetRelativePath($ResultDirectory,
        [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../For Contributors/csharp-invalidation-investigation.md'))).Replace('\', '/').Replace(' ', '%20')
    $text = "# C# invalidation measurements`r`n`r`nSee the [engineering investigation]($investigation) for methodology, environment, interpretation and limitations.`r`n`r`n$content`r`n"
}
$text = $text.Replace("`r`n", "`n").Replace("`n", "`r`n")
[IO.File]::WriteAllText($report, $text.TrimEnd("`r", "`n") + "`r`n", [Text.UTF8Encoding]::new($false))
Write-Host "Wrote $($samples.Count) observations to $ResultDirectory"
