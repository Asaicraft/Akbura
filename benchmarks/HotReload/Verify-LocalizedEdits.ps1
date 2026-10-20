#requires -Version 7.2
param(
    [string]$InputDirectory = 'bin/SemanticContracts/final/prepared',
    [string]$OutputDirectory = 'bin/FineGrained/final',
    [string]$BaselineDirectory = 'bin/FineGrained/before',
    [string]$OriginalInstrumentedDirectory = '',
    [string]$OriginalProductionDirectory = 'bin/FineGrained/original-production/tool',
    [string]$VerifiedRunDirectory = '',
    [string]$ResultDirectory = 'benchmarks/HotReload/results/2026-10-19-akbura-fine-grained-incrementality',
    [switch]$Worker,
    [switch]$SkipFullUnitTests,
    [switch]$SkipWatch
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repository
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$BaselineDirectory = [IO.Path]::GetFullPath($BaselineDirectory)
if ($OriginalInstrumentedDirectory) { $OriginalInstrumentedDirectory = [IO.Path]::GetFullPath($OriginalInstrumentedDirectory) }
$OriginalProductionDirectory = [IO.Path]::GetFullPath($OriginalProductionDirectory)
if ($VerifiedRunDirectory) { $VerifiedRunDirectory = [IO.Path]::GetFullPath($VerifiedRunDirectory) }
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
if ((-not (Test-Path -LiteralPath (Join-Path $BaselineDirectory 'run.json')) -and
    (-not $OriginalInstrumentedDirectory -or -not (Test-Path -LiteralPath (Join-Path $OriginalInstrumentedDirectory 'Akbura.Generator.dll')))) -or
    -not (Test-Path -LiteralPath (Join-Path $OriginalProductionDirectory 'Akbura.Generator.dll'))) {
    throw 'Freeze the original instrumented and Release binaries before changing production behavior. A new implementation cannot serve as its own baseline.'
}
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
if (-not $Worker) {
    $arguments = @('-NoProfile','-File',$PSCommandPath,'-Worker','-InputDirectory',$InputDirectory,'-OutputDirectory',$OutputDirectory,
        '-BaselineDirectory',$BaselineDirectory,'-OriginalProductionDirectory',$OriginalProductionDirectory,'-ResultDirectory',$ResultDirectory)
    if ($OriginalInstrumentedDirectory) { $arguments += @('-OriginalInstrumentedDirectory',$OriginalInstrumentedDirectory) }
    if ($VerifiedRunDirectory) { $arguments += @('-VerifiedRunDirectory',$VerifiedRunDirectory) }
    if ($SkipFullUnitTests) { $arguments += '-SkipFullUnitTests' }
    if ($SkipWatch) { $arguments += '-SkipWatch' }
    $quoted = @($arguments | ForEach-Object { '"' + $_ + '"' })
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $process = Start-Process (Get-Command pwsh).Source -ArgumentList $quoted -WindowStyle Hidden -WorkingDirectory $repository -PassThru `
        -RedirectStandardOutput (Join-Path $OutputDirectory "controller-$stamp.stdout.log") -RedirectStandardError (Join-Path $OutputDirectory "controller-$stamp.stderr.log")
    Write-Host "Controller PID: $($process.Id)"
    Write-Host "Progress: Get-Content -LiteralPath '$OutputDirectory/journal.log' -Tail 20 -Wait"
    return
}

$lock = [IO.File]::Open((Join-Path $OutputDirectory 'controller.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$encoding = [Text.UTF8Encoding]::new($false)
$journal = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'journal.log'), $true, $encoding)
$journal.AutoFlush = $true
$step = 'Starting'
$childPid = $null
$sourcePaths = @(& git ls-files --cached --others --exclude-standard -- 'src/Akbura.Generator' 'src/Akbura.BlackSilence' 'src/Akbura.UnitTests' 'benchmarks/HotReload/*.cs' 'benchmarks/HotReload/*.ps1') | Sort-Object -Unique
$sourceHashes = @($sourcePaths | ForEach-Object { "$_ $((Get-FileHash -LiteralPath $_).Hash)" })
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($sourceHashes -join "`n"))))
$manifestPath = Join-Path $OutputDirectory 'sources.json'
if (Test-Path -LiteralPath $manifestPath) {
    if ((Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).Fingerprint -ne $fingerprint) {
        throw 'Source fingerprint changed; use a new output directory instead of mixing verification evidence.'
    }
}
else {
    [IO.File]::WriteAllText($manifestPath, (@{ Revision = (& git rev-parse HEAD); Fingerprint = $fingerprint; Files = $sourceHashes } | ConvertTo-Json -Depth 4) + "`r`n", $encoding)
}

function Log([string]$message) {
    $journal.WriteLine([DateTime]::UtcNow.ToString('o') + ' ' + $message)
    $journal.BaseStream.Flush($true)
}

function Status([string]$phase, [string]$failure = '') {
    $value = @{ Phase = $phase; Current = $script:step; ControllerPid = $PID; CurrentPid = $script:childPid
        SourceFingerprint = $fingerprint; UpdatedUtc = [DateTime]::UtcNow.ToString('o'); Failure = $failure }
    $path = Join-Path $OutputDirectory 'status.json'
    [IO.File]::WriteAllText(($path + '.tmp'), ($value | ConvertTo-Json) + "`r`n", $encoding)
    [IO.File]::Move(($path + '.tmp'), $path, $true)
}

function Assert-Sources {
    $currentPaths = @(& git ls-files --cached --others --exclude-standard -- 'src/Akbura.Generator' 'src/Akbura.BlackSilence' 'src/Akbura.UnitTests' 'benchmarks/HotReload/*.cs' 'benchmarks/HotReload/*.ps1') | Sort-Object -Unique
    $currentHashes = @($currentPaths | ForEach-Object { "$_ $((Get-FileHash -LiteralPath $_).Hash)" })
    if (($currentHashes -join "`n") -cne ($sourceHashes -join "`n")) {
        throw 'Sources changed during verification. Preserve this evidence and start a new output directory.'
    }
}

function Run([string]$name, [string]$command, [string[]]$arguments) {
    Assert-Sources
    $script:step = $name
    $marker = Join-Path $OutputDirectory ($name + '.success.json')
    if (Test-Path -LiteralPath $marker) {
        $saved = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($saved.Fingerprint -eq $fingerprint -and $saved.Command -eq $command -and
            (ConvertTo-Json -InputObject @($saved.Arguments | Where-Object { $_ -ne '-Resume' }) -Compress) -ceq
            (ConvertTo-Json -InputObject @($arguments | Where-Object { $_ -ne '-Resume' }) -Compress)) {
            Log "RESUMED $name"
            return $saved.Directory
        }
        throw "Verification command or source fingerprint changed: $marker"
    }
    $directory = Join-Path $OutputDirectory ($name + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
    $null = New-Item -ItemType Directory -Path $directory
    $info = [Diagnostics.ProcessStartInfo]::new($command)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.WorkingDirectory = $repository
    foreach ($argument in $arguments) { $info.ArgumentList.Add($argument.Replace('{RESULTS}', $directory)) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $writer = [IO.StreamWriter]::new((Join-Path $directory 'output.log'), $false, $encoding)
    $errors = [IO.StreamWriter]::new((Join-Path $directory 'stderr.log'), $false, $encoding)
    $writer.AutoFlush = $true
    $errors.AutoFlush = $true
    $started = $false
    try {
        $null = $process.Start()
        $started = $true
        $script:childPid = $process.Id
        Status 'Verifying'
        Log "START $name pid=$script:childPid logs=$directory"
        $stdout = $process.StandardOutput.ReadLineAsync()
        $stderr = $process.StandardError.ReadLineAsync()
        $heartbeat = [Diagnostics.Stopwatch]::StartNew()
        while ($null -ne $stdout -or $null -ne $stderr -or -not $process.HasExited) {
            while ($null -ne $stdout -and $stdout.IsCompleted) {
                $line = $stdout.GetAwaiter().GetResult()
                if ($null -eq $line) { $stdout = $null }
                else { $writer.WriteLine($line); $stdout = $process.StandardOutput.ReadLineAsync() }
            }
            while ($null -ne $stderr -and $stderr.IsCompleted) {
                $line = $stderr.GetAwaiter().GetResult()
                if ($null -eq $line) { $stderr = $null }
                else { $errors.WriteLine($line); $stderr = $process.StandardError.ReadLineAsync() }
            }
            if ($heartbeat.Elapsed.TotalSeconds -ge 15) {
                $nestedPath = Join-Path $OutputDirectory 'quality/status.json'
                $detail = if ($name -eq 'quality' -and (Test-Path -LiteralPath $nestedPath)) {
                    $nested = Get-Content -LiteralPath $nestedPath -Raw | ConvertFrom-Json
                    "$($nested.Phase) / $($nested.Current)"
                } else { 'Running' }
                Log "HEARTBEAT $name $detail"
                Status 'Verifying'
                $heartbeat.Restart()
            }
            Start-Sleep -Milliseconds 100
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "Exit $($process.ExitCode): $directory" }
        Assert-Sources
        [IO.File]::WriteAllText($marker, (@{ Directory = $directory; Fingerprint = $fingerprint; Command = $command; Arguments = $arguments
            CompletedUtc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 5) + "`r`n", $encoding)
        Log "DONE $name"
        return $directory
    }
    finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $writer.Dispose()
        $errors.Dispose()
        $process.Dispose()
        $script:childPid = $null
    }
}

function Matrix([string]$name, [string]$path, [string]$report, [string]$tool, [switch]$Production) {
    $script:step = $name
    $selectionArguments = @{ CellOverrideFile = $(if ($Production) { $productionCellSelectionPath } else { $cellSelectionPath }) }
    if (-not (Test-Path -LiteralPath (Join-Path $path 'run.json'))) {
        $baselinePath = Join-Path $BaselineDirectory 'run.json'
        $baseline = if (Test-Path -LiteralPath $baselinePath) { Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json } else { $null }
        $scenarios = if ($Production) { @('localized-literal','localized-binding-sibling','localized-diagnostic') }
            elseif ($baseline) { @($baseline.Scenarios) }
            else { @('localized-literal','localized-property','localized-akcss','localized-binding-sibling','localized-expression',
                'localized-property-add-remove','localized-structure','localized-name','localized-diagnostic','localized-trivia') }
        $components = if ($Production) { @('gallery:Counter.akbura','purity:Sidebar.akbura') }
            elseif ($baseline) { @($baseline.Components) }
            else { @('gallery:Counter.akbura','gallery:DataTemplatesPage.akbura','purity:NeedHelp.akbura','purity:Sidebar.akbura') }
        $null = & (Join-Path $PSScriptRoot 'Start-ContractBenchmarks.ps1') -InputDirectory $InputDirectory -OutputDirectory $path `
            -ResultDirectory $report -ToolDirectory $tool -Scenario $scenarios -Component $components -Mode @('enabled') `
            -Profiling $(if ($Production) { 'off' } else { 'stats' }) -SkipLifetime -SkipWatch -SkipPreflight @selectionArguments
    }
    elseif (-not $Production -and (Test-Path -LiteralPath (Join-Path $path 'status.json'))) {
        $previous = Get-Content -LiteralPath (Join-Path $path 'status.json') -Raw | ConvertFrom-Json
        if ($previous.Phase -eq 'Failed' -and $previous.Current -in @(
            'gallery / DataTemplatesPage.akbura / localized-binding-sibling / enabled',
            'gallery / DataTemplatesPage.akbura / localized-expression / enabled')) {
            Log 'RESUME corrected component selection; completed timing groups remain unchanged.'
            $null = & (Join-Path $PSScriptRoot 'Start-ContractBenchmarks.ps1') -OutputDirectory $path -Resume @selectionArguments
            $deadline = [DateTime]::UtcNow.AddSeconds(30)
            do {
                Start-Sleep -Milliseconds 200
                $fresh = Get-Content -LiteralPath (Join-Path $path 'status.json') -Raw | ConvertFrom-Json
                if ([DateTime]::UtcNow -gt $deadline) { throw 'Resumed benchmark controller did not update its status.' }
            } while ($fresh.ControllerPid -eq $previous.ControllerPid)
        }
    }
    while ($true) {
        Assert-Sources
        if (-not (Test-Path -LiteralPath (Join-Path $path 'status.json'))) { Start-Sleep -Milliseconds 200; continue }
        $state = Get-Content -LiteralPath (Join-Path $path 'status.json') -Raw | ConvertFrom-Json
        $script:childPid = $state.CurrentPid
        Status 'Measuring'
        Log "PROGRESS $name $($state.Phase) $($state.Current) $($state.CompletedGroups)/$($state.TotalGroups)"
        if ($state.Phase -eq 'Complete') { $script:childPid = $null; return }
        $active = Get-Process -Id $state.ControllerPid -ErrorAction SilentlyContinue
        if ($state.Phase -eq 'Failed' -and $state.Failure -notmatch 'Other test/build activity') {
            throw "Benchmark failed: $($state.Failure)"
        }
        if ($state.Phase -eq 'Failed' -and $active) { Start-Sleep -Seconds 1; continue }
        if (-not $active -or $state.Phase -eq 'Failed') {
            Log "RESUME $name; incomplete groups remain excluded from accepted timings."
            $null = & (Join-Path $PSScriptRoot 'Start-ContractBenchmarks.ps1') -OutputDirectory $path -Resume
        }
        Start-Sleep -Seconds 15
    }
}

function Assert-Harness {
    $benchmarkHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'bin/Debug/net10.0/HotReloadBenchmarks.dll')).Hash
    $originalTool = if ($OriginalInstrumentedDirectory) { $OriginalInstrumentedDirectory } else { Join-Path $BaselineDirectory 'tool' }
    foreach ($tool in @($originalTool,$OriginalProductionDirectory)) {
        if ((Get-FileHash -LiteralPath (Join-Path $tool 'HotReloadBenchmarks.dll')).Hash -ne $benchmarkHash) {
            throw 'Before/after must use the same benchmark host. Preserve frozen analyzers when updating only the harness.'
        }
    }
}

try {
    Assert-Harness
    $cellSelectionPath = Join-Path $OutputDirectory 'localized-cells.json'
    $cellSelection = @{ Reason = 'Use real inline Text expressions in template/navigation components; retain all other page/sidebar scenarios unchanged.'
        Overrides = @('gallery:DataTemplatesPage.akbura/localized-binding-sibling=DataTemplates.akbura',
            'gallery:DataTemplatesPage.akbura/localized-expression=DataTemplates.akbura',
            'purity:Sidebar.akbura/localized-binding-sibling=NavButton.akbura',
            'purity:Sidebar.akbura/localized-expression=NavButton.akbura') }
    if (Test-Path -LiteralPath $cellSelectionPath) {
        $existing = Get-Content -LiteralPath $cellSelectionPath -Raw | ConvertFrom-Json
        if ((ConvertTo-Json -InputObject @($existing.Overrides) -Compress) -cne
            (ConvertTo-Json -InputObject @($cellSelection.Overrides) -Compress)) { throw 'Localized cell routing changed.' }
    }
    else { [IO.File]::WriteAllText($cellSelectionPath, ($cellSelection | ConvertTo-Json -Depth 4) + "`r`n", $encoding) }
    $productionCellSelectionPath = Join-Path $OutputDirectory 'production-cells.json'
    $productionSelection = @{ Reason = $cellSelection.Reason
        Overrides = @('purity:Sidebar.akbura/localized-binding-sibling=NavButton.akbura') }
    if (-not (Test-Path -LiteralPath $productionCellSelectionPath)) {
        [IO.File]::WriteAllText($productionCellSelectionPath, ($productionSelection | ConvertTo-Json -Depth 4) + "`r`n", $encoding)
    }
    else {
        $existing = Get-Content -LiteralPath $productionCellSelectionPath -Raw | ConvertFrom-Json
        if ((ConvertTo-Json -InputObject @($existing.Overrides) -Compress) -cne
            (ConvertTo-Json -InputObject @($productionSelection.Overrides) -Compress)) { throw 'Production cell routing changed.' }
    }
    $reuse = $null
    if ($VerifiedRunDirectory) {
        $step = 'Validating previously successful unit suites'
        Status 'Verifying'
        Log 'START verification reuse; compiler/test source changes are not allowed.'
        $reuse = & (Join-Path $PSScriptRoot 'Get-VerificationReuse.ps1') -PreviousDirectory $VerifiedRunDirectory -CurrentManifest $manifestPath
        $null = Run 'verification-reuse-guards' 'pwsh' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Test-VerificationReuse.ps1'),
            '-PreviousDirectory',$VerifiedRunDirectory,'-CurrentManifest',$manifestPath)
    }
    $originalTool = if ($OriginalInstrumentedDirectory) { $OriginalInstrumentedDirectory } else { Join-Path $BaselineDirectory 'tool' }
    foreach ($entry in @(
        @{ Name = 'original'; Tool = $originalTool },
        @{ Name = 'optimized'; Tool = (Join-Path $PSScriptRoot 'bin/Debug/net10.0') },
        @{ Name = 'original-production'; Tool = $OriginalProductionDirectory }
    )) {
        $logs = Run ('statistics-' + $entry.Name) 'dotnet' @((Join-Path $entry.Tool 'HotReloadBenchmarks.dll'),'statistics-check')
        $portable = Join-Path $ResultDirectory ('verification/statistics/' + $entry.Name)
        $null = New-Item -ItemType Directory -Path $portable -Force
        Copy-Item -LiteralPath (Join-Path $logs 'output.log') -Destination $portable -Force
    }
    $coverage = Run 'localized-input-coverage' 'pwsh' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Test-LocalizedInputs.ps1'),
        '-ToolDirectory',(Join-Path $PSScriptRoot 'bin/Debug/net10.0'),'-InputDirectory',$InputDirectory,
        '-CellOverrideFile',$cellSelectionPath,'-OutputPath','{RESULTS}/inputs.json')
    Copy-Item -LiteralPath (Join-Path $coverage 'inputs.json') -Destination (Join-Path $ResultDirectory 'verification/localized-inputs.json') -Force
    # These short functional checks are not accepted timing samples. Catch an
    # invalid utility/target pair before the long test suites and timing matrix.
    foreach ($mode in @('original','optimized')) {
        $tool = if ($mode -eq 'original') {
            if ($OriginalInstrumentedDirectory) { $OriginalInstrumentedDirectory } else { Join-Path $BaselineDirectory 'tool' }
        } else { Join-Path $PSScriptRoot 'bin/Debug/net10.0' }
        $null = Run ("literal-stats-$mode") 'dotnet' @((Join-Path $tool 'HotReloadBenchmarks.dll'),'localized-check',
            (Join-Path $InputDirectory 'gallery-inputs.json'),'Counter.akbura','localized-literal','stats')
        foreach ($component in @('gallery:Counter.akbura','gallery:DataTemplatesPage.akbura','purity:NeedHelp.akbura','purity:Sidebar.akbura')) {
            $workload,$file = $component.Split(':')
            $null = Run ("akcss-$mode-$workload-" + [IO.Path]::GetFileNameWithoutExtension($file)) 'dotnet' @(
                (Join-Path $tool 'HotReloadBenchmarks.dll'),'localized-check',(Join-Path $InputDirectory "$workload-inputs.json"),$file,'localized-akcss','stats')
        }
        foreach ($target in @('gallery:DataTemplates.akbura','purity:NavButton.akbura')) {
            $workload,$file = $target.Split(':')
            foreach ($scenario in @('localized-binding-sibling','localized-expression')) {
                $null = Run ("expression-$mode-$workload-$scenario") 'dotnet' @((Join-Path $tool 'HotReloadBenchmarks.dll'),'localized-check',
                    (Join-Path $InputDirectory "$workload-inputs.json"),$file,$scenario,'stats')
            }
        }
    }
    # Functional checks may run alongside another worktree; clean timing groups
    # wait for them. Any baseline group overlapping a build is discarded and retried.
    if ($VerifiedRunDirectory) {
        $proof = ($reuse | ConvertTo-Json -Depth 8) + "`r`n"
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'verification-reuse.json'), $proof, $encoding)
        [IO.File]::WriteAllText((Join-Path $ResultDirectory 'verification/reuse.json'), $proof, $encoding)
        foreach ($suite in $reuse.Suites) {
            $portable = Join-Path $ResultDirectory ('verification/' + $suite.Name)
            $null = New-Item -ItemType Directory -Path $portable -Force
            Copy-Item -LiteralPath $suite.Trx -Destination $portable -Force
            Log "REUSED $($suite.Name) $($suite.Passed)/$($suite.Executed); original fingerprint=$($reuse.PreviousFingerprint)"
        }
        foreach ($check in @('Test-Report.ps1','Test-InvalidationReport.ps1','Test-ContractRunner.ps1','Test-StatisticsCompatibility.ps1')) {
            $null = Run ($check.Replace('.ps1','')) 'pwsh' @('-NoProfile','-File',(Join-Path $PSScriptRoot $check))
        }
    }
    else {
        $quality = Join-Path $OutputDirectory 'quality'
        $arguments = @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Verify-SemanticDependencies.ps1'),'-LocalizedEdits','-SkipBenchmarks',
            '-InputDirectory',$InputDirectory,'-OutputDirectory',$quality,'-ResultDirectory',$ResultDirectory)
        if (Test-Path -LiteralPath (Join-Path $quality 'source-manifest.json')) { $arguments += '-Resume' }
        $null = Run 'quality' 'pwsh' $arguments
        if (-not $SkipFullUnitTests) {
            $full = Run 'unit-full-release' 'dotnet' @('test','src/Akbura.UnitTests/Akbura.UnitTests.csproj','-c','Release','--no-build','--no-restore',
                '--logger','trx;LogFileName=tests.trx','--results-directory','{RESULTS}','--blame-hang-timeout','15m','-v:normal','--','xUnit.MaxParallelThreads=2')
            $portable = Join-Path $ResultDirectory 'verification/unit-full-release'
            $null = New-Item -ItemType Directory -Path $portable -Force
            Get-ChildItem -LiteralPath $full -File | Copy-Item -Destination $portable -Force
        }
    }
    $changedCSharp = @(& git diff --name-only HEAD -- '*.cs') + @(& git ls-files --others --exclude-standard -- '*.cs')
    $null = Run 'style' 'dotnet' (@((Join-Path $PSScriptRoot 'bin/Debug/net10.0/HotReloadBenchmarks.dll'),'style') + $changedCSharp)
    $originalTool = if ($OriginalInstrumentedDirectory) { $OriginalInstrumentedDirectory } else { Join-Path $BaselineDirectory 'tool' }
    Assert-Harness
    Matrix 'before' $BaselineDirectory (Join-Path $ResultDirectory 'before') $originalTool
    Matrix 'after' (Join-Path $OutputDirectory 'after') (Join-Path $ResultDirectory 'after') (Join-Path $PSScriptRoot 'bin/Debug/net10.0')
    $null = Run 'compare' 'pwsh' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Compare-CollectionAllocations.ps1'),'-ResultDirectory',$ResultDirectory,'-LocalizedEdits')
    $production = Join-Path $OutputDirectory 'optimized-production'
    if (-not (Test-Path -LiteralPath $production)) {
        Copy-Item -LiteralPath $OriginalProductionDirectory -Destination $production -Recurse
        foreach ($name in @('Akbura.BlackSilence','Akbura.Generator')) {
            foreach ($extension in @('dll','pdb')) {
                Copy-Item -LiteralPath (Join-Path $repository "src/$name/bin/Release/netstandard2.0/$name.$extension") -Destination $production -Force
            }
        }
    }
    $null = Run 'statistics-optimized-production' 'dotnet' @((Join-Path $production 'HotReloadBenchmarks.dll'),'statistics-check')
    Matrix 'before-production' (Join-Path $OutputDirectory 'before-production') (Join-Path $ResultDirectory 'production/before') $OriginalProductionDirectory -Production
    Matrix 'after-production' (Join-Path $OutputDirectory 'after-production') (Join-Path $ResultDirectory 'production/after') $production -Production
    $null = Run 'compare-production' 'pwsh' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Compare-CollectionAllocations.ps1'),
        '-ResultDirectory',(Join-Path $ResultDirectory 'production'),'-Unprofiled')
    if (-not $SkipWatch) {
        foreach ($mode in @('before','after')) {
            $tool = if ($mode -eq 'before') { $OriginalProductionDirectory } else { $production }
            $logs = Run ('watch-' + $mode) 'pwsh' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Verify-MarkupWatch.ps1'),
                '-ToolDirectory',$tool,'-OutputDirectory','{RESULTS}/watch')
            $portable = Join-Path $ResultDirectory ('watch/' + $mode)
            $null = New-Item -ItemType Directory -Path $portable -Force
            foreach ($name in @('result.json','watch.jsonl','build.log')) {
                Copy-Item -LiteralPath (Join-Path $logs ('watch/' + $name)) -Destination $portable -Force
            }
        }
    }
    foreach ($item in @(
        @{ Name = 'before'; Path = $BaselineDirectory },
        @{ Name = 'after'; Path = (Join-Path $OutputDirectory 'after') },
        @{ Name = 'before-production'; Path = (Join-Path $OutputDirectory 'before-production') },
        @{ Name = 'after-production'; Path = (Join-Path $OutputDirectory 'after-production') }
    )) {
        $interrupted = Join-Path $item.Path 'interrupted'
        if (-not (Test-Path -LiteralPath $interrupted)) { continue }
        $portable = Join-Path $ResultDirectory ('unsuccessful/' + $item.Name)
        $null = New-Item -ItemType Directory -Path $portable -Force
        foreach ($file in Get-ChildItem -LiteralPath $interrupted -File) {
            $inputStream = [IO.File]::OpenRead($file.FullName)
            $outputStream = [IO.File]::Create((Join-Path $portable ($file.Name + '.gz')))
            $gzip = [IO.Compression.GZipStream]::new($outputStream, [IO.Compression.CompressionLevel]::SmallestSize)
            try { $inputStream.CopyTo($gzip) }
            finally { $gzip.Dispose(); $inputStream.Dispose(); $outputStream.Dispose() }
        }
    }
    $null = Run 'summarize' 'pwsh' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Summarize-LocalizedEdits.ps1'),'-ResultDirectory',$ResultDirectory)
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $ResultDirectory 'sources.json') -Force
    foreach ($file in Get-ChildItem -LiteralPath $ResultDirectory -File -Recurse |
        Where-Object Extension -in @('.md','.json','.csv','.trx','.log','.txt')) {
        $value = [regex]::Replace([IO.File]::ReadAllText($file.FullName), '\r?\n', "`r`n")
        if (-not $value.EndsWith("`r`n")) { $value += "`r`n" }
        [IO.File]::WriteAllText($file.FullName, $value, $encoding)
    }
    Status 'EvidenceCompleteNeedsReview'
    Log 'COMPLETE Evidence is ready for review. No commit, push, merge or next optimization stage was performed.'
}
catch {
    Status 'Failed' ($_ | Out-String)
    Log ('FAILED ' + ($_ | Out-String))
    exit 1
}
finally {
    $journal.Dispose()
    $lock.Dispose()
}
