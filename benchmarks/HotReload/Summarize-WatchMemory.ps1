#requires -Version 7.2
param([Parameter(Mandatory)][string]$ResultDirectory, [int]$Rounds = 3, [switch]$AllowPartial, [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
if (-not $OutputDirectory) { $OutputDirectory = $ResultDirectory }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$culture = [Globalization.CultureInfo]::InvariantCulture
function N($value) { [double]::Parse("$value",$culture) }
function Complete-GcSample($values) {
    foreach ($prefix in @('dotnet.gc.last_collection.heap.size ','dotnet.gc.last_collection.heap.fragmentation.size ')) {
        $metrics = @($values | Where-Object { $_.'Counter Name'.StartsWith($prefix) })
        if ($metrics.Count -ne 5) { return $false }
        foreach ($generation in @('gen0','gen1','gen2','loh','poh')) {
            if (@($metrics | Where-Object { $_.'Counter Name' -ceq ($prefix + "(By)[gc.heap.generation=$generation]") }).Count -ne 1) { return $false }
        }
    }
    return $true
}
function Save-Csv([string]$name, $rows) {
    $values = foreach ($row in $rows) {
        $record = [ordered]@{}
        foreach ($property in $row.PSObject.Properties) { $record[$property.Name] = if ($property.Value -is [double]) { $property.Value.ToString('R',$culture) } else { $property.Value } }
        [pscustomobject]$record
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $name),((@($values | ConvertTo-Csv -NoTypeInformation) -join "`r`n") + "`r`n"),[Text.UTF8Encoding]::new($false))
}
function Slope($rows, [int]$min, [int]$max) {
    $points = @($rows | Where-Object { $_.Phase -ne 'startup' -and $_.Revision -ge $min -and $_.Revision -le $max })
    if ($points.Count -lt 2) { return $null }
    $x = ($points.Revision | Measure-Object -Average).Average; $y = ($points.LiveHeapBytes | Measure-Object -Average).Average
    $numerator = 0.0; $denominator = 0.0
    foreach ($point in $points) { $numerator += ($point.Revision-$x)*($point.LiveHeapBytes-$y); $denominator += [math]::Pow($point.Revision-$x,2) }
    if ($denominator -eq 0) { return $null }
    return $numerator / $denominator
}
$runs = @(Get-ChildItem -LiteralPath (Join-Path $ResultDirectory 'memory/runs') -Directory)
if (-not $AllowPartial -and $runs.Count -ne 12 * $Rounds) { throw "Expected $($Rounds * 12) matched sessions, found $($runs.Count)." }
$series = [Collections.Generic.List[object]]::new()
$summaries = [Collections.Generic.List[object]]::new()
$retained = [Collections.Generic.List[object]]::new()
foreach ($run in $runs) {
    $session = Get-Content (Join-Path $run.FullName 'session.json') -Raw | ConvertFrom-Json
    if (-not $session.Complete -or $session.Revisions -ne 100) { throw "Incomplete session: $run" }
    $checkpoints = @(Get-ChildItem -LiteralPath $run.FullName -Filter '*.checkpoint.json' | Sort-Object Name | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
    if ($checkpoints.Count -ne 8 -or ($checkpoints.Revision -join ',') -ne '0,0,1,10,25,50,75,100') { throw 'Incorrect checkpoint sequence.' }
    $roleRows = @{}
    foreach ($role in @('compilerWatch','application','launcher')) {
        $file = Join-Path $run.FullName "counters-$role.csv"
        if (-not (Test-Path -LiteralPath $file)) {
            if ($role -ne 'launcher') { throw "Missing required external process counters: $role" }
            continue
        }
        $counterRows = @(Import-Csv -LiteralPath $file)
        $groups = @($counterRows | Group-Object Timestamp | ForEach-Object {
            [pscustomobject]@{ Utc = [DateTime]::Parse($_.Name,$culture,[Globalization.DateTimeStyles]::AssumeLocal).ToUniversalTime()
                Values = $_.Group; CompleteGcSample = (Complete-GcSample $_.Group) }
        })
        $completeGroups = @($groups | Where-Object CompleteGcSample)
        if (-not $completeGroups.Count) { throw "Required complete external GC samples are unavailable: $($run.Name)/$role; do not substitute zeros." }
        foreach ($checkpoint in $checkpoints) {
            $native = @($checkpoint.Metrics | Where-Object Role -eq $role)
            if ($native.Count -ne 1) { throw 'Missing process-specific metric.' }
            $instant = if ($native[0].SampledUtc -is [DateTime]) { $native[0].SampledUtc.ToUniversalTime() } else {
                [DateTime]::Parse($native[0].SampledUtc,$culture,[Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
            }
            $nearest = $completeGroups | Sort-Object { [math]::Abs(($_.Utc-$instant).TotalSeconds) },Utc | Select-Object -First 1
            $skew = [math]::Abs(($nearest.Utc-$instant).TotalSeconds)
            if ($skew -gt 5) { throw "No complete external GC sample within five seconds: $($run.Name)/$role checkpoint $($checkpoint.Sequence); nearest skew=$skew" }
            $rejectedNearer = @($groups | Where-Object { -not $_.CompleteGcSample -and [math]::Abs(($_.Utc-$instant).TotalSeconds) -le $skew }).Count
            function Metric([string]$prefix) {
                $values = @($nearest.Values | Where-Object { $_.'Counter Name'.StartsWith($prefix) })
                if (-not $values.Count) { return $null }
                return ($values | ForEach-Object { N $_.'Mean/Increment' } | Measure-Object -Sum).Sum
            }
            $heap = Metric 'dotnet.gc.last_collection.heap.size '
            $fragmentation = Metric 'dotnet.gc.last_collection.heap.fragmentation.size '
            if ($null -eq $heap -or $null -eq $fragmentation) { throw 'Required external GC metrics are unavailable; do not substitute zeros.' }
            $live = $heap - $fragmentation
            $heapMeaning = 'External last-GC heap less fragmentation, not an object-graph measurement'
            $objects = $null
            if ($role -eq 'application' -and $checkpoint.Application) {
                $live = [double]$checkpoint.Application.ManagedHeapBytes
                $heapMeaning = 'Application GC.GetTotalMemory(false) at preceding verified UI acknowledgment; not post-GC live-object size'
            }
            $heapstat = Join-Path $run.FullName ('{0:D2}-{1}-{2}-{3}-heapstat.stdout.log' -f $checkpoint.Sequence,$checkpoint.Phase,$checkpoint.Revision,$role)
            if ($session.Mode -eq 'forced' -and $role -ne 'launcher' -and -not (Test-Path -LiteralPath $heapstat)) { throw "Missing forced-GC object graph statistics: $($run.Name)/$role checkpoint $($checkpoint.Sequence)" }
            if (Test-Path -LiteralPath $heapstat) {
                $text = [IO.File]::ReadAllText($heapstat)
                if ($text -notmatch '(?m)^\s*([\d,]+)\s+GC Heap bytes') { throw 'Invalid gcdump heap statistics.' }
                $live = [double]::Parse($Matches[1],[Globalization.NumberStyles]::Number,$culture)
                if ($text -match '(?m)^\s*([\d,]+)\s+GC Heap objects') { $objects = [long]::Parse($Matches[1],[Globalization.NumberStyles]::Number,$culture) }
                $heapMeaning = 'External full-GC gcdump graph byte count; not exclusive retained owner size'
            }
            $row = [pscustomobject]@{ Run=$run.Name; Variant=$session.Variant; Observer=$session.Observer; Mode=$session.Mode; Role=$role
                Sequence=$checkpoint.Sequence; Phase=$checkpoint.Phase; Revision=$checkpoint.Revision; LiveHeapBytes=[double]$live; HeapMeaning=$heapMeaning
                LastGcHeapBytes=$heap; FragmentationBytes=$fragmentation; CommittedBytes=(Metric 'dotnet.gc.last_collection.memory.committed_size ')
                LohBytes=(Metric 'dotnet.gc.last_collection.heap.size (By)[gc.heap.generation=loh]'); PohBytes=(Metric 'dotnet.gc.last_collection.heap.size (By)[gc.heap.generation=poh]')
                ObjectCount=$objects; AssemblyCount=(Metric 'dotnet.assembly.count '); AllocatedBytesPerSecond=(Metric 'dotnet.gc.heap.total_allocated ')
                ApplicationAllocatedTotalBytes=$(if ($role -eq 'application') { $checkpoint.Application.AllocatedBytes } else { $null })
                Gen0CollectionsPerSecond=(Metric 'dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen0]')
                Gen1CollectionsPerSecond=(Metric 'dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen1]')
                Gen2CollectionsPerSecond=(Metric 'dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen2]')
                CpuSeconds=$native[0].CpuSeconds; WorkingSetBytes=$native[0].WorkingSetBytes; PrivateBytes=$native[0].PrivateBytes
                CounterSkewSeconds=$skew; CounterSampleUtc=$nearest.Utc.ToString('o'); RejectedNearerIncompleteSamples=$rejectedNearer
                CounterSampleSelection='Nearest timestamp group containing exactly one heap and fragmentation metric for each of gen0/gen1/gen2/LOH/POH, within five seconds'
                SampledUtc=$native[0].SampledUtc }
            $series.Add($row)
            if (-not $roleRows.ContainsKey($role)) { $roleRows[$role] = @() }
            $roleRows[$role] += $row
        }
    }
    foreach ($role in $roleRows.Keys) {
        $rows = $roleRows[$role]
        $warm = @($rows | Where-Object Phase -eq warmup)[0]; $end = @($rows | Where-Object Revision -eq 100)[0]
        $summaries.Add([pscustomobject]@{ Run=$run.Name; Variant=$session.Variant; Observer=$session.Observer; Mode=$session.Mode; Role=$role
            WarmupBytes=$warm.LiveHeapBytes; Revision100Bytes=$end.LiveHeapBytes; IncreaseBytes=$end.LiveHeapBytes-$warm.LiveHeapBytes
            IncreasePct=$(if ($warm.LiveHeapBytes -gt 0) { 100*($end.LiveHeapBytes/$warm.LiveHeapBytes-1) } else { $null }); SlopeBytesPerRevision=(Slope $rows 0 100)
            EarlySlopeBytesPerRevision=(Slope $rows 0 25); LateSlopeBytesPerRevision=(Slope $rows 50 100)
            PeakObservedBytes=($rows.LiveHeapBytes | Measure-Object -Maximum).Maximum })
    }
    foreach ($profile in Get-ChildItem -LiteralPath $run.FullName -Directory -Filter '*-analysis') {
        foreach ($type in Import-Csv (Join-Path $profile.FullName 'retained-types.csv')) {
            $retained.Add([pscustomobject]@{ Run=$run.Name; Checkpoint=$profile.Name; Type=$type.Type; RootReachableCount=$type.RootReachableCount; ShallowBytes=$type.ShallowBytes })
        }
    }
}
Save-Csv 'memory-series.csv' $series
Save-Csv 'memory-summary.csv' $summaries
Save-Csv 'retained-types.csv' $retained
foreach ($mode in @('natural','forced')) {
    $points = @($series | Where-Object { $_.Role -eq 'compilerWatch' -and $_.Mode -eq $mode -and $_.Phase -ne 'startup' })
    if (-not $points.Count) { continue }
    $maximum = ($points.LiveHeapBytes | Measure-Object -Maximum).Maximum * 1.1
    $svg = [Collections.Generic.List[string]]::new()
    $svg.Add('<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="600" viewBox="0 0 1000 600"><rect width="1000" height="600" fill="white"/><g font-family="sans-serif" font-size="13">')
    $svg.Add("<text x=`"60`" y=`"25`">Compiler/watch heap, $mode GC; thin lines are independent sessions</text>")
    foreach ($tick in 0..4) { $y=500-$tick*110; $label=($maximum*$tick/4/1MB).ToString('F1',$culture); $svg.Add("<path d=`"M60 $y H740`" stroke=`"#ddd`"/><text x=`"5`" y=`"$y`">$label MiB</text>") }
    $colors = @('#225ea8','#d7301f','#238b45','#88419d','#636363','#dd1c77'); $legend=0
    foreach ($category in $points | Group-Object { $_.Run -replace '^(natural|forced)-\d+-','' } | Sort-Object Name) {
        $color=$colors[$legend % $colors.Count]; $legend++
        $svg.Add("<text x=`"755`" y=`"$(40+$legend*22)`" fill=`"$color`">$($category.Name)</text>")
        foreach ($run in $category.Group | Group-Object Run) {
            $coordinates = @($run.Group | Sort-Object Revision | ForEach-Object { (60+6.8*$_.Revision).ToString('F2',$culture)+','+(500-440*$_.LiveHeapBytes/$maximum).ToString('F2',$culture) }) -join ' '
            $svg.Add("<polyline fill=`"none`" stroke=`"$color`" stroke-width=`"1.5`" opacity=`"0.7`" points=`"$coordinates`"/>")
        }
    }
    $svg.Add('<path d="M60 60 V500 H740" stroke="black" fill="none"/><text x="60" y="525">0 (after warmup)</text><text x="690" y="525">100 edits</text><text x="60" y="560">Forced: gcdump live graph bytes. Natural: external last-GC heap less fragmentation; not equivalent measurements.</text></g></svg>')
    [IO.File]::WriteAllText((Join-Path $OutputDirectory "memory-$mode.svg"),($svg -join "`r`n")+"`r`n",[Text.UTF8Encoding]::new($false))
}
Write-Output "Summarized $($runs.Count) dedicated sessions. Review roots and matched variants; no automatic leak or performance conclusion."
