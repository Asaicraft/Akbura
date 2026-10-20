#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$ResultDirectory,
    [switch]$Resume,
    [switch]$SkipBenchmarks,
    [switch]$CollectionAllocations,
    [switch]$LocalizedEdits
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and -not $Resume) {
    throw 'Use a new output directory, or -Resume to preserve and reuse successful verification steps.'
}
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$journal = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'journal.log'), $true, [Text.UTF8Encoding]::new($false))
$journal.AutoFlush = $true
$lock = [IO.File]::Open((Join-Path $OutputDirectory 'verification.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$step = 'Waiting for existing builds/tests'
$childPid = $null
$statusPath = Join-Path $OutputDirectory 'status.json'
$sourcePaths = @(& git -C $repository ls-files --cached --others --exclude-standard -- 'src/Akbura.Generator' 'src/Akbura.BlackSilence' 'src/Akbura.UnitTests' 'benchmarks/HotReload/*.cs' 'benchmarks/HotReload/*.ps1') | Sort-Object -Unique
$sourceHashes = foreach ($path in $sourcePaths) {
    $absolute = Join-Path $repository $path
    if (Test-Path -LiteralPath $absolute -PathType Leaf) {
        "$path $((Get-FileHash -LiteralPath $absolute).Hash)"
    }
}
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($sourceHashes -join "`n"))))
$manifestPath = Join-Path $OutputDirectory 'source-manifest.json'
$verificationMode = if ($LocalizedEdits) { 'LocalizedEdits' } elseif ($CollectionAllocations) { 'CollectionAllocations' } else { 'SemanticDependencies' }
if (Test-Path -LiteralPath $manifestPath) {
    $previousManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($previousManifest.Fingerprint -ne $fingerprint) {
        throw 'Implementation sources changed. Use a new output directory; never resume evidence from different source revisions.'
    }
    if ($previousManifest.VerificationMode -and $previousManifest.VerificationMode -ne $verificationMode) {
        throw 'Verification mode changed. Use a new output directory rather than reusing different test selections.'
    }
}
else {
    [IO.File]::WriteAllText($manifestPath, (@{ Fingerprint = $fingerprint; Revision = (& git -C $repository rev-parse HEAD)
        Files = @($sourceHashes); VerificationMode = $verificationMode; StartedUtc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 5) + "`r`n", [Text.UTF8Encoding]::new($false))
}

function Log([string]$message) {
    $line = [DateTime]::UtcNow.ToString('o') + ' ' + $message
    $journal.WriteLine($line)
    Write-Host $line
}

function Status([string]$phase, [string]$failure = '') {
    $value = @{ Phase = $phase; Current = $script:step; ChildPid = $script:childPid; ControllerPid = $PID
        UpdatedUtc = [DateTime]::UtcNow.ToString('o'); Failure = $failure; SourceFingerprint = $fingerprint }
    [IO.File]::WriteAllText(($statusPath + '.tmp'), ($value | ConvertTo-Json) + "`r`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::Move(($statusPath + '.tmp'), $statusPath, $true)
}

function Wait-Quiet {
    # Functional verification can overlap another worktree. The timing controller
    # independently defers and discards contaminated groups; these are not samples.
    if ($LocalizedEdits -and $SkipBenchmarks) {
        return
    }
    while (@(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'testhost.exe'" | Where-Object {
        $_.CommandLine -match '(?i)(\bdotnet(?:\.exe)?"?\s+(?:test|build|vstest)\b|vstest\.console\.dll|testhost(?:\.exe)?\b)'
    }).Count) {
        Status 'WaitingForOtherBuilds'
        Log 'WAIT Existing test/build activity; no verification child started.'
        Start-Sleep -Seconds 15
    }
}

function Assert-Sources {
    $currentPaths = @(& git -C $repository ls-files --cached --others --exclude-standard -- 'src/Akbura.Generator' 'src/Akbura.BlackSilence' 'src/Akbura.UnitTests' 'benchmarks/HotReload/*.cs' 'benchmarks/HotReload/*.ps1') | Sort-Object -Unique
    $currentHashes = foreach ($path in $currentPaths) {
        $absolute = Join-Path $repository $path
        if (Test-Path -LiteralPath $absolute -PathType Leaf) {
            "$path $((Get-FileHash -LiteralPath $absolute).Hash)"
        }
    }
    $currentFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($currentHashes -join "`n"))))
    if ($currentFingerprint -ne $fingerprint) {
        throw 'Implementation changed during verification. Preserve this attempt and start with a new output directory.'
    }
}

function Run([string]$name, [string]$executable, [string[]]$arguments) {
    $script:step = $name
    $marker = Join-Path $OutputDirectory ($name + '.success.json')
    if ($Resume -and (Test-Path -LiteralPath $marker)) {
        Log "SKIP Validated completed step $name"
        return (Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json).Directory
    }
    Wait-Quiet
    Assert-Sources
    $directory = Join-Path $OutputDirectory ($name + '-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $directory
    $arguments = @($arguments | ForEach-Object { $_.Replace('{RESULTS}', $directory) })
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($executable)
    $process.StartInfo.WorkingDirectory = $repository
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    foreach ($argument in $arguments) {
        $process.StartInfo.ArgumentList.Add($argument)
    }
    $writer = [IO.StreamWriter]::new((Join-Path $directory 'output.log'), $false, [Text.UTF8Encoding]::new($false))
    $errors = [IO.StreamWriter]::new((Join-Path $directory 'stderr.log'), $false, [Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    $errors.AutoFlush = $true
    $started = $false
    try {
        Log "START $name logs=$directory"
        $null = $process.Start()
        $started = $true
        $script:childPid = $process.Id
        Status 'Running'
        $stdout = $process.StandardOutput.ReadLineAsync()
        $stderr = $process.StandardError.ReadLineAsync()
        $heartbeat = [Diagnostics.Stopwatch]::StartNew()
        while ($null -ne $stdout -or $null -ne $stderr -or -not $process.HasExited) {
            foreach ($kind in @('stdout', 'stderr')) {
                $task = Get-Variable -Name $kind -ValueOnly
                if ($null -ne $task -and $task.IsCompleted) {
                    $line = $task.GetAwaiter().GetResult()
                    if ($null -eq $line) {
                        Set-Variable -Name $kind -Value $null
                    }
                    else {
                        if ($kind -eq 'stdout') {
                            $writer.WriteLine($line)
                            $stdout = $process.StandardOutput.ReadLineAsync()
                        }
                        else {
                            $errors.WriteLine($line)
                            $stderr = $process.StandardError.ReadLineAsync()
                        }
                        Write-Host $line
                    }
                }
            }
            if ($heartbeat.Elapsed.TotalSeconds -ge 15) {
                $process.Refresh()
                Log "HEARTBEAT $name pid=$script:childPid cpuSeconds=$($process.TotalProcessorTime.TotalSeconds)"
                Status 'Running'
                $heartbeat.Restart()
            }
            Start-Sleep -Milliseconds 100
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Exit $($process.ExitCode): $directory"
        }
        Assert-Sources
        $portable = Join-Path $ResultDirectory ('verification/' + $name)
        $null = New-Item -ItemType Directory -Path $portable -Force
        foreach ($file in Get-ChildItem -LiteralPath $directory -File -Recurse | Where-Object {
            $_.Name -in @('output.log', 'stderr.log', 'tests.trx', 'sdk-invalidation.trx', 'sdk-verification.json') -and
            $_.FullName -notmatch '[\\/]test-host[\\/]'
        }) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $portable $file.Name) -Force
        }
        [IO.File]::WriteAllText($marker, (@{ Directory = $directory; CompletedUtc = [DateTime]::UtcNow.ToString('o')
            Fingerprint = $fingerprint; Command = $executable; Arguments = $arguments } | ConvertTo-Json -Depth 5) + "`r`n", [Text.UTF8Encoding]::new($false))
        Log "DONE $name"
        return $directory
    }
    finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $script:childPid = $null
        $writer.Dispose()
        $errors.Dispose()
        $process.Dispose()
    }
}

function Matrix([string]$name, [string[]]$scenarios, [switch]$Off) {
    Assert-Sources
    $script:step = $name
    $output = Join-Path $OutputDirectory $name
    $report = if ($Off) { Join-Path $ResultDirectory 'profiling-off' } else { $ResultDirectory }
    $arguments = @{ OutputDirectory = $output }
    if (Test-Path -LiteralPath (Join-Path $output 'run.json')) {
        $state = Get-Content -LiteralPath (Join-Path $output 'status.json') -Raw | ConvertFrom-Json
        $active = Get-Process -Id $state.ControllerPid -ErrorAction SilentlyContinue
        if (-not $active -and $state.Phase -ne 'Complete') {
            & (Join-Path $PSScriptRoot 'Start-ContractBenchmarks.ps1') @arguments -Resume | Out-Host
        }
    }
    else {
        & (Join-Path $PSScriptRoot 'Start-ContractBenchmarks.ps1') @arguments -InputDirectory $InputDirectory -ResultDirectory $report `
            -Scenario $scenarios -SemanticDependencies -RetentionRevisions 100 -ConcurrentSamples 8 `
            -SkipLifetime:$Off -SkipWatch:$Off -Profiling $(if ($Off) { 'off' } else { 'stats' }) | Out-Host
    }
    while ($true) {
        Start-Sleep -Seconds 15
        $state = Get-Content -LiteralPath (Join-Path $output 'status.json') -Raw | ConvertFrom-Json
        Status 'Benchmarking'
        if ($state.Phase -eq 'Complete') {
            Log "DONE $name"
            break
        }
        if ($state.Phase -eq 'Failed') {
            throw "Benchmark failed: $($state.Failure)"
        }
        if (-not (Get-Process -Id $state.ControllerPid -ErrorAction SilentlyContinue)) {
            throw "Benchmark controller stopped without a terminal status: $output"
        }
        Log "PROGRESS $name $($state.Current) index=$($state.UpdatedIndex) groups=$($state.CompletedGroups)/$($state.TotalGroups)"
    }
}

try {
    Push-Location $repository
    $project = 'src/Akbura.UnitTests/Akbura.UnitTests.csproj'
    $null = Run 'build-debugstats' 'dotnet' @('build', $project, '-c', 'DebugStats', '--no-restore', '-p:UseSharedCompilation=false', '-nr:false', '-v:minimal')
    $filter = 'FullyQualifiedName~BlackSilence|FullyQualifiedName~AkburaProjectIndex|FullyQualifiedName~DocumentSyntaxVersion|FullyQualifiedName~GenerationStatistics'
    $collectionFilter = '|FullyQualifiedName~CollectionAllocationTests|FullyQualifiedName~PooledBuilderTests|FullyQualifiedName~PooledImmutableListTests'
    if ($CollectionAllocations) {
        $filter += $collectionFilter
    }
    $localizedFilter = '|FullyQualifiedName~MarkupExpressionReuseTests|FullyQualifiedName~MarkupIncrementalParserTests|FullyQualifiedName~RealisticIncrementalParserTests|FullyQualifiedName~CSharpProbeOptimizationTests|FullyQualifiedName~SemanticBindingCacheTests|FullyQualifiedName~SemanticArchitecture|FullyQualifiedName~ComponentHotReload|FullyQualifiedName~AkcssHotReload|FullyQualifiedName~ClrHotReloadSmoke'
    if ($LocalizedEdits) {
        $filter += $localizedFilter
    }
    $tests = Run 'debugstats' 'dotnet' @('test', $project, '-c', 'DebugStats', '--no-build', '--no-restore', '--filter', $filter,
        '--logger', 'trx;LogFileName=tests.trx', '--results-directory', '{RESULTS}', '-v:normal', '--', 'xUnit.MaxParallelThreads=2')
    [xml]$trx = Get-Content -LiteralPath (Join-Path $tests 'tests.trx') -Raw
    if ([int]$trx.TestRun.ResultSummary.Counters.executed -le 0 -or [int]$trx.TestRun.ResultSummary.Counters.failed -ne 0) {
        throw 'The relevant DebugStats test set did not execute successfully.'
    }
    $sdkPattern = 'InvalidationTrace_|BlackSilenceGenerationStatisticsTests|DetachedArtifacts_|MemberContracts_|SemanticDependencies_'
    if ($CollectionAllocations) {
        $sdkPattern += '|CollectionAllocationTests|PooledBuilderTests|PooledImmutableListTests'
    }
    if ($LocalizedEdits) {
        $sdkPattern += '|BlackSilenceIncrementalGenerationTests.Localized|MarkupExpressionReuseTests|CSharpProbeOptimizationTests'
    }
    $sdkCount = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match $sdkPattern }).Count
    if ($sdkCount -le 0) {
        throw 'No SDK-host tests matched the verified package-host results.'
    }
    $sdkFilter = 'FullyQualifiedName~InvalidationTrace_|FullyQualifiedName~BlackSilenceGenerationStatisticsTests|FullyQualifiedName~DetachedArtifacts_|FullyQualifiedName~MemberContracts_|FullyQualifiedName~SemanticDependencies_'
    if ($CollectionAllocations) {
        $sdkFilter += $collectionFilter
    }
    if ($LocalizedEdits) {
        $sdkFilter += '|FullyQualifiedName~BlackSilenceIncrementalGenerationTests.Localized|FullyQualifiedName~MarkupExpressionReuseTests|FullyQualifiedName~CSharpProbeOptimizationTests'
    }
    $null = Run 'sdk' 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Verify-SdkRoslyn.ps1'), '-OutputDirectory', '{RESULTS}', '-TestFilter', $sdkFilter, '-ExpectedTestCount', "$sdkCount")
    $releaseFilter = '(FullyQualifiedName~DetachedArtifacts_|FullyQualifiedName~MemberContracts_|FullyQualifiedName~SemanticDependencies_)&FullyQualifiedName!~OnlyNewestStoreAlive&FullyQualifiedName!~MixedLongSession&FullyQualifiedName!~MixedRevisions'
    if ($CollectionAllocations) {
        $releaseFilter = $releaseFilter.Replace(')&', $collectionFilter + ')&')
    }
    if ($LocalizedEdits) {
        $releaseFilter = $releaseFilter.Replace(')&', '|FullyQualifiedName~Localized' + $localizedFilter + ')&')
    }
    $null = Run 'release' 'dotnet' @('test', $project, '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false', '-nr:false', '--filter', $releaseFilter,
        '--logger', 'trx;LogFileName=tests.trx', '--results-directory', '{RESULTS}', '-v:normal', '--', 'xUnit.MaxParallelThreads=2')
    $null = Run 'build-tool' 'dotnet' @('build', 'benchmarks/HotReload/HotReloadBenchmarks.csproj', '-p:BlackSilenceConfiguration=DebugStats', '--no-restore', '-p:UseSharedCompilation=false', '-nr:false', '-v:minimal')
    foreach ($check in @('Test-Report.ps1', 'Test-InvalidationReport.ps1', 'Test-ContractRunner.ps1')) {
        $null = Run ($check.Replace('.ps1', '')) 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot $check))
    }
    if (-not $SkipBenchmarks) {
        Matrix 'matrix' @('semantic-property', 'semantic-signature', 'semantic-add-overload', 'semantic-remove-overload', 'semantic-negative',
            'semantic-multiple', 'semantic-unrelated', 'semantic-mixed', 'semantic-global', 'real-referenced-method', 'real-signature', 'akbura')
        Matrix 'overhead' @('semantic-signature', 'akbura') -Off
    }
    $verification = @{ CompletedUtc = [DateTime]::UtcNow.ToString('o'); Fingerprint = $fingerprint; Revision = (& git -C $repository rev-parse HEAD)
        Scope = 'Relevant generator/ownership suites, not every repository test project.'; BenchmarksRequested = -not [bool]$SkipBenchmarks
        DebugStats = @{ Executed = [int]$trx.TestRun.ResultSummary.Counters.executed; Passed = [int]$trx.TestRun.ResultSummary.Counters.passed }
        ExpectedSdkTests = $sdkCount; VerificationMode = $verificationMode }
    $null = New-Item -ItemType Directory -Path (Join-Path $ResultDirectory 'verification') -Force
    [IO.File]::WriteAllText((Join-Path $ResultDirectory 'verification/results.json'), ($verification | ConvertTo-Json -Depth 5) + "`r`n", [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $ResultDirectory 'verification/source-manifest.json') -Force
    Status 'Complete'
    Log 'COMPLETE Verification and selected measurements finished; inspect evidence before final engineering sign-off.'
}
catch {
    Status 'Failed' ($_ | Out-String)
    Log ('FAILED ' + ($_ | Out-String))
    exit 1
}
finally {
    Pop-Location
    $lock.Dispose()
    $journal.Dispose()
}
