#requires -Version 7.2
param([Parameter(Mandatory = $true)][string]$ResultDirectory)

$ErrorActionPreference = 'Stop'
[Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
$encoding = [Text.UTF8Encoding]::new($false)

function Read-Parity([string]$path) {
    $stream = [IO.Compression.GZipStream]::new([IO.File]::OpenRead($path), [IO.Compression.CompressionMode]::Decompress)
    $reader = [IO.StreamReader]::new($stream)
    $records = @{}
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            if ($line.Contains('"Kind":"invalidation-parity"') -or $line.Contains('"Kind":"invalidation-validation"')) {
                $row = $line | ConvertFrom-Json
                $key = @($row.Kind,$row.Project,$row.File,$row.Scenario,$row.Optimization,$row.Index) -join "`0"
                if ($records.ContainsKey($key)) {
                    throw "Duplicate comparison revision: $key"
                }
                if ($row.Kind -eq 'invalidation-parity') {
                    if (-not $row.EqualSources -or -not $row.EqualDiagnostics) {
                        throw "Failed fresh parity: $key"
                    }
                    $records[$key] = @($row.SourceDigests.psobject.Properties | Sort-Object Name | ForEach-Object { $_.Name + "`0" + $_.Value }) -join "`n"
                }
                else {
                    $records[$key] = ConvertTo-Json -InputObject @($row.Diagnostics) -Depth 50 -Compress
                }
            }
        }
    }
    finally {
        $reader.Dispose()
    }
    return $records
}

function Median($values) {
    $ordered = @($values | ForEach-Object { [double]$_ } | Sort-Object)
    if (-not $ordered.Count) {
        throw 'Missing comparison samples.'
    }
    if ($ordered.Count % 2) {
        return $ordered[[int][Math]::Floor($ordered.Count / 2)]
    }
    return ($ordered[$ordered.Count / 2 - 1] + $ordered[$ordered.Count / 2]) / 2
}

$beforeRoot = Join-Path $ResultDirectory 'before'
$afterRoot = Join-Path $ResultDirectory 'after'
$beforeParity = Read-Parity (Join-Path $beforeRoot 'invalidation-traces.jsonl.gz')
$afterParity = Read-Parity (Join-Path $afterRoot 'invalidation-traces.jsonl.gz')
if (-not $beforeParity.Count -or $beforeParity.Count -ne $afterParity.Count) {
    throw 'Before/after revision sets differ.'
}
foreach ($key in $beforeParity.Keys) {
    if (-not $afterParity.ContainsKey($key) -or $beforeParity[$key] -cne $afterParity[$key]) {
        throw "Original/optimized source or diagnostic mismatch: $key"
    }
}
$samples = @{}
$stages = @{}
foreach ($mode in @('before','after')) {
    $root = Join-Path $ResultDirectory $mode
    $samples[$mode] = @(Import-Csv -LiteralPath (Join-Path $root 'invalidation-samples.csv') | Where-Object { $_.First -eq 'False' -and $_.Warmup -eq 'False' })
    $stages[$mode] = @(Import-Csv -LiteralPath (Join-Path $root 'invalidation-stages.csv') | Where-Object { $_.First -eq 'False' -and $_.Warmup -eq 'False' })
}
$summary = [Collections.Generic.List[object]]::new()
foreach ($group in ($samples.before | Group-Object Project,File,Scenario,Optimization)) {
    $identity = $group.Group[0]
    $right = @($samples.after | Where-Object { $_.Project -eq $identity.Project -and $_.File -eq $identity.File -and $_.Scenario -eq $identity.Scenario -and $_.Optimization -eq $identity.Optimization })
    if ($group.Count -ne 8 -or $right.Count -ne 8) {
        throw "Incomplete comparison cell: $($group.Name)"
    }
    foreach ($metric in @('GeneratorMs','ProcessCpuMs','ProcessAllocatedBytes','PostGcHeapBytes',
        'SemanticDependencyCapture','DependencySetDetachment','DocumentDependencyValidation','CSharpProbeBinding','CSharpProbeDiagnostics')) {
        $values = @{}
        foreach ($mode in @('before','after')) {
            if ($metric -in @('GeneratorMs','ProcessCpuMs','ProcessAllocatedBytes','PostGcHeapBytes')) {
                $batch = if ($mode -eq 'before') { $group.Group } else { $right }
                $values[$mode] = Median $batch.$metric
            }
            else {
                $batch = @($stages[$mode] | Where-Object { $_.Project -eq $identity.Project -and $_.File -eq $identity.File -and $_.Scenario -eq $identity.Scenario -and $_.Optimization -eq $identity.Optimization -and $_.Stage -eq $metric })
                if ($batch.Count -ne 8) {
                    throw "Missing stage: $($group.Name) / $metric / $mode"
                }
                $values[$mode] = Median $batch.InclusiveMs
            }
        }
        $summary.Add([pscustomobject][ordered]@{ Project = $identity.Project; File = $identity.File; Scenario = $identity.Scenario
            Optimization = $identity.Optimization; Metric = $metric; Before = $values.before; After = $values.after
            ChangePercent = if ($values.before -eq 0) { $null } else { 100 * ($values.after / $values.before - 1) } })
    }
}
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'comparison.csv'), (($summary | ConvertTo-Csv -NoTypeInformation) -join "`r`n") + "`r`n", $encoding)
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'comparison-parity.json'), (@{ Equal = $true; Records = $beforeParity.Count
    Scope = 'Matching initial, warm-up and measured source digests and generator diagnostic values across original and optimized binaries.' } | ConvertTo-Json) + "`r`n", $encoding)
Write-Host "$($beforeParity.Count) original/optimized parity records matched; $($summary.Count) timing/allocation/stage comparisons written."
