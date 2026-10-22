#requires -Version 7.2
param(
    [string]$OutputDirectory = 'bin/SemanticWatch/gate',
    [string]$ResultDirectory = 'benchmarks/HotReload/results/2026-10-22-semantic-watch-validation',
    [string]$SeedDirectory = 'bin/SemanticReuse/gate-release-fix/tools/baseline-stats',
    [string]$ReuseCompilerEvidence = '',
    [ValidateRange(3, 6)][int]$Rounds = 3,
    [ValidateRange(20, 50)][int]$Updates = 20,
    [switch]$Worker
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repository
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$SeedDirectory = [IO.Path]::GetFullPath($SeedDirectory)
if ($ReuseCompilerEvidence) { $ReuseCompilerEvidence = [IO.Path]::GetFullPath($ReuseCompilerEvidence) }
$encoding = [Text.UTF8Encoding]::new($false)
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
if (-not $Worker) {
    $arguments = @('-NoProfile','-File',$PSCommandPath,'-Worker','-OutputDirectory',$OutputDirectory,
        '-ResultDirectory',$ResultDirectory,'-SeedDirectory',$SeedDirectory,'-Rounds',"$Rounds",'-Updates',"$Updates")
    if ($ReuseCompilerEvidence) { $arguments += @('-ReuseCompilerEvidence',$ReuseCompilerEvidence) }
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $process = Start-Process (Get-Command pwsh).Source -WindowStyle Hidden -PassThru -WorkingDirectory $repository `
        -ArgumentList @($arguments | ForEach-Object { '"' + $_ + '"' }) `
        -RedirectStandardOutput (Join-Path $OutputDirectory "controller-$stamp.stdout.log") `
        -RedirectStandardError (Join-Path $OutputDirectory "controller-$stamp.stderr.log")
    Write-Output "Controller PID: $($process.Id)"
    Write-Output "Progress: Get-Content -LiteralPath '$OutputDirectory/journal.log' -Tail 20 -Wait"
    return
}

$lock = [IO.File]::Open((Join-Path $OutputDirectory 'controller.lock'),'OpenOrCreate','ReadWrite','None')
$journal = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'journal.log'),$true,$encoding)
$journal.AutoFlush = $true
$step = 'Starting'
$childPid = $null
$completed = 0
function Json([string]$path, $value) {
    $text = [regex]::Replace(($value | ConvertTo-Json -Depth 12),'\r?\n',"`r`n") + "`r`n"
    [IO.File]::WriteAllText(($path + '.tmp'),$text,$encoding)
    [IO.File]::Move(($path + '.tmp'),$path,$true)
}
function Log([string]$message) {
    $journal.WriteLine([DateTime]::UtcNow.ToString('o') + ' ' + $message)
    $journal.BaseStream.Flush($true)
}
function Status([string]$phase, [string]$failure = '') {
    Json (Join-Path $OutputDirectory 'status.json') @{ Phase = $phase; Current = $script:step; CurrentPid = $script:childPid
        ControllerPid = $PID; CompletedSteps = $script:completed; Failure = $failure; UpdatedUtc = [DateTime]::UtcNow.ToString('o') }
}
function Source-Hashes {
    @(& git ls-files --cached --others --exclude-standard -- 'src/Akbura.Generator' 'src/Akbura.BlackSilence' 'src/Akbura.UnitTests' 'benchmarks/HotReload' |
        Where-Object { -not $_.StartsWith('benchmarks/HotReload/results/') -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Sort-Object -Unique | ForEach-Object { "$_ $((Get-FileHash -LiteralPath $_).Hash)" })
}
function Assert-Sources {
    if (((Source-Hashes) -join "`n") -cne ($script:manifest.Files -join "`n")) { throw 'Sources changed; preserve evidence and select a new output directory.' }
}
function Foreign-Builds {
    $all = @(Get-CimInstance Win32_Process)
    $owned = [Collections.Generic.HashSet[int]]::new()
    $null = $owned.Add($PID)
    do {
        $changed = $false
        foreach ($process in $all) {
            if ($owned.Contains([int]$process.ParentProcessId) -and $owned.Add([int]$process.ProcessId)) { $changed = $true }
        }
    } while ($changed)
    @($all | Where-Object { -not $owned.Contains([int]$_.ProcessId) -and
        $_.CommandLine -notmatch '(?i)[/-]nodemode' -and
        ($_.Name -match '^(testhost|MSBuild|csc)\.exe$' -or
        $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)(\s(test|build|vstest)\s|MSBuild\.dll|csc\.dll|HotReloadBenchmarks\.dll|dotnet-watch\.dll)') })
}
function Run([string]$name, [string[]]$arguments, [switch]$Timing, [string]$WatchDirectory = '') {
    Assert-Sources
    $script:step = $name
    $marker = Join-Path $OutputDirectory "$name.success.json"
    if (Test-Path -LiteralPath $marker) {
        $saved = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($saved.Fingerprint -ne $manifest.Fingerprint) { throw "Incompatible completed step: $name" }
        $script:completed++
        Log "REUSED $name"
        return
    }
    if ($Timing) {
        while (@(Foreign-Builds).Count) { Status 'WaitingForOtherBuilds'; Log "WAIT $name foreign build/test activity"; Start-Sleep -Seconds 15 }
    }
    $logs = Join-Path $OutputDirectory ($name + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
    $null = New-Item -ItemType Directory -Path $logs
    $info = [Diagnostics.ProcessStartInfo]::new($arguments[0])
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.WorkingDirectory = $repository
    foreach ($argument in $arguments | Select-Object -Skip 1) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $stdout = [IO.FileStream]::new((Join-Path $logs 'stdout.log'),'CreateNew','Write','ReadWrite')
    $stderr = [IO.FileStream]::new((Join-Path $logs 'stderr.log'),'CreateNew','Write','ReadWrite')
    $contaminated = $false
    try {
        $null = $process.Start()
        $script:childPid = $process.Id
        $copyOut = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $copyErr = $process.StandardError.BaseStream.CopyToAsync($stderr)
        Log "START $name pid=$script:childPid logs=$logs"
        Status 'Running'
        while (-not $process.WaitForExit(15000)) {
            $stdout.Flush($true); $stderr.Flush($true)
            if ($Timing -and @(Foreign-Builds).Count) { $contaminated = $true }
            $last = ''
            if ($WatchDirectory -and (Test-Path -LiteralPath (Join-Path $WatchDirectory 'watch.jsonl'))) {
                $last = @(Get-Content -LiteralPath (Join-Path $WatchDirectory 'watch.jsonl') -Tail 20 | ForEach-Object { try { $_ | ConvertFrom-Json -ErrorAction Stop } catch { } } |
                    Where-Object Kind -eq 'semantic-watch-sample' | Select-Object -Last 1 | ForEach-Object { " revision=$($_.Revision) phase=$($_.Phase)" }) -join ''
            }
            Status 'Running'
            Log "HEARTBEAT $name parentCpuSeconds=$($process.TotalProcessorTime.TotalSeconds)$last contaminated=$contaminated"
            if (([DateTime]::Now - $process.StartTime).TotalSeconds -gt $(if ($WatchDirectory) { 600 } else { 3600 })) {
                $process.Kill($true)
                throw "Owned child exceeded the protocol timeout: $name"
            }
        }
        $null = $copyOut.GetAwaiter().GetResult(); $null = $copyErr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Exit $($process.ExitCode): $logs" }
        Assert-Sources
        if ($contaminated) { throw "TimingContamination: preserve $WatchDirectory and retry this group after foreign activity finishes." }
        Json $marker @{ Fingerprint = $manifest.Fingerprint; Arguments = $arguments; Logs = $logs; CompletedUtc = [DateTime]::UtcNow.ToString('o') }
        $script:completed++
        Log "DONE $name"
    }
    finally { $stdout.Dispose(); $stderr.Dispose(); $process.Dispose(); $script:childPid = $null }
}
function Freeze([string]$variant, [string]$buildProfile) {
    $name = "$variant-$buildProfile"
    $build = Join-Path $OutputDirectory "build/$name"
    $configuration = if ($buildProfile -eq 'stats') { 'DebugStats' } else { 'Release' }
    if (-not $ReuseCompilerEvidence) {
        Run "build-$name" @('dotnet','build','src/Akbura.BlackSilence/Akbura.BlackSilence.csproj','-c',$configuration,
            "-p:ProbeReuseStage=$variant",'--artifacts-path',$build,'-p:UseSharedCompilation=false','-nr:false','--nologo','-v:minimal')
    }
    $destination = Join-Path $OutputDirectory "tools/$name"
    $proof = Join-Path $OutputDirectory "$name.closure.json"
    if (-not (Test-Path -LiteralPath $proof)) {
        if (Test-Path -LiteralPath $destination) { throw "Incomplete frozen closure: $destination" }
        $compilerProof = $null
        if ($ReuseCompilerEvidence) {
            $compilerProof = Join-Path $ReuseCompilerEvidence "$name.closure.json"
            $previous = Get-Content -LiteralPath $compilerProof -Raw | ConvertFrom-Json
            if ($previous.Fingerprint -ne $reuseManifest.Fingerprint -or $previous.Variant -ne $variant -or $previous.Configuration -ne $configuration) {
                throw "Incompatible compiler evidence: $name"
            }
            $frozen = Join-Path $ReuseCompilerEvidence "tools/$name"
            foreach ($binary in $previous.Binaries) {
                if ((Get-FileHash -LiteralPath (Join-Path $frozen $binary.File)).Hash -ne $binary.Sha256) { throw "Previous frozen closure changed: $name" }
            }
            Copy-Item -LiteralPath $frozen -Destination $destination -Recurse
            Log "REUSED_COMPILER $name exact verified binaries; host will be rebuilt"
        }
        else {
            Copy-Item -LiteralPath $SeedDirectory -Destination $destination -Recurse
            foreach ($assembly in @('Akbura.Generator','Akbura.BlackSilence')) {
                $binary = @(Get-ChildItem -LiteralPath (Join-Path $build "bin/$assembly") -File -Filter "$assembly.dll" -Recurse)
                if ($binary.Count -ne 1) { throw "Expected one compiler binary: $name/$assembly" }
                foreach ($extension in @('dll','pdb')) {
                    Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($binary[0].FullName,$extension)) -Destination (Join-Path $destination "$assembly.$extension") -Force
                }
            }
        }
        foreach ($file in @('HotReloadBenchmarks.dll','HotReloadBenchmarks.pdb','HotReloadBenchmarks.deps.json','HotReloadBenchmarks.runtimeconfig.json')) {
            Copy-Item -LiteralPath (Join-Path $OutputDirectory "host-build/bin/HotReloadBenchmarks/debug/$file") -Destination (Join-Path $destination $file) -Force
        }
        Json $proof @{ Fingerprint = $manifest.Fingerprint; Variant = $variant; Configuration = $configuration; ReusedCompilerClosure = $compilerProof
            Binaries = @(Get-ChildItem -LiteralPath $destination -File | ForEach-Object { @{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } }) }
    }
    $saved = Get-Content -LiteralPath $proof -Raw | ConvertFrom-Json
    foreach ($binary in $saved.Binaries) {
        if ((Get-FileHash -LiteralPath (Join-Path $destination $binary.File)).Hash -ne $binary.Sha256) { throw 'Frozen closure changed.' }
    }
    Copy-Item -LiteralPath $proof -Destination $ResultDirectory -Force
    Run "instrumentation-$name" @('dotnet',(Join-Path $destination 'HotReloadBenchmarks.dll'),'watch-instrumentation-check',$(if ($buildProfile -eq 'stats') { 'stats' } else { 'off' }))
    return $destination
}
function Reuse-Quality([string]$variant) {
    $origin = Join-Path $reuseManifest.ResultDirectory "tests/$variant"
    $results = Join-Path $ResultDirectory "tests/$variant"
    $proofs = @()
    foreach ($profile in @('release','debugstats','sdk')) {
        $marker = Join-Path $ReuseCompilerEvidence "$profile-$variant.success.json"
        $saved = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($saved.Fingerprint -ne $reuseManifest.Fingerprint) { throw 'Previous test evidence has incompatible sources.' }
        $trx = if ($profile -eq 'sdk') { Join-Path $origin 'sdk/sdk-invalidation.trx' } else { Join-Path $origin "$profile.trx" }
        [xml]$testResults = Get-Content -LiteralPath $trx -Raw
        if ([int]$testResults.TestRun.ResultSummary.Counters.failed -ne 0 -or [int]$testResults.TestRun.ResultSummary.Counters.passed -le 0) {
            throw "Previous correctness evidence did not pass: $trx"
        }
        $proofs += @{ Marker = $marker; MarkerSha256 = (Get-FileHash -LiteralPath $marker).Hash; CompletedUtc = $saved.CompletedUtc
            TestResults = $trx; TestResultsSha256 = (Get-FileHash -LiteralPath $trx).Hash; Passed = [int]$testResults.TestRun.ResultSummary.Counters.passed }
        $script:completed++
        Log "REUSED_CORRECTNESS $profile-$variant previous execution $($saved.CompletedUtc); unchanged compiler and test sources, not a new test run"
    }
    $null = New-Item -ItemType Directory -Path $results -Force
    Get-ChildItem -LiteralPath $origin -File | Copy-Item -Destination $results -Force
    $sdkResults = Join-Path $results 'sdk'
    $null = New-Item -ItemType Directory -Path $sdkResults -Force
    Get-ChildItem -LiteralPath (Join-Path $origin 'sdk') -File | Copy-Item -Destination $sdkResults -Force
    Json (Join-Path $results 'reused-evidence.json') @{ OriginFingerprint = $reuseManifest.Fingerprint; CurrentFingerprint = $manifest.Fingerprint
        Reason = 'Only the allowlisted watch harness changed. Compiler/test sources and frozen compiler binaries are identical.'; Executions = $proofs }
}
function Watch([string]$name, [string]$variant, [string]$buildProfile, [string]$scenario, [int]$count, [int]$warmups = 2) {
    $marker = Join-Path $OutputDirectory "$name.success.json"
    if (Test-Path -LiteralPath $marker) {
        $saved = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($saved.Fingerprint -ne $manifest.Fingerprint) { throw "Incompatible completed watch step: $name" }
        $argumentIndex = [array]::IndexOf(@($saved.Arguments), '-OutputDirectory')
        if ($argumentIndex -lt 0) { throw 'Completed watch step has no preserved attempt directory.' }
        $attempt = $saved.Arguments[$argumentIndex + 1]
        $script:completed++
        Log "REUSED $name"
    }
    else { while ($true) {
        $attempt = Join-Path $OutputDirectory "runs/$name-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))"
        $arguments = @('pwsh','-NoProfile','-File',(Join-Path $PSScriptRoot 'Verify-SupportedWatch.ps1'),'-OutputDirectory',$attempt,
            '-ToolDirectory',$script:tools["$variant-$buildProfile"],'-Scenario',$scenario,'-Variant',$variant,'-Updates',"$count",'-Warmups',"$warmups")
        if ($buildProfile -eq 'stats') { $arguments += '-Instrumented' }
        try { Run $name $arguments -Timing:($buildProfile -eq 'production') -WatchDirectory $attempt; break }
        catch { if ($_ -notlike '*TimingContamination:*') { throw }; Log "RETRY $name contaminated attempt preserved" }
    } }
    $archive = Join-Path $ResultDirectory "runs/$name"
    $null = New-Item -ItemType Directory -Path $archive -Force
    foreach ($file in @('watch.jsonl','result.json','restore.log','build.log')) { Copy-Item -LiteralPath (Join-Path $attempt $file) -Destination (Join-Path $archive $file) }
    foreach ($directory in @('analyzer-observations','fixture')) {
        $source = Join-Path $attempt $directory
        if (Test-Path -LiteralPath $source) {
            $target = Join-Path $archive $directory
            $null = New-Item -ItemType Directory -Path $target -Force
            Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $target
        }
    }
    Json (Join-Path $archive 'acceptance.json') @{ Fingerprint = $manifest.Fingerprint; Attempt = $attempt; TimingContaminationObserved = $false; Profile = $buildProfile }
    $result = Get-Content -LiteralPath (Join-Path $archive 'result.json') -Raw | ConvertFrom-Json
    if ($result.CausalSkipGate -eq 'UnexpectedReuse') { throw "Unexpected semantic reuse in a negative control: $name" }
}

try {
    $previousStatus = Join-Path $OutputDirectory 'status.json'
    if (Test-Path -LiteralPath $previousStatus) {
        $previous = Get-Content -LiteralPath $previousStatus -Raw | ConvertFrom-Json
        if ($previous.CurrentPid) {
            $active = Get-CimInstance Win32_Process -Filter "ProcessId = $($previous.CurrentPid)" -ErrorAction SilentlyContinue
            if ($active -and $active.CommandLine -and $active.CommandLine.Contains($OutputDirectory)) { throw 'A previous owned child is still running; do not launch a duplicate.' }
        }
    }
    $files = Source-Hashes
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($files -join "`n"))))
    $reuseManifest = $null
    $reuseProof = $null
    if ($ReuseCompilerEvidence) {
        if ($ReuseCompilerEvidence -eq $OutputDirectory) { throw 'Compiler evidence must be preserved in a separate output directory.' }
        $originManifest = Join-Path $ReuseCompilerEvidence 'sources.json'
        $reuseManifest = Get-Content -LiteralPath $originManifest -Raw | ConvertFrom-Json
        $originFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($reuseManifest.Files -join "`n"))))
        if ($originFingerprint -ne $reuseManifest.Fingerprint) { throw 'Previous source manifest fingerprint is invalid.' }
        if ($reuseManifest.Revision -ne (& git rev-parse HEAD)) { throw 'Compiler evidence must have the same starting commit.' }
        $allowed = @('benchmarks/HotReload/Verify-SemanticWatch.ps1','benchmarks/HotReload/Program.SemanticWatch.cs',
            'benchmarks/HotReload/SupportedWatchFixture/Program.cs')
        $oldFiles = @{}; $newFiles = @{}
        foreach ($line in $reuseManifest.Files) { if ($line -notmatch '^(.*) ([A-F0-9]{64})$') { throw 'Invalid origin source manifest.' }; $oldFiles[$Matches[1]] = $Matches[2] }
        foreach ($line in $files) { if ($line -notmatch '^(.*) ([A-F0-9]{64})$') { throw 'Invalid current source manifest.' }; $newFiles[$Matches[1]] = $Matches[2] }
        $differences = @(@($oldFiles.Keys) + @($newFiles.Keys) | Sort-Object -Unique | Where-Object { $oldFiles[$_] -cne $newFiles[$_] })
        foreach ($path in $differences) { if ($path -notin $allowed) { throw "Compiler/test or other non-allowlisted input changed: $path" } }
        $reuseProof = @{ Manifest = $originManifest; ManifestSha256 = (Get-FileHash -LiteralPath $originManifest).Hash
            Fingerprint = $reuseManifest.Fingerprint; HarnessChanges = $differences; NoNewCompilerTestExecution = $true }
    }
    $manifestPath = Join-Path $OutputDirectory 'sources.json'
    $seedHashes = @(Get-ChildItem -LiteralPath $SeedDirectory -File | Sort-Object Name | ForEach-Object { "$($_.Name) $((Get-FileHash -LiteralPath $_.FullName).Hash)" })
    $manifest = @{ Revision = (& git rev-parse HEAD); Fingerprint = $fingerprint; Files = $files; Rounds = $Rounds; Updates = $Updates
        SeedHashes = $seedHashes; ResultDirectory = $ResultDirectory; ReuseCompilerEvidence = $ReuseCompilerEvidence; ReuseProof = $reuseProof }
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.Fingerprint -ne $fingerprint -or $manifest.Rounds -ne $Rounds -or $manifest.Updates -ne $Updates -or
            $manifest.ResultDirectory -ne $ResultDirectory -or $manifest.ReuseCompilerEvidence -ne $ReuseCompilerEvidence -or
            ($ReuseCompilerEvidence -and $manifest.ReuseProof.ManifestSha256 -ne $reuseProof.ManifestSha256) -or
            ($manifest.SeedHashes -join "`n") -cne ($seedHashes -join "`n")) { throw 'Sources, seed or protocol changed; use a new directory.' }
    }
    else { Json $manifestPath $manifest }
    $null = New-Item -ItemType Directory -Path $ResultDirectory -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'tools') -Force
    Copy-Item -LiteralPath $manifestPath -Destination $ResultDirectory -Force
    Run 'build-host' @('dotnet','build','benchmarks/HotReload/HotReloadBenchmarks.csproj','-c','Debug',
        '--artifacts-path',(Join-Path $OutputDirectory 'host-build'),"-p:CompilerToolDirectory=$SeedDirectory",'-p:UseSharedCompilation=false','-nr:false','--nologo','-v:minimal')
    Run 'summary-guards' @('pwsh','-NoProfile','-File',(Join-Path $PSScriptRoot 'Test-SemanticWatchSummary.ps1'))
    $tools = @{}
    foreach ($variant in @('baseline','projection','binding','combined')) {
        foreach ($buildProfile in @('stats','production')) { $tools["$variant-$buildProfile"] = @(Freeze $variant $buildProfile)[0] }
    }
    Json (Join-Path $ResultDirectory 'controls.json') @{ Sources = $manifest.Fingerprint; Closures = @($tools.Keys | Sort-Object)
        Runtime = (& dotnet --info | Out-String); Environment = @(Get-ChildItem Env: | Where-Object { $_.Name -in @('DOTNET_CLI_UI_LANGUAGE','DOTNET_MODIFIABLE_ASSEMBLIES','DOTNET_ROOT') } | Select-Object Name,Value) }
    $debugFilter = '(FullyQualifiedName~SemanticReuse|FullyQualifiedName~WatchObservation|FullyQualifiedName~Localized|FullyQualifiedName~MarkupExpressionReuseTests|FullyQualifiedName~ProbeDeclarationChanges|FullyQualifiedName~ProbeCompilationChanges|FullyQualifiedName~ProbeErrorsAndRepairs|FullyQualifiedName~ConcurrentProbeBranches|FullyQualifiedName~CancellationDuringProbeBinding|FullyQualifiedName~BlackSilenceGenerationStatisticsTests|FullyQualifiedName~BlackSilenceDiagnostic|FullyQualifiedName~InvalidationTrace_UnchangedCompilationSkipsCallbacks)&FullyQualifiedName!~LiveStateKeepsOnlyBounded'
    foreach ($variant in @('binding','combined')) {
        if ($ReuseCompilerEvidence) { Reuse-Quality $variant; continue }
        $artifacts = Join-Path $OutputDirectory "tests-$variant"
        $results = Join-Path $ResultDirectory "tests/$variant"
        Run "release-$variant" @('dotnet','test','src/Akbura.UnitTests/Akbura.UnitTests.csproj','-c','Release',"-p:ProbeReuseStage=$variant",'--artifacts-path',$artifacts,
            '--filter','(FullyQualifiedName~SemanticReuse|FullyQualifiedName~Localized|FullyQualifiedName~MarkupExpressionReuseTests|FullyQualifiedName~HotReload|FullyQualifiedName~RenderRevision|FullyQualifiedName~CollectionAllocationTests)&FullyQualifiedName!~LiveStateKeepsOnlyBounded',
            '--logger','trx;LogFileName=release.trx','--results-directory',$results,'--nologo','-v:minimal','-p:UseSharedCompilation=false','-nr:false')
        Run "debugstats-$variant" @('dotnet','test','src/Akbura.UnitTests/Akbura.UnitTests.csproj','-c','DebugStats',"-p:ProbeReuseStage=$variant",'--artifacts-path',$artifacts,
            '--filter',$debugFilter,'--logger','trx;LogFileName=debugstats.trx','--results-directory',$results,'--nologo','-v:minimal','-p:UseSharedCompilation=false','-nr:false')
        [xml]$trx = Get-Content -LiteralPath (Join-Path $results 'debugstats.trx') -Raw
        $sdkCount = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match 'SemanticReuse|WatchObservation|InvalidationTrace_UnchangedCompilationSkipsCallbacks' }).Count
        $sdk = Join-Path $OutputDirectory "sdk-$variant"
        Run "sdk-$variant" @('pwsh','-NoProfile','-File',(Join-Path $PSScriptRoot 'Verify-SdkRoslyn.ps1'),'-TestDirectory',(Join-Path $artifacts 'bin/Akbura.UnitTests/debugstats'),
            '-OutputDirectory',$sdk,'-TestFilter','(FullyQualifiedName~SemanticReuse|FullyQualifiedName~WatchObservation|FullyQualifiedName~InvalidationTrace_UnchangedCompilationSkipsCallbacks)&FullyQualifiedName!~LiveStateKeepsOnlyBounded','-ExpectedTestCount',"$sdkCount")
        $target = Join-Path $results 'sdk'
        $null = New-Item -ItemType Directory -Path $target -Force
        Get-ChildItem -LiteralPath $sdk -Directory | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -File | Copy-Item -Destination $target -Force }
    }
    foreach ($variant in @('baseline','projection','binding','combined')) {
        Watch "causal-$variant-margin" $variant 'stats' 'margin' 5
        Watch "negative-$variant-text" $variant 'stats' 'text' 3 0
        Watch "negative-$variant-scope" $variant 'stats' 'scope' 2 0
        Watch "negative-$variant-compilation" $variant 'stats' 'compilation' 2 0
    }
    Watch 'diagnostic-baseline' 'baseline' 'stats' 'stress' 1 0
    Watch 'stress-combined' 'combined' 'stats' 'stress' 100 2
    for ($round = 1; $round -le $Rounds; $round++) {
        $order = switch ($round % 3) { 1 { @('baseline','projection','binding','combined') } 2 { @('combined','binding','projection','baseline') } 0 { @('binding','baseline','combined','projection') } }
        foreach ($scenario in @('margin','text')) {
            foreach ($variant in $order) { Watch "production-$round-$variant-$scenario" $variant 'production' $scenario $Updates }
        }
    }
    Run 'summarize' @('pwsh','-NoProfile','-File',(Join-Path $PSScriptRoot 'Summarize-SemanticWatch.ps1'),'-ResultDirectory',$ResultDirectory,'-Rounds',"$Rounds",'-Updates',"$Updates")
    Status 'EvidenceReadyNeedsReview'
    Log 'COMPLETE Live semantic watch evidence is ready. Review per-revision causality, negative controls, stress, source/assembly provenance and independent production rounds. No default activation, commit, push or next stage was performed.'
}
catch { Status 'Failed' ($_ | Out-String); Log ('FAILED ' + ($_ | Out-String)); exit 1 }
finally { $journal.Dispose(); $lock.Dispose() }
