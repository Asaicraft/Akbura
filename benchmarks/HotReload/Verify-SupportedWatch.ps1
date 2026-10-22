#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$ToolDirectory,
    [ValidateSet('margin','text','scope','compilation','stress')][string]$Scenario = 'margin',
    [ValidateSet('baseline','projection','binding','combined')][string]$Variant = 'combined',
    [ValidateRange(1, 500)][int]$Updates = 20,
    [ValidateRange(0, 10)][int]$Warmups = 2,
    [switch]$Instrumented
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ToolDirectory = [IO.Path]::GetFullPath($ToolDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Preserve existing watch evidence and select a new output directory.'
}
$encoding = [Text.UTF8Encoding]::new($false)
$fixture = Join-Path $OutputDirectory 'fixture'
$null = New-Item -ItemType Directory -Path $fixture
foreach ($directory in @('WatchFixture','SemanticWatchFixture','SupportedWatchFixture')) {
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $directory) -File | Copy-Item -Destination $fixture -Force
}
$project = Join-Path $fixture 'WatchFixture.csproj'
$xml = [IO.File]::ReadAllText($project).Replace('$(MSBuildThisFileDirectory)../../..', [Security.SecurityElement]::Escape($repository))
foreach ($assembly in @('Akbura.Generator','Akbura.BlackSilence')) {
    $xml = $xml.Replace(('$(AkburaRepositoryRoot)/src/' + $assembly + '/bin/Debug/netstandard2.0/' + $assembly + '.dll'),
        [Security.SecurityElement]::Escape((Join-Path $ToolDirectory ($assembly + '.dll'))))
}
$xml = $xml.Replace('</PropertyGroup>', '<UseSharedCompilation>false</UseSharedCompilation><AkburaCrossCompilationReuse>true</AkburaCrossCompilationReuse></PropertyGroup>')
[IO.File]::WriteAllText($project, $xml, $encoding)
$telemetry = if ($Instrumented) { Join-Path $OutputDirectory 'analyzer-observations' } else { 'off' }
foreach ($step in @(
    @{ Name = 'restore.log'; Arguments = @('restore', $project, '--nologo', '-v:minimal') },
    @{ Name = 'build.log'; Arguments = @('build', $project, '--no-restore', '-p:UseSharedCompilation=false', '-nr:false', '--nologo', '-v:minimal') },
    @{ Name = 'watch.jsonl'; Arguments = @((Join-Path $ToolDirectory 'HotReloadBenchmarks.dll'), 'semantic-watch', $project,
        (Join-Path $OutputDirectory 'observer-command.json'), $Scenario, "$Updates", "$Warmups", $telemetry) }
)) {
    $writer = [IO.StreamWriter]::new((Join-Path $OutputDirectory $step.Name), $false, $encoding)
    $writer.AutoFlush = $true
    try {
        & dotnet @($step.Arguments) 2>&1 | ForEach-Object { $writer.WriteLine("$_") }
        if ($LASTEXITCODE -ne 0) { throw "Failed $($step.Name); see $OutputDirectory" }
    }
    finally { $writer.Dispose() }
}
$rows = @(Get-Content -LiteralPath (Join-Path $OutputDirectory 'watch.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
$samples = @($rows | Where-Object { $_.Kind -eq 'semantic-watch-sample' -and $_.Phase -eq $Scenario -and -not $_.Warmup })
if ($samples.Count -ne $Updates -or @($rows | Where-Object Kind -eq 'semantic-watch-complete').Count -ne 1) {
    throw 'Incomplete measured updates or final completion marker.'
}
$causal = @($rows | Where-Object { $_.Kind -eq 'semantic-watch-causal' -and $_.Phase -eq $Scenario })
$skips = @($causal | ForEach-Object { [long]$_.Record.'counter.CrossRevisionFirstBindingSkipped' } | Measure-Object -Sum).Sum
$hits = @($causal | ForEach-Object { [long]$_.Record.'counter.CrossRevisionBindingHit' } | Measure-Object -Sum).Sum
$imports = @($causal | ForEach-Object { [long]$_.Record.'counter.CrossRevisionSemanticContributionImported' } | Measure-Object -Sum).Sum
$probeSkips = @($causal | ForEach-Object { [long]$_.Record.'counter.CrossRevisionProbeCompilationSkipped' } | Measure-Object -Sum).Sum
$modelSkips = @($causal | ForEach-Object { [long]$_.Record.'counter.CrossRevisionProbeSemanticModelSkipped' } | Measure-Object -Sum).Sum
$result = @{ Scenario = $Scenario; Variant = $Variant; Instrumented = [bool]$Instrumented; Updates = $Updates; Warmups = $Warmups
    RuntimeCorrectness = $true; Restarts = 0; SubscriptionFailures = 0; TelemetryRecords = $causal.Count
    SemanticHits = $hits; FirstBindingsSkipped = $skips; ProbeCompilationsSkipped = $probeSkips; ProbeModelsSkipped = $modelSkips
    ContributionsImported = $imports; CausalEvidence = $(if ($Instrumented) { 'Observed in the watched analyzer; see per-revision records.' } else { 'Unavailable per production update; do not substitute instrumented counts.' })
    CausalSkipGate = $(if (-not $Instrumented) { 'Unverified' } elseif ($Scenario -in @('margin','stress') -and $Variant -in @('binding','combined')) {
        if ($skips -ge 2 * ($Updates + $Warmups) -and $hits -eq $skips -and $probeSkips -eq $skips -and $modelSkips -eq $skips -and $imports -eq $skips) { 'Passed' } else { 'NotMet' }
    } elseif ($skips -eq 0) { 'PassedNegativeControl' } else { 'UnexpectedReuse' })
    TimingBoundary = 'File write completed (including closing the file) to received live UI acknowledgment after property and runtime-invariant verification.'
    Binaries = @(Get-ChildItem -LiteralPath $ToolDirectory -File | ForEach-Object { @{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
    Fixture = @(Get-ChildItem -LiteralPath $fixture -File | ForEach-Object { @{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
    CompletedUtc = [DateTime]::UtcNow.ToString('o') }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'result.json'), (($result | ConvertTo-Json -Depth 6) + "`r`n"), $encoding)
Write-Output "$Scenario/${Variant}: $Updates live updates; causal gate=$($result.CausalSkipGate); first bindings skipped=$skips"
