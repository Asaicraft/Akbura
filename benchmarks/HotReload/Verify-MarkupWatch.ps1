#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$ToolDirectory,
    [switch]$MultipleBindings,
    [switch]$DiagnosticRepair
)

$ErrorActionPreference = 'Stop'
if ($DiagnosticRepair -and -not $MultipleBindings) {
    throw 'Diagnostic repair requires the multiple-binding fixture and a host supporting watch repair.'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ToolDirectory = [IO.Path]::GetFullPath($ToolDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Use a new markup-watch output directory. Preserve unsuccessful attempts.'
}
$fixture = Join-Path $OutputDirectory 'fixture'
$null = New-Item -ItemType Directory -Path $fixture
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'WatchFixture') -File | Copy-Item -Destination $fixture
if ($MultipleBindings) {
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'SemanticWatchFixture') -File | Copy-Item -Destination $fixture -Force
}
$project = Join-Path $fixture 'WatchFixture.csproj'
$xml = [IO.File]::ReadAllText($project).Replace('$(MSBuildThisFileDirectory)../../..', [Security.SecurityElement]::Escape($repository))
foreach ($name in @('Akbura.BlackSilence', 'Akbura.Generator')) {
    $xml = $xml.Replace(('$(AkburaRepositoryRoot)/src/' + $name + '/bin/Debug/netstandard2.0/' + $name + '.dll'),
        [Security.SecurityElement]::Escape((Join-Path $ToolDirectory ($name + '.dll'))))
}
$xml = $xml.Replace('</PropertyGroup>', '<UseSharedCompilation>false</UseSharedCompilation><ReportAnalyzer>true</ReportAnalyzer></PropertyGroup>')
[IO.File]::WriteAllText($project, $xml, [Text.UTF8Encoding]::new($false))
foreach ($step in @(
    @{ Name = 'restore.log'; Arguments = @('restore', $project, '--nologo', '-v:minimal') },
    @{ Name = 'build.log'; Arguments = @('build', $project, '--no-restore', '-p:UseSharedCompilation=false', '-nr:false', '-v:minimal') },
    @{ Name = 'watch.jsonl'; Arguments = @((Join-Path $ToolDirectory 'HotReloadBenchmarks.dll'), 'watch', $project, (Join-Path $fixture 'Counter.akbura'), 'Fixture') + $(if ($DiagnosticRepair) { @('repair') } else { @() }) }
)) {
    $writer = [IO.StreamWriter]::new((Join-Path $OutputDirectory $step.Name), $false, [Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    try {
        $arguments = $step.Arguments
        & dotnet @arguments 2>&1 | ForEach-Object { $writer.WriteLine("$_") }
        if ($LASTEXITCODE -ne 0) {
            throw "Failed $($step.Name); see $OutputDirectory"
        }
    }
    finally {
        $writer.Dispose()
    }
}
$observations = @(Get-Content -LiteralPath (Join-Path $OutputDirectory 'watch.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object Kind -eq 'watch')
if ($observations.Count -ne 9) {
    throw 'Expected nine actual markup Hot Reload updates with original control, state and input preserved.'
}
if ($MultipleBindings) {
    $bindingChecks = @(Get-Content -LiteralPath (Join-Path $OutputDirectory 'watch.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object { $_.Kind -eq 'watch-log' -and $_.Text -match '^PROBE_VALUE .*live_bindings=2 subscriptions=1$' })
    if ($bindingChecks.Count -ne $(if ($DiagnosticRepair) { 11 } else { 10 })) {
        throw 'Expected initial and nine updated observations of both bindings with one shared external event subscription.'
    }
}
if ($DiagnosticRepair) {
    $repair = @(Get-Content -LiteralPath (Join-Path $OutputDirectory 'watch.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object { $_.Kind -eq 'watch-repair' -and $_.DiagnosticRepaired })
    if ($repair.Count -ne 1) {
        throw 'Expected one real diagnostic introduction and repaired UI observation without a process restart.'
    }
}
$binaryHashes = @(Get-ChildItem -LiteralPath $ToolDirectory -File | Where-Object Name -in @('HotReloadBenchmarks.dll','Akbura.Generator.dll','Akbura.BlackSilence.dll') |
    ForEach-Object { @{ Name = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
$fixtureHashes = @(Get-ChildItem -LiteralPath $fixture -File | ForEach-Object {
    @{ Name = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'result.json'), (@{ Updates = 9; Edit = 'Existing Text literal, unchanged count binding'
    OriginalProcessAndControlPreserved = $true; StateAndInputPreserved = $true; BinaryHashes = $binaryHashes
    MultipleBindings = [bool]$MultipleBindings; ReactiveBindingAndSubscriptionChecks = [bool]$MultipleBindings
    DiagnosticRepairVerified = [bool]$DiagnosticRepair
    FixtureHashes = $fixtureHashes; VerifierSha256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash
    SemanticHitObservation = 'Not available inside dotnet watch. Text edits intentionally reject the restricted Margin-only semantic contract.'
    Boundaries = 'File write to observed rendered value; exact compiler delta submission and per-update generator time unavailable.' } |
    ConvertTo-Json -Depth 5) + "`r`n", [Text.UTF8Encoding]::new($false))
