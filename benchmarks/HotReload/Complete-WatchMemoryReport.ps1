#requires -Version 7.2
param([Parameter(Mandatory)][string]$ResultDirectory, [Parameter(Mandatory)][string]$OutputDirectory, [int]$Rounds = 3)
$ErrorActionPreference = 'Stop'
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Preserve previous report attempts; choose a fresh output directory.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$encoding = [Text.UTF8Encoding]::new($false)
$journal = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'journal.log'),$false,$encoding)
$journal.AutoFlush = $true
function Log([string]$message) { $journal.WriteLine([DateTime]::UtcNow.ToString('o') + ' ' + $message); $journal.BaseStream.Flush($true); Write-Output $message }
function Save([string]$name, $value) {
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $name),(($value | ConvertTo-Json -Depth 10).ReplaceLineEndings("`r`n") + "`r`n"),$encoding)
}
function Input-Hashes {
    @(Get-ChildItem -LiteralPath (Join-Path $ResultDirectory 'memory/runs') -File -Recurse | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($ResultDirectory,$_.FullName).Replace('\','/') + ' ' + (Get-FileHash -LiteralPath $_.FullName).Hash
    })
}
try {
    Log 'START read-only postprocessing; no builds, watch sessions, forced GC or measurement reruns'
    $inputs = Input-Hashes
    $sourceFiles = @($PSCommandPath,(Join-Path $PSScriptRoot 'Summarize-WatchMemory.ps1'),(Join-Path $PSScriptRoot 'Test-WatchMemoryReport.ps1'))
    $sources = @($sourceFiles | ForEach-Object { @{ File = [IO.Path]::GetFileName($_); Sha256 = (Get-FileHash -LiteralPath $_).Hash } })
    Save 'provenance.json' @{ StartedUtc = [DateTime]::UtcNow.ToString('o'); InputDirectory = $ResultDirectory
        OriginalMeasurementSourceManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $ResultDirectory 'sources.json')).Hash
        InputFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($inputs -join "`n"))))
        Inputs = $inputs; ReportSources = $sources; PowerShell = $PSVersionTable.PSVersion.ToString(); LocalTimeZone = [TimeZoneInfo]::Local.Id
        Meaning = 'New postprocessing only. Original measurements and source manifests are not rewritten or relabeled.' }
    & (Join-Path $PSScriptRoot 'Test-WatchMemoryReport.ps1') | ForEach-Object { Log "TEST $_" }
    $runs = @(Get-ChildItem -LiteralPath (Join-Path $ResultDirectory 'memory/runs') -Directory)
    if ($runs.Count -ne 12 * $Rounds) { throw 'Unexpected matched-session count.' }
    foreach ($run in $runs) {
        $result = Get-Content -LiteralPath (Join-Path $run.FullName 'result.json') -Raw | ConvertFrom-Json
        if (-not $result.RuntimeCorrectness -or $result.Updates -ne 100 -or $result.Restarts -ne 0 -or $result.SubscriptionFailures -ne 0 -or
            $result.CausalSkipGate -notin @('Passed','PassedNegativeControl','Unverified')) { throw "Invalid archived runtime/causal verification: $($run.Name)" }
    }
    Log "VERIFIED $($runs.Count) completed runtime sessions; processing external counters and archived object graphs"
    & (Join-Path $PSScriptRoot 'Summarize-WatchMemory.ps1') -ResultDirectory $ResultDirectory -OutputDirectory $OutputDirectory -Rounds $Rounds | ForEach-Object { Log $_ }
    if (((Input-Hashes) -join "`n") -cne ($inputs -join "`n")) { throw 'Raw input evidence changed during postprocessing; this report is not valid.' }
    foreach ($source in $sources) {
        $path = $sourceFiles | Where-Object { [IO.Path]::GetFileName($_) -eq $source.File }
        if ((Get-FileHash -LiteralPath $path).Hash -ne $source.Sha256) { throw 'Report source changed during postprocessing.' }
    }
    $series = @(Import-Csv -LiteralPath (Join-Path $OutputDirectory 'memory-series.csv'))
    Save 'status.json' @{ Phase = 'SummaryCompleteNeedsReview'; Sessions = $runs.Count; MeasuredUpdates = $runs.Count * 100
        CheckpointRows = $series.Count; RejectedNearerIncompleteSamples = ($series.RejectedNearerIncompleteSamples | Measure-Object -Sum).Sum
        RawInputsUnchanged = $true; CompletedUtc = [DateTime]::UtcNow.ToString('o') }
    Log 'COMPLETE summary ready; raw input hashes unchanged. Retention attribution and production performance are not automatically accepted.'
}
catch { Save 'status.json' @{ Phase = 'Failed'; Error = ($_ | Out-String); UpdatedUtc = [DateTime]::UtcNow.ToString('o') }; Log ('FAILED ' + ($_ | Out-String)); throw }
finally { $journal.Dispose() }
