#requires -Version 7.2
param(
    [string]$InputDirectory = 'bin/SemanticContracts/final/prepared',
    [string]$OutputDirectory = 'bin/CollectionAllocations/final',
    [string]$ResultDirectory = 'benchmarks/HotReload/results/2026-10-19-collection-allocations',
    [switch]$Worker,
    [switch]$MicroOnly
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repository
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
if (-not $Worker) {
    $arguments = @('-NoProfile', '-File', ('"' + $PSCommandPath + '"'), '-Worker',
        '-InputDirectory', ('"' + $InputDirectory + '"'), '-OutputDirectory', ('"' + $OutputDirectory + '"'),
        '-ResultDirectory', ('"' + $ResultDirectory + '"'))
    if ($MicroOnly) {
        $arguments += '-MicroOnly'
    }
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $process = Start-Process (Get-Command pwsh).Source -ArgumentList $arguments -WorkingDirectory $repository -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $OutputDirectory "console-$stamp.log") -RedirectStandardError (Join-Path $OutputDirectory "errors-$stamp.log")
    Write-Host "Controller PID: $($process.Id)"
    Write-Host "Progress: Get-Content -LiteralPath '$OutputDirectory/journal.log' -Tail 20 -Wait"
    exit
}

$lock = [IO.File]::Open((Join-Path $OutputDirectory 'controller.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$journal = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'journal.log'), $true, [Text.UTF8Encoding]::new($false))
$journal.AutoFlush = $true
$step = 'Starting'
$activePid = $null
$sourcePaths = @(& git ls-files --cached --others --exclude-standard -- 'src/Akbura.Generator' 'src/Akbura.BlackSilence' 'src/Akbura.UnitTests' 'benchmarks/HotReload/*.cs' 'benchmarks/HotReload/*.ps1') | Sort-Object -Unique
$sourceHashes = @($sourcePaths | ForEach-Object { "$_ $((Get-FileHash -LiteralPath $_).Hash)" })
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($sourceHashes -join "`n"))))
$savedPath = Join-Path $OutputDirectory 'sources.json'
$encoding = [Text.UTF8Encoding]::new($false)
if (Test-Path -LiteralPath $savedPath) {
    $previous = Get-Content -LiteralPath $savedPath -Raw | ConvertFrom-Json
    if ($previous.Fingerprint -ne $fingerprint) {
        throw 'Sources changed; use a new output directory instead of combining evidence from different implementations.'
    }
}
else {
    [IO.File]::WriteAllText($savedPath, (@{ Fingerprint = $fingerprint; Files = $sourceHashes; Revision = (& git rev-parse HEAD) } | ConvertTo-Json -Depth 4) + "`r`n", $encoding)
}

function Log([string]$message) {
    $line = [DateTime]::UtcNow.ToString('o') + ' ' + $message
    $journal.WriteLine($line)
    $journal.BaseStream.Flush($true)
    Write-Host $line
}

function Status([string]$phase, [string]$failure = '') {
    $value = @{ Phase = $phase; Current = $script:step; ControllerPid = $PID; CurrentPid = $script:activePid
        UpdatedUtc = [DateTime]::UtcNow.ToString('o'); Failure = $failure; SourceFingerprint = $fingerprint }
    $path = Join-Path $OutputDirectory 'status.json'
    [IO.File]::WriteAllText(($path + '.tmp'), ($value | ConvertTo-Json) + "`r`n", $encoding)
    [IO.File]::Move(($path + '.tmp'), $path, $true)
}

function Assert-Sources {
    foreach ($record in $sourceHashes) {
        $separator = $record.LastIndexOf(' ')
        $path = $record.Substring(0, $separator)
        if ((Get-FileHash -LiteralPath $path).Hash -ne $record.Substring($separator + 1)) {
            throw "Sources changed during verification: $path"
        }
    }
    $currentPaths = @(& git ls-files --cached --others --exclude-standard -- 'src/Akbura.Generator' 'src/Akbura.BlackSilence' 'src/Akbura.UnitTests' 'benchmarks/HotReload/*.cs' 'benchmarks/HotReload/*.ps1') | Sort-Object -Unique
    if (($currentPaths -join "`n") -ne ($sourcePaths -join "`n")) {
        throw 'Source inventory changed during verification.'
    }
}

function Other-Builds {
    return @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'testhost.exe'" | Where-Object {
            $_.CommandLine -match '(?i)(\bdotnet(?:\.exe)?"?\s+(?:test|build|vstest)\b|vstest\.console\.dll|testhost(?:\.exe)?\b)'
        } | Select-Object ProcessId,Name)
}

function Wait-Quiet {
    while ($true) {
        $busy = Other-Builds
        if (-not $busy.Count) {
            return
        }
        Status 'WaitingForOtherBuilds'
        Log ('WAIT Other build/test processes: ' + (ConvertTo-Json -InputObject $busy -Compress))
        Start-Sleep -Seconds 15
    }
}

function Run([string]$name, [string]$executable, [string[]]$arguments, [switch]$Retryable, [switch]$Timing) {
    $script:step = $name
    Wait-Quiet
    Assert-Sources
    $directory = Join-Path $OutputDirectory ($name + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
    $null = New-Item -ItemType Directory -Path $directory
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.WorkingDirectory = $repository
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $arguments) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $output = [IO.StreamWriter]::new((Join-Path $directory 'output.log'), $false, $encoding)
    $errors = [IO.StreamWriter]::new((Join-Path $directory 'stderr.log'), $false, $encoding)
    $output.AutoFlush = $true
    $errors.AutoFlush = $true
    $started = $false
    $contaminated = $false
    try {
        $null = $process.Start()
        $started = $true
        $script:activePid = $process.Id
        Status 'Running'
        Log "START $name pid=$script:activePid logs=$directory"
        $stdout = $process.StandardOutput.ReadLineAsync()
        $stderr = $process.StandardError.ReadLineAsync()
        $heartbeat = [Diagnostics.Stopwatch]::StartNew()
        $activityCheck = [Diagnostics.Stopwatch]::StartNew()
        while ($null -ne $stdout -or $null -ne $stderr -or -not $process.HasExited) {
            if ($Timing -and -not $contaminated -and $activityCheck.Elapsed.TotalSeconds -ge 1) {
                $busy = Other-Builds
                if ($busy.Count) {
                    Log ('DISCARD Other build/test activity during timing: ' + (ConvertTo-Json -InputObject $busy -Compress))
                    $contaminated = $true
                    if (-not $process.HasExited) {
                        $process.Kill($true)
                    }
                }
                $activityCheck.Restart()
            }
            foreach ($kind in @('stdout','stderr')) {
                $task = Get-Variable -Name $kind -ValueOnly
                while ($null -ne $task -and $task.IsCompleted) {
                    $line = $task.GetAwaiter().GetResult()
                    if ($null -eq $line) {
                        Set-Variable -Name $kind -Value $null
                    }
                    elseif ($kind -eq 'stdout') {
                        $output.WriteLine($line)
                        $stdout = $process.StandardOutput.ReadLineAsync()
                    }
                    else {
                        $errors.WriteLine($line)
                        $stderr = $process.StandardError.ReadLineAsync()
                    }
                    $task = Get-Variable -Name $kind -ValueOnly
                }
            }
            if ($heartbeat.Elapsed.TotalSeconds -ge 15) {
                Status 'Running'
                Log "HEARTBEAT $name pid=$script:activePid"
                $heartbeat.Restart()
            }
            Start-Sleep -Milliseconds 50
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0 -and -not $Retryable) {
            throw "Exit $($process.ExitCode): $directory"
        }
        Assert-Sources
        return @{ Directory = $directory; ExitCode = $process.ExitCode; Contaminated = $contaminated }
    }
    finally {
        $output.Dispose()
        $errors.Dispose()
        if ($started -and -not $process.HasExited) {
            $process.Kill($true)
        }
        $process.Dispose()
        $script:activePid = $null
    }
}

function Matrix([string]$name, [string]$path) {
    $script:step = $name
    if (Test-Path -LiteralPath (Join-Path $path 'run.json')) {
        $null = & (Join-Path $PSScriptRoot 'Start-ContractBenchmarks.ps1') -OutputDirectory $path -Resume
    }
    else {
        if ($name -eq 'before') {
            throw 'The pre-change frozen baseline is missing; do not silently build a baseline from optimized sources.'
        }
        $null = & (Join-Path $PSScriptRoot 'Start-ContractBenchmarks.ps1') -InputDirectory $InputDirectory -OutputDirectory $path `
            -ResultDirectory (Join-Path $ResultDirectory $name) -Scenario @('semantic-signature','akbura') -SemanticDependencies -SkipLifetime -SkipWatch
    }
    while ($true) {
        Start-Sleep -Seconds 15
        Assert-Sources
        $state = Get-Content -LiteralPath (Join-Path $path 'status.json') -Raw | ConvertFrom-Json
        $script:activePid = $state.CurrentPid
        Status 'Measuring'
        Log "PROGRESS $name $($state.Phase) $($state.Current) $($state.CompletedGroups)/$($state.TotalGroups)"
        if ($state.Phase -eq 'Complete') {
            $script:activePid = $null
            return
        }
        if ($state.Phase -eq 'Failed') {
            if ($state.Failure -notmatch 'Other test/build activity') {
                throw "Measurement failed: $($state.Failure)"
            }
            Log "RETRY ${name}: contaminated group is discarded; completed groups stay valid."
            Wait-Quiet
            $null = & (Join-Path $PSScriptRoot 'Start-ContractBenchmarks.ps1') -OutputDirectory $path -Resume
        }
        elseif (-not (Get-Process -Id $state.ControllerPid -ErrorAction SilentlyContinue)) {
            throw "Measurement controller stopped without a terminal status: $path"
        }
    }
}

try {
    $null = Run 'build-generator' 'dotnet' @('build','src/Akbura.BlackSilence/Akbura.BlackSilence.csproj','-c','DebugStats','--no-restore',
        '-p:UseSharedCompilation=false','-nr:false','-v:minimal')
    $null = Run 'build-tool' 'dotnet' @('build','benchmarks/HotReload/HotReloadBenchmarks.csproj','--no-restore',
        '-p:BlackSilenceConfiguration=DebugStats','-p:UseSharedCompilation=false','-nr:false','-v:minimal')
    $null = Run 'style' 'dotnet' @((Join-Path $PSScriptRoot 'bin/Debug/net10.0/HotReloadBenchmarks.dll'),'style',
        'src/Akbura.Generator/Collections/EnumerableExtensions.Sorting.cs','src/Akbura.Generator/Pools/PooledStringHashSet.cs',
        'src/Akbura.Generator/Pools/PooledHashSet.cs','src/Akbura.Generator/Language/Binder/DocumentSemanticDependencyCollector.cs',
        'src/Akbura.UnitTests/CollectionAllocationTests.cs','benchmarks/HotReload/Program.Collections.cs')
    do {
        $micro = Run 'micro' 'dotnet' @((Join-Path $PSScriptRoot 'bin/Debug/net10.0/HotReloadBenchmarks.dll'),'collections') -Retryable -Timing
        if (-not $micro.Contaminated -and $micro.ExitCode -ne 0) {
            throw "Microbenchmark failed: $($micro.Directory)"
        }
    } while ($micro.Contaminated)
    $null = Run 'summarize-micro' 'pwsh' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Summarize-Collections.ps1'),
        '-TracePath',(Join-Path $micro.Directory 'output.log'),'-ResultDirectory',(Join-Path $ResultDirectory 'micro'))
    if (-not $MicroOnly) {
        $qualityDirectory = Join-Path $OutputDirectory 'quality'
        $arguments = @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Verify-SemanticDependencies.ps1'),'-CollectionAllocations','-SkipBenchmarks',
            '-InputDirectory',$InputDirectory,'-OutputDirectory',$qualityDirectory,'-ResultDirectory',$ResultDirectory)
        if (Test-Path -LiteralPath (Join-Path $qualityDirectory 'source-manifest.json')) {
            $arguments += '-Resume'
        }
        $null = Run 'quality' 'pwsh' $arguments
        Matrix 'before' (Join-Path $repository 'bin/CollectionAllocations/before')
        Matrix 'after' (Join-Path $OutputDirectory 'after')
        $null = Run 'compare' 'pwsh' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Compare-CollectionAllocations.ps1'),'-ResultDirectory',$ResultDirectory)
    }
    Copy-Item -LiteralPath $savedPath -Destination (Join-Path $ResultDirectory 'sources.json') -Force
    Status 'EvidenceCompleteNeedsReview'
    Log 'COMPLETE Evidence collection finished; inspect microbenchmark choices, regressions and final report before sign-off.'
}
catch {
    Status 'Failed' $_.ToString()
    Log ('FAILED ' + $_.ToString())
    throw
}
finally {
    $journal.Dispose()
    $lock.Dispose()
}
