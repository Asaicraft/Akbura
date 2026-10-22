#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('Akbura-PurityTrace-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$encoding = [Text.UTF8Encoding]::new($false)
function Save-Lines([string]$name, $values) {
    [IO.File]::WriteAllText((Join-Path $root $name),((@($values | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 6 }) -join "`r`n") + "`r`n"),$encoding)
}
$events = @(
    @{ Provider='Microsoft-CodeAnalysis-General'; Event='StartGeneratorDriverRunTime/Start'; Opcode='Start'; Pid=42; LocalMs=100.0; Payload=@{ id='driver-a' } },
    @{ Provider='Akbura-CriticalPath'; Event='Revision'; Pid=42; LocalMs=110.0; Payload=@{ revision='1'; sourceSha256='ABC' } },
    @{ Provider='Microsoft-CodeAnalysis-General'; Event='StopGeneratorDriverRunTime'; Opcode='Stop'; Pid=42; LocalMs=130.0; Payload=@{ id='driver-a' } })
Save-Lines 'roslyn-events.jsonl' $events
Save-Lines 'watch.jsonl' @(@{ Kind='purity-critical-sample'; Revision=1; SourceSha256='ABC'; Warmup=$false })
& (Join-Path $PSScriptRoot 'Summarize-PurityTrace.ps1') -SessionDirectory $root
$row = Import-Csv -LiteralPath (Join-Path $root 'roslyn-spans.csv')
if ($row.Revision -ne '1' -or $row.DurationMs -ne '30') { throw 'Incorrect single-clock interval/correlation.' }
foreach ($case in @('wrong-hash','truncated','foreign-process','ambiguous')) {
    $changed = switch ($case) {
        'wrong-hash' { @($events[0],@{ Provider='Akbura-CriticalPath'; Pid=42; LocalMs=110.0; Payload=@{ revision='1'; sourceSha256='BAD' } },$events[2]) }
        'truncated' { @($events[0],$events[1]) }
        'foreign-process' { @($events[0],@{ Provider='Akbura-CriticalPath'; Pid=99; LocalMs=110.0; Payload=@{ revision='1'; sourceSha256='ABC' } },$events[2]) }
        'ambiguous' { @($events[0],$events[1],@{ Provider='Akbura-CriticalPath'; Pid=42; LocalMs=120.0; Payload=@{ revision='2'; sourceSha256='XYZ' } },$events[2]) }
    }
    Save-Lines 'roslyn-events.jsonl' $changed
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Summarize-PurityTrace.ps1') -SessionDirectory $root } catch { $rejected = $true }
    if (-not $rejected) { throw "Invalid trace accepted: $case" }
}
Write-Output 'PASS exact in-process marker correlation, one-clock durations, and rejection of wrong hashes, truncated spans, foreign PIDs and ambiguous markers.'
