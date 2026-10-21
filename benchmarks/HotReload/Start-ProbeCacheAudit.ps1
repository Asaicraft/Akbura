#requires -Version 7.2
param(
    [string]$OutputDirectory = 'bin/CrossRevision/audit-validated',
    [string]$InputDirectory = 'bin/SemanticContracts/final/prepared',
    [string]$ResultDirectory = 'benchmarks/HotReload/results/2026-10-21-cross-revision-binding',
    [switch]$Worker,
    [ValidateRange(2, 5)][int]$Rounds = 2
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repository
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$encoding = [Text.UTF8Encoding]::new($false)
if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory 'controls.json'))) {
    throw 'Freeze original/current production and STATS closures and controls.json before editing compiler behavior.'
}
if (-not $Worker) {
    $arguments = @('-NoProfile', '-File', $PSCommandPath, '-Worker', '-OutputDirectory', $OutputDirectory,
        '-InputDirectory', $InputDirectory, '-ResultDirectory', $ResultDirectory, '-Rounds', "$Rounds")
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
$total = 8 * 4 + $Rounds * 12 + 2
function Json([string]$path, $value) {
    $text = [regex]::Replace(($value | ConvertTo-Json -Depth 8), '\r?\n', "`r`n") + "`r`n"
    [IO.File]::WriteAllText(($path + '.tmp'), $text, $encoding)
    [IO.File]::Move(($path + '.tmp'), $path, $true)
}
function Log([string]$text) {
    $journal.WriteLine([DateTime]::UtcNow.ToString('o') + ' ' + $text)
    $journal.BaseStream.Flush($true)
}
function Status([string]$phase, [string]$failure = '') {
    Json (Join-Path $OutputDirectory 'status.json') @{ Phase = $phase; Current = $script:step; CurrentPid = $script:childPid
        CompletedSteps = $script:completed; TotalSteps = $total; ControllerPid = $PID; UpdatedUtc = [DateTime]::UtcNow.ToString('o'); Failure = $failure }
}
function Source-Hashes {
    @(& git ls-files --cached --others --exclude-standard -- 'src/Akbura.Generator' 'src/Akbura.BlackSilence' 'src/Akbura.UnitTests/MarkupExpressionReuseTests.cs' 'benchmarks/HotReload/*.ps1' |
        Sort-Object -Unique | ForEach-Object { "$_ $((Get-FileHash -LiteralPath $_).Hash)" })
}
function Assert-Sources {
    if (((Source-Hashes) -join "`n") -cne ($script:manifest.Files -join "`n")) {
        throw 'Audit sources changed. Preserve this evidence and use a new output directory.'
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
            throw "Incompatible successful step: $name"
        }
        $script:completed++
        Log "REUSED $name"
        return
    }
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $logs = Join-Path $OutputDirectory "$name-$stamp"
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
            Log "HEARTBEAT $name Running"
        }
        $null = $copyOut.GetAwaiter().GetResult()
        $null = $copyErr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Exit $($process.ExitCode): $logs" }
        Json $marker @{ Fingerprint = $script:manifest.Fingerprint; Arguments = $arguments; Logs = $logs; CompletedUtc = [DateTime]::UtcNow.ToString('o') }
        $script:completed++
        Log "DONE $name"
    }
    finally {
        $stdout.Dispose()
        $stderr.Dispose()
        $process.Dispose()
        $script:childPid = $null
    }
}
function Freeze([string]$variant, [string]$profile) {
    $name = "$variant-$profile"
    $destination = Join-Path $OutputDirectory "tools/$name"
    $configuration = if ($profile -eq 'stats') { 'DebugStats' } else { 'Release' }
    $build = Join-Path $OutputDirectory "build/$name"
    Run "build-$name" @('dotnet', 'build', 'src/Akbura.Generator/Akbura.Generator.csproj', '-c', $configuration,
        '--artifacts-path', $build, '--nologo', '-v:minimal', '-p:UseSharedCompilation=false', '-nr:false', "-p:ProbeCacheAuditVariant=$variant")
    $generator = @(Get-ChildItem -LiteralPath (Join-Path $build 'bin/Akbura.Generator') -Filter 'Akbura.Generator.dll' -File -Recurse)
    if ($generator.Count -ne 1) { throw "Expected exactly one built generator for $name" }
    $proof = Join-Path $OutputDirectory "$name.closure.json"
    if (-not (Test-Path -LiteralPath $proof)) {
        if (Test-Path -LiteralPath $destination) { throw "Incomplete frozen closure: $destination" }
        Copy-Item -LiteralPath (Join-Path $OutputDirectory "controls/current-$profile") -Destination $destination -Recurse
        Copy-Item -LiteralPath $generator[0].FullName -Destination (Join-Path $destination 'Akbura.Generator.dll') -Force
        $pdb = [IO.Path]::ChangeExtension($generator[0].FullName, '.pdb')
        if (Test-Path -LiteralPath $pdb) { Copy-Item -LiteralPath $pdb -Destination (Join-Path $destination 'Akbura.Generator.pdb') -Force }
        $hashes = @(Get-ChildItem -LiteralPath $destination -File | ForEach-Object { @{ Name = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
        Json $proof @{ Variant = $variant; Profile = $profile; SourceFingerprint = $script:manifest.Fingerprint; ToolHashes = $hashes }
    }
    $saved = Get-Content -LiteralPath $proof -Raw | ConvertFrom-Json
    foreach ($hash in $saved.ToolHashes) {
        if ((Get-FileHash -LiteralPath (Join-Path $destination $hash.Name)).Hash -ne $hash.Sha256) { throw "Frozen closure changed: $name/$($hash.Name)" }
    }
    return $destination
}
function Matrix([string]$name, [string]$tool, [string]$profile, [string]$category) {
    Assert-Sources
    $script:step = $name
    $path = Join-Path $OutputDirectory "runs/$name"
    $portable = Join-Path $ResultDirectory "audit/$name"
    $arguments = @{ InputDirectory = $InputDirectory; OutputDirectory = $path; ResultDirectory = $portable; ToolDirectory = $tool
        Scenario = $(if ($category -eq 'literal') { @('localized-literal') } else { @('localized-binding-sibling') })
        Component = $(if ($category -eq 'literal') { @('gallery:Counter.akbura', 'gallery:DataTemplatesPage.akbura', 'purity:NeedHelp.akbura', 'purity:Sidebar.akbura') }
            else { @('gallery:DataTemplates.akbura', 'purity:NavButton.akbura') })
        Mode = @('enabled'); Profiling = $profile; SkipLifetime = $true; SkipWatch = $true; SkipPreflight = $true }
    while ($true) {
        $statePath = Join-Path $path 'status.json'
        $state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
        if ($state -and $state.Phase -eq 'Complete') {
            $script:completed++
            Log "DONE $name $($state.CompletedGroups)/$($state.TotalGroups)"
            return
        }
        if ($state -and $state.Phase -eq 'Failed' -and $state.Failure -notlike '*Other test/build activity started*') {
            throw "Benchmark failed: $($state.Failure)"
        }
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
    $previousStatusPath = Join-Path $OutputDirectory 'status.json'
    if (Test-Path -LiteralPath $previousStatusPath) {
        $previous = Get-Content -LiteralPath $previousStatusPath -Raw | ConvertFrom-Json
        if ($previous.CurrentPid) {
            $orphan = Get-CimInstance Win32_Process -Filter "ProcessId = $($previous.CurrentPid)" -ErrorAction SilentlyContinue
            if ($orphan -and $orphan.CommandLine -and $orphan.CommandLine.Contains($OutputDirectory) -and
                $orphan.CommandLine -notlike '*Start-ContractBenchmarks.ps1*') {
                throw "A previous audit child is still running: $($previous.CurrentPid). Preserve it; do not start a duplicate build."
            }
        }
    }
    $manifestPath = Join-Path $OutputDirectory 'sources.json'
    $files = Source-Hashes
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($files -join "`n"))))
    $manifest = @{ Revision = (& git rev-parse HEAD); Fingerprint = $fingerprint; Files = $files; Rounds = $Rounds }
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.Fingerprint -ne $fingerprint -or $manifest.Rounds -ne $Rounds) { throw 'Use a new audit directory after changing sources or round count.' }
    }
    else { Json $manifestPath $manifest }
    $controls = Get-Content -LiteralPath (Join-Path $OutputDirectory 'controls.json') -Raw | ConvertFrom-Json
    foreach ($hash in $controls.Controls) {
        if ((Get-FileHash -LiteralPath (Join-Path $OutputDirectory "controls/$($hash.Control)/$($hash.File)")).Hash -ne $hash.Sha256) { throw 'A frozen original/current control changed.' }
    }
    $null = New-Item -ItemType Directory -Path $ResultDirectory -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'tools') -Force
    Copy-Item -LiteralPath (Join-Path $OutputDirectory 'controls.json'), $manifestPath -Destination $ResultDirectory -Force
    Run 'audit-report-guards' @('pwsh', '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-ProbeCacheAudit.ps1'))
    $tools = @{}
    foreach ($variant in @('current', 'disabled', 'prehashed', 'repeat-admission')) {
        foreach ($profile in @('stats', 'production')) {
            $frozen = @(Freeze $variant $profile)
            if ($frozen.Count -ne 1 -or -not (Test-Path -LiteralPath (Join-Path $frozen[0] 'HotReloadBenchmarks.dll'))) {
                throw "Freezing must return exactly one existing tool path: $variant-$profile"
            }
            $tools["$variant-$profile"] = $frozen[0]
            Run "abi-$variant-$profile" @('dotnet', (Join-Path $tools["$variant-$profile"] 'HotReloadBenchmarks.dll'), 'statistics-check')
        }
    }
    # Every new binary must complete real initial/edited/fresh-driver checks before timing.
    foreach ($variant in @('current', 'disabled', 'prehashed', 'repeat-admission')) {
        foreach ($profile in @('stats', 'production')) {
            $tool = $tools["$variant-$profile"]
            Run "preflight-$variant-$profile" @('dotnet', (Join-Path $tool 'HotReloadBenchmarks.dll'), 'localized-check',
                (Join-Path $InputDirectory 'gallery-inputs.json'), 'Counter.akbura', 'localized-literal', $(if ($profile -eq 'stats') { 'stats' } else { 'off' }))
        }
    }
    foreach ($variant in @('current', 'disabled', 'prehashed', 'repeat-admission')) {
        foreach ($category in @('literal', 'binding')) { Matrix "stats-$variant-$category" $tools["$variant-stats"] 'stats' $category }
    }
    for ($round = 1; $round -le $Rounds; $round++) {
        $order = @('original', 'current', 'rebuilt-current', 'disabled', 'prehashed', 'repeat-admission')
        if ($round % 2 -eq 0) { [array]::Reverse($order) }
        foreach ($variant in $order) {
            $tool = if ($variant -in @('original', 'current')) { Join-Path $OutputDirectory "controls/$variant-production" }
                elseif ($variant -eq 'rebuilt-current') { $tools['current-production'] } else { $tools["$variant-production"] }
            foreach ($category in @('literal', 'binding')) { Matrix "production-$round-$variant-$category" $tool 'off' $category }
        }
    }
    Run 'summarize-audit' @('pwsh', '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Summarize-ProbeCacheAudit.ps1'),
        '-InputDirectory', $OutputDirectory, '-ResultDirectory', $ResultDirectory)
    Status 'AuditEvidenceReadyNeedsDecision'
    Log 'COMPLETE Scoped-cache audit evidence is ready. No cross-revision cache decision, commit, push or next optimization was performed.'
}
catch {
    Status 'Failed' ($_ | Out-String)
    Log ('FAILED ' + ($_ | Out-String))
    exit 1
}
finally { $journal.Dispose(); $lock.Dispose() }
