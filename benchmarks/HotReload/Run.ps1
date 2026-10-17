param(
    [ValidateSet('runtime', 'generator', 'graph', 'build', 'watch', 'invalidation', 'artifacts', 'retention')]
    [string[]]$Suite = @('runtime', 'generator', 'graph', 'build', 'watch'),
    [string]$OutputDirectory,
    [string]$PurityRoot,
    [switch]$SkipPurity,
    [switch]$Prepare,
    [string[]]$InvalidationScenario = @(),
    [ValidateSet('off', 'stats', 'trace')][string[]]$InvalidationProfiling = @('trace'),
    [ValidateSet('NeedHelp.akbura', 'Sidebar.akbura')][string[]]$InvalidationPurityFile = @('Sidebar.akbura'),
    [ValidateSet('disabled', 'enabled')][string[]]$ArtifactOptimization = @('disabled', 'enabled'),
    [ValidateRange(1, 10000)][int]$RetentionRevisions = 100
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repository 'bin/HotReloadBenchmarks'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$toolProject = Join-Path $PSScriptRoot 'HotReloadBenchmarks.csproj'
$tool = Join-Path $PSScriptRoot 'bin/Debug/net10.0/HotReloadBenchmarks.dll'
$fixture = Join-Path $OutputDirectory 'fixture'
$gallery = Join-Path $repository 'src/Akbura.FeatureGallery/Akbura.FeatureGallery/Akbura.FeatureGallery.csproj'

function Invoke-Dotnet([string[]]$Arguments, [string]$LogPath) {
    Write-Host ('dotnet ' + ($Arguments -join ' '))
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & dotnet @Arguments 2>&1 | ForEach-Object { "$_" } | Set-Content -LiteralPath $LogPath -Encoding UTF8
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $LogPath -Tail 30 | ForEach-Object {
            if ($_.Length -gt 500) {
                Write-Host ($_.Substring(0, 500) + ' ... [full structured row retained in the log]')
            }
            else {
                Write-Host $_
            }
        }
        throw "dotnet exited with $LASTEXITCODE; see $LogPath"
    }
}

function Export-Inputs([string]$Project, [string]$Name) {
    $path = Join-Path $OutputDirectory "$Name-inputs.json"
    Invoke-Dotnet @('msbuild', $Project, '-nologo', '-v:q', '-t:ResolveReferences',
        '-getProperty:RootNamespace,MSBuildProjectDirectory,DefineConstants',
        '-getItem:Compile,ReferencePath,AdditionalFiles') $path
    $raw = [IO.File]::ReadAllText($path)
    $start = $raw.IndexOf('{')
    if ($start -lt 0) {
        throw "MSBuild did not export JSON: $path"
    }
    $json = $raw.Substring($start)
    $null = $json | ConvertFrom-Json
    [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
    return $path
}

function Measure-Build([string]$Name, [string]$Project, [string[]]$Extra = @()) {
    $binlog = Join-Path $OutputDirectory "$Name.binlog"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    Invoke-Dotnet (@('build', $Project, '--no-restore', '--nologo', '-v:minimal', "-bl:$binlog") + $Extra) (Join-Path $OutputDirectory "$Name.log")
    $timer.Stop()
    @{ Kind = 'build-wall'; Scenario = $Name; TotalMs = $timer.Elapsed.TotalMilliseconds } |
        ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $OutputDirectory 'build-wall.jsonl') -Encoding UTF8
    Invoke-Dotnet @($tool, 'binlog', $binlog) (Join-Path $OutputDirectory "$Name.jsonl")
}

Push-Location $repository
try {
    if ($Prepare) {
        Invoke-Dotnet @('build', 'src/Akbura.Diagnostics/Akbura.Diagnostics.csproj', '--nologo', '-v:minimal') (Join-Path $OutputDirectory 'prepare-runtime.log')
        $toolArguments = @('build', $toolProject, '--nologo', '-v:minimal')
        if (@($Suite | Where-Object { $_ -in @('invalidation', 'artifacts', 'retention') }).Count -gt 0) {
            Invoke-Dotnet @('build', 'src/Akbura.BlackSilence/Akbura.BlackSilence.csproj', '-c', 'DebugStats', '--nologo', '-v:minimal') (Join-Path $OutputDirectory 'prepare-invalidation.log')
            $toolArguments += '-p:BlackSilenceConfiguration=DebugStats'
        }
        Invoke-Dotnet $toolArguments (Join-Path $OutputDirectory 'prepare-tool.log')
    }
    if (-not (Test-Path -LiteralPath $tool)) {
        throw 'Run with -Prepare to build the runtime and benchmark executable first.'
    }
    $usePurity = -not $SkipPurity -and @($Suite | Where-Object { $_ -in @('generator', 'graph', 'watch', 'invalidation', 'artifacts', 'retention') }).Count -gt 0
    if ($usePurity) {
        if (-not $PurityRoot) {
            $PurityRoot = Join-Path $PSScriptRoot 'Akbura-PurityUI-Tutorial'
        }
        $PurityRoot = & (Join-Path $PSScriptRoot 'Prepare-Purity.ps1') -SourceRoot $PurityRoot -OutputDirectory $OutputDirectory -RepositoryRoot $repository
        $hostProject = Join-Path $PurityRoot 'PurityHost/PurityHost.csproj'
        Invoke-Dotnet @('build', $hostProject, '--nologo', '-v:minimal') (Join-Path $OutputDirectory 'purity-prepare.log')
    }
    if ('runtime' -in $Suite) {
        $previousModifiable = $env:DOTNET_MODIFIABLE_ASSEMBLIES
        try {
            $env:DOTNET_MODIFIABLE_ASSEMBLIES = 'debug'
            Invoke-Dotnet @($tool, 'runtime') (Join-Path $OutputDirectory 'runtime.jsonl')
        }
        finally {
            $env:DOTNET_MODIFIABLE_ASSEMBLIES = $previousModifiable
        }
    }
    if ('watch' -in $Suite -or 'build' -in $Suite) {
        $null = New-Item -ItemType Directory -Path $fixture -Force
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'WatchFixture') -File) {
            Copy-Item -LiteralPath $file.FullName -Destination $fixture -Force
        }
        $fixtureProject = Join-Path $fixture 'WatchFixture.csproj'
        $fixtureXml = [IO.File]::ReadAllText($fixtureProject).Replace('$(MSBuildThisFileDirectory)../../..', [Security.SecurityElement]::Escape($repository))
        [IO.File]::WriteAllText($fixtureProject, $fixtureXml, [Text.UTF8Encoding]::new($false))
        Invoke-Dotnet @('restore', $fixtureProject, '--nologo') (Join-Path $OutputDirectory 'fixture-restore.log')
        Invoke-Dotnet @('build', $fixtureProject, '--no-restore', '--nologo', '-v:minimal') (Join-Path $OutputDirectory 'fixture-prepare.log')
    }
    if ('build' -in $Suite) {
        $wallPath = Join-Path $OutputDirectory 'build-wall.jsonl'
        [IO.File]::WriteAllText($wallPath, '', [Text.UTF8Encoding]::new($false))
        Measure-Build 'fixture-rebuild' $fixtureProject @('-t:Rebuild')
        foreach ($index in 1..3) {
            Measure-Build "fixture-noop-$index" $fixtureProject
        }
        Measure-Build 'fixture-no-node-reuse' $fixtureProject @('-nr:false')
        $componentPath = Join-Path $fixture 'Counter.akbura'
        $componentBytes = [IO.File]::ReadAllBytes($componentPath)
        $componentText = [IO.File]::ReadAllText($componentPath)
        $codePath = Join-Path $fixture 'Counter.akbura.cs'
        $codeBytes = [IO.File]::ReadAllBytes($codePath)
        $codeText = [IO.File]::ReadAllText($codePath)
        try {
            [IO.File]::WriteAllText($componentPath, $componentText.Replace('Width="100"', 'Width="101"'), [Text.UTF8Encoding]::new($false))
            Measure-Build 'fixture-component-edit' $fixtureProject
            [IO.File]::WriteAllText($codePath, $codeText.Replace('base(AkburaEngine.Empty)', 'base(AkburaEngine.Empty /* benchmark edit */)'), [Text.UTF8Encoding]::new($false))
            Measure-Build 'fixture-csharp-edit' $fixtureProject
            $manifest = Join-Path $fixture 'obj/Debug/net10.0/Akbura.module.xml'
            $manifestBytes = [IO.File]::ReadAllBytes($manifest)
            try {
                [IO.File]::WriteAllText($manifest, '<invalid benchmark="true" />', [Text.UTF8Encoding]::new($false))
                Measure-Build 'fixture-corrupt-manifest' $fixtureProject
            }
            finally {
                if ([IO.File]::ReadAllText($manifest).Contains('<invalid')) {
                    [IO.File]::WriteAllBytes($manifest, $manifestBytes)
                }
            }
            Measure-Build 'fixture-recovered-noop' $fixtureProject
        }
        finally {
            [IO.File]::WriteAllBytes($componentPath, $componentBytes)
            [IO.File]::WriteAllBytes($codePath, $codeBytes)
        }
        Invoke-Dotnet @('build', $gallery, '--nologo', '-v:minimal') (Join-Path $OutputDirectory 'gallery-prepare.log')
        Measure-Build 'gallery-rebuild' $gallery @('-t:Rebuild')
        foreach ($index in 1..3) {
            Measure-Build "gallery-noop-$index" $gallery
        }
        Measure-Build 'gallery-no-node-reuse' $gallery @('-nr:false')
    }
    if ('generator' -in $Suite) {
        Invoke-Dotnet @('build', $gallery, '--nologo', '-v:minimal') (Join-Path $OutputDirectory 'generator-prepare.log')
        $galleryInputs = Export-Inputs $gallery 'gallery'
        foreach ($scenario in @('cold', 'noop', 'whitespace', 'scalar', 'csharp', 'diagnostics-off')) {
            Invoke-Dotnet @($tool, 'generator', $galleryInputs, 'Counter.akbura', $scenario) (Join-Path $OutputDirectory "generator-gallery-$scenario.jsonl")
        }
        if ($usePurity) {
            $purityProject = Join-Path $PurityRoot 'PurityUIDashborad/PurityUIDashborad.csproj'
            $purityInputs = Export-Inputs $purityProject 'purity'
            foreach ($file in @('NeedHelp.akbura', 'Sidebar.akbura')) {
                foreach ($scenario in @('noop', 'whitespace', 'scalar', 'csharp', 'diagnostics-off')) {
                    Invoke-Dotnet @($tool, 'generator', $purityInputs, $file, $scenario) (Join-Path $OutputDirectory "generator-purity-$file-$scenario.jsonl")
                }
            }
        }
    }
    if ('graph' -in $Suite) {
        if (-not $galleryInputs) {
            Invoke-Dotnet @('build', $gallery, '--nologo', '-v:minimal') (Join-Path $OutputDirectory 'graph-prepare.log')
            $galleryInputs = Export-Inputs $gallery 'gallery'
        }
        Invoke-Dotnet @($tool, 'graph', $galleryInputs, 'Counter.akbura') (Join-Path $OutputDirectory 'graph-gallery.jsonl')
        if ($usePurity) {
            if (-not $purityInputs) {
                $purityInputs = Export-Inputs (Join-Path $PurityRoot 'PurityUIDashborad/PurityUIDashborad.csproj') 'purity'
            }
            foreach ($file in @('NeedHelp.akbura', 'Sidebar.akbura')) {
                Invoke-Dotnet @($tool, 'graph', $purityInputs, $file) (Join-Path $OutputDirectory "graph-purity-$file.jsonl")
            }
        }
    }
    if (@($Suite | Where-Object { $_ -in @('invalidation', 'artifacts', 'retention') }).Count -gt 0) {
        if (-not $galleryInputs) {
            Invoke-Dotnet @('build', $gallery, '--nologo', '-v:minimal') (Join-Path $OutputDirectory 'invalidation-gallery-prepare.log')
            $galleryInputs = Export-Inputs $gallery 'gallery'
        }
        $workloads = @(@{ Name = 'gallery'; Inputs = $galleryInputs; File = 'Counter.akbura' })
        if ($usePurity) {
            $purityInputs = Export-Inputs (Join-Path $PurityRoot 'PurityUIDashborad/PurityUIDashborad.csproj') 'purity'
            foreach ($file in $InvalidationPurityFile) {
                $workloads += @{ Name = "purity-$file"; Inputs = $purityInputs; File = $file }
            }
        }
        $scenarios = if ($InvalidationScenario.Count -gt 0) { $InvalidationScenario } else {
            @('unchanged', 'fresh-driver', 'akbura', 'akbura-whitespace', 'original-add-tree', 'unrelated-body', 'csharp-trivia', 'csharp-whitespace', 'inert-tree',
                'referenced-body', 'referenced-signature', 'overload', 'const', 'attribute', 'global-using', 'reference-compatible',
                'reference-incompatible', 'unresolved', 'nullable', 'preprocessor', 'language', 'compilation-options',
                'add-tree', 'remove-tree', 'repeat', 'revert', 'multi-tree')
        }
        $changedPaths = @(& git diff --name-only HEAD) + @(& git ls-files --others --exclude-standard)
        $sourceHashes = foreach ($relativePath in $changedPaths | Sort-Object -Unique) {
            $path = Join-Path $repository $relativePath
            if ((Test-Path -LiteralPath $path -PathType Leaf) -and [IO.Path]::GetExtension($path) -in @('.cs', '.csproj', '.ps1', '.slnx')) {
                @{ Path = $relativePath; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
            }
        }
        $os = Get-CimInstance Win32_OperatingSystem
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        $environment = @{
            RepositoryRevision = (& git rev-parse HEAD)
            StartedUtc = [DateTime]::UtcNow.ToString('o')
            Sdk = (& dotnet --version)
            OS = $os.Caption; OSVersion = $os.Version; Cpu = $cpu.Name
            LogicalProcessors = $cpu.NumberOfLogicalProcessors
            PhysicalMemoryKiB = $os.TotalVisibleMemorySize; AvailableMemoryKiB = $os.FreePhysicalMemory
            SourceHashes = @($sourceHashes)
            AnalyzerHashes = @(foreach ($name in @('Akbura.BlackSilence', 'Akbura.Generator')) {
                @{ Name = $name; Sha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $tool) "$name.dll") -Algorithm SHA256).Hash }
            })
        }
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'invalidation-environment.json'),
            ($environment | ConvertTo-Json -Depth 10) + "`r`n", [Text.UTF8Encoding]::new($false))
        foreach ($workload in $workloads) {
            if ('invalidation' -in $Suite -or 'artifacts' -in $Suite) {
                foreach ($scenario in $scenarios) {
                    foreach ($profiling in $InvalidationProfiling) {
                        if ('invalidation' -in $Suite) {
                            Invoke-Dotnet @($tool, 'invalidation', $workload.Inputs, $workload.File, $scenario, $profiling) (
                                Join-Path $OutputDirectory "invalidation-$($workload.Name)-$scenario-$profiling.jsonl")
                        }
                        if ('artifacts' -in $Suite) {
                            foreach ($optimization in $ArtifactOptimization) {
                                Invoke-Dotnet @($tool, 'artifacts', $workload.Inputs, $workload.File, $scenario, $profiling, $optimization) (
                                    Join-Path $OutputDirectory "invalidation-$($workload.Name)-$scenario-$profiling-$optimization.jsonl")
                            }
                        }
                    }
                }
            }
            if ('retention' -in $Suite) {
                foreach ($optimization in $ArtifactOptimization) {
                    Invoke-Dotnet @($tool, 'artifact-memory', $workload.Inputs, $workload.File, "$RetentionRevisions", $optimization) (
                        Join-Path $OutputDirectory "artifact-memory-$($workload.Name)-$optimization.jsonl")
                }
            }
        }
    }
    if ('watch' -in $Suite) {
        Invoke-Dotnet @($tool, 'watch', $fixtureProject, (Join-Path $fixture 'Counter.akbura'), 'Fixture') (Join-Path $OutputDirectory 'watch-fixture.jsonl')
        if ($usePurity) {
            foreach ($component in @('NeedHelp', 'Sidebar')) {
                Invoke-Dotnet @($tool, 'watch', $hostProject, (Join-Path $PurityRoot "PurityUIDashborad/Components/$component.akbura"), $component) (Join-Path $OutputDirectory "watch-$component.jsonl")
            }
        }
    }
}
finally {
    Pop-Location
}
