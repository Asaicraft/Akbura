#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$ResultDirectory
)

$ErrorActionPreference = 'Stop'
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$culture = [Globalization.CultureInfo]::InvariantCulture
[Globalization.CultureInfo]::CurrentCulture = $culture
$encoding = [Text.UTF8Encoding]::new($false)
$rows = [Collections.Generic.List[object]]::new()
$parityRecords = [Collections.Generic.List[string]]::new()
$oracle = @{}
$stages = [Collections.Generic.List[object]]::new()
function Median($values) {
    $ordered = @($values | ForEach-Object { [double]::Parse("$_", $culture) } | Sort-Object)
    if ($ordered.Count -ne 8) { throw 'A complete measured group must contain eight samples.' }
    return ($ordered[3] + $ordered[4]) / 2
}
foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $ResultDirectory 'audit') -Directory | Sort-Object Name) {
    $name = $directory.Name
    if ($name -notmatch '^(stats|production)(?:-(\d+))?-(.+)-(literal|binding)$') { throw "Unknown audit directory: $name" }
    $profile = $Matches[1]
    $round = if ($Matches[2]) { [int]$Matches[2] } else { 0 }
    $variant = $Matches[3]
    $configuration = Get-Content -LiteralPath (Join-Path $directory.FullName 'run.json') -Raw | ConvertFrom-Json
    if ($configuration.Profiling -ne $(if ($profile -eq 'stats') { 'stats' } else { 'off' })) { throw 'Profile mismatch.' }
    $samples = @(Import-Csv -LiteralPath (Join-Path $directory.FullName 'invalidation-samples.csv'))
    foreach ($group in $samples | Group-Object Project,File,Scenario) {
        if ($group.Count -ne 11 -or @($group.Group.Index | Sort-Object -Unique).Count -ne 11 -or
            @($group.Group | Where-Object { [int]$_.Index -notin 0..10 -or $_.VerifyEveryUpdate -ne 'True' }).Count) {
            throw "Incomplete/nonverified group: $name"
        }
        $measured = @($group.Group | Where-Object { $_.First -eq 'False' -and $_.Warmup -eq 'False' })
        $first = $measured[0]
        $row = [ordered]@{ Profile = $profile; Round = $round; Variant = $variant; Project = $first.Project; File = $first.File
            Scenario = $first.Scenario; MedianMs = Median $measured.GeneratorMs
            MinMs = ($measured.GeneratorMs | ForEach-Object { [double]$_ } | Measure-Object -Minimum).Minimum
            MaxMs = ($measured.GeneratorMs | ForEach-Object { [double]$_ } | Measure-Object -Maximum).Maximum
            AllocatedBytes = Median $measured.ProcessAllocatedBytes; PostGcHeapBytes = Median $measured.PostGcHeapBytes }
        foreach ($metric in @('ProbeCompilationCreated','ProbeSemanticModelAcquired','MarkupExpressionRequest','MarkupExpressionCacheHit',
            'ExpressionCacheEntryPublished','ExpressionCacheAdmissionOnly','ExpressionKeyHash','ExpressionKeyComparison',
            'ExpressionKeyNonEqualComparison','ExpressionKeySyntaxComparison')) {
            $row[$metric] = if ($first.PSObject.Properties[$metric]) { Median $measured.$metric } else { $null }
        }
        $rows.Add([pscustomobject]$row)
    }
    foreach ($group in (Import-Csv -LiteralPath (Join-Path $directory.FullName 'invalidation-stages.csv') |
        Where-Object { $_.First -eq 'False' -and $_.Warmup -eq 'False' -and $_.Stage -like 'Expression*' } |
        Group-Object Project,File,Scenario,Stage)) {
        $first = $group.Group[0]
        $stages.Add([pscustomobject]@{ Profile = $profile; Round = $round; Variant = $variant; Project = $first.Project; File = $first.File
            Scenario = $first.Scenario; Stage = $first.Stage; MedianInclusiveMs = Median $group.Group.InclusiveMs
            MedianCount = Median ($group.Group | ForEach-Object { $_.Count }); MedianInclusiveBytes = Median $group.Group.InclusiveCurrentThreadBytes })
    }
    $tracePath = Join-Path $directory.FullName 'invalidation-traces.jsonl.gz'
    $stream = [IO.File]::OpenRead($tracePath)
    $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionMode]::Decompress)
    $reader = [IO.StreamReader]::new($gzip)
    $checks = 0
    $validationIndices = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $parityIndices = [Collections.Generic.HashSet[string]]::new()
    $sampleKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($sample in $samples) { $null = $sampleKeys.Add((@($sample.Project,$sample.File,$sample.Scenario,$sample.Index) -join '|')) }
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            $record = $line | ConvertFrom-Json
            $recordKey = @($record.Project,$record.File,$record.Scenario,$record.Index) -join '|'
            if ($record.Kind -eq 'invalidation-validation') {
                $key = @($profile,$record.Project,$record.File,$record.Scenario,$record.Index,'diagnostics') -join '|'
                if (-not $sampleKeys.Contains($recordKey) -or -not $validationIndices.Add($recordKey)) { throw "Unexpected/duplicate diagnostic record: $key" }
                $value = ConvertTo-Json -InputObject @($record.Diagnostics) -Depth 50 -Compress
                if ($oracle.ContainsKey($key) -and $oracle[$key] -cne $value) { throw "Cross-variant diagnostics differ: $key" }
                $oracle[$key] = $value
                continue
            }
            if ($record.Kind -ne 'invalidation-parity') { continue }
            if (-not $record.EqualSources -or -not $record.EqualDiagnostics) { throw "Failed fresh-driver parity: $name" }
            $checks++
            # Volatile publisher stamps are normalized by the existing complete parity oracle.
            $key = @($profile,$record.Project,$record.File,$record.Scenario,$record.Index,'sources') -join '|'
            if (-not $sampleKeys.Contains($recordKey) -or -not $parityIndices.Add($recordKey)) { throw "Unexpected/duplicate parity record: $key" }
            $value = @($record.SourceDigests.PSObject.Properties | Sort-Object Name | ForEach-Object { $_.Name + "`0" + $_.Value }) -join "`n"
            if ($oracle.ContainsKey($key) -and $oracle[$key] -cne $value) { throw "Cross-variant source digests differ: $key" }
            $oracle[$key] = $value
            $parityRecords.Add($key)
        }
    }
    finally { $reader.Dispose(); $gzip.Dispose(); $stream.Dispose() }
    if ($checks -ne $samples.Count -or $validationIndices.Count -ne $samples.Count) { throw "Missing fresh-driver parity or diagnostic records: $name" }
}
$expected = 24 + 36 * (Get-Content -LiteralPath (Join-Path $InputDirectory 'sources.json') -Raw | ConvertFrom-Json).Rounds
if ($rows.Count -ne $expected) { throw "Expected $expected complete cells; found $($rows.Count)." }
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Scoped-expression cache audit measurements')
$lines.Add('')
$lines.Add('Generated from frozen tools and identical real project inputs. Each row has an initial generation, two warm-ups and eight measured edits. Production has independent forward/reverse-order rounds. Negative differences mean less time, but no statistical significance or cross-revision reuse is implied. Engineering acceptance remains manual.')
$lines.Add('')
$lines.Add('| Profile / round | Variant | Component / edit | Median ms | Range ms | Allocated bytes | Post-GC heap bytes |')
$lines.Add('| --- | --- | --- | ---: | ---: | ---: | ---: |')
foreach ($row in $rows) {
    $lines.Add(('| {0} / {1} | {2} | {3} / {4} | {5:F2} | {6:F2}-{7:F2} | {8:F0} | {9:F0} |' -f
        $row.Profile,$row.Round,$row.Variant,$row.File,$row.Scenario,$row.MedianMs,$row.MinMs,$row.MaxMs,$row.AllocatedBytes,$row.PostGcHeapBytes))
}
$lines.Add('')
$lines.Add('Original/current production controls are historical frozen analyzers; rebuilt-current controls for the audit refactoring. Disabled uses the same current compiler with only scoped memoization bypassed. Prehashed preserves full exact-text/type/query-span/syntax equality and computes the hash once. Repeat-admission additionally retains a result only after a second successful exact request; admission keys still retain current-owner data.')
$lines.Add('')
$lines.Add('STATS timing scopes include their instrumentation overhead. Lookup/equality/syntax/hash scopes are nested and must not be summed. Factory-binding time is actual executed work, not a prediction of time saved by hits. Post-GC heap is the whole active-driver heap, not an isolated cache-size measurement. Published-entry counts are actual successful insertions during that update, not the number of all live entries across the process. A rejected key comparison may be a hash collision; its counter is not by itself a full collision-cost model.')
$lines.Add('')
$lines.Add("Completed cells: $($rows.Count). Fresh-driver checks: $($parityRecords.Count). No final cache decision or cross-revision implementation is asserted by this generated file.")
foreach ($item in @(@{Name='audit-samples.csv';Values=$rows.ToArray()}, @{Name='audit-stages.csv';Values=$stages.ToArray()})) {
    $csv = @($item.Values | ConvertTo-Csv -NoTypeInformation)
    [IO.File]::WriteAllText((Join-Path $ResultDirectory $item.Name), ($csv -join "`r`n")+"`r`n", $encoding)
}
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'audit-measured-summary.md'), ($lines -join "`r`n")+"`r`n", $encoding)
Write-Host "Summarized $($rows.Count) audit cells; $($parityRecords.Count) fresh-driver comparisons passed."
