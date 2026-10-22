#requires -Version 7.2
param(
    [string]$OutputDirectory = 'bin/PurityCriticalPath/gate',
    [string]$ResultDirectory = 'benchmarks/HotReload/results/2026-10-22-real-hot-reload-critical-path',
    [string]$SeedDirectory = 'bin/PurityCriticalPath/seed',
    [ValidateRange(5,8)][int]$Rounds = 5,
    [ValidateRange(30,40)][int]$Updates = 30,
    [switch]$PreflightOnly,
    [switch]$Worker
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repository
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$SeedDirectory = [IO.Path]::GetFullPath($SeedDirectory)
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$encoding = [Text.UTF8Encoding]::new($false)
if (-not $Worker) {
    $arguments = @('-NoProfile','-File',$PSCommandPath,'-Worker','-OutputDirectory',$OutputDirectory,'-ResultDirectory',$ResultDirectory,
        '-SeedDirectory',$SeedDirectory,'-Rounds',"$Rounds",'-Updates',"$Updates")
    if ($PreflightOnly) { $arguments += '-PreflightOnly' }
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $process = Start-Process (Get-Command pwsh).Source -WindowStyle Hidden -PassThru -WorkingDirectory $repository `
        -ArgumentList @($arguments | ForEach-Object { '"'+$_+'"' }) -RedirectStandardOutput (Join-Path $OutputDirectory "controller-$stamp.stdout.log") `
        -RedirectStandardError (Join-Path $OutputDirectory "controller-$stamp.stderr.log")
    Write-Output "Controller PID: $($process.Id)"
    Write-Output "Progress: Get-Content -LiteralPath '$OutputDirectory/journal.log' -Tail 20 -Wait"
    return
}
$lock = [IO.File]::Open((Join-Path $OutputDirectory 'controller.lock'),'OpenOrCreate','ReadWrite','None')
$journal = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'journal.log'),$true,$encoding)
$journal.AutoFlush = $true
$current = 'preflight'; $childPid = $null; $completed = 0
function Log([string]$message) { $journal.WriteLine([DateTime]::UtcNow.ToString('o')+' '+$message); $journal.BaseStream.Flush($true) }
function Save([string]$path, $value) {
    [IO.File]::WriteAllText(($path+'.tmp'),(($value | ConvertTo-Json -Depth 12).ReplaceLineEndings("`r`n")+"`r`n"),$encoding)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        try { [IO.File]::Move(($path+'.tmp'),$path,$true); return }
        catch {
            $cause = $_.Exception
            while ($cause.InnerException) { $cause = $cause.InnerException }
            if (($cause -isnot [IO.IOException] -and $cause -isnot [UnauthorizedAccessException]) -or ($cause.HResult -band 0xFFFF) -notin @(5,32,33) -or $timer.Elapsed.TotalSeconds -ge 10) { throw }
            Start-Sleep -Milliseconds 25
        }
    }
}
function Status([string]$phase, [string]$failureMessage = '') {
    Save (Join-Path $OutputDirectory 'status.json') @{ Phase=$phase; Current=$script:current; CurrentPid=$script:childPid; ControllerPid=$PID
        CompletedSteps=$script:completed; Failure=$failureMessage; UpdatedUtc=[DateTime]::UtcNow.ToString('o') }
}
function Sources {
    @(& git ls-files --cached --others --exclude-standard -- src/Akbura.Generator src/Akbura.BlackSilence src/Akbura.UnitTests benchmarks/HotReload |
        Where-Object { -not $_.StartsWith('benchmarks/HotReload/results/') -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Sort-Object -Unique | ForEach-Object { "$_ $((Get-FileHash -LiteralPath $_).Hash)" })
}
function Assert-Sources { if (((Sources) -join "`n") -cne ($manifest.Files -join "`n")) { throw 'Sources changed; preserve this gate and use a new output directory.' } }
function Foreign-Builds {
    $all = @(Get-CimInstance Win32_Process)
    $owned = [Collections.Generic.HashSet[int]]::new(); $null = $owned.Add($PID)
    do {
        $changed = $false
        foreach ($item in $all) { if ($owned.Contains([int]$item.ParentProcessId) -and $owned.Add([int]$item.ProcessId)) { $changed = $true } }
    } while ($changed)
    @($all | Where-Object { -not $owned.Contains([int]$_.ProcessId) -and $_.CommandLine -notmatch '(?i)[/-]nodemode' -and
        ($_.Name -match '^(testhost|MSBuild|csc)\.exe$' -or $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)(\s(test|build|vstest)\s|MSBuild\.dll|csc\.dll|HotReloadBenchmarks\.dll|dotnet-watch\.dll)') })
}
function Run([string]$name, [string[]]$arguments, [string]$session = '', [switch]$Timing) {
    Assert-Sources
    $script:current = $name
    $marker = Join-Path $OutputDirectory "$name.success.json"
    if (Test-Path -LiteralPath $marker) {
        $saved = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($saved.Fingerprint -ne $manifest.Fingerprint) { throw 'Incompatible success marker.' }
        $script:completed++; Log "REUSED $name"; return $saved
    }
    while ($Timing -and @(Foreign-Builds).Count) { Status 'WaitingForOtherBuilds'; Log "WAIT $name foreign builds/tests"; Start-Sleep -Seconds 15 }
    $logs = Join-Path $OutputDirectory "$name-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))"
    $null = New-Item -ItemType Directory -Path $logs
    $info = [Diagnostics.ProcessStartInfo]::new($arguments[0]); $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true; $info.WorkingDirectory=$repository
    foreach ($argument in $arguments | Select-Object -Skip 1) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo=$info
    $stdout = [IO.FileStream]::new((Join-Path $logs 'stdout.log'),'CreateNew','Write','ReadWrite')
    $stderr = [IO.FileStream]::new((Join-Path $logs 'stderr.log'),'CreateNew','Write','ReadWrite')
    $contaminated = $false
    try {
        $null = $process.Start(); $script:childPid=$process.Id
        $copyOut=$process.StandardOutput.BaseStream.CopyToAsync($stdout); $copyErr=$process.StandardError.BaseStream.CopyToAsync($stderr)
        Log "START $name pid=$script:childPid session=$session logs=$logs"; Status 'Running'
        while (-not $process.WaitForExit(15000)) {
            $stdout.Flush($true); $stderr.Flush($true)
            if ($Timing -and @(Foreign-Builds).Count) { $contaminated=$true }
            $progress = if ($session -and (Test-Path -LiteralPath (Join-Path $session 'watch.jsonl'))) {
                Get-Content -LiteralPath (Join-Path $session 'watch.jsonl') -Tail 15 | ForEach-Object { try { $_ | ConvertFrom-Json -ErrorAction Stop } catch { } } |
                    Where-Object Kind -eq 'purity-critical-sample' | Select-Object -Last 1 | ForEach-Object { "revision=$($_.Revision) scenario=$($_.Scenario) warmup=$($_.Warmup)" }
            } else { 'preparing or active' }
            Log "HEARTBEAT $name $progress cpuSeconds=$($process.TotalProcessorTime.TotalSeconds) contaminated=$contaminated"; Status 'Running'
            if (([DateTime]::Now-$process.StartTime).TotalHours -gt 1) { $process.Kill($true); throw 'Owned session exceeded its one-hour deadline.' }
        }
        $null=$copyOut.GetAwaiter().GetResult(); $null=$copyErr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Exit $($process.ExitCode): $logs" }
        Assert-Sources
        if ($contaminated) { throw 'TimingContamination: session preserved, not accepted; resume after foreign activity finishes.' }
        $saved=@{ Fingerprint=$manifest.Fingerprint; Arguments=$arguments; Session=$session; Logs=$logs; TimingContaminationObserved=$false; CompletedUtc=[DateTime]::UtcNow.ToString('o') }
        Save $marker $saved; $script:completed++; Log "DONE $name"; return $saved
    }
    finally {
        if ($process.Id -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $stdout.Dispose(); $stderr.Dispose(); $process.Dispose(); $script:childPid=$null
    }
}
function Archive([string]$destination, $saved, [string]$profile) {
    $null = New-Item -ItemType Directory -Path $destination -Force
    foreach ($name in @('watch.jsonl','roslyn-events.jsonl','roslyn-spans.csv','result.json','workload.json','inputs.json','purity-source.json','build.log','trace-target.json','trace.stdout.log','trace.stderr.log','trace-ready.json.stopped')) {
        $path=Join-Path $saved.Session $name
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $destination -Force }
    }
    Save (Join-Path $destination 'acceptance.json') @{ Profile=$profile; TimingContaminationObserved=$false; Fingerprint=$saved.Fingerprint
        OriginalSession=$saved.Session; CompletedUtc=$saved.CompletedUtc; Meaning='Diagnostic and production timing populations are separate; no previous measurement is presented as a new execution.' }
}
try {
    $seedProof = Get-Content -LiteralPath (Join-Path $SeedDirectory 'baseline.json') -Raw | ConvertFrom-Json
    & git merge-base --is-ancestor $seedProof.BaselineRevision HEAD
    if ($LASTEXITCODE -ne 0) { throw 'The frozen baseline is not an ancestor of HEAD; do not silently relabel it.' }
    foreach ($line in $seedProof.SourceFiles | Where-Object { $_.StartsWith('src/Akbura.Generator/') -or $_.StartsWith('src/Akbura.BlackSilence/') }) {
        if ($line -notmatch '^(.*) ([A-F0-9]{64})$') { throw 'Invalid baseline source manifest.' }
        if ($Matches[1] -ne 'src/Akbura.BlackSilence/BlackSilenceWatchObservation.cs' -and (Get-FileHash -LiteralPath $Matches[1]).Hash -ne $Matches[2]) {
            throw 'Production compiler source changed beyond the preregistered STATS observation extension.'
        }
    }
    $files=Sources
    $manifest=@{ Revision=(& git rev-parse HEAD); Files=$files; Fingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($files -join "`n"))))
        Rounds=$Rounds; Updates=$Updates; SeedDirectory=$SeedDirectory; SeedProofSha256=(Get-FileHash -LiteralPath (Join-Path $SeedDirectory 'baseline.json')).Hash
        ResultDirectory=$ResultDirectory; PreflightOnly=[bool]$PreflightOnly }
    $manifestPath=Join-Path $OutputDirectory 'sources.json'
    if (Test-Path -LiteralPath $manifestPath) {
        $previous=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($previous.Fingerprint -ne $manifest.Fingerprint -or $previous.Rounds -ne $Rounds -or $previous.Updates -ne $Updates -or
            $previous.ResultDirectory -ne $ResultDirectory -or $previous.PreflightOnly -ne [bool]$PreflightOnly) { throw 'Incompatible source/protocol; use a new gate.' }
    } else { Save $manifestPath $manifest }
    $null = New-Item -ItemType Directory -Path $ResultDirectory -Force
    Copy-Item -LiteralPath $manifestPath -Destination $ResultDirectory -Force
    Copy-Item -LiteralPath (Join-Path $SeedDirectory 'baseline.json') -Destination $ResultDirectory -Force
    Save (Join-Path $ResultDirectory 'environment.json') @{ Runtime=(& dotnet --info | Out-String); PowerShell=$PSVersionTable.PSVersion.ToString(); OS=[Environment]::OSVersion.ToString()
        LocalTimeZone=[TimeZoneInfo]::Local.Id; TutorialRevision=(& git -C benchmarks/HotReload/Akbura-PurityUI-Tutorial rev-parse HEAD)
        ParentClock='Stopwatch, completed write to verified UI acknowledgment'; DriverClock='EventPipe trace-local paired spans with same-process revision/hash markers'
        Bootstrap='10000 paired-round session-cluster draws'; SourceMutation='STATS-only observability; original non-STATS compiler binaries reused' }
    $null=Run 'build-host' @('dotnet','build','benchmarks/HotReload/HotReloadBenchmarks.csproj','--artifacts-path',(Join-Path $OutputDirectory 'host'),"-p:CompilerToolDirectory=$(Join-Path $SeedDirectory 'baseline-stats')",'-p:UseSharedCompilation=false','-nr:false','--nologo','-v:minimal')
    $null=Run 'build-trace' @('dotnet','build','benchmarks/HotReload/CriticalPathTrace/CriticalPathTrace.csproj','--artifacts-path',(Join-Path $OutputDirectory 'trace'),'-p:UseSharedCompilation=false','-nr:false','--nologo','-v:minimal')
    $null=Run 'test-trace' @('pwsh','-NoProfile','-File',(Join-Path $PSScriptRoot 'Test-PurityTrace.ps1'))
    $null=Run 'test-statistics' @('pwsh','-NoProfile','-File',(Join-Path $PSScriptRoot 'Test-PurityCriticalPathSummary.ps1'))
    $unitDirectory=Join-Path $OutputDirectory 'unit-observation'
    $null=Run 'test-observation' @('dotnet','test','src/Akbura.UnitTests/Akbura.UnitTests.csproj','-c','DebugStats','-p:ProbeReuseStage=combined',
        '--artifacts-path',$unitDirectory,'--filter','FullyQualifiedName~BlackSilenceCriticalPathObservationTests|FullyQualifiedName~BlackSilenceGenerationStatisticsTests',
        '--logger','trx;LogFileName=critical-observation.trx','--results-directory',(Join-Path $unitDirectory 'results'),'-p:UseSharedCompilation=false','-nr:false','--nologo','-v:minimal')
    $testArchive=Join-Path $ResultDirectory 'tests'; $null=New-Item -ItemType Directory -Path $testArchive -Force
    Copy-Item -LiteralPath (Join-Path $unitDirectory 'results/critical-observation.trx') -Destination $testArchive -Force
    $collector=Join-Path $OutputDirectory 'trace/bin/CriticalPathTrace/debug/CriticalPathTrace.dll'
    $tools=@{}
    foreach ($variant in @('baseline','projection','binding','combined')) {
        foreach ($profile in @('production','stats')) {
            $name="$variant-$profile"; $destination=Join-Path $OutputDirectory "tools/$name"; $proofPath=Join-Path $OutputDirectory "$name.closure.json"
            $build=Join-Path $OutputDirectory "build/$name"
            if ($profile -eq 'stats') { $null=Run "build-$name" @('dotnet','build','src/Akbura.BlackSilence/Akbura.BlackSilence.csproj','-c','DebugStats',"-p:ProbeReuseStage=$variant",'--artifacts-path',$build,'-p:UseSharedCompilation=false','-nr:false','--nologo','-v:minimal') }
            if (-not (Test-Path -LiteralPath $proofPath)) {
                if (Test-Path -LiteralPath $destination) { throw 'An incomplete closure must be preserved; choose a fresh gate.' }
                $origin=Get-Content -LiteralPath (Join-Path $SeedDirectory "$name.closure.json") -Raw | ConvertFrom-Json
                foreach ($binary in $origin.Binaries) { if ((Get-FileHash -LiteralPath (Join-Path $SeedDirectory "$name/$($binary.File)")).Hash -ne $binary.Sha256) { throw 'Frozen baseline closure changed.' } }
                $null=New-Item -ItemType Directory -Path (Split-Path $destination) -Force
                Copy-Item -LiteralPath (Join-Path $SeedDirectory $name) -Destination $destination -Recurse
                if ($profile -eq 'stats') {
                    foreach ($assembly in @('Akbura.Generator','Akbura.BlackSilence')) { foreach ($extension in @('dll','pdb')) {
                        Copy-Item -LiteralPath (Join-Path $build "bin/$assembly/debugstats/$assembly.$extension") -Destination $destination -Force
                    } }
                }
                foreach ($file in @('HotReloadBenchmarks.dll','HotReloadBenchmarks.pdb','HotReloadBenchmarks.deps.json','HotReloadBenchmarks.runtimeconfig.json')) {
                    Copy-Item -LiteralPath (Join-Path $OutputDirectory "host/bin/HotReloadBenchmarks/debug/$file") -Destination $destination -Force
                }
                Save $proofPath @{ Fingerprint=$manifest.Fingerprint; Variant=$variant; Profile=$profile; Origin=(Join-Path $SeedDirectory "$name.closure.json")
                    CompilerRebuiltForStatsTelemetry=($profile -eq 'stats'); Binaries=@(Get-ChildItem -LiteralPath $destination -File | ForEach-Object { @{ File=$_.Name; Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash } }) }
            }
            $proof=Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
            foreach ($binary in $proof.Binaries) { if ((Get-FileHash -LiteralPath (Join-Path $destination $binary.File)).Hash -ne $binary.Sha256) { throw 'Frozen measurement closure changed.' } }
            Copy-Item -LiteralPath $proofPath -Destination $ResultDirectory -Force
            $tools[$name]=$destination
        }
    }
    foreach ($scenario in @('supported','fallback','binding','compilation')) { foreach ($variant in @('baseline','projection','binding','combined')) {
        $name="diagnostic-$scenario-$variant"; $marker=Join-Path $OutputDirectory "$name.success.json"
        $session=if (Test-Path -LiteralPath $marker) { (Get-Content $marker -Raw | ConvertFrom-Json).Session } else { Join-Path $OutputDirectory "runs/$name-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))" }
        $saved=Run $name @('pwsh','-NoProfile','-File',(Join-Path $PSScriptRoot 'Run-PurityCriticalPath.ps1'),'-OutputDirectory',$session,'-ToolDirectory',$tools["$variant-stats"],
            '-Variant',$variant,'-Scenario',$scenario,'-Updates','5','-Warmups','3','-Instrumented','-TraceCollector',$collector) $session
        Archive (Join-Path $ResultDirectory "diagnostic/$scenario-$variant") $saved 'stats-diagnostic'
    } }
    foreach ($variant in @('baseline','projection','binding','combined')) {
        $name="control-member-chain-$variant"; $marker=Join-Path $OutputDirectory "$name.success.json"
        $session=if (Test-Path -LiteralPath $marker) { (Get-Content $marker -Raw | ConvertFrom-Json).Session } else { Join-Path $OutputDirectory "runs/$name-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))" }
        $saved=Run $name @('pwsh','-NoProfile','-File',(Join-Path $PSScriptRoot 'Verify-SupportedWatch.ps1'),'-OutputDirectory',$session,'-ToolDirectory',$tools["$variant-stats"],
            '-Variant',$variant,'-Scenario','margin','-Updates','5','-Warmups','2','-Instrumented') $session
        $control=Get-Content -LiteralPath (Join-Path $saved.Session 'result.json') -Raw | ConvertFrom-Json
        if ($control.CausalSkipGate -notin @('Passed','PassedNegativeControl')) { throw 'The existing member-chain causal control did not pass.' }
        Archive (Join-Path $ResultDirectory "control/member-chain-$variant") $saved 'stats-diagnostic'
    }
    if ($PreflightOnly) { Status 'PreflightComplete'; Log 'COMPLETE diagnostic preflight only; no primary timing, production change or default decision.'; return }
    $orders=@(@('baseline','projection','binding','combined'),@('binding','combined','baseline','projection'),@('combined','binding','projection','baseline'),@('projection','baseline','combined','binding'))
    foreach ($scenario in @('supported','fallback')) {
        $roundCount=if ($scenario -eq 'supported') { $Rounds } else { 3 }
        $sampleCount=if ($scenario -eq 'supported') { $Updates } else { 20 }
        foreach ($round in 1..$roundCount) { foreach ($variant in $orders[($round-1)%$orders.Count]) {
            $name='production-{0}-{1:D2}-{2}' -f $scenario,$round,$variant; $marker=Join-Path $OutputDirectory "$name.success.json"
            $session=if (Test-Path -LiteralPath $marker) { (Get-Content $marker -Raw | ConvertFrom-Json).Session } else { Join-Path $OutputDirectory "runs/$name-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))" }
            $saved=Run $name @('pwsh','-NoProfile','-File',(Join-Path $PSScriptRoot 'Run-PurityCriticalPath.ps1'),'-OutputDirectory',$session,'-ToolDirectory',$tools["$variant-production"],
                '-Variant',$variant,'-Scenario',$scenario,'-Updates',"$sampleCount",'-Warmups','3') $session -Timing
            Archive (Join-Path $ResultDirectory ('watch/sidebar/{0}/round-{1:D2}-{2}' -f $scenario,$round,$variant)) $saved 'production'
        } }
    }
    $null=Run 'summarize' @('pwsh','-NoProfile','-File',(Join-Path $PSScriptRoot 'Summarize-PurityCriticalPath.ps1'),'-ResultDirectory',$ResultDirectory,'-Rounds',"$Rounds",'-Updates',"$Updates")
    Status 'EvidenceCompleteNeedsReview'
    Log 'COMPLETE real Sidebar critical-path matrix ready. Review production confidence intervals, direct Roslyn spans and causal controls. No optimization, default activation, memory extension, commit, push or merge performed.'
}
catch { Status 'Failed' ($_ | Out-String); Log ('FAILED '+($_ | Out-String)); exit 1 }
finally { $journal.Dispose(); $lock.Dispose() }
