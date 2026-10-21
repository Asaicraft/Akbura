#requires -Version 7.2
param(
    [string]$OutputDirectory = 'bin/ProjectionReuse/gate',
    [string]$InputDirectory = 'bin/CrossRevision/audit-validated/runs/stats-current-literal',
    [string]$BaselineDirectory = 'bin/ProjectionReuse/baseline',
    [string]$ResultDirectory = 'benchmarks/HotReload/results/2026-10-21-projection-reuse',
    [ValidateRange(2, 5)][int]$Rounds = 2,
    [switch]$Worker
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repository
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$BaselineDirectory = [IO.Path]::GetFullPath($BaselineDirectory)
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$encoding = [Text.UTF8Encoding]::new($false)
if (-not (Test-Path -LiteralPath (Join-Path $BaselineDirectory 'baseline.json'))) {
    throw 'Freeze the unmodified baseline closure and baseline.json before changing compiler behavior.'
}
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
if (-not $Worker) {
    $arguments = @('-NoProfile', '-File', $PSCommandPath, '-Worker', '-OutputDirectory', $OutputDirectory,
        '-InputDirectory', $InputDirectory, '-BaselineDirectory', $BaselineDirectory, '-ResultDirectory', $ResultDirectory, '-Rounds', "$Rounds")
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $process = Start-Process (Get-Command pwsh).Source -WindowStyle Hidden -PassThru -WorkingDirectory $repository `
        -ArgumentList @($arguments | ForEach-Object { '"' + $_ + '"' }) `
        -RedirectStandardOutput (Join-Path $OutputDirectory "controller-$stamp.stdout.log") `
        -RedirectStandardError (Join-Path $OutputDirectory "controller-$stamp.stderr.log")
    Write-Host "Controller PID: $($process.Id)"
    Write-Host "Progress: Get-Content -LiteralPath '$OutputDirectory/journal.log' -Tail 20 -Wait"
    return
}

$lock = [IO.File]::Open((Join-Path $OutputDirectory 'controller.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$journal = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'journal.log'), $true, $encoding)
$journal.AutoFlush = $true
$step = 'Starting'
$childPid = $null
$completed = 0
function Json([string]$path, $value) {
    $text = [regex]::Replace(($value | ConvertTo-Json -Depth 12), '\r?\n', "`r`n") + "`r`n"
    [IO.File]::WriteAllText(($path + '.tmp'), $text, $encoding)
    [IO.File]::Move(($path + '.tmp'), $path, $true)
}
function Log([string]$message) {
    $journal.WriteLine([DateTime]::UtcNow.ToString('o') + ' ' + $message)
    $journal.BaseStream.Flush($true)
}
function Status([string]$phase, [string]$failure = '') {
    Json (Join-Path $OutputDirectory 'status.json') @{ Phase = $phase; Current = $script:step; CurrentPid = $script:childPid
        CompletedSteps = $script:completed; ControllerPid = $PID; UpdatedUtc = [DateTime]::UtcNow.ToString('o'); Failure = $failure }
}
function Source-Hashes {
    @(& git ls-files --cached --others --exclude-standard -- 'src/Akbura.Generator' 'src/Akbura.BlackSilence' 'src/Akbura.UnitTests' 'benchmarks/HotReload/*.ps1' 'benchmarks/HotReload/ProjectionFixture' |
        Sort-Object -Unique | ForEach-Object { "$_ $((Get-FileHash -LiteralPath $_).Hash)" })
}
function Assert-Sources {
    if (((Source-Hashes) -join "`n") -cne ($script:manifest.Files -join "`n")) {
        throw 'Sources changed. Preserve this evidence and select a new output directory.'
    }
}
function Run([string]$name, [string[]]$arguments) {
    Assert-Sources
    $script:step = $name
    $marker = Join-Path $OutputDirectory ($name + '.success.json')
    if (Test-Path -LiteralPath $marker) {
        $saved = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($saved.Fingerprint -ne $script:manifest.Fingerprint -or
            (ConvertTo-Json -InputObject @($saved.Arguments) -Compress) -cne (ConvertTo-Json -InputObject $arguments -Compress)) {
            throw "Incompatible completed step: $name"
        }
        $script:completed++
        Log "REUSED $name"
        return
    }
    $logs = Join-Path $OutputDirectory ($name + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
    $null = New-Item -ItemType Directory -Path $logs
    $start = [Diagnostics.ProcessStartInfo]::new($arguments[0])
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $repository
    foreach ($argument in $arguments | Select-Object -Skip 1) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $stdout = [IO.File]::Create((Join-Path $logs 'stdout.log'))
    $stderr = [IO.File]::Create((Join-Path $logs 'stderr.log'))
    try {
        $null = $process.Start()
        $script:childPid = $process.Id
        $copyOut = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $copyErr = $process.StandardError.BaseStream.CopyToAsync($stderr)
        Log "START $name pid=$script:childPid logs=$logs"
        while (-not $process.WaitForExit(15000)) {
            Status 'Running'
            $stdout.Flush($true)
            $stderr.Flush($true)
            Log "HEARTBEAT $name cpuSeconds=$($process.TotalProcessorTime.TotalSeconds)"
        }
        $null = $copyOut.GetAwaiter().GetResult()
        $null = $copyErr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Exit $($process.ExitCode): $logs" }
        Json $marker @{ Fingerprint = $script:manifest.Fingerprint; Arguments = $arguments; Logs = $logs; CompletedUtc = [DateTime]::UtcNow.ToString('o') }
        $script:completed++
        Log "DONE $name"
    }
    finally { $stdout.Dispose(); $stderr.Dispose(); $process.Dispose(); $script:childPid = $null }
}
function Freeze([string]$profile) {
    $destination = Join-Path $OutputDirectory "tools/projection-$profile"
    $configuration = if ($profile -eq 'stats') { 'DebugStats' } else { 'Release' }
    $build = Join-Path $OutputDirectory "build/$profile"
    Run "build-$profile" @('dotnet', 'build', 'src/Akbura.BlackSilence/Akbura.BlackSilence.csproj', '-c', $configuration,
        '--artifacts-path', $build, '--nologo', '-v:minimal', '-p:UseSharedCompilation=false', '-nr:false', '-p:ProbeReuseStage=projection')
    $proof = Join-Path $OutputDirectory "$profile.closure.json"
    if (-not (Test-Path -LiteralPath $proof)) {
        if (Test-Path -LiteralPath $destination) { throw "Incomplete frozen closure: $destination" }
        Copy-Item -LiteralPath (Join-Path $BaselineDirectory $profile) -Destination $destination -Recurse
        foreach ($assembly in @('Akbura.Generator', 'Akbura.BlackSilence')) {
            $binaries = @(Get-ChildItem -LiteralPath (Join-Path $build "bin/$assembly") -Filter "$assembly.dll" -File -Recurse)
            if ($binaries.Count -ne 1) { throw "Expected one candidate assembly: $assembly/$profile" }
            Copy-Item -LiteralPath $binaries[0].FullName -Destination (Join-Path $destination "$assembly.dll") -Force
            Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($binaries[0].FullName, '.pdb')) -Destination (Join-Path $destination "$assembly.pdb") -Force
        }
        Json $proof @{ Fingerprint = $script:manifest.Fingerprint; Binaries = @(Get-ChildItem -LiteralPath $destination -File |
            ForEach-Object { @{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } }) }
    }
    $saved = Get-Content -LiteralPath $proof -Raw | ConvertFrom-Json
    if ($saved.Fingerprint -ne $script:manifest.Fingerprint) { throw 'Candidate source fingerprint changed.' }
    foreach ($binary in $saved.Binaries) {
        if ((Get-FileHash -LiteralPath (Join-Path $destination $binary.File)).Hash -ne $binary.Sha256) { throw 'Candidate closure changed.' }
    }
    Copy-Item -LiteralPath $proof -Destination $ResultDirectory -Force
    return $destination
}
function Matrix([string]$name, [string]$tool, [string]$profile, [bool]$binding, [bool]$focused = $false) {
    Assert-Sources
    $script:step = $name
    $path = Join-Path $OutputDirectory "runs/$name"
    $arguments = @{ InputDirectory = $(if ($focused) { Join-Path $OutputDirectory 'focused-inputs' } else { $InputDirectory })
        OutputDirectory = $path; ResultDirectory = (Join-Path $ResultDirectory "runs/$name")
        ToolDirectory = $tool; Scenario = $(if ($binding) { @('localized-binding-sibling') } else { @('localized-literal') })
        Component = $(if ($focused) { @('gallery:Siblings.akbura') } elseif ($binding) { @('gallery:DataTemplates.akbura', 'purity:NavButton.akbura') }
            else { @('gallery:Counter.akbura', 'gallery:DataTemplatesPage.akbura', 'purity:NeedHelp.akbura', 'purity:Sidebar.akbura') })
        Mode = @('enabled'); Profiling = $profile; SkipLifetime = $true; SkipWatch = $true; SkipPreflight = $true; SkipPurity = $focused }
    while ($true) {
        $statePath = Join-Path $path 'status.json'
        $state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
        if ($state -and $state.Phase -eq 'Complete') { $script:completed++; Log "DONE $name $($state.CompletedGroups)/$($state.TotalGroups)"; return }
        if ($state -and $state.Phase -eq 'Failed' -and $state.Failure -notlike '*Other test/build activity started*') { throw "Benchmark failed: $($state.Failure)" }
        $active = $false
        if ($state) {
            $controller = Get-CimInstance Win32_Process -Filter "ProcessId = $($state.ControllerPid)" -ErrorAction SilentlyContinue
            $active = $controller -and $controller.CommandLine -like '*Start-ContractBenchmarks.ps1*' -and $controller.CommandLine.Contains($path)
        }
        if (-not $active) {
            $arguments.Resume = Test-Path -LiteralPath (Join-Path $path 'run.json')
            & (Join-Path $PSScriptRoot 'Start-ContractBenchmarks.ps1') @arguments
            Start-Sleep -Seconds 2
        }
        if (Test-Path -LiteralPath $statePath) {
            $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
            $script:childPid = $state.ControllerPid
            Status 'Measuring'
            Log "PROGRESS $name $($state.Phase) $($state.Current) $($state.CompletedGroups)/$($state.TotalGroups)"
        }
        Start-Sleep -Seconds 15
    }
}

try {
    $previousStatus = Join-Path $OutputDirectory 'status.json'
    if (Test-Path -LiteralPath $previousStatus) {
        $previous = Get-Content -LiteralPath $previousStatus -Raw | ConvertFrom-Json
        if ($previous.CurrentPid) {
            $orphan = Get-CimInstance Win32_Process -Filter "ProcessId = $($previous.CurrentPid)" -ErrorAction SilentlyContinue
            if ($orphan -and $orphan.CommandLine -and $orphan.CommandLine.Contains($OutputDirectory) -and
                $orphan.CommandLine -notlike '*Start-ContractBenchmarks.ps1*') { throw 'A previous child is still running; do not start a duplicate.' }
        }
    }
    $manifestPath = Join-Path $OutputDirectory 'sources.json'
    $files = Source-Hashes
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($files -join "`n"))))
    $manifest = @{ Revision = (& git rev-parse HEAD); Fingerprint = $fingerprint; Files = $files; Rounds = $Rounds }
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.Fingerprint -ne $fingerprint -or $manifest.Rounds -ne $Rounds) { throw 'Sources or round count changed; use a new output directory.' }
    }
    else { Json $manifestPath $manifest }
    $baseline = Get-Content -LiteralPath (Join-Path $BaselineDirectory 'baseline.json') -Raw | ConvertFrom-Json
    foreach ($binary in $baseline.Binaries) {
        if ((Get-FileHash -LiteralPath (Join-Path $BaselineDirectory "$($binary.Profile)/$($binary.File)")).Hash -ne $binary.Sha256) { throw 'Frozen baseline changed.' }
    }
    $null = New-Item -ItemType Directory -Path $ResultDirectory -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'tools') -Force
    Copy-Item -LiteralPath $manifestPath, (Join-Path $BaselineDirectory 'baseline.json') -Destination $ResultDirectory -Force
    $focusedInputs = Join-Path $OutputDirectory 'focused-inputs'
    $null = New-Item -ItemType Directory -Path $focusedInputs -Force
    $gallery = Get-Content -LiteralPath (Join-Path $InputDirectory 'gallery-inputs.json') -Raw | ConvertFrom-Json
    Json (Join-Path $focusedInputs 'gallery-inputs.json') @{ Properties = @{ RootNamespace = 'ProjectionFixture'
        MSBuildProjectDirectory = (Join-Path $PSScriptRoot 'ProjectionFixture'); DefineConstants = 'DEBUG' }
        Items = @{ Compile = @(); AdditionalFiles = @(@{ FullPath = (Join-Path $PSScriptRoot 'ProjectionFixture/Siblings.akbura') })
            ReferencePath = @($gallery.Items.ReferencePath) } }
    Run 'summary-guards' @('pwsh', '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-ProjectionSummary.ps1'))
    Run 'projection-tests' @('dotnet', 'test', 'src/Akbura.UnitTests/Akbura.UnitTests.csproj', '-c', 'DebugStats',
        '-p:ProbeReuseStage=projection', '--artifacts-path', (Join-Path $OutputDirectory 'tests'), '--filter',
        'FullyQualifiedName~Projection|FullyQualifiedName~Localized|FullyQualifiedName~MarkupExpressionReuseTests|FullyQualifiedName~ProbeDeclarationChanges|FullyQualifiedName~ProbeCompilationChanges|FullyQualifiedName~ProbeErrorsAndRepairs|FullyQualifiedName~ConcurrentProbeBranches|FullyQualifiedName~CancellationDuringProbeBinding',
        '--logger', 'trx;LogFileName=projection-gate.trx', '--results-directory', (Join-Path $ResultDirectory 'tests'),
        '--nologo', '-v:minimal', '-p:UseSharedCompilation=false', '-nr:false')
    $tools = @{}
    foreach ($profile in @('stats', 'production')) {
        $tools[$profile] = @(Freeze $profile)[0]
        Run "abi-$profile" @('dotnet', (Join-Path $tools[$profile] 'HotReloadBenchmarks.dll'), 'statistics-check')
        Run "preflight-$profile" @('dotnet', (Join-Path $tools[$profile] 'HotReloadBenchmarks.dll'), 'localized-check',
            (Join-Path $InputDirectory 'gallery-inputs.json'), 'Counter.akbura', 'localized-literal', $(if ($profile -eq 'stats') { 'stats' } else { 'off' }))
        Run "preflight-focused-$profile" @('dotnet', (Join-Path $tools[$profile] 'HotReloadBenchmarks.dll'), 'localized-check',
            (Join-Path $focusedInputs 'gallery-inputs.json'), 'Siblings.akbura', 'localized-binding-sibling', $(if ($profile -eq 'stats') { 'stats' } else { 'off' }))
    }
    foreach ($variant in @('baseline', 'projection')) {
        $tool = if ($variant -eq 'baseline') { Join-Path $BaselineDirectory 'stats' } else { $tools['stats'] }
        Matrix "stats-$variant-literal" $tool 'stats' $false
        Matrix "stats-$variant-binding" $tool 'stats' $true
        Matrix "stats-$variant-focused" $tool 'stats' $true $true
    }
    for ($round = 1; $round -le $Rounds; $round++) {
        $order = if ($round % 2 -eq 0) { @('projection', 'baseline') } else { @('baseline', 'projection') }
        foreach ($variant in $order) {
            $tool = if ($variant -eq 'baseline') { Join-Path $BaselineDirectory 'production' } else { $tools['production'] }
            Matrix "production-$round-$variant-literal" $tool 'off' $false
            Matrix "production-$round-$variant-binding" $tool 'off' $true
            Matrix "production-$round-$variant-focused" $tool 'off' $true $true
        }
    }
    Run 'summarize' @('pwsh', '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Summarize-ProjectionReuse.ps1'),
        '-InputDirectory', $OutputDirectory, '-ResultDirectory', $ResultDirectory, '-Rounds', "$Rounds")
    Status 'EvidenceReadyNeedsReview'
    Log 'COMPLETE Projection-only evidence is ready. Review correctness, GC, causal counters, memory and repeated production timings before implementing semantic-result reuse. No commit or next optimization was performed.'
}
catch { Status 'Failed' ($_ | Out-String); Log ('FAILED ' + ($_ | Out-String)); exit 1 }
finally { $journal.Dispose(); $lock.Dispose() }
