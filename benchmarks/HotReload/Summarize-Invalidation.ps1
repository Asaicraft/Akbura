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
$heaps = @{}
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
            if (-not $row.PSObject.Properties['Optimization']) {
                $row | Add-Member -NotePropertyName Optimization -NotePropertyValue 'legacy'
            }
            $writer.WriteLine($line)
            if ($row.Kind -eq 'artifact-heap') {
                $key = @($row.Project, $row.File, $row.Scenario, $row.Profiling, $row.BinaryFlavor, $row.Optimization, $row.Index) -join '|'
                $heaps[$key] = $row
            }
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
                BinaryFlavor = $row.BinaryFlavor; Optimization = $row.Optimization; VerifyEveryUpdate = $row.VerifyEveryUpdate
                PostGcHeapBytes = $null; PostGcWorkingSetBytes = $null; ArtifactEntries = $null; IdentitySlots = $null
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
                    BinaryFlavor = $row.BinaryFlavor; Optimization = $row.Optimization
                    Index = $row.Index; First = $row.First; Warmup = $row.Warmup; Stage = $stage.Stage
                    Count = $stage.Count; InclusiveMs = $stage.InclusiveMs; InclusiveCurrentThreadBytes = $stage.InclusiveCurrentThreadBytes
                })
            }
            foreach ($step in $row.Steps.PSObject.Properties) {
                foreach ($reason in $step.Value.PSObject.Properties) {
                    $steps.Add([PSCustomObject]@{
                        Project = $row.Project; File = $row.File; Scenario = $row.Scenario; Profiling = $row.Profiling
                        BinaryFlavor = $row.BinaryFlavor; Optimization = $row.Optimization
                        Index = $row.Index; Stage = $step.Name; Reason = $reason.Name; Count = $reason.Value
                    })
                }
            }
            foreach ($event in $row.Events | Where-Object Kind -eq 'CSharpEnvironmentComparison') {
                foreach ($reason in $event.Reasons) {
                    $causes.Add([PSCustomObject]@{
                        Project = $row.Project; File = $row.File; Scenario = $row.Scenario; Profiling = $row.Profiling
                        BinaryFlavor = $row.BinaryFlavor; Optimization = $row.Optimization
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
foreach ($sample in $samples) {
    $key = @($sample.Project, $sample.File, $sample.Scenario, $sample.Profiling, $sample.BinaryFlavor, $sample.Optimization, $sample.Index) -join '|'
    if ($sample.VerifyEveryUpdate -and -not $heaps.ContainsKey($key)) {
        throw "Missing post-GC measurement: $key"
    }
    if ($heaps.ContainsKey($key)) {
        $sample.PostGcHeapBytes = $heaps[$key].PostGcHeapBytes
        $sample.PostGcWorkingSetBytes = $heaps[$key].WorkingSetBytes
        $sample.ArtifactEntries = $heaps[$key].Entries
        $sample.IdentitySlots = $heaps[$key].IdentitySlots
    }
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

$summaries = foreach ($group in $samples | Group-Object Project, File, Scenario, Profiling, BinaryFlavor, Optimization | Sort-Object Name) {
    $measured = @($group.Group | Where-Object { -not $_.First -and -not $_.Warmup })
    $initial = @($group.Group | Where-Object First)
    $warmups = @($group.Group | Where-Object Warmup)
    if ($measured.Count -ne 8 -or $initial.Count -ne 1 -or $warmups.Count -ne 2) {
        throw "Incomplete sampling in $($group.Name): expected 1 initial + 2 warm-ups + 8 measured."
    }
    $first = $group.Group[0]
    $checks = @($parity | Where-Object { $_.Project -eq $first.Project -and $_.File -eq $first.File -and $_.Scenario -eq $first.Scenario -and $_.Profiling -eq $first.Profiling -and $_.BinaryFlavor -eq $first.BinaryFlavor -and $_.Optimization -eq $first.Optimization })
    $expectedIndices = if ($first.VerifyEveryUpdate) { @(0..10) } else { @(9, 10) }
    if ($checks.Count -ne $expectedIndices.Count -or @($checks.Index | Sort-Object -Unique).Count -ne $expectedIndices.Count -or
        @($checks | Where-Object { $_.Index -notin $expectedIndices }).Count -gt 0) {
        throw "Expected fresh-driver parity at indices $($expectedIndices -join ', ') for $($group.Name)."
    }
    $values = @($measured.GeneratorMs | Sort-Object)
    $groupStages = @($stages | Where-Object { $_.Project -eq $first.Project -and $_.File -eq $first.File -and
        $_.Scenario -eq $first.Scenario -and $_.Profiling -eq $first.Profiling -and $_.BinaryFlavor -eq $first.BinaryFlavor -and
        $_.Optimization -eq $first.Optimization -and $_.Index -in 3..10 })
    [PSCustomObject]@{
        Project = $first.Project; File = $first.File; Scenario = $first.Scenario; Profiling = $first.Profiling
        BinaryFlavor = $first.BinaryFlavor; Optimization = $first.Optimization
        Count = $values.Count; InitialMs = $initial[0].GeneratorMs; MedianMs = Median $values
        MinMs = $values[0]; MaxMs = $values[-1]; P95Ms = $values[[int][Math]::Ceiling(0.95 * $values.Count) - 1]
        MedianProcessAllocatedBytes = Median $measured.ProcessAllocatedBytes
        MedianCurrentThreadAllocatedBytes = Median $measured.CurrentThreadAllocatedBytes
        MedianComponentsGenerated = Median $measured.ComponentGenerated
        MedianComponentsReused = Median $measured.ComponentReused
        MedianAkcssGenerated = Median $measured.AkcssGenerated
        MedianDiagnosticsEvaluated = Median $measured.DiagnosticDocumentEvaluated
        MedianChangedSources = Median $measured.ChangedSources
        MedianPostGcHeapBytes = Median $measured.PostGcHeapBytes
        MedianWorkingSetBytes = Median $measured.PostGcWorkingSetBytes
        MedianArtifactHits = Median $measured.CrossCompilationArtifactHit
        MedianArtifactFallbacks = Median $measured.ArtifactValidationFallback
        MedianDetachedDiagnosticsReused = Median $measured.DetachedDiagnosticsReused
        MedianSemanticModels = Median $measured.SemanticModelCreated
        MedianProbeCompilations = Median $measured.ProbeCompilationCreated
        MedianProbeSemanticModels = Median $measured.ProbeSemanticModelAcquired
        MedianDiagnosticQueries = Median $measured.ProbeDiagnosticQuery
        MedianGeneratedTexts = Median $measured.GeneratedSourceTextCreated
        MedianDetachedTextsCreated = Median $measured.DetachedSourceTextCreated
        MedianSemanticPreparationMs = Median @($groupStages | Where-Object Stage -eq 'SemanticBinding' | ForEach-Object InclusiveMs)
        MedianSemanticPreparationCount = Median @($groupStages | Where-Object Stage -eq 'SemanticBinding' | ForEach-Object Count)
        MedianEmissionMs = Median @($groupStages | Where-Object Stage -eq 'CSharpEmission' | ForEach-Object InclusiveMs)
        MedianEmissionCount = Median @($groupStages | Where-Object Stage -eq 'CSharpEmission' | ForEach-Object Count)
        MedianArtifactValidationMs = Median @($groupStages | Where-Object Stage -eq 'ArtifactValidation' | ForEach-Object InclusiveMs)
        MedianArtifactCaptureMs = Median @($groupStages | Where-Object Stage -eq 'ArtifactCapture' | ForEach-Object InclusiveMs)
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
    Group-Object Project, File, Scenario, Profiling, BinaryFlavor, Optimization, Stage | Sort-Object Name) {
    $first = $group.Group[0]
    $values = [ordered]@{
        Project = $first.Project; File = $first.File; Scenario = $first.Scenario; Profiling = $first.Profiling
        BinaryFlavor = $first.BinaryFlavor; Optimization = $first.Optimization; Stage = $first.Stage
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
$memorySamples = [Collections.Generic.List[object]]::new()
$memoryResults = [Collections.Generic.List[object]]::new()
$concurrent = [Collections.Generic.List[object]]::new()
$concurrentStages = [Collections.Generic.List[object]]::new()
$memoryFiles = @(Get-ChildItem -LiteralPath $InputDirectory -Filter 'artifact-memory-*.jsonl' -File | Sort-Object Name)
if ($memoryFiles.Count -gt 0) {
    $memoryStream = [IO.File]::Create((Join-Path $ResultDirectory 'artifact-memory-traces.jsonl.gz'))
    $memoryGzip = [IO.Compression.GZipStream]::new($memoryStream, [IO.Compression.CompressionLevel]::SmallestSize)
    $memoryWriter = [IO.StreamWriter]::new($memoryGzip, [Text.UTF8Encoding]::new($false))
    try {
        foreach ($file in $memoryFiles) {
            $rows = @([IO.File]::ReadLines($file.FullName) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
                $memoryWriter.WriteLine($_)
                $_ | ConvertFrom-Json
            })
            $final = @($rows | Where-Object Kind -eq 'artifact-memory-result')
            if ($final.Count -ne 1) {
                throw "Missing completed lifetime proof: $($file.Name)"
            }
            foreach ($property in $final[0].Objects.PSObject.Properties) {
                $limit = if ($property.Name -eq 'DetachedSnapshot' -and $final[0].Optimization -eq 'enabled') { 1 } else { 0 }
                if ($property.Value.Alive -gt $limit) {
                    throw "Unexpected retained $($property.Name) in $($file.Name)"
                }
            }
            $observations = @($rows | Where-Object Kind -eq 'artifact-memory-sample')
            if ($observations.Count -ne $final[0].Revisions + 1) {
                throw "Missing lifetime samples in $($file.Name)"
            }
            foreach ($row in $observations) {
                $row.CumulativeCollections = $row.CumulativeCollections -join '/'
                $row.CollectionsSinceUpdateStartIncludingOracle = $row.CollectionsSinceUpdateStartIncludingOracle -join '/'
                $memorySamples.Add($row)
            }
            $memoryResults.Add($final[0])
            foreach ($row in $rows | Where-Object Kind -eq 'artifact-concurrent') {
                $row.BranchCompletionMs = @($row.BranchCompletionMs | ForEach-Object { [Convert]::ToString($_, $culture) }) -join '/'
                $row.BranchGeneratorMs = @($row.BranchGeneratorMs | ForEach-Object { [Convert]::ToString($_, $culture) }) -join '/'
                $row.Collections = $row.Collections -join '/'
                foreach ($counter in $row.Counters.PSObject.Properties) {
                    $row | Add-Member -NotePropertyName $counter.Name -NotePropertyValue $counter.Value
                }
                foreach ($stage in $row.Stages) {
                    $concurrentStages.Add([PSCustomObject]@{ Project = $row.Project; Optimization = $row.Optimization
                        Stage = $stage.Stage; Count = $stage.Count; InclusiveMs = $stage.InclusiveMs })
                }
                $row.PSObject.Properties.Remove('Counters')
                $row.PSObject.Properties.Remove('Stages')
                $concurrent.Add($row)
            }
        }
    }
    finally {
        $memoryWriter.Dispose()
        $memoryGzip.Dispose()
        $memoryStream.Dispose()
    }
    Save-Csv $memorySamples 'artifact-memory-samples.csv'
    Save-Csv $concurrent 'artifact-concurrent.csv'
    Save-Csv $concurrentStages 'artifact-concurrent-stages.csv'
    [IO.File]::WriteAllText((Join-Path $ResultDirectory 'artifact-memory-results.json'),
        (ConvertTo-Json -InputObject @($memoryResults) -Depth 12) + "`r`n", [Text.UTF8Encoding]::new($false))
}
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
    $table += '| ' + $row.Project + ' / ' + $row.File + ' | ' + $row.Scenario + ' | ' + $row.BinaryFlavor + ' / ' + $row.Profiling + ' / ' + $row.Optimization + ' | ' +
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
