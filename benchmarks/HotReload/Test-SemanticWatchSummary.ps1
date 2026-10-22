#requires -Version 7.2
param()
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('AkburaWatchSummary-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$encoding = [Text.UTF8Encoding]::new($false)
foreach ($round in 1..3) {
    foreach ($scenario in @('margin','text')) {
        foreach ($variant in @('baseline','projection','binding','combined')) {
            $directory = Join-Path $root "runs/production-$round-$variant-$scenario"
            $null = New-Item -ItemType Directory -Path $directory -Force
            [IO.File]::WriteAllText((Join-Path $directory 'acceptance.json'), '{"Profile":"production","TimingContaminationObserved":false}', $encoding)
            $rows = @(foreach ($revision in 1..2) {
                @{ Kind = 'semantic-watch-sample'; Phase = $scenario; Warmup = $false; Revision = $revision
                    FileWriteToObservedUiMs = 8.25 + $revision; FileWriteDurationMs = 0.125
                    Application = @{ Pid = 123; ControlsPreserved = $true; StatePreserved = $true; InputPreserved = $true
                        BindingsValid = $true; Subscriptions = 1; ManagedHeapBytes = 1000; AllocatedBytes = 2000; WorkingSetBytes = 3000 } }
            }) + @(@{ Kind = 'semantic-watch-sample'; Phase = 'memory'; Application = @{ PostGc = $true; ManagedHeapBytes = 1000 } }, @{ Kind = 'semantic-watch-complete' })
            [IO.File]::WriteAllText((Join-Path $directory 'watch.jsonl'), ((@($rows | ForEach-Object { $_ | ConvertTo-Json -Depth 5 -Compress }) -join "`r`n") + "`r`n"), $encoding)
        }
    }
}
$previous = [Globalization.CultureInfo]::CurrentCulture
try {
    [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('en-US')
    & (Join-Path $PSScriptRoot 'Summarize-SemanticWatch.ps1') -ResultDirectory $root -Updates 2
    $hashes = @('samples.csv','summary.csv','comparisons.csv','measured-summary.md') | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $root $_)).Hash }
    $summary = Import-Csv -LiteralPath (Join-Path $root 'summary.csv')
    if ($summary.Count -ne 24 -or [double]::Parse($summary[0].MedianMs, [Globalization.CultureInfo]::InvariantCulture) -ne 9.75) { throw 'Incorrect fractional median or group count.' }
    [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('ru-RU')
    & (Join-Path $PSScriptRoot 'Summarize-SemanticWatch.ps1') -ResultDirectory $root -Updates 2
    $newHashes = @('samples.csv','summary.csv','comparisons.csv','measured-summary.md') | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $root $_)).Hash }
    if (($hashes -join ';') -cne ($newHashes -join ';')) { throw 'Locale-dependent summary.' }
    $path = Join-Path $root 'runs/production-1-baseline-margin/watch.jsonl'
    $original = [IO.File]::ReadAllText($path)
    foreach ($mutation in @('duplicate','runtime','unfinished')) {
        $rows = @($original -split '\r?\n' | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
        switch ($mutation) {
            'duplicate' { $rows[1].Revision = $rows[0].Revision }
            'runtime' { $rows[0].Application.BindingsValid = $false }
            'unfinished' { $rows = @($rows | Where-Object Kind -ne 'semantic-watch-complete') }
        }
        [IO.File]::WriteAllText($path, (@($rows | ForEach-Object { $_ | ConvertTo-Json -Depth 5 -Compress }) -join "`r`n"), $encoding)
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'Summarize-SemanticWatch.ps1') -ResultDirectory $root -Updates 2 } catch { $rejected = $true }
        if (-not $rejected) { throw "Accepted invalid synthetic fixture: $mutation" }
        [IO.File]::WriteAllText($path, $original, $encoding)
    }
}
finally { [Globalization.CultureInfo]::CurrentCulture = $previous }
Write-Output 'Summary guards passed: fractional distributions, invariant culture, duplicate rejection, runtime validation and completion. Synthetic data is not benchmark evidence.'
