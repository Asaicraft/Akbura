#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$ToolDirectory
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ToolDirectory = [IO.Path]::GetFullPath($ToolDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Use a new markup-watch output directory. Preserve unsuccessful attempts.'
}
$fixture = Join-Path $OutputDirectory 'fixture'
$null = New-Item -ItemType Directory -Path $fixture
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'WatchFixture') -File | Copy-Item -Destination $fixture
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
    @{ Name = 'watch.jsonl'; Arguments = @((Join-Path $ToolDirectory 'HotReloadBenchmarks.dll'), 'watch', $project, (Join-Path $fixture 'Counter.akbura'), 'Fixture') }
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
$binaryHashes = @(Get-ChildItem -LiteralPath $ToolDirectory -File | Where-Object Name -in @('HotReloadBenchmarks.dll','Akbura.Generator.dll','Akbura.BlackSilence.dll') |
    ForEach-Object { @{ Name = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'result.json'), (@{ Updates = 9; Edit = 'Existing Text literal, unchanged count binding'
    OriginalProcessAndControlPreserved = $true; StateAndInputPreserved = $true; BinaryHashes = $binaryHashes
    Boundaries = 'File write to observed rendered value; exact compiler delta submission and per-update generator time unavailable.' } |
    ConvertTo-Json -Depth 5) + "`r`n", [Text.UTF8Encoding]::new($false))
