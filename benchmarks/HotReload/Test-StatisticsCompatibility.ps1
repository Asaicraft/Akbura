#requires -Version 7.2
param([string[]]$ToolDirectory = @((Join-Path $PSScriptRoot 'bin/Debug/net10.0')))

$ErrorActionPreference = 'Stop'
foreach ($directory in $ToolDirectory) {
    $tool = Join-Path ([IO.Path]::GetFullPath($directory)) 'HotReloadBenchmarks.dll'
    $lines = @(& dotnet $tool statistics-check)
    if ($LASTEXITCODE -ne 0) {
        throw "Statistics compatibility failed: $tool"
    }
    $results = @($lines | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object Kind -eq 'statistics-compatibility')
    if ($results.Count -ne 1 -or $results[0].Counters.PSObject.Properties.Name -contains 'Count' -or
        $results[0].Stages.PSObject.Properties.Name -contains 'Count') {
        throw "Invalid statistics compatibility evidence: $tool"
    }
    Write-Host "Statistics compatibility passed: $directory; $($results[0].CounterLimit) counters, $($results[0].StageLimit) stages."
}
