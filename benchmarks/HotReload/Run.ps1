param(
    [ValidateSet('runtime', 'generator', 'graph', 'build', 'watch')]
    [string[]]$Suite = @('runtime', 'generator', 'graph', 'build', 'watch'),
    [string]$OutputDirectory,
    [string]$PurityRoot,
    [switch]$Prepare
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
        Get-Content -LiteralPath $LogPath -Tail 30 | Write-Host
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
        Invoke-Dotnet @('build', 'src/Akbura/Akbura.csproj', '--nologo', '-v:minimal') (Join-Path $OutputDirectory 'prepare-runtime.log')
        Invoke-Dotnet @('build', $toolProject, '--nologo', '-v:minimal') (Join-Path $OutputDirectory 'prepare-tool.log')
    }
    if (-not (Test-Path -LiteralPath $tool)) {
        throw 'Run with -Prepare to build the runtime and benchmark executable first.'
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
        if ($PurityRoot) {
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
        if ($PurityRoot) {
            if (-not $purityInputs) {
                $purityInputs = Export-Inputs (Join-Path $PurityRoot 'PurityUIDashborad/PurityUIDashborad.csproj') 'purity'
            }
            foreach ($file in @('NeedHelp.akbura', 'Sidebar.akbura')) {
                Invoke-Dotnet @($tool, 'graph', $purityInputs, $file) (Join-Path $OutputDirectory "graph-purity-$file.jsonl")
            }
        }
    }
    if ('watch' -in $Suite) {
        Invoke-Dotnet @($tool, 'watch', $fixtureProject, (Join-Path $fixture 'Counter.akbura'), 'Fixture') (Join-Path $OutputDirectory 'watch-fixture.jsonl')
        if ($PurityRoot) {
            $hostProject = Join-Path $PurityRoot 'TimingHost/TimingHost.csproj'
            foreach ($component in @('NeedHelp', 'Sidebar')) {
                Invoke-Dotnet @($tool, 'watch', $hostProject, (Join-Path $PurityRoot "PurityUIDashborad/Components/$component.akbura"), $component) (Join-Path $OutputDirectory "watch-$component.jsonl")
            }
        }
    }
}
finally {
    Pop-Location
}
