#requires -Version 7.2
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot ('../../bin/MemberWatch/' + [Guid]::NewGuid().ToString('N'))),
    [string]$ToolDirectory = (Join-Path $PSScriptRoot 'bin/Debug/net10.0'),
    [ValidateSet('enabled', 'disabled')][string]$Optimization = 'enabled'
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ToolDirectory = [IO.Path]::GetFullPath($ToolDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Use a fresh member-watch output directory; failed logs must not be overwritten.'
}
$fixture = Join-Path $OutputDirectory 'fixture'
$null = New-Item -ItemType Directory -Path $fixture -Force
foreach ($sourceDirectory in @('WatchFixture', 'MemberWatchFixture')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $sourceDirectory) -File) {
        Copy-Item -LiteralPath $file.FullName -Destination $fixture -Force
    }
}
$project = Join-Path $fixture 'WatchFixture.csproj'
$xml = [IO.File]::ReadAllText($project).Replace('$(MSBuildThisFileDirectory)../../..', [Security.SecurityElement]::Escape($repository))
foreach ($name in @('Akbura.BlackSilence', 'Akbura.Generator')) {
    $original = '$(AkburaRepositoryRoot)/src/' + $name + '/bin/Debug/netstandard2.0/' + $name + '.dll'
    $xml = $xml.Replace($original, [Security.SecurityElement]::Escape((Join-Path $ToolDirectory ($name + '.dll'))))
}
$properties = '<UseSharedCompilation>false</UseSharedCompilation><ReportAnalyzer>true</ReportAnalyzer><AkburaCrossCompilationReuse>' +
    ($Optimization -eq 'enabled').ToString().ToLowerInvariant() + '</AkburaCrossCompilationReuse>'
$xml = $xml.Replace('</PropertyGroup>', $properties + '</PropertyGroup>')
[IO.File]::WriteAllText($project, $xml, [Text.UTF8Encoding]::new($false))

function Invoke-Logged([string[]]$arguments, [string]$name) {
    $path = Join-Path $OutputDirectory $name
    $writer = [IO.StreamWriter]::new($path, $false, [Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    try {
        & dotnet @arguments 2>&1 | ForEach-Object { $writer.WriteLine("$_") }
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet exited with $LASTEXITCODE; see $path"
        }
    }
    finally {
        $writer.Dispose()
    }
}

Invoke-Logged @('restore', $project, '--nologo', '-v:minimal') 'restore.log'
Invoke-Logged @('build', $project, '--no-restore', '-p:UseSharedCompilation=false', '-nr:false', '--nologo', '-v:minimal') 'build.log'
$tool = Join-Path $ToolDirectory 'HotReloadBenchmarks.dll'
Invoke-Logged @($tool, 'watch', $project, (Join-Path $fixture 'MemberViewModel.cs'), 'MemberFixture') 'watch.jsonl'
$observations = @([IO.File]::ReadLines((Join-Path $OutputDirectory 'watch.jsonl')) | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object Kind -eq 'watch')
if ($observations.Count -ne 9 -or @($observations | Where-Object { $_.TotalMs -le 0 }).Count -gt 0) {
    throw 'Expected one first edit and eight measured live member updates.'
}
$result = @{ Kind = 'member-watch-result'; CompletedUtc = [DateTime]::UtcNow.ToString('o'); Optimization = $Optimization
    Updates = 9; OriginalProcessAndControlPreserved = $true; MemberImplementationAndDisplayedValueVerified = $true
    StateAndInputPreserved = $true; ToolSha256 = (Get-FileHash -LiteralPath $tool).Hash
    RefreshPolicy = 'The application increments a separate tick state every 10 ms; no arbitrary-service runtime refresh graph is claimed.'
    StageBoundaries = 'Native detection logs and post-delta metadata callbacks are observed. Exact EnC generator processing and delta submission are unavailable, not inferred from generator-only timings.' }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'result.json'), ($result | ConvertTo-Json) + "`r`n", [Text.UTF8Encoding]::new($false))
Write-Output "Verified nine live C# member changes: $OutputDirectory"
