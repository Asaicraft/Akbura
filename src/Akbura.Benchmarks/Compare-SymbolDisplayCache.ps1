param(
    [string]$Output = (Join-Path $env:TEMP ([guid]::NewGuid().ToString().ToUpperInvariant())),
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
    $files = @('src/Akbura.Generator/Collections/CachedSymbolDisplay.cs', 'src/Akbura.Generator/Language/Compilation/AkburaSemanticModel.cs',
        'src/Akbura.UnitTests/CachedSymbolDisplayTests.cs', 'src/Akbura.Benchmarks/FeatureGallerySymbolDisplayComparison.cs',
        'src/Akbura.Benchmarks/Program.cs', 'src/Akbura.Benchmarks/Compare-SymbolDisplayCache.ps1')
    $manifest = @($files | ForEach-Object { [ordered]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $repo $_)).Hash } })
    Save-Json (Join-Path $runRoot 'sources.json') $manifest
    $manifestHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Compress))))
    $projects = @{}
    foreach ($name in @('Generator','BlackSilence','Benchmarks','UnitTests')) { $projects[$name] = Join-Path $repo "src/Akbura.$name/Akbura.$name.csproj" }
    $cachedModel = [IO.File]::ReadAllText((Join-Path $repo 'src/Akbura.Generator/Language/Compilation/AkburaSemanticModel.cs'))
    $cachedHelper = [IO.File]::ReadAllText((Join-Path $repo 'src/Akbura.Generator/Collections/CachedSymbolDisplay.cs'))
    $cachedExpression = @'
            CachedSymbolDisplay.GetFullyQualifiedName(left) ==
            CachedSymbolDisplay.GetFullyQualifiedName(right);
'@
    $originalExpression = @'
            left.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            right.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
'@
    $baselineModel = Replace-Once $cachedModel $cachedExpression $originalExpression
    $baselineModel = Replace-Once $baselineModel "using Akbura.Collections;`r`n" ''
    $hooks = @'

internal static class SymbolDisplayTrace
{
    public static System.Action<ITypeSymbol, ITypeSymbol, bool>? Compare;
    public static System.Action<ITypeSymbol, long>? Format;
    public static System.Action<bool>? Lookup;
    public static System.Func<long>? Bytes;
    public static string Display(ITypeSymbol symbol)
    {
        var before = Bytes!();
        var display = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        Format?.Invoke(symbol, Bytes!() - before);
        return display;
    }
}
'@
    foreach ($variant in @('Baseline','Cached','ProfileBaseline','ProfileCached')) {
        $directory = Join-Path $runRoot "variants/$variant"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $profile = $variant.StartsWith('Profile')
        $baseline = $variant.EndsWith('Baseline')
        $model = if ($baseline) { $baselineModel } else { $cachedModel }
        $helper = $cachedHelper
        if ($profile) {
            $model = Replace-Once $model '        return SymbolEqualityComparer.Default.Equals(left, right) ||' '        var traceResult = SymbolEqualityComparer.Default.Equals(left, right) ||'
            $anchor = if ($baseline) { $originalExpression } else { $cachedExpression }
            $model = Replace-Once $model $anchor ($anchor + "`r`n        SymbolDisplayTrace.Compare?.Invoke(left, right, traceResult);`r`n        return traceResult;")
            if ($baseline) {
                $model = Replace-Once $model 'left.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)' 'SymbolDisplayTrace.Display(left)'
                $model = Replace-Once $model 'right.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)' 'SymbolDisplayTrace.Display(right)'
            } else {
                $helper = Replace-Once $helper '        return s_fullyQualifiedNames.GetValue(symbol,' "        Akbura.Language.SymbolDisplayTrace.Lookup?.Invoke(s_fullyQualifiedNames.TryGetValue(symbol, out _));`r`n        return s_fullyQualifiedNames.GetValue(symbol,"
                $helper = Replace-Once $helper 'type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)' 'Akbura.Language.SymbolDisplayTrace.Display(type)'
            }
            $model += $hooks
        }
        [IO.File]::WriteAllText((Join-Path $directory 'AkburaSemanticModel.cs'), $model, $encoding)
        [IO.File]::WriteAllText((Join-Path $directory 'CachedSymbolDisplay.cs'), $helper, $encoding)
        $escaped = [Security.SecurityElement]::Escape($directory)
        $includeHelper = if ($baseline) { '' } else { ";$escaped\CachedSymbolDisplay.cs" }
        $targetPath = Join-Path $directory 'Variant.targets'
        [IO.File]::WriteAllText($targetPath, @"
<Project><Target Name="SymbolDisplayVariant" BeforeTargets="CoreCompile"><ItemGroup>
<Compile Remove="Language\Compilation\AkburaSemanticModel.cs;Collections\CachedSymbolDisplay.cs" />
<Compile Include="$escaped\AkburaSemanticModel.cs$includeHelper" />
</ItemGroup></Target></Project>
"@, $encoding)
        Run-Checked "build-$variant-Generator" @('build', $projects.Generator, '-c','Release','--no-restore','--nologo','-v','quiet',
            '-p:BuildProjectReferences=false','-p:EnableAkburaStats=false','-p:EnableQuickScanBenchmark=false','-p:AkburaEmbedSourceFiles=false',
            "-p:OutputPath=$directory/", "-p:IntermediateOutputPath=$directory/obj/", "-p:CustomBeforeMicrosoftCommonTargets=$targetPath") $manifestHash
    }
    $hostDirectory = Join-Path $runRoot 'host'
    New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repo 'src/Akbura.Benchmarks/bin/Release/net10.0') |
        Where-Object Name -NotIn @('Akbura.Benchmarks.dll','Akbura.Benchmarks.pdb') | Copy-Item -Destination $hostDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $runRoot 'variants/Cached/Akbura.Generator.dll') -Destination $hostDirectory -Force
    Run-Checked 'build-BlackSilence' @('build',$projects.BlackSilence,'-c','Release','--no-restore','--nologo','-v','quiet',
        '-p:BuildProjectReferences=false','-p:EnableAkburaStats=false','-p:EnableQuickScanBenchmark=false','-p:AkburaEmbedSourceFiles=false',
        "-p:OutputPath=$hostDirectory/", "-p:IntermediateOutputPath=$hostDirectory/obj-black/") $manifestHash
    Run-Checked 'build-Benchmarks' @('build',$projects.Benchmarks,'-c','Release','--no-restore','--nologo','-v','quiet',
        '-p:BuildProjectReferences=false','-p:EnableAkburaStats=false','-p:EnableQuickScanBenchmark=false','-p:AkburaEmbedSourceFiles=false',
        "-p:OutputPath=$hostDirectory/", "-p:IntermediateOutputPath=$hostDirectory/obj-bench/") $manifestHash
    foreach ($variant in @('Baseline','Cached','ProfileBaseline','ProfileCached')) {
        foreach ($name in @('Akbura.BlackSilence.dll','Akbura.Benchmarks.dll','Akbura.Benchmarks.pdb')) {
            Copy-Item -LiteralPath (Join-Path $hostDirectory $name) -Destination (Join-Path $runRoot "variants/$variant") -Force
        }
    }
    if ($PrepareOnly) { Status 'Prepared' 'Four isolated assemblies prepared.'; exit 0 }
    $testFilter = 'FullyQualifiedName~CachedSymbolDisplayTests|FullyQualifiedName~Akcss|FullyQualifiedName~CSharpProbe|FullyQualifiedName~SemanticPipelineTests|FullyQualifiedName~ComponentBinding|FullyQualifiedName~DiagnosticParity|FullyQualifiedName~SourceGeneration'
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
        Run-Checked "macro-$round" @($hostDll,'--symbol-display-cache','--variants',(Join-Path $runRoot 'variants'),
            '--output',(Join-Path $runRoot "macro-$round"),'--round',"$round") $manifestHash
    }
    for ($round = 0; $round -lt 3; $round++) {
        Run-Checked "focused-$round" @($hostDll,'--symbol-display-cache','--variants',(Join-Path $runRoot 'variants'),
            '--output',(Join-Path $runRoot "focused-$round"),'--round',"$round",'--focused') $manifestHash
    }
    $script:activeStep = 'complete'
    Status 'Completed' 'All regression configurations, macro measurements, exact parity checks and actual-pair replays passed. Ready for analysis.'
    Log 'COMPLETED. Results are ready for analysis; no commit was made by this worker.'
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
