param(
    [string]$InputDirectory = (Join-Path $PSScriptRoot '../../bin/HotReloadBenchmarks'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'results/local')
)

$ErrorActionPreference = 'Stop'
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$rows = @()
$environmentRows = @()
$taskRows = @()
$startupRows = @()
$validationRows = @()
$watchEvents = @()
$graphRows = @()
foreach ($file in Get-ChildItem -LiteralPath $InputDirectory -Filter '*.jsonl' -File | Sort-Object Name) {
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        if (-not $line.StartsWith('{')) {
            continue;
        }
        $item = $line | ConvertFrom-Json
        if ($item.Kind -eq 'environment') {
            $environmentRows += [pscustomobject]@{ Log = $file.Name; Environment = $item }
        }
        if ($item.Kind -eq 'build-task') {
            $taskRows += [pscustomobject]@{ Scenario = $item.Scenario; Task = $item.TaskName; Project = [IO.Path]::GetFileName($item.ProjectFile); Ms = $item.Ms }
        }
        if ($item.Kind -in @('setup', 'watch-startup')) {
            $startupRows += [pscustomobject]@{ Kind = $item.Kind; Scenario = $item.Scenario; Ms = if ($item.Kind -eq 'setup') { $item.initialGeneratorMs } else { $item.Ms }; Nodes = $item.Nodes; Owners = $item.Owners }
        }
        if ($item.Kind -in @('generator-validation', 'generator-parity')) {
            $validationRows += [pscustomobject]@{ Log = $file.Name; Kind = $item.Kind; Scenario = $item.Scenario; Index = $item.Index; BaselineCompilerErrors = if ($null -ne $item.BaselineCompilerErrors) { @($item.BaselineCompilerErrors).Count } else { 0 }; Equal = $item.Equal }
        }
        if ($item.Kind -eq 'watch-log' -and $item.Text -match 'Sending update batch|PROBE_VALUE') {
            $watchEvents += [pscustomobject]@{ Scenario = $item.Scenario; Ms = $item.Ms; Text = $item.Text }
        }
        if ($item.Kind -eq 'graph') {
            $graphRows += [pscustomobject]@{
                Scenario = $item.Scenario; Index = $item.Index; Warmup = $item.Warmup
                Components = $item.Requests.Components.Total; RecomputeComponents = $item.Requests.Components.Recompute
                ReuseComponents = $item.Requests.Components.Reuse; RecomputeDiagnostics = $item.Requests.Diagnostics.Recompute
                ExternalAkcss = $item.Requests.ExternalAkcss.Recompute; InlineAkcss = $item.Requests.InlineAkcss.Recompute
                ChangedSources = @($item.Changed).Count
            }
        }
        if ($item.Kind -in @('runtime', 'generator', 'watch', 'build-wall')) {
            $rows += [pscustomobject][ordered]@{
                Log = $file.Name
                Kind = $item.Kind
                Scenario = $item.Scenario
                Index = $item.Index
                Warmup = [bool]$item.Warmup
                GeneratorMs = $item.GeneratorMs
                ValidationMs = $item.ValidationMs
                DeltaMs = $item.DeltaMs
                HandlerMs = $item.HandlerMs
                TotalMs = $item.TotalMs
                GeneratedSources = $item.Sources
                ChangedSources = if ($null -ne $item.Changed) { @($item.Changed).Count } else { $null }
                ChangedNames = $item.Changed -join ';'
            }
        }
    }
}
if ($rows.Count -eq 0 -and $graphRows.Count -eq 0) {
    throw 'No benchmark samples found.'
}
$rows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'samples.csv') -NoTypeInformation -Encoding UTF8
$summaries = @()
foreach ($group in $rows | Where-Object { -not $_.Warmup } | Group-Object Kind, Scenario) {
    foreach ($metric in @('GeneratorMs', 'ValidationMs', 'DeltaMs', 'HandlerMs', 'TotalMs')) {
        $values = @($group.Group | Where-Object { $null -ne $_.$metric } | ForEach-Object { [double]$_.$metric } | Sort-Object)
        if ($values.Count -eq 0) {
            continue;
        }
        $count = $values.Count
        $median = ($values[[int][Math]::Floor(($count - 1) / 2)] + $values[[int][Math]::Floor($count / 2)]) / 2
        $summaries += [pscustomobject][ordered]@{
            Kind = $group.Group[0].Kind
            Scenario = $group.Group[0].Scenario
            Metric = $metric
            Count = $count
            MinMs = $values[0]
            MedianMs = $median
            P95Ms = $values[[int][Math]::Ceiling($count * 0.95) - 1]
            MaxMs = $values[-1]
        }
    }
}
$summaries | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation -Encoding UTF8
$taskRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'build-tasks.csv') -NoTypeInformation -Encoding UTF8
$startupRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'startup.csv') -NoTypeInformation -Encoding UTF8
$validationRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'validation.csv') -NoTypeInformation -Encoding UTF8
$watchEvents | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'watch-events.csv') -NoTypeInformation -Encoding UTF8
$graphRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'graph.csv') -NoTypeInformation -Encoding UTF8
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'environment.json'),
    ($environmentRows | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$puritySource = Join-Path $InputDirectory 'purity-source.json'
if (Test-Path -LiteralPath $puritySource) {
    $source = [IO.File]::ReadAllText($puritySource) | ConvertFrom-Json
    $portableSource = @{ Repository = $source.Repository; Revision = $source.Revision } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'purity-source.json'), $portableSource, [Text.UTF8Encoding]::new($false))
}
foreach ($path in @('samples.csv', 'summary.csv', 'environment.json', 'build-tasks.csv', 'startup.csv', 'validation.csv', 'watch-events.csv', 'graph.csv', 'purity-source.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $path))) {
        continue;
    }
    $absolute = Join-Path $OutputDirectory $path
    $text = [IO.File]::ReadAllText($absolute) -replace '\r?\n', "`r`n"
    [IO.File]::WriteAllText($absolute, $text.TrimEnd("`r", "`n") + "`r`n", [Text.UTF8Encoding]::new($false))
}
Write-Host "$($rows.Count) samples; $($summaries.Count) metric summaries written to $OutputDirectory"
& (Join-Path $PSScriptRoot 'Update-Report.ps1') -ResultDirectory $OutputDirectory
