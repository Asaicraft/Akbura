#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('Akbura-PuritySummary-'+[Guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $root
$encoding=[Text.UTF8Encoding]::new($false)
function Json([string]$path, $value) { [IO.File]::WriteAllText($path,(($value | ConvertTo-Json -Depth 8).ReplaceLineEndings("`r`n")+"`r`n"),$encoding) }
foreach ($scenario in @('supported','fallback')) {
    $roundCount=if ($scenario -eq 'supported') { 5 } else { 3 }
    foreach ($round in 1..$roundCount) { foreach ($variant in @('baseline','projection','binding','combined')) {
        $path=Join-Path $root ('watch/sidebar/{0}/round-{1:D2}-{2}' -f $scenario,$round,$variant)
        $null=New-Item -ItemType Directory -Path $path -Force
        Json (Join-Path $path 'acceptance.json') @{ Profile='production'; TimingContaminationObserved=$false }
        Json (Join-Path $path 'result.json') @{ Complete=$true; RuntimeCorrectness=$true; Instrumented=$false; Updates=2; Restarts=0 }
        $factor=if ($scenario -eq 'fallback') { 1.0 } else { switch ($variant) { 'baseline' { 1.0 }; 'projection' { 1.1 }; 'binding' { 0.9 }; 'combined' { 0.85 } } }
        $lines=@(foreach ($revision in 1..2) {
            @{ Kind='purity-critical-sample'; Revision=$revision; Warmup=$false; FileWriteToObservedUiMs=(100+$round)*$factor
                Application=@{ Pid=42; OwnerPreserved=$true; ControlsPreserved=$true; StatePreserved=$true; CommandsValid=$true } } | ConvertTo-Json -Compress -Depth 6
        }) + (@{ Kind='purity-critical-complete' } | ConvertTo-Json -Compress)
        [IO.File]::WriteAllText((Join-Path $path 'watch.jsonl'),(($lines -join "`r`n")+"`r`n"),$encoding)
    } }
}
& (Join-Path $PSScriptRoot 'Summarize-PurityCriticalPath.ps1') -ResultDirectory $root -Rounds 5 -Updates 2 -FallbackRounds 3 -FallbackUpdates 2
$rows=Import-Csv -LiteralPath (Join-Path $root 'comparison.csv')
$binding=$rows | Where-Object { $_.Scenario -eq 'supported' -and $_.Variant -eq 'binding' }
if ([math]::Abs([double]::Parse($binding.ImprovementPct,[Globalization.CultureInfo]::InvariantCulture)-10) -gt 0.00001 -or
    [double]::Parse($binding.Low95Pct,[Globalization.CultureInfo]::InvariantCulture) -le 0 -or -not $binding.Bootstrap.Contains('session-cluster')) { throw 'Incorrect session-weighted statistic/bootstrap.' }
$statistics=Get-Content -LiteralPath (Join-Path $root 'statistics.json') -Raw | ConvertFrom-Json
if ($statistics.PrimarySessions -ne 32 -or $statistics.PrimaryUpdates -ne 64 -or -not $statistics.NoDefaultActivation) { throw 'Incorrect counts or automatic default acceptance.' }
$target=Join-Path $root 'watch/sidebar/supported/round-01-baseline/acceptance.json'
Json $target @{ Profile='production'; TimingContaminationObserved=$true }
$rejected=$false
try { & (Join-Path $PSScriptRoot 'Summarize-PurityCriticalPath.ps1') -ResultDirectory $root -Rounds 5 -Updates 2 -FallbackRounds 3 -FallbackUpdates 2 } catch { $rejected=$true }
if (-not $rejected) { throw 'Contaminated production evidence accepted.' }
Write-Output 'PASS session-cluster statistics, exact independent-session counts, contamination rejection and no automatic default activation.'
