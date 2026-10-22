#requires -Version 7.2
param([Parameter(Mandatory)][string]$SessionDirectory)
$ErrorActionPreference = 'Stop'
$SessionDirectory = [IO.Path]::GetFullPath($SessionDirectory)
$events = @(Get-Content -LiteralPath (Join-Path $SessionDirectory 'roslyn-events.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } | Sort-Object LocalMs)
$rows = @(Get-Content -LiteralPath (Join-Path $SessionDirectory 'watch.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
$samples = @($rows | Where-Object Kind -eq 'purity-critical-sample')
$markers = @($events | Where-Object Provider -eq 'Akbura-CriticalPath')
$open = @{}
$spans = [Collections.Generic.List[object]]::new()
foreach ($item in $events | Where-Object Provider -eq 'Microsoft-CodeAnalysis-General') {
    if ($item.Event -notmatch '^(Start|Stop)(GeneratorDriverRunTime|SingleGeneratorRunTime)(?:/(?:Start|Stop))?$') { continue }
    $operation = $Matches[1]; $scope = $Matches[2]
    $key = "$scope/$($item.Payload.id)/$($item.Payload.generatorName)"
    if ($operation -eq 'Start') {
        if ($open.ContainsKey($key)) { throw 'An overlapping Roslyn event ID is ambiguous; do not guess a pairing.' }
        $open[$key] = $item
        continue
    }
    if (-not $open.ContainsKey($key)) { throw 'A Roslyn stop event has no matching start.' }
    $start = $open[$key]; $open.Remove($key)
    if ($start.Pid -ne $item.Pid -or $item.LocalMs -lt $start.LocalMs) { throw 'Invalid process/clock pairing.' }
    $inside = @($markers | Where-Object { $_.Pid -eq $item.Pid -and $_.LocalMs -ge $start.LocalMs -and $_.LocalMs -le $item.LocalMs })
    $keys = @($inside | ForEach-Object { "$($_.Payload.revision)/$($_.Payload.sourceSha256)" } | Sort-Object -Unique)
    $matched = if ($keys.Count -eq 1) {
        @($samples | Where-Object { "$($_.Revision)/$($_.SourceSha256)" -ceq $keys[0] })
    } else { @() }
    if ($matched.Count -gt 1) { throw 'Duplicate sample revision/hash.' }
    $spans.Add([pscustomobject]@{ Scope=$scope; Generator=$item.Payload.generatorName; CompilerPid=$item.Pid
        Revision=$(if ($matched.Count -eq 1) { $matched[0].Revision } else { $null }); Warmup=$(if ($matched.Count -eq 1) { $matched[0].Warmup } else { $null })
        DurationMs=[double]$item.LocalMs-[double]$start.LocalMs; StartLocalMs=$start.LocalMs; EndLocalMs=$item.LocalMs
        Correlation=$(if ($matched.Count -eq 1) { 'Exact in-process revision/hash marker enclosed by the paired Roslyn span' } elseif ($keys.Count -gt 1) { 'AmbiguousMultipleMarkers' } else { 'UnattributedNoMarker' })
        Clock='One EventPipe trace-local clock; no watch-parent or application timestamps subtracted' })
}
if ($open.Count) { throw 'The trace is incomplete: unmatched Roslyn start event.' }
foreach ($sample in $samples) {
    if (@($spans | Where-Object { $_.Scope -eq 'GeneratorDriverRunTime' -and $_.Revision -eq $sample.Revision }).Count -ne 1) {
        throw "No uniquely correlated complete GeneratorDriver interval for revision $($sample.Revision)."
    }
}
$output = foreach ($row in $spans) {
    $record = [ordered]@{}
    foreach ($property in $row.PSObject.Properties) { $record[$property.Name] = if ($property.Value -is [double]) { $property.Value.ToString('R',[Globalization.CultureInfo]::InvariantCulture) } else { $property.Value } }
    [pscustomobject]$record
}
[IO.File]::WriteAllText((Join-Path $SessionDirectory 'roslyn-spans.csv'),((@($output | ConvertTo-Csv -NoTypeInformation) -join "`r`n") + "`r`n"),[Text.UTF8Encoding]::new($false))
Write-Output "Validated $($samples.Count) exact revision/hash-correlated GeneratorDriver spans. Unattributed spans remain explicitly unassigned. EnC duration is not inferred."
