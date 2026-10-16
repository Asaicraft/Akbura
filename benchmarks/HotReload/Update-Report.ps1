param(
    [string]$ResultDirectory = (Join-Path $PSScriptRoot 'results/local')
)

$ErrorActionPreference = 'Stop'
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$reportPath = Join-Path $ResultDirectory 'README.md'
$culture = [Globalization.CultureInfo]::InvariantCulture
$missing = [string][char]0x2014
$rangeSeparator = [string][char]0x2013

function Read-Table([string]$Name) {
    $path = Join-Path $ResultDirectory $Name
    if (Test-Path -LiteralPath $path) {
        Import-Csv -LiteralPath $path
    }
}

function Number($Value) {
    return [double]::Parse([string]$Value, $culture)
}

function Format-Number($Value, [int]$Digits = 2, [double]$Divisor = 1) {
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $missing
    }
    return ((Number $Value) / $Divisor).ToString("F$Digits", $culture)
}

function Cell($Value) {
    return ([string]$Value).Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('|', '&#124;').Replace('`', '&#96;').Replace("`r", ' ').Replace("`n", ' ')
}

function Median($Values) {
    $sorted = @($Values | ForEach-Object { Number $_ } | Sort-Object)
    if ($sorted.Count -eq 0) {
        return $null
    }
    return ($sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)] + $sorted[[int][Math]::Floor($sorted.Count / 2)]) / 2
}

function Count-Range($Values) {
    $unique = @($Values | ForEach-Object { Number $_ } | Sort-Object -Unique)
    if ($unique.Count -eq 0) {
        return $missing
    }
    if ($unique.Count -eq 1) {
        return Format-Number $unique[0] 0
    }
    return (Format-Number $unique[0] 0) + $rangeSeparator + (Format-Number $unique[-1] 0)
}

function Measured($Items) {
    $Items | Where-Object { [string]$_.Warmup -eq 'False' }
}

function Summary([string]$Kind, [string]$Scenario, [string]$Metric, [int]$ExpectedCount) {
    $matches = @($summaries | Where-Object { $_.Kind -eq $Kind -and $_.Scenario -eq $Scenario -and $_.Metric -eq $Metric })
    if ($matches.Count -gt 1) {
        throw "Duplicate summary for $Kind / $Scenario / $Metric."
    }
    if ($matches.Count -eq 1) {
        if ([int]$matches[0].Count -ne $ExpectedCount) {
            throw "Sample/summary count mismatch for $Kind / $Scenario / $Metric."
        }
        return $matches[0]
    }
    return $null
}

function Table([string[]]$Header, $Rows) {
    if (@($Rows).Count -eq 0) {
        return '_No measurements recorded for this section._'
    }
    $lines = @('| ' + ($Header -join ' | ') + ' |')
    $lines += '| ' + (($Header | ForEach-Object { '---' }) -join ' | ') + ' |'
    $lines += $Rows
    return $lines -join "`r`n"
}

$samples = @(Read-Table 'samples.csv')
$summaries = @(Read-Table 'summary.csv')
$startup = @(Read-Table 'startup.csv')
$events = @(Read-Table 'watch-events.csv')
$graph = @(Read-Table 'graph.csv')
$tasks = @(Read-Table 'build-tasks.csv')
$validation = @(Read-Table 'validation.csv')
if (($samples.Count -eq 0 -and $graph.Count -eq 0) -or ($samples.Count -gt 0 -and $summaries.Count -eq 0)) {
    throw 'Recorded observations and their summaries are required; graph-only results are also supported.'
}
$blocks = [ordered]@{}

$tableRows = @()
$watchGroups = @($samples | Where-Object Kind -eq 'watch' | Group-Object Scenario | Sort-Object Name)
foreach ($group in $watchGroups) {
    $measured = @(Measured $group.Group)
    $stats = Summary 'watch' $group.Name 'TotalMs' $measured.Count
    $first = @($group.Group | Sort-Object { [int]$_.Index })[0]
    $started = @($startup | Where-Object { $_.Kind -eq 'watch-startup' -and $_.Scenario -eq $group.Name })
    if ($started.Count -gt 1) {
        throw "Duplicate watch startup for $($group.Name)."
    }
    $startupMs = if ($started.Count -eq 1) { $started[0].Ms } else { $null }
    $tableRows += '| ' + (Cell $group.Name) + ' | ' + (Format-Number $startupMs 3 1000) + ' | ' +
        (Format-Number $first.TotalMs 3 1000) + ' | ' + $measured.Count + ' | ' + (Format-Number $stats.MedianMs 3 1000) + ' | ' +
        (Format-Number $stats.MinMs 3 1000) + $rangeSeparator + (Format-Number $stats.MaxMs 3 1000) + ' | ' + (Format-Number $stats.P95Ms 3 1000) + ' |'
}
$blocks['watch'] = Table @('Scenario', 'Startup (s)', 'First recorded edit (s)', 'Measured n', 'Repeat median (s)', 'Repeat range (s)', 'Repeat p95 (s)') $tableRows

$tableRows = @()
foreach ($group in $watchGroups) {
    $ordered = @($group.Group | Sort-Object { [int]$_.Index })
    $sends = @($events | Where-Object { $_.Scenario -eq $group.Name -and $_.Text -match 'Sending update batch' })
    $before = @()
    $after = @()
    if ($sends.Count -eq $ordered.Count) {
        for ($index = 0; $index -lt $ordered.Count; $index++) {
            if ([string]$ordered[$index].Warmup -ne 'False') {
                continue
            }
            $difference = (Number $ordered[$index].TotalMs) - (Number $sends[$index].Ms)
            if ($difference -lt 0) {
                throw "Send timestamp follows UI observation in $($group.Name)."
            }
            $before += $sends[$index].Ms
            $after += $difference
        }
    }
    $tableRows += '| ' + (Cell $group.Name) + ' | ' + $before.Count + ' | ' + (Format-Number (Median $before)) + ' | ' + (Format-Number (Median $after)) + ' |'
}
$blocks['watch-boundary'] = (Table @('Scenario', 'Matched measured n', 'Before send message, median (ms)', 'After message to UI, median (ms)') $tableRows) +
    "`r`n`r`nMissing or unmatched send logs are shown as $missing; no boundary is inferred from independent runs."

$tableRows = @()
foreach ($group in $samples | Where-Object Kind -eq 'generator' | Group-Object Log | Sort-Object Name) {
    $measured = @(Measured $group.Group)
    $sample = $group.Group[0]
    $stats = Summary 'generator' $sample.Scenario 'GeneratorMs' $measured.Count
    $initial = @($group.Group | Where-Object { [string]$_.Index -eq '0' })
    if ($initial.Count -gt 1) {
        throw "Duplicate initial generator pass in $($group.Name)."
    }
    $initialMs = if ($initial.Count -eq 1) { $initial[0].GeneratorMs } else { $null }
    $project = if ($group.Name -like 'generator-gallery-*') { 'Gallery' } elseif ($group.Name -like 'generator-purity-*') { 'Purity' } else { [IO.Path]::GetFileNameWithoutExtension($group.Name) }
    $tableRows += '| ' + (Cell "$project / $($sample.Scenario)") + ' | ' + (Format-Number $initialMs) + ' | ' + $measured.Count + ' | ' +
        (Format-Number $stats.MedianMs) + ' | ' + (Format-Number $stats.P95Ms) + ' | ' +
        (Count-Range $measured.ChangedSources) + '/' + (Count-Range $measured.GeneratedSources) + ' |'
}
$blocks['generator'] = Table @('Project / scenario', 'Initial pass (ms)', 'Measured n', 'Median (ms)', 'p95 (ms)', 'Text changed / total') $tableRows

$tableRows = @()
foreach ($group in (Measured $graph) | Group-Object Scenario | Sort-Object Name) {
    $tableRows += '| ' + (Cell $group.Name) + ' | ' + $group.Count + ' | ' + (Count-Range $group.Group.Components) + ' | ' +
        (Count-Range $group.Group.RecomputeComponents) + ' | ' + (Count-Range $group.Group.ReuseComponents) + ' | ' +
        (Count-Range $group.Group.RecomputeDiagnostics) + ' | ' +
        (Count-Range @($group.Group | ForEach-Object { (Number $_.ExternalAkcss) + (Number $_.InlineAkcss) })) + ' |'
}
$blocks['graph'] = Table @('Scenario', 'Measured n', 'Components', 'Recomputed', 'Reused', 'Diagnostic documents recomputed', 'AKCSS modules recomputed') $tableRows

$tableRows = @()
foreach ($group in $samples | Where-Object Kind -eq 'runtime' | Group-Object Scenario | Sort-Object Name) {
    $measured = @(Measured $group.Group)
    $cells = @((Cell $group.Name), [string]$measured.Count)
    foreach ($metric in @('GeneratorMs', 'ValidationMs', 'DeltaMs', 'HandlerMs', 'TotalMs')) {
        $stats = Summary 'runtime' $group.Name $metric $measured.Count
        $cells += Format-Number $stats.MedianMs
    }
    $total = Summary 'runtime' $group.Name 'TotalMs' $measured.Count
    $cells += Format-Number $total.P95Ms
    $tableRows += '| ' + ($cells -join ' | ') + ' |'
}
$blocks['runtime'] = Table @('Scenario', 'Measured n', 'Generator (ms)', 'Validation (ms)', 'Delta + CLR (ms)', 'Handlers (ms)', 'Total (ms)', 'Total p95 (ms)') $tableRows

$tableRows = @()
foreach ($group in $samples | Where-Object Kind -eq 'build-wall' | Group-Object { $_.Scenario -replace '-noop-\d+$', '-noop' } | Sort-Object Name) {
    $values = @($group.Group | ForEach-Object { Number $_.TotalMs } | Sort-Object)
    $names = @($group.Group.Scenario)
    $recorded = @($tasks | Where-Object { $_.Scenario -in $names })
    $recordedNames = @($recorded.Scenario | Sort-Object -Unique)
    $completeEvidence = @($names | Where-Object { $_ -notin $recordedNames }).Count -eq 0
    $csc = @($recorded | Where-Object Task -eq 'Csc').Count
    $manifest = @($recorded | Where-Object Task -eq 'GenerateAkburaModuleManifest').Count
    $cscCount = if ($completeEvidence) { [string]$csc } else { $missing }
    $manifestCount = if ($completeEvidence) { [string]$manifest } else { $missing }
    $tableRows += '| ' + (Cell $group.Name) + ' | ' + $group.Count + ' | ' + (Format-Number (Median $values) 3 1000) + ' | ' +
        (Format-Number $values[0] 3 1000) + $rangeSeparator + (Format-Number $values[-1] 3 1000) + ' | ' + $cscCount + ' | ' + $manifestCount + ' |'
}
$blocks['builds'] = (Table @('Condition', 'n', 'CLI median (s)', 'CLI range (s)', 'Csc task events', 'Manifest generation task events') $tableRows) +
    "`r`n`r`nTask event counts span all builds in the row; $missing means task evidence was not recorded."

$counts = @()
foreach ($kind in @('runtime', 'generator', 'watch', 'build-wall')) {
    $all = @($samples | Where-Object Kind -eq $kind)
    $counts += '| ' + $kind + ' | ' + $all.Count + ' | ' + @(Measured $all).Count + ' |'
}
$blocks['coverage'] = Table @('Series', 'All observations', 'Measured observations') $counts
$parity = @($validation | Where-Object Kind -eq 'generator-parity')
$passed = @($parity | Where-Object Equal -eq 'True').Count
$parityStatus = if ($parity.Count -gt 0) { "$passed/$($parity.Count) passed" } else { 'not recorded' }
$blocks['coverage'] += "`r`n`r`nPrimary observations: $($samples.Count). Graph observations: $($graph.Count). Fresh-driver parity: $parityStatus. Counts do not imply complete scenario coverage."

$sourcePath = Join-Path $ResultDirectory 'purity-source.json'
$blocks['provenance'] = '_No tutorial revision metadata was recorded for this run._'
if (Test-Path -LiteralPath $sourcePath) {
    $source = [IO.File]::ReadAllText($sourcePath) | ConvertFrom-Json
    $blocks['provenance'] = Table @('Tutorial repository', 'Revision') @('| ' + (Cell $source.Repository) + ' | ' + (Cell $source.Revision) + ' |')
}

$exists = [IO.File]::Exists($reportPath)
if ($exists) {
    $original = [IO.File]::ReadAllText($reportPath)
}
else {
    $original = @'
# Hot Reload timing measurements

## Environment and methodology

_Describe the machine, SDK, build configuration, sampling policy and limitations manually. Review these notes after every run._

<!-- BEGIN GENERATED: provenance -->
<!-- END GENERATED: provenance -->

## Real dotnet watch

<!-- BEGIN GENERATED: watch -->
<!-- END GENERATED: watch -->

<!-- BEGIN GENERATED: watch-boundary -->
<!-- END GENERATED: watch-boundary -->

## BlackSilence

<!-- BEGIN GENERATED: generator -->
<!-- END GENERATED: generator -->

## Dependency graph decisions

<!-- BEGIN GENERATED: graph -->
<!-- END GENERATED: graph -->

## CLR integration

<!-- BEGIN GENERATED: runtime -->
<!-- END GENERATED: runtime -->

## Builds

<!-- BEGIN GENERATED: builds -->
<!-- END GENERATED: builds -->

## Recorded observations

<!-- BEGIN GENERATED: coverage -->
<!-- END GENERATED: coverage -->

## Analysis and limitations

_Write conclusions manually. Distinguish first edits from repeated edits, missing data from zero cost, and full rebuilds from CLR updates. Do not combine independent measurements into one trace._

Tables are generated from the CSV artifacts by `Update-Report.ps1`. Only marked blocks are replaced. Interpretations and environment notes outside them require manual review; generation does not prove a complete or successful benchmark run.
'@
}
$markerPattern = '<!-- (BEGIN|END) GENERATED: ([a-z-]+) -->'
$markers = @([regex]::Matches($original, $markerPattern))
if ($markers.Count -eq 0) {
    throw 'Existing README.md has no generated blocks. Add BEGIN/END GENERATED markers before updating it.'
}
$updated = $original
$openBlock = $null
foreach ($marker in $markers) {
    $id = $marker.Groups[2].Value
    if (-not $blocks.Contains($id)) {
        throw "Unknown generated block: $id."
    }
    if ($marker.Groups[1].Value -eq 'BEGIN') {
        if ($null -ne $openBlock) {
            throw 'Generated blocks must not be nested or overlapping.'
        }
        $openBlock = $id
    }
    else {
        if ($openBlock -ne $id) {
            throw "Unmatched generated block end: $id."
        }
        $openBlock = $null
    }
}
if ($null -ne $openBlock) {
    throw "Missing generated block end: $openBlock."
}
foreach ($id in $blocks.Keys) {
    $begin = "<!-- BEGIN GENERATED: $id -->"
    $end = "<!-- END GENERATED: $id -->"
    $begins = [regex]::Matches($original, [regex]::Escape($begin)).Count
    $ends = [regex]::Matches($original, [regex]::Escape($end)).Count
    if ($begins -eq 0 -and $ends -eq 0) {
        continue
    }
    $pattern = '(?s)' + [regex]::Escape($begin) + '.*?' + [regex]::Escape($end)
    if ($begins -ne 1 -or $ends -ne 1 -or -not [regex]::IsMatch($original, $pattern)) {
        throw "Generated block $id must have exactly one ordered BEGIN/END pair."
    }
    $replacement = $begin + "`r`n" + $blocks[$id] + "`r`n" + $end
    $updated = [regex]::Replace($updated, $pattern, [Text.RegularExpressions.MatchEvaluator]{ param($match) $replacement })
}
$updated = ($updated -replace '\r?\n', "`r`n").TrimEnd("`r", "`n") + "`r`n"
$temporary = $reportPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary, $updated, [Text.UTF8Encoding]::new($false))
    if ($exists) {
        [IO.File]::Replace($temporary, $reportPath, [NullString]::Value)
    }
    else {
        [IO.File]::Move($temporary, $reportPath)
    }
}
finally {
    if ([IO.File]::Exists($temporary)) {
        [IO.File]::Delete($temporary)
    }
}
Write-Host "Generated report blocks updated in $reportPath. Review the manually written notes and conclusions."
