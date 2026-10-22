#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$encoding = [Text.UTF8Encoding]::new($false)
$root = Join-Path ([IO.Path]::GetTempPath()) ('Akbura-WatchMemoryReport-' + [Guid]::NewGuid().ToString('N'))
$run = Join-Path $root 'memory/runs/natural-1-baseline-release'
$reports = Join-Path $root 'summary'
$null = New-Item -ItemType Directory -Path $run
function Save([string]$name, $value) { [IO.File]::WriteAllText((Join-Path $run $name),($value | ConvertTo-Json -Depth 8)+"`r`n",$encoding) }
Save 'session.json' @{ Complete=$true; Revisions=100; Variant='baseline'; Observer=$false; Mode='natural' }
$counterRows = @(); $index = 0
foreach ($revision in @(0,0,1,10,25,50,75,100)) {
    $index++
    $instant = [DateTime]::new(2026,1,1,0,0,0,[DateTimeKind]::Utc).AddSeconds($index*10)
    $phase = if ($index -eq 1) { 'startup' } elseif ($index -eq 2) { 'warmup' } else { 'revision' }
    Save ('{0:D2}-{1}-{2}.checkpoint.json' -f $index,$phase,$revision) @{ Sequence=$index; Phase=$phase; Revision=$revision
        Metrics=@(@{ Role='compilerWatch'; SampledUtc=$instant.ToString('o'); CpuSeconds=$index; WorkingSetBytes=999; PrivateBytes=888 },
            @{ Role='application'; SampledUtc=$instant.ToString('o'); CpuSeconds=$index; WorkingSetBytes=999; PrivateBytes=888 }) }
    $sampleInstant = if ($index -eq 8) { $instant.AddSeconds(-1) } else { $instant }
    foreach ($generation in @('gen0','gen1','gen2','loh','poh')) {
        foreach ($metric in @(@("dotnet.gc.last_collection.heap.size (By)[gc.heap.generation=$generation]",$(if ($generation -eq 'gen2') { 1000+10*$revision } else { 0 })),
            @("dotnet.gc.last_collection.heap.fragmentation.size (By)[gc.heap.generation=$generation]",0))) {
            $counterRows += [pscustomobject]@{ Timestamp=$sampleInstant.ToLocalTime().ToString('MM/dd/yyyy HH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
                Provider='System.Runtime'; 'Counter Name'=$metric[0]; 'Counter Type'='Metric'; 'Mean/Increment'=$metric[1] }
        }
    }
}
function Counters($rows) {
    [IO.File]::WriteAllText((Join-Path $run 'counters-compilerWatch.csv'),(@($rows | ConvertTo-Csv -NoTypeInformation) -join "`r`n")+"`r`n",$encoding)
    Copy-Item -LiteralPath (Join-Path $run 'counters-compilerWatch.csv') -Destination (Join-Path $run 'counters-application.csv') -Force
}
function Report {
    & (Join-Path $PSScriptRoot 'Summarize-WatchMemory.ps1') -ResultDirectory $root -OutputDirectory $reports -AllowPartial
}
function Rejected([string]$pattern) {
    $rejected = $false
    try { Report } catch { if ($_.Exception.Message -notlike $pattern) { throw }; $rejected = $true }
    if (-not $rejected) { throw "Invalid synthetic evidence was accepted: $pattern" }
}
function Check-Summary {
    $summary = Import-Csv (Join-Path $reports 'memory-summary.csv') | Where-Object Role -eq compilerWatch
    if ([double]::Parse($summary.SlopeBytesPerRevision,[Globalization.CultureInfo]::InvariantCulture) -ne 10 -or $summary.IncreaseBytes -ne '1000') { throw 'Incorrect memory slope or checkpoint baseline.' }
    $end = Import-Csv (Join-Path $reports 'memory-series.csv') | Where-Object { $_.Role -eq 'compilerWatch' -and $_.Revision -eq 100 }
    if ($end.CounterSkewSeconds -ne '1' -or $end.RejectedNearerIncompleteSamples -ne '1' -or $end.AssemblyCount -ne '') { throw 'Incorrect sample selection/skew or fabricated optional metric.' }
}
$tailTimestamp = $instant.ToLocalTime().ToString('MM/dd/yyyy HH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
$cpuTail = [pscustomobject]@{ Timestamp=$tailTimestamp; Provider='System.Runtime'; 'Counter Name'='dotnet.process.cpu.time (s / 1 sec)[cpu.mode=system]'; 'Counter Type'='Metric'; 'Mean/Increment'=1 }
Counters @($counterRows + $cpuTail)
$counterHash = (Get-FileHash (Join-Path $run 'counters-compilerWatch.csv')).Hash
Report
Check-Summary
if ((Get-FileHash (Join-Path $run 'counters-compilerWatch.csv')).Hash -ne $counterHash) { throw 'Report generation modified raw evidence.' }

# A closer group containing only one generation must not undercount the heap.
$partialTail = foreach ($prefix in @('dotnet.gc.last_collection.heap.size ','dotnet.gc.last_collection.heap.fragmentation.size ')) {
    [pscustomobject]@{ Timestamp=$tailTimestamp; Provider='System.Runtime'; 'Counter Name'=($prefix+'(By)[gc.heap.generation=gen2]'); 'Counter Type'='Metric'; 'Mean/Increment'=9000000 }
}
Counters @($counterRows + $partialTail)
Report
Check-Summary

# Five rows with a duplicated generation are still not a complete sample.
$duplicateTail = foreach ($prefix in @('dotnet.gc.last_collection.heap.size ','dotnet.gc.last_collection.heap.fragmentation.size ')) {
    foreach ($generation in @('gen0','gen0','gen2','loh','poh')) {
        [pscustomobject]@{ Timestamp=$tailTimestamp; Provider='System.Runtime'; 'Counter Name'=($prefix+"(By)[gc.heap.generation=$generation]"); 'Counter Type'='Metric'; 'Mean/Increment'=9000000 }
    }
}
Counters @($counterRows + $duplicateTail)
Report
Check-Summary

$lastCompleteTimestamp = $instant.AddSeconds(-1).ToLocalTime().ToString('MM/dd/yyyy HH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
Counters @(@($counterRows | Where-Object Timestamp -ne $lastCompleteTimestamp) + $cpuTail)
Rejected '*No complete external GC sample within five seconds*'
Counters @($counterRows | Where-Object { -not $_.'Counter Name'.StartsWith('dotnet.gc.last_collection.heap.size ') })
Rejected '*Required complete external GC samples are unavailable*'
Counters @($counterRows + $cpuTail)
Save 'session.json' @{ Complete=$true; Revisions=100; Variant='baseline'; Observer=$false; Mode='forced' }
Rejected '*Missing forced-GC object graph statistics*'
Save 'session.json' @{ Complete=$true; Revisions=100; Variant='baseline'; Observer=$false; Mode='natural' }
Save 'session.json' @{ Complete=$false; Revisions=100 }
Rejected '*Incomplete session*'
Write-Output "PASS: UTC/local joins, nearest complete GC sample, CPU-only/partial/duplicate tails, skew bound, missing metrics, missing forced graph, optional nulls, slope/completion guards and immutable raw evidence; fixture retained at $root"
