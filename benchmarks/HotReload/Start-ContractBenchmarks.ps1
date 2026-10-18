#requires -Version 7.2
param(
    [string]$InputDirectory,
    [string]$OutputDirectory,
    [string]$ResultDirectory,
    [switch]$Resume,
    [switch]$Worker,
    [string[]]$Scenario = @('akbura', 'real-method', 'real-referenced-method', 'real-service', 'real-property', 'real-multiple',
        'real-mixed', 'real-signature', 'real-overload', 'unrelated-body', 'referenced-signature'),
    [switch]$SkipPurity,
    [switch]$SkipLifetime,
    [switch]$SkipWatch,
    [ValidateRange(1, 10000)][int]$RetentionRevisions = 100,
    [ValidateRange(3, 100)][int]$ConcurrentSamples = 8
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

function Assert-Scenarios([string[]]$names) {
    $supported = @('akbura', 'real-method', 'real-referenced-method', 'real-service', 'real-property', 'real-multiple',
        'real-mixed', 'real-signature', 'real-overload', 'unrelated-body', 'referenced-signature')
    if (-not $names.Count -or @($names | Sort-Object -Unique).Count -ne $names.Count) {
        throw 'Select at least one scenario, without duplicates.'
    }
    foreach ($name in $names) {
        if ($name -notin $supported) {
            throw "Unsupported contract benchmark scenario: $name"
        }
    }
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repository ('bin/MemberContractBenchmarks/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$toolDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'bin/Debug/net10.0'))
if ($OutputDirectory.Equals($toolDirectory, [StringComparison]::OrdinalIgnoreCase) -or
    $OutputDirectory.StartsWith($toolDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep output outside the source tool directory so freezing cannot recursively copy itself.'
}
$configurationPath = Join-Path $OutputDirectory 'run.json'
if (-not $Worker) {
    if ($Resume) {
        if (-not (Test-Path -LiteralPath $configurationPath)) {
            throw 'Resume requires the original output directory and run.json.'
        }
    }
    else {
        Assert-Scenarios $Scenario
        if (-not $InputDirectory) {
            throw 'Prepare evaluated inputs with Run.ps1, then pass -InputDirectory.'
        }
        if (Test-Path -LiteralPath $OutputDirectory) {
            throw 'Use a fresh output directory, or explicitly pass -Resume.'
        }
        $InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
        if (-not $ResultDirectory) {
            $ResultDirectory = Join-Path $OutputDirectory 'report'
        }
        $ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
        if ($ResultDirectory -eq $OutputDirectory -or (Test-Path -LiteralPath (Join-Path $ResultDirectory 'invalidation-provenance.json'))) {
            throw 'Keep raw output and a new portable result archive separate; existing measurements must not be overwritten.'
        }
        $null = New-Item -ItemType Directory -Path $OutputDirectory
        # Freeze the complete tool closure. Subsequent working-tree builds must
        # not silently replace the implementation halfway through a timing pair.
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Debug/net10.0') -Destination (Join-Path $OutputDirectory 'tool') -Recurse
        foreach ($name in @('gallery-inputs.json', 'purity-inputs.json', 'invalidation-environment.json', 'purity-source.json')) {
            if ($SkipPurity -and $name.StartsWith('purity', [StringComparison]::Ordinal)) {
                continue
            }
            $source = Join-Path $InputDirectory $name
            if (Test-Path -LiteralPath $source) {
                Copy-Item -LiteralPath $source -Destination (Join-Path $OutputDirectory $name)
            }
            elseif ($name.EndsWith('-inputs.json')) {
                throw "Missing evaluated inputs: $source"
            }
        }
        $hashes = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $OutputDirectory 'tool') -File) {
            @{ Name = $file.Name; Sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash }
        }
        $inputPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($workload in @('gallery', 'purity') | Where-Object { $_ -eq 'gallery' -or -not $SkipPurity }) {
            $inputPath = Join-Path $OutputDirectory ($workload + '-inputs.json')
            $null = $inputPaths.Add($inputPath)
            $inputData = Get-Content -LiteralPath $inputPath -Raw | ConvertFrom-Json
            foreach ($kind in @('Compile', 'AdditionalFiles', 'ReferencePath')) {
                foreach ($item in $inputData.Items.$kind) {
                    if ($item.FullPath -and (Test-Path -LiteralPath $item.FullPath -PathType Leaf)) {
                        $null = $inputPaths.Add($item.FullPath)
                    }
                }
            }
        }
        $inputHashes = foreach ($path in $inputPaths) {
            @{ Path = $path; Sha256 = (Get-FileHash -LiteralPath $path).Hash }
        }
        $configuration = @{ RepositoryRevision = (& git -C $repository rev-parse HEAD); StartedUtc = [DateTime]::UtcNow.ToString('o')
            ResultDirectory = $ResultDirectory; ToolHashes = @($hashes); InputHashes = @($inputHashes)
            RetentionRevisions = $RetentionRevisions; ConcurrentSamples = $ConcurrentSamples; Scenarios = $Scenario
            SkipPurity = [bool]$SkipPurity; SkipLifetime = [bool]$SkipLifetime; SkipWatch = [bool]$SkipWatch }
        [IO.File]::WriteAllText($configurationPath, ($configuration | ConvertTo-Json -Depth 5) + "`r`n", [Text.UTF8Encoding]::new($false))
    }
    $previousStatusPath = Join-Path $OutputDirectory 'status.json'
    if (Test-Path -LiteralPath $previousStatusPath) {
        $previousStatus = Get-Content -LiteralPath $previousStatusPath -Raw | ConvertFrom-Json
        $previousController = Get-CimInstance Win32_Process -Filter "ProcessId = $($previousStatus.ControllerPid)" -ErrorAction SilentlyContinue
        if ($previousController -and $previousController.CommandLine -and $previousController.CommandLine.Contains($PSCommandPath) -and $previousController.CommandLine.Contains($OutputDirectory)) {
            throw "This run already has an active controller: $($previousStatus.ControllerPid)."
        }
        if ($previousStatus.CurrentPid) {
            $previousChild = Get-CimInstance Win32_Process -Filter "ProcessId = $($previousStatus.CurrentPid)" -ErrorAction SilentlyContinue
            if ($previousChild -and $previousChild.CommandLine -and $previousChild.CommandLine.Contains((Join-Path $OutputDirectory 'tool/HotReloadBenchmarks.dll'))) {
                throw "A child from the interrupted controller is still active: $($previousStatus.CurrentPid). Do not start a duplicate run."
            }
        }
    }
    $shell = (Get-Process -Id $PID).Path
    $process = Start-Process -FilePath $shell -ArgumentList @('-NoProfile', '-File', ('"' + $PSCommandPath + '"'),
        '-Worker', '-OutputDirectory', ('"' + $OutputDirectory + '"')) -WorkingDirectory $repository -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $OutputDirectory 'controller.stdout.log') -RedirectStandardError (Join-Path $OutputDirectory 'controller.stderr.log')
    Write-Output "Background controller PID: $($process.Id)"
    Write-Output "Status: $(Join-Path $OutputDirectory 'status.json')"
    Write-Output "Live log: Get-Content -LiteralPath '$(Join-Path $OutputDirectory 'journal.log')' -Tail 20 -Wait"
    return
}

Set-Location -LiteralPath $repository
$configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
$statusPath = Join-Path $OutputDirectory 'status.json'
$journal = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'journal.log'), $true, [Text.UTF8Encoding]::new($false))
$journal.AutoFlush = $true
$lock = $null
$completed = 0
$current = 'Validation'
$currentPid = $null
$currentIndex = -1
$lastObservation = ''
$jobs = [Collections.Generic.List[object]]::new()

function Log([string]$message) {
    $script:journal.WriteLine([DateTime]::UtcNow.ToString('o') + ' ' + $message)
}

function Status([string]$phase, [string]$failure = '') {
    $data = @{ Phase = $phase; Current = $script:current; CompletedGroups = $script:completed; TotalGroups = $script:jobs.Count
        CurrentPid = $script:currentPid; UpdatedIndex = $script:currentIndex; LastObservation = $script:lastObservation
        UpdatedUtc = [DateTime]::UtcNow.ToString('o'); ControllerPid = $PID; Failure = $failure }
    $temporary = $script:statusPath + '.tmp'
    [IO.File]::WriteAllText($temporary, ($data | ConvertTo-Json) + "`r`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $script:statusPath, $true)
}

function Assert-Inputs {
    foreach ($hash in $configuration.InputHashes) {
        if (-not (Test-Path -LiteralPath $hash.Path) -or (Get-FileHash -LiteralPath $hash.Path).Hash -ne $hash.Sha256) {
            throw "Evaluated input changed: $($hash.Path). Preserve this run and prepare a new one."
        }
    }
}

function Foreign-Builds {
    return @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'testhost.exe'" | Where-Object {
        $_.CommandLine -match '(?i)(\bdotnet(?:\.exe)?"?\s+(?:test|build|vstest)\b|vstest\.console\.dll|testhost(?:\.exe)?\b)' -and
        $_.ProcessId -ne $script:currentPid
    } | Select-Object ProcessId, Name)
}

function Wait-QuietHost {
    while (@(Foreign-Builds).Count -gt 0) {
        Status 'WaitingForOtherBuilds'
        Log 'WAIT Other test/build processes are active; no benchmark child has been started.'
        Start-Sleep -Seconds 15
    }
}

function Complete([string]$path, $job) {
    if (-not (Test-Path -LiteralPath $path)) {
        return $false
    }
    try {
        $rows = @([IO.File]::ReadLines($path) | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json -ErrorAction Stop })
        $provenanceKind = if ($job.Command -eq 'artifacts') { 'invalidation-provenance' } else { 'artifact-memory-provenance' }
        $provenance = @($rows | Where-Object Kind -eq $provenanceKind)
        $blackSilenceHash = ($configuration.ToolHashes | Where-Object Name -eq 'Akbura.BlackSilence.dll').Sha256
        $generatorHash = ($configuration.ToolHashes | Where-Object Name -eq 'Akbura.Generator.dll').Sha256
        $inputHash = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory ($job.Workload + '-inputs.json'))).Hash
        if ($provenance.Count -ne 1 -or $provenance[0].BlackSilenceSha256 -ne $blackSilenceHash -or
            $provenance[0].GeneratorSha256 -ne $generatorHash -or $provenance[0].InputsSha256 -ne $inputHash -or
            $provenance[0].Optimization -ne $job.Mode) {
            return $false
        }
        if ($job.Command -eq 'artifacts') {
            $samples = @($rows | Where-Object Kind -eq 'invalidation')
            $checks = @($rows | Where-Object Kind -eq 'invalidation-parity')
            $heaps = @($rows | Where-Object Kind -eq 'artifact-heap')
            return $provenance[0].VerifyEveryUpdate -and $provenance[0].Profiling -eq 'stats' -and
                $samples.Count -eq 11 -and $checks.Count -eq 11 -and
                @($checks | Where-Object { -not $_.EqualSources -or -not $_.EqualDiagnostics }).Count -eq 0 -and
                @($checks.Index | Sort-Object -Unique).Count -eq 11 -and
                @($samples.Index | Sort-Object -Unique).Count -eq 11 -and @($heaps.Index | Sort-Object -Unique).Count -eq 11 -and
                @($samples + $checks + $heaps | Where-Object { $_.Index -notin 0..10 }).Count -eq 0 -and $heaps.Count -eq 11
        }
        $final = @($rows | Where-Object Kind -eq 'artifact-memory-result')
        if ($final.Count -eq 1) {
            foreach ($name in @('CSharpCompilation', 'AkburaCompilation', 'ProjectIndex', 'ProjectState', 'ProjectSnapshot', 'DetachedSnapshot')) {
                if (-not $final[0].Objects.PSObject.Properties[$name]) {
                    return $false
                }
            }
            foreach ($property in $final[0].Objects.PSObject.Properties) {
                $limit = if ($property.Name -eq 'DetachedSnapshot' -and $job.Mode -eq 'enabled') { 1 } else { 0 }
                if ($property.Value.Alive -gt $limit) {
                    return $false
                }
            }
        }
        $samples = @($rows | Where-Object Kind -eq 'artifact-memory-sample')
        $concurrent = @($rows | Where-Object Kind -eq 'artifact-concurrent')
        return $provenance[0].RealMembers -and $provenance[0].Revisions -eq $job.Revisions -and
            $provenance[0].ConcurrentSamples -eq $job.ConcurrentSamples -and $final.Count -eq 1 -and
            $final[0].Revisions -eq $job.Revisions -and $final[0].ConcurrentSamples -eq $job.ConcurrentSamples -and
            $final[0].FreshParityChecks -eq $job.Revisions + 1 + $job.ConcurrentSamples * 2 -and
            $final[0].ConcurrentBranches -eq $job.ConcurrentSamples * 2 -and
            $samples.Count -eq $job.Revisions + 1 -and @($samples.Index | Sort-Object -Unique).Count -eq $job.Revisions + 1 -and
            @($samples | Where-Object { $_.Index -notin 0..$job.Revisions }).Count -eq 0 -and
            $concurrent.Count -eq $job.ConcurrentSamples -and @($concurrent.Index | Sort-Object -Unique).Count -eq $job.ConcurrentSamples -and
            @($concurrent | Where-Object { $_.Index -notin 0..($job.ConcurrentSamples - 1) }).Count -eq 0
    }
    catch {
        return $false
    }
}

function Observe([string]$line) {
    try {
        $row = $line | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return
    }
    if ($row.Kind -in @('invalidation', 'invalidation-parity', 'artifact-memory-sample', 'artifact-concurrent')) {
        if ($null -ne $row.Index) {
            $script:currentIndex = $row.Index
        }
        $script:lastObservation = $row.Kind
        if ($row.Kind -in @('invalidation-parity', 'artifact-memory-sample', 'artifact-concurrent')) {
            Log "PROGRESS $script:current index=$script:currentIndex kind=$($row.Kind)"
        }
        Status 'Running'
    }
}

function Run($job, [string]$path) {
    $writer = [IO.StreamWriter]::new($path, $false, [Text.UTF8Encoding]::new($false))
    $errors = [IO.StreamWriter]::new(($path + '.stderr.log'), $false, [Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    $errors.AutoFlush = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.WorkingDirectory = $repository
    $processStarted = $false
    $arguments = @((Join-Path $OutputDirectory 'tool/HotReloadBenchmarks.dll'), $job.Command,
        (Join-Path $OutputDirectory ($job.Workload + '-inputs.json')), $job.File)
    if ($job.Command -eq 'artifacts') {
        $arguments += @($job.Scenario, 'stats', $job.Mode)
    }
    else {
        $arguments += @("$($job.Revisions)", $job.Mode, "$($job.ConcurrentSamples)")
    }
    foreach ($argument in $arguments) {
        $process.StartInfo.ArgumentList.Add($argument)
    }
    try {
        $null = $process.Start()
        $processStarted = $true
        $script:currentPid = $process.Id
        $stdout = $process.StandardOutput.ReadLineAsync()
        $stderr = $process.StandardError.ReadLineAsync()
        $heartbeat = [Diagnostics.Stopwatch]::StartNew()
        while ($null -ne $stdout -or $null -ne $stderr -or -not $process.HasExited) {
            if ($null -ne $stdout -and $stdout.IsCompleted) {
                $line = $stdout.GetAwaiter().GetResult()
                if ($null -eq $line) {
                    $stdout = $null
                }
                else {
                    $writer.WriteLine($line)
                    Observe $line
                    $stdout = $process.StandardOutput.ReadLineAsync()
                }
            }
            if ($null -ne $stderr -and $stderr.IsCompleted) {
                $line = $stderr.GetAwaiter().GetResult()
                if ($null -eq $line) {
                    $stderr = $null
                }
                else {
                    $errors.WriteLine($line)
                    Log "STDERR $line"
                    $stderr = $process.StandardError.ReadLineAsync()
                }
            }
            if ($heartbeat.Elapsed.TotalSeconds -ge 15) {
                if (@(Foreign-Builds).Count -gt 0) {
                    throw 'Other test/build activity started during this group. The partial group is not a valid timing sample; resume after that activity finishes.'
                }
                $process.Refresh()
                Log "HEARTBEAT $script:current pid=$script:currentPid index=$script:currentIndex cpuSeconds=$($process.TotalProcessorTime.TotalSeconds)"
                Status 'Running'
                $heartbeat.Restart()
            }
            Start-Sleep -Milliseconds 50
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Exit $($process.ExitCode); stdout=$path; stderr=$path.stderr.log"
        }
    }
    finally {
        if ($processStarted -and -not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $writer.Dispose()
        $errors.Dispose()
        $process.Dispose()
        $script:currentPid = $null
    }
}

try {
    $lock = [IO.File]::Open((Join-Path $OutputDirectory 'controller.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    Assert-Scenarios $configuration.Scenarios
    foreach ($hash in $configuration.ToolHashes) {
        if ((Get-FileHash -LiteralPath (Join-Path $OutputDirectory ('tool/' + $hash.Name))).Hash -ne $hash.Sha256) {
            throw "Frozen tool changed: $($hash.Name)"
        }
    }
    # Fail early on real-project parity/ownership errors rather than discovering
    # them only after the complete timing matrix. Keep preflight out of reports.
    $current = 'Preflight real-member parity and owner-only collection'
    Wait-QuietHost
    Assert-Inputs
    $preflightDirectory = Join-Path $OutputDirectory 'preflight'
    $null = New-Item -ItemType Directory -Path $preflightDirectory -Force
    $preflightPath = Join-Path $preflightDirectory ([Guid]::NewGuid().ToString('N') + '.jsonl')
    $preflight = @{ Workload = 'gallery'; File = 'Counter.akbura'; Command = 'member-memory'; Mode = 'enabled'
        Revisions = 1; ConcurrentSamples = 3 }
    Status 'Running'
    Log "START $current"
    Run $preflight $preflightPath
    Assert-Inputs
    if (-not (Complete $preflightPath $preflight)) {
        throw "Incomplete successful preflight: $preflightPath"
    }
    Log "DONE $current"
    foreach ($workload in @(@{ Name = 'gallery'; File = 'Counter.akbura' }, @{ Name = 'purity'; File = 'Sidebar.akbura' }) |
        Where-Object { $_.Name -eq 'gallery' -or -not $configuration.SkipPurity }) {
        foreach ($scenario in $configuration.Scenarios) {
            foreach ($mode in @('disabled', 'enabled')) {
                $jobs.Add(@{ Workload = $workload.Name; File = $workload.File; Command = 'artifacts'; Scenario = $scenario; Mode = $mode })
            }
        }
        foreach ($mode in @('disabled', 'enabled') | Where-Object { -not $configuration.SkipLifetime }) {
            $jobs.Add(@{ Workload = $workload.Name; File = $workload.File; Command = 'member-memory'; Scenario = 'lifetime'; Mode = $mode
                Revisions = $(if ($workload.Name -eq 'gallery') { $configuration.RetentionRevisions } else { 1 }); ConcurrentSamples = $configuration.ConcurrentSamples })
        }
    }
    foreach ($job in $jobs) {
        Wait-QuietHost
        Assert-Inputs
        $current = "$($job.Workload) / $($job.Scenario) / $($job.Mode)"
        $currentIndex = -1
        $lastObservation = ''
        $name = if ($job.Command -eq 'artifacts') { "invalidation-$($job.Workload)-$($job.Scenario)-stats-$($job.Mode).jsonl" }
            else { "artifact-memory-$($job.Workload)-$($job.Mode).jsonl" }
        $path = Join-Path $OutputDirectory $name
        if (Complete $path $job) {
            $completed++
            Log "RESUMED $current"
            continue
        }
        if (Test-Path -LiteralPath $path) {
            $interrupted = Join-Path $OutputDirectory 'interrupted'
            $null = New-Item -ItemType Directory -Path $interrupted -Force
            Move-Item -LiteralPath $path -Destination (Join-Path $interrupted ($name + '.' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')))
        }
        Status 'Running'
        Log "START $current"
        Run $job $path
        Assert-Inputs
        if (-not (Complete $path $job)) {
            throw "Incomplete successful output: $path"
        }
        $completed++
        Log "DONE $current"
    }
    $current = 'Report generation'
    Status 'Summarizing'
    & (Join-Path $PSScriptRoot 'Summarize-Invalidation.ps1') -InputDirectory $OutputDirectory -ResultDirectory $configuration.ResultDirectory
    Copy-Item -LiteralPath $configurationPath -Destination (Join-Path $configuration.ResultDirectory 'run.json') -Force
    if (-not $configuration.SkipWatch) {
        $current = 'Live C# member dotnet watch'
        Wait-QuietHost
        Status 'VerifyingWatch'
        $watchDirectory = Join-Path $OutputDirectory ('watch-' + [Guid]::NewGuid().ToString('N'))
        & (Join-Path $PSScriptRoot 'Verify-MemberWatch.ps1') -OutputDirectory $watchDirectory -ToolDirectory (Join-Path $OutputDirectory 'tool')
        foreach ($name in @('watch.jsonl', 'result.json')) {
            Copy-Item -LiteralPath (Join-Path $watchDirectory $name) -Destination (Join-Path $configuration.ResultDirectory ('member-' + $name)) -Force
        }
    }
    Status 'Complete'
    Log 'COMPLETE'
}
catch {
    if ($null -ne $lock) {
        Status 'Failed' ($_ | Out-String)
    }
    Log ('FAILED ' + ($_ | Out-String))
    exit 1
}
finally {
    if ($null -ne $lock) {
        $lock.Dispose()
    }
    $journal.Dispose()
}
