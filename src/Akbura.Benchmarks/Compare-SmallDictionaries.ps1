param(
    [string]$Output = (Join-Path $env:TEMP ([guid]::NewGuid().ToString().ToUpperInvariant())),
    [int]$Processes = 2,
    [int]$FocusedProcesses = 4,
    [int]$Iterations = 3,
    [int]$TimeoutMinutes = 40,
    [switch]$PrepareOnly,
    [switch]$SmokeOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
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
    if ($Processes -lt 1 -or $FocusedProcesses -lt 1 -or $Iterations -lt 2 -or $TimeoutMinutes -lt 1) {
        throw 'Use at least one process of each kind, two iterations and a positive timeout.'
    }
    $runLock = [IO.File]::Open((Join-Path $runRoot 'worker.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    if (Test-Path -LiteralPath (Join-Path $runRoot 'child.json')) {
        $previousChild = Get-Content (Join-Path $runRoot 'child.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $candidate = Get-Process -Id $previousChild.Pid -ErrorAction SilentlyContinue
        if ($null -ne $candidate -and $candidate.StartTime.ToUniversalTime().ToString('o') -eq $previousChild.Started) {
            throw "Previous child PID $($candidate.Id) is still alive. Wait for it before resuming."
        }
    }
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($runRoot))
    if ($drive.AvailableFreeSpace -lt 2GB) { throw 'At least 2 GB free space is required on the Output volume.' }
    $env:DOTNET_TieredCompilation = '0'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    Log "RUN Output=$runRoot; repo=$repo; worker PID=$PID"
    $manifestPath = Join-Path $runRoot 'manifest.json'
    $hostDirectory = Join-Path $runRoot 'host'
    $variantsDirectory = Join-Path $runRoot 'variants'
    if (!(Test-Path -LiteralPath $manifestPath)) {
        Run-Checked 'build-benchmarks' @('build', (Join-Path $PSScriptRoot 'Akbura.Benchmarks.csproj'), '-c', 'Release', '--no-restore', '-p:BuildProjectReferences=false', '--nologo', '-v', 'quiet')
        New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
        Copy-Item -Path (Join-Path $PSScriptRoot 'bin/Release/net10.0/*') -Destination $hostDirectory -Recurse -Force
        $namePath = 'src/Akbura.Generator/Language/Binder/MarkupNameScope.cs'
        $capturePath = 'src/Akbura.Generator/Language/CodeGeneration/ComponentPlanner.ConditionalCaptures.cs'
        $smallPath = 'src/Akbura.Generator/Collections/SmallDictionary.cs'
        $originalNames = [IO.File]::ReadAllText((Join-Path $repo $namePath))
        $originalCaptures = [IO.File]::ReadAllText((Join-Path $repo $capturePath))
        $originalSmall = [IO.File]::ReadAllText((Join-Path $repo $smallPath))
        $nameType = 'Dictionary<string, MarkupNameDeclaration>'
        $declarationType = 'Dictionary<CSharpExpressionSyntax, ImmutableArray<CSharpLocalSymbol>>'
        $flowType = 'Dictionary<int, ConditionalCaptureEntryFlow>'
        foreach ($variant in @('Dictionary', 'Capacity3', 'SmallDictionary', 'Profile')) {
            $directory = Join-Path $variantsDirectory $variant
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            $names = $originalNames
            $captures = $originalCaptures
            $small = $originalSmall
            if ($variant -eq 'SmallDictionary') {
                $names = $names.Replace($nameType, 'Small' + $nameType)
                $captures = $captures.Replace($declarationType, 'Small' + $declarationType).Replace($flowType, 'Small' + $flowType)
                $captures = Replace-Once $captures 'EntryFlows { get; } = [];' 'EntryFlows { get; } = new();'
            }
            elseif ($variant -eq 'Capacity3') {
                $names = Replace-Once $names 'new(StringComparer.Ordinal);' 'new(3, StringComparer.Ordinal);'
                $captures = Replace-Once $captures "new $declarationType();" "new $declarationType(3);"
                $captures = Replace-Once $captures 'EntryFlows { get; } = [];' 'EntryFlows { get; } = new(3);'
            }
            elseif ($variant -eq 'Profile') {
                $names = $names.Replace($nameType, 'Traced' + $nameType)
                $names = Replace-Once $names 'new(StringComparer.Ordinal);' 'new("NameScope", StringComparer.Ordinal);'
                $captures = $captures.Replace($declarationType, 'Traced' + $declarationType).Replace($flowType, 'Traced' + $flowType)
                $captures = Replace-Once $captures "new Traced$declarationType();" "new Traced$declarationType(`"Declarations`");"
                $captures = Replace-Once $captures 'EntryFlows { get; } = [];' 'EntryFlows { get; } = new("EntryFlows");'
                $small += @'

internal static class SmallMapTrace
{
    public static System.Action<object, string, object?, object?>? Record;
}

internal sealed class TracedDictionary<TKey, TValue> where TKey : notnull
{
    private readonly System.Collections.Generic.Dictionary<TKey, TValue> _map;
    public TracedDictionary(string category, System.Collections.Generic.IEqualityComparer<TKey>? comparer = null)
    {
        comparer ??= System.Collections.Generic.EqualityComparer<TKey>.Default;
        _map = new(comparer);
        SmallMapTrace.Record?.Invoke(this, "Create", category, comparer);
    }
    public bool TryGetValue(TKey key, out TValue value)
    {
        var hit = _map.TryGetValue(key, out value!);
        SmallMapTrace.Record?.Invoke(this, hit ? "Hit" : "Miss", key, value);
        return hit;
    }
    public void Add(TKey key, TValue value)
    {
        _map.Add(key, value);
        SmallMapTrace.Record?.Invoke(this, "Add", key, value);
    }
    public TValue this[TKey key]
    {
        get => _map[key];
        set
        {
            _map[key] = value;
            SmallMapTrace.Record?.Invoke(this, "Set", key, value);
        }
    }
}
'@
            }
            if ($variant -ne 'Dictionary') {
                $names = "using Akbura.Collections;`r`n" + $names
                $captures = "using Akbura.Collections;`r`n" + $captures
            }
            [IO.File]::WriteAllText((Join-Path $directory 'MarkupNameScope.cs'), $names, $encoding)
            [IO.File]::WriteAllText((Join-Path $directory 'ComponentPlanner.ConditionalCaptures.cs'), $captures, $encoding)
            [IO.File]::WriteAllText((Join-Path $directory 'SmallDictionary.cs'), $small, $encoding)
            $escaped = [Security.SecurityElement]::Escape($directory)
            $targets = @"
<Project>
  <Target Name="UseSmallDictionaryVariantSources" BeforeTargets="CoreCompile">
    <ItemGroup>
      <Compile Remove="Language\Binder\MarkupNameScope.cs;Language\CodeGeneration\ComponentPlanner.ConditionalCaptures.cs;Collections\SmallDictionary.cs" />
      <Compile Include="$escaped\MarkupNameScope.cs;$escaped\ComponentPlanner.ConditionalCaptures.cs;$escaped\SmallDictionary.cs" />
    </ItemGroup>
  </Target>
</Project>
"@
            $targetPath = Join-Path $directory 'Variant.targets'
            [IO.File]::WriteAllText($targetPath, $targets, $encoding)
            Run-Checked "build-$variant" @('build', (Join-Path $repo 'src/Akbura.Generator/Akbura.Generator.csproj'), '-c', 'Release', '--no-restore', '--nologo', '-v', 'quiet',
                "-p:OutputPath=$directory/", "-p:IntermediateOutputPath=$directory/obj/", '-p:AkburaEmbedSourceFiles=false', "-p:CustomBeforeMicrosoftCommonTargets=$targetPath")
            Copy-Item -LiteralPath (Join-Path $hostDirectory 'Akbura.BlackSilence.dll'), (Join-Path $hostDirectory 'Akbura.Benchmarks.dll') -Destination $directory -Force
        }
        $files = @(Get-ChildItem -LiteralPath $variantsDirectory -File -Recurse | Where-Object Extension -eq '.dll')
        Save-Json $manifestPath @{
            Prepared = [DateTimeOffset]::Now.ToString('o'); Repository = $repo; BaseCommit = (& git -C $repo rev-parse HEAD)
            Processes = $Processes; FocusedProcesses = $FocusedProcesses; Iterations = $Iterations
            Files = @($files | ForEach-Object { @{ Path = $_.FullName; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
            Inputs = @($namePath, $capturePath, $smallPath | ForEach-Object { @{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash } })
        }
        Log 'PREPARED frozen host and four independent generator builds. Production source files were never modified.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($file in $manifest.Files) {
        if ((Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash -ne $file.Sha256) { throw "Frozen DLL changed: $($file.Path)" }
    }
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    if ($PrepareOnly) { Status 'Prepared' 'Ready to launch benchmarks'; return }
    $hostDll = Join-Path $hostDirectory 'Akbura.Benchmarks.dll'
    Run-Checked 'smoke' @($hostDll, '--small-dictionary', '--variants', $variantsDirectory, '--output', (Join-Path $runRoot 'smoke'), '--focused-only', '--round', '0', '--iterations', '2') $manifestHash
    if ($SmokeOnly) { Status 'SmokePassed' 'Profiling and trace replay smoke test passed'; return }
    for ($round = 0; $round -lt $Processes; $round++) {
        Run-Checked "full-$round" @($hostDll, '--small-dictionary', '--variants', $variantsDirectory, '--output', (Join-Path $runRoot "full-$round"), '--round', "$round", '--iterations', "$Iterations") $manifestHash
    }
    for ($round = 0; $round -lt $FocusedProcesses; $round++) {
        Run-Checked "focused-$round" @($hostDll, '--small-dictionary', '--variants', $variantsDirectory, '--output', (Join-Path $runRoot "focused-$round"), '--focused-only', '--round', "$round", '--iterations', '2') $manifestHash
    }
    $script:activeStep = 'complete'
    Status 'Completed' 'All measurements, trace replay checks and source/diagnostic comparisons passed.'
    Log 'COMPLETED. Results are ready for analysis; no optimization choice or commit has been made.'
}
catch {
    $failure = $_ | Out-String
    if ($null -ne $runLock) {
        Log "FAILED $script:activeStep : $failure"
        Status 'Failed' $failure
    }
    Write-Error $failure -ErrorAction Continue
    exit 1
}
finally {
    $env:DOTNET_TieredCompilation = $previousTiering
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetry
    $env:DOTNET_NOLOGO = $previousNoLogo
    if ($null -ne $runLock) { $runLock.Dispose() }
}
