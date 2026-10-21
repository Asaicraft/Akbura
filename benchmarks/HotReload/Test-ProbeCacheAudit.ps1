#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$root = Join-Path $repository ('bin/ProbeCacheAuditTests/' + [Guid]::NewGuid().ToString('N'))
$encoding = [Text.UTF8Encoding]::new($false)
$null = New-Item -ItemType Directory -Path $root
function Fixture([string]$fault) {
    $fixture = Join-Path $root $fault
    $null = New-Item -ItemType Directory -Path $fixture
    [IO.File]::WriteAllText((Join-Path $fixture 'sources.json'), '{"Rounds":2}' + "`r`n", $encoding)
    $jobs = @('stats-current','stats-disabled','stats-prehashed','stats-repeat-admission')
    foreach ($round in 1..2) {
        foreach ($variant in @('original','current','rebuilt-current','disabled','prehashed','repeat-admission')) { $jobs += "production-$round-$variant" }
    }
    foreach ($job in $jobs) {
        foreach ($category in @('literal','binding')) {
            $path = Join-Path $fixture "audit/$job-$category"
            $null = New-Item -ItemType Directory -Path $path
            $profile = if ($job.StartsWith('stats-')) { 'stats' } else { 'off' }
            $configuration = @{ Profiling = $(if ($fault -eq 'profile' -and $job -eq 'stats-current') { 'off' } else { $profile }) }
            [IO.File]::WriteAllText((Join-Path $path 'run.json'), ($configuration | ConvertTo-Json) + "`r`n", $encoding)
            $files = if ($category -eq 'literal') { @('Counter.akbura','DataTemplatesPage.akbura','NeedHelp.akbura','Sidebar.akbura') } else { @('DataTemplates.akbura','NavButton.akbura') }
            $samples = [Collections.Generic.List[object]]::new()
            $stream = [IO.File]::Create((Join-Path $path 'invalidation-traces.jsonl.gz'))
            $gzip = [IO.Compression.GZipStream]::new($stream,[IO.Compression.CompressionLevel]::Fastest)
            $writer = [IO.StreamWriter]::new($gzip,$encoding)
            try {
                foreach ($file in $files) {
                    foreach ($index in 0..10) {
                        $isFault = $job -eq 'production-2-prehashed' -and $category -eq 'literal' -and $file -eq 'Counter.akbura' -and $index -eq 10
                        $samples.Add([pscustomobject]@{ Project='Demo'; File=$file; Scenario="localized-$category"; Index=$(if ($isFault -and $fault -eq 'duplicate-index') { 9 } else { $index })
                            First=($index -eq 0); Warmup=($index -in 1..2); VerifyEveryUpdate=$true; GeneratorMs=10; ProcessAllocatedBytes=1000; PostGcHeapBytes=2000 })
                        $validation = @{ Kind='invalidation-validation'; Project='Demo'; File=$file; Scenario="localized-$category"; Index=$index
                            Diagnostics=@($(if ($isFault -and $fault -eq 'diagnostics') { 'changed' } else { 'same' })) }
                        $parity = @{ Kind='invalidation-parity'; Project='Demo'; File=$file; Scenario="localized-$category"; Index=$index
                            EqualSources=$true; EqualDiagnostics=$true; SourceDigests=@{'sample.g.cs'=$(if ($isFault -and $fault -eq 'sources') { 'DIFFERENT' } else { 'ABC' })} }
                        $writer.WriteLine(($validation | ConvertTo-Json -Depth 4 -Compress))
                        if (-not ($isFault -and $fault -eq 'missing-parity')) { $writer.WriteLine(($parity | ConvertTo-Json -Depth 4 -Compress)) }
                    }
                }
            }
            finally { $writer.Dispose(); $gzip.Dispose(); $stream.Dispose() }
            $samples | Export-Csv -LiteralPath (Join-Path $path 'invalidation-samples.csv') -NoTypeInformation
            [IO.File]::WriteAllText((Join-Path $path 'invalidation-stages.csv'), '"Project","File","Scenario","Stage","First","Warmup","Count","InclusiveMs","InclusiveCurrentThreadBytes"' + "`r`n", $encoding)
        }
    }
    return $fixture
}
$controller = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Start-ProbeCacheAudit.ps1'))
if ([regex]::Matches($controller, '(?m)^\s*\$null\s*=\s*\$copy(?:Out|Err)\.GetAwaiter\(\)\.GetResult\(\)\s*$').Count -ne 2) {
    throw 'Stream-copy task results must not leak into frozen tool paths.'
}
$checks = 1
foreach ($fault in @('valid','diagnostics','sources','duplicate-index','missing-parity','profile')) {
    $fixture = Fixture $fault
    $failed = $false
    try { & (Join-Path $PSScriptRoot 'Summarize-ProbeCacheAudit.ps1') -InputDirectory $fixture -ResultDirectory $fixture 6>$null }
    catch { $failed = $true; if ($fault -eq 'valid') { throw } }
    if (($fault -eq 'valid') -eq $failed) { throw "Unexpected audit guard outcome: $fault" }
    $checks++
}
Write-Host "$checks scoped-cache audit report checks passed. Fixtures preserved: $root"
