param(
    [string]$Output = (Join-Path $env:TEMP ([guid]::NewGuid().ToString().ToUpperInvariant())),
    [string]$Baseline = '485f7929edc7a45b960db2855bc8ac4626708ca1',
    [int]$TimeoutMinutes = 40,
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runRoot = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$journal = Join-Path $runRoot 'journal.log'
$statusPath = Join-Path $runRoot 'status.json'
$encoding = [Text.UTF8Encoding]::new($false)
$ownedChild = $null
$runLock = $null
$previousTiering = $env:DOTNET_TieredCompilation
$previousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$previousNoLogo = $env:DOTNET_NOLOGO
$activeStep = 'initialization'

function Log([string]$Message) {
    $line = '[{0}] {1}' -f [DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss zzz'), $Message
    [IO.File]::AppendAllText($journal, $line + "`r`n", $encoding)
    Write-Host $line
}

function Save-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path + '.tmp', ($Value | ConvertTo-Json -Depth 30) + "`r`n", $encoding)
    [IO.File]::Move($Path + '.tmp', $Path, $true)
}

function Status([string]$State, [string]$Message) {
    Save-Json $statusPath ([ordered]@{
        State = $State; Step = $script:activeStep; Message = $Message
        Updated = [DateTimeOffset]::Now.ToString('o'); WorkerPid = $PID
        ChildPid = $(if ($null -ne $script:ownedChild -and !$script:ownedChild.HasExited) { $script:ownedChild.Id } else { $null })
        Output = $runRoot
    })
}

function Run-Checked([string]$Name, [string[]]$Arguments, [string]$ManifestHash = '') {
    $script:activeStep = $Name
    $checkpoint = Join-Path $runRoot ($Name + '.done.json')
    $signature = $Arguments | ConvertTo-Json -Compress
    if (Test-Path -LiteralPath $checkpoint) {
        $previous = Get-Content -LiteralPath $checkpoint -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($previous.ManifestHash -eq $ManifestHash -and $previous.Arguments -eq $signature) {
            Log "SKIP $Name (successful checkpoint)."
            return
        }
        throw "Checkpoint inputs changed for $Name. Use a new Output GUID."
    }
    $attempt = 1
    while (Test-Path -LiteralPath (Join-Path $runRoot "$Name-attempt$attempt.out.log")) { $attempt++ }
    $stdout = Join-Path $runRoot "$Name-attempt$attempt.out.log"
    $stderr = Join-Path $runRoot "$Name-attempt$attempt.err.log"
    $startInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.WorkingDirectory = $repo
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $script:ownedChild = [Diagnostics.Process]::new()
    $script:ownedChild.StartInfo = $startInfo
    $outStream = [IO.File]::Open($stdout, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
    $errStream = [IO.File]::Open($stderr, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
    try {
        if (!$script:ownedChild.Start()) { throw "Could not start $Name" }
        $outCopy = $script:ownedChild.StandardOutput.BaseStream.CopyToAsync($outStream)
        $errCopy = $script:ownedChild.StandardError.BaseStream.CopyToAsync($errStream)
        Save-Json (Join-Path $runRoot 'child.json') @{
            Pid = $script:ownedChild.Id; Started = $script:ownedChild.StartTime.ToUniversalTime().ToString('o'); Step = $Name
        }
        Log "START $Name; PID=$($script:ownedChild.Id); attempt=$attempt; stdout=$stdout"
        Status 'Running' "Started $Name"
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $lastHeartbeat = -15
        $reportedLines = 0
        while (!$script:ownedChild.WaitForExit(1000)) {
            if ($watch.Elapsed.TotalMinutes -gt $TimeoutMinutes) { throw "$Name exceeded $TimeoutMinutes minutes." }
            if ($watch.Elapsed.TotalSeconds - $lastHeartbeat -ge 15) {
                $outStream.Flush()
                $lines = @(Get-Content -LiteralPath $stdout -Encoding UTF8)
                foreach ($line in $lines | Select-Object -Skip $reportedLines) { Log "  $line" }
                $reportedLines = $lines.Count
                Log "HEARTBEAT $Name; elapsed=$([int]$watch.Elapsed.TotalSeconds)s; PID=$($script:ownedChild.Id)"
                Status 'Running' "$Name, elapsed $([int]$watch.Elapsed.TotalSeconds)s"
                $lastHeartbeat = $watch.Elapsed.TotalSeconds
            }
        }
        [void]$outCopy.GetAwaiter().GetResult()
        [void]$errCopy.GetAwaiter().GetResult()
        $outStream.Flush()
        $errStream.Flush()
        foreach ($line in @(Get-Content -LiteralPath $stdout -Encoding UTF8) | Select-Object -Skip $reportedLines) { Log "  $line" }
        if ($script:ownedChild.ExitCode -ne 0) {
            foreach ($line in Get-Content -LiteralPath $stderr -Tail 35 -Encoding UTF8) { Log "  ERROR $line" }
            throw "$Name failed with exit code $($script:ownedChild.ExitCode). See $stdout and $stderr"
        }
        Save-Json $checkpoint @{
            ManifestHash = $ManifestHash; Arguments = $signature; ExitCode = 0
            Finished = [DateTimeOffset]::Now.ToString('o'); Seconds = $watch.Elapsed.TotalSeconds
            Stdout = $stdout; Stderr = $stderr
        }
        Log "PASS $Name ($([int]$watch.Elapsed.TotalSeconds)s)."
    }
    finally {
        if ($null -ne $script:ownedChild -and !$script:ownedChild.HasExited) {
            # Stop only the child process tree started by this invocation.
            # Never terminate shared build servers or unrelated dotnet processes.
            $script:ownedChild.Kill($true)
            $script:ownedChild.WaitForExit()
        }
        $outStream.Dispose()
        $errStream.Dispose()
        if ($null -ne $script:ownedChild) { $script:ownedChild.Dispose(); $script:ownedChild = $null }
    }
}

function Replace-Once([string]$Text, [string]$Old, [string]$New) {
    if (($Text.Split(@($Old), [StringSplitOptions]::None).Length - 1) -ne 1) {
        throw "Expected exactly one source anchor: $Old"
    }
    return $Text.Replace($Old, $New)
}

try {
    $runLock = [IO.File]::Open((Join-Path $runRoot 'worker.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $env:DOTNET_TieredCompilation = '0'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $production = @('Language/Operations/MetadataAkcssOperationFactory.cs','Language/Symbols/MetadataAkcssModuleSymbol.cs',
        'Language/Metadata/AkburaReferencedModule.cs','Language/Operations/MetadataAkcssLookupContext.cs')
    $files = @($production | ForEach-Object { "src/Akbura.Generator/$_" }) + @(
        'src/Akbura.Benchmarks/FeatureGalleryMetadataLookupComparison.cs','src/Akbura.Benchmarks/Compare-MetadataAkcssLookup.ps1',
        'src/Akbura.Benchmarks/FeatureGallerySymbolDisplayComparison.cs','src/Akbura.Benchmarks/Program.cs','src/Akbura.Benchmarks/Akbura.Benchmarks.csproj',
        'src/Akbura.UnitTests/MetadataAkcssLookupContextTests.cs','src/Akbura.UnitTests/Akbura.UnitTests.csproj',
        'src/Akbura.TestUtilities/MetadataAkcssLookupFixture.cs','src/Akbura.Generator/Collections/CachedSymbolDisplay.cs')
    $manifest = @($files | ForEach-Object { [ordered]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $repo $_)).Hash } })
    Save-Json (Join-Path $runRoot 'sources.json') $manifest
    Save-Json (Join-Path $runRoot 'baseline.json') @{ Commit=$Baseline; CurrentHead=(& git -C $repo rev-parse HEAD) }
    $manifestHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Compress) + $Baseline)))
    foreach ($file in $files) {
        $copy = Join-Path $runRoot "source-snapshot/$file"
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($copy)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $copy -Force
    }
    $projects = @{}
    foreach ($name in @('Generator','BlackSilence','Benchmarks','UnitTests')) { $projects[$name] = Join-Path $repo "src/Akbura.$name/Akbura.$name.csproj" }
    $hooks = @'

internal static class MetadataLookupTrace
{
    public static System.Action<string, object?, object?, long, long>? Record;
    public static System.Func<long>? Bytes;
}
'@
    $wrapper = @'
    private static IReadOnlyDictionary<string, ImmutableArray<IAkcssSymbol>> CreateSymbolLookup(ImmutableArray<IAkcssSymbol> symbols)
    {
        var before = MetadataLookupTrace.Bytes!();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = CreateSymbolLookupCore(symbols);
        var ticks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        var allocated = MetadataLookupTrace.Bytes!() - before;
        MetadataLookupTrace.Record?.Invoke("Build", result, (long)symbols.Length, allocated, ticks);
        return result;
    }

'@
    foreach ($variant in @('Baseline','SharedLazy','ProfileBaseline','ProfileSharedLazy')) {
        $directory = Join-Path $runRoot "variants/$variant"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $profile = $variant.StartsWith('Profile')
        $isBaseline = $variant.EndsWith('Baseline')
        $texts = @{}
        foreach ($file in $production) {
            if ($isBaseline -and $file.EndsWith('MetadataAkcssLookupContext.cs')) { continue }
            if ($isBaseline) {
                $lines = & git -C $repo show "${Baseline}:src/Akbura.Generator/$file"
                if ($LASTEXITCODE -ne 0) { throw "Cannot read baseline $file" }
                $texts[$file] = ($lines -join "`r`n") + "`r`n"
            } else { $texts[$file] = [IO.File]::ReadAllText((Join-Path $repo "src/Akbura.Generator/$file")) }
        }
        if ($profile) {
            $factory = 'Language/Operations/MetadataAkcssOperationFactory.cs'
            $context = 'Language/Operations/MetadataAkcssLookupContext.cs'
            $texts[$factory] = Replace-Once $texts[$factory] '        data.Sort(static (left, right) => left.Order.CompareTo(right.Order));' @'
        data.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        MetadataLookupTrace.Record?.Invoke("Symbol", containingSymbol, data, 0, 0);
'@
            $texts[$factory] = Replace-Once $texts[$factory] "        foreach (var metadataName in data.AppliedSymbols)`r`n        {" @'
        foreach (var metadataName in data.AppliedSymbols)
        {
            MetadataLookupTrace.Record?.Invoke("Request", containingSymbol, metadataName, 0, 0);
'@
            $owner = if ($isBaseline) { $factory } else { $context }
            $signature = '    private static IReadOnlyDictionary<string, ImmutableArray<IAkcssSymbol>> CreateSymbolLookup('
            $core = '    private static IReadOnlyDictionary<string, ImmutableArray<IAkcssSymbol>> CreateSymbolLookupCore('
            $texts[$owner] = Replace-Once $texts[$owner] $signature ($wrapper + $core)
            if (!$isBaseline) {
                $texts[$context] = Replace-Once $texts[$context] '        _symbols = symbols;' "        _symbols = symbols;`r`n        MetadataLookupTrace.Record?.Invoke(`"Context`", this, null, 0, 0);"
            }
            $texts[$factory] += $hooks
        }
        foreach ($file in $texts.Keys) { [IO.File]::WriteAllText((Join-Path $directory ([IO.Path]::GetFileName($file))), $texts[$file], $encoding) }
        $remove = ($production | ForEach-Object { $_.Replace('/','\') }) -join ';'
        $include = ($texts.Keys | Sort-Object | ForEach-Object { [Security.SecurityElement]::Escape((Join-Path $directory ([IO.Path]::GetFileName($_)))) }) -join ';'
        $targetPath = Join-Path $directory 'Variant.targets'
        [IO.File]::WriteAllText($targetPath,"<Project><Target Name=`"MetadataLookupVariant`" BeforeTargets=`"CoreCompile`"><ItemGroup><Compile Remove=`"$remove`" /><Compile Include=`"$include`" /></ItemGroup></Target></Project>",$encoding)
        Run-Checked "build-$variant-Generator" @('build',$projects.Generator,'-c','Release','--no-restore','--nologo','-v','quiet',
            '-p:BuildProjectReferences=false','-p:EnableAkburaStats=false','-p:EnableQuickScanBenchmark=false','-p:AkburaEmbedSourceFiles=false',
            "-p:OutputPath=$directory/","-p:IntermediateOutputPath=$directory/obj/","-p:CustomBeforeMicrosoftCommonTargets=$targetPath") $manifestHash
    }
    $hostDirectory = Join-Path $runRoot 'host'
    New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repo 'src/Akbura.Benchmarks/bin/Release/net10.0') |
        Where-Object Name -NotIn @('Akbura.Benchmarks.dll','Akbura.Benchmarks.pdb') | Copy-Item -Destination $hostDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $runRoot 'variants/SharedLazy/Akbura.Generator.dll') -Destination $hostDirectory -Force
    foreach ($project in @('BlackSilence','Benchmarks')) {
        Run-Checked "build-$project" @('build',$projects[$project],'-c','Release','--no-restore','--nologo','-v','quiet',
            '-p:BuildProjectReferences=false','-p:EnableAkburaStats=false','-p:EnableQuickScanBenchmark=false','-p:AkburaEmbedSourceFiles=false',
            "-p:OutputPath=$hostDirectory/","-p:IntermediateOutputPath=$hostDirectory/obj-$project/") $manifestHash
    }
    foreach ($variant in @('Baseline','SharedLazy','ProfileBaseline','ProfileSharedLazy')) {
        foreach ($name in @('Akbura.BlackSilence.dll','Akbura.Benchmarks.dll','Akbura.Benchmarks.pdb')) {
            Copy-Item -LiteralPath (Join-Path $hostDirectory $name) -Destination (Join-Path $runRoot "variants/$variant") -Force
        }
    }
    if ($PrepareOnly) { Status 'Prepared' 'Four isolated assemblies prepared; CachedSymbolDisplay preserved in both variants.'; exit 0 }
    $testFilter = 'FullyQualifiedName~MetadataAkcssLookupContextTests|FullyQualifiedName~Akcss|FullyQualifiedName~CrossAssemblySemanticTests|FullyQualifiedName~AkburaGenerationCatalogTests|FullyQualifiedName~SourceProjectReferenceTests|FullyQualifiedName~SemanticPipelineTests|FullyQualifiedName~BlackSilence|FullyQualifiedName~AkburaBlackSilenceGeneratorTests'
    $previousProcessorCount = $env:DOTNET_PROCESSOR_COUNT
    try {
        $env:DOTNET_PROCESSOR_COUNT = '2'
        foreach ($configuration in @('Debug','Release','DebugStats','ReleaseStats')) {
            foreach ($name in @('Generator','BlackSilence','UnitTests')) {
                Run-Checked "regression-build-$configuration-$name" @('build',$projects[$name],'-c',$configuration,'--no-restore','--nologo','-v','quiet','-p:BuildProjectReferences=false') $manifestHash
            }
            Run-Checked "regression-$configuration" @('test',$projects.UnitTests,'-c',$configuration,'--no-build','--no-restore','--filter',$testFilter,
                '--logger',"trx;LogFileName=regression-$configuration.trx",'--results-directory',$runRoot) $manifestHash
        }
    } finally { $env:DOTNET_PROCESSOR_COUNT = $previousProcessorCount }
    $hostDll = Join-Path $hostDirectory 'Akbura.Benchmarks.dll'
    for ($round = 0; $round -lt 4; $round++) {
        Run-Checked "macro-$round" @($hostDll,'--metadata-akcss-shared-lazy','--variants',(Join-Path $runRoot 'variants'),
            '--output',(Join-Path $runRoot "macro-$round"),'--round',"$round") $manifestHash
    }
    for ($round = 0; $round -lt 3; $round++) {
        Run-Checked "profile-$round" @($hostDll,'--metadata-akcss-shared-lazy','--variants',(Join-Path $runRoot 'variants'),
            '--output',(Join-Path $runRoot "profile-$round"),'--round',"$round",'--profile') $manifestHash
    }
    $script:activeStep = 'complete'
    Status 'Completed' 'All regression configurations, paired measurements, output parity and lookup profiling passed. Ready for analysis.'
    Log 'COMPLETED. No commit or optimization decision was made by this worker.'
}
catch {
    $failure = $_ | Out-String
    Log "FAILED $script:activeStep : $failure"
    Status 'Failed' $failure
    Write-Error $failure -ErrorAction Continue
    exit 1
}
finally {
    $env:DOTNET_TieredCompilation = $previousTiering
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetry
    $env:DOTNET_NOLOGO = $previousNoLogo
    if ($null -ne $runLock) { $runLock.Dispose() }
}
