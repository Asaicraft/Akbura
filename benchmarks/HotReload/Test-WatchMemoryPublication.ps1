#requires -Version 7.2
param([switch]$ExpectFailure, [switch]$HoldRead, [switch]$RequestReader, [switch]$HoldExclusive,
    [string]$Path, [string]$Ready, [string]$Publisher = 'Run-WatchMemorySession.ps1')
$ErrorActionPreference = 'Stop'
$encoding = [Text.UTF8Encoding]::new($false)
if ($HoldRead) {
    $stream = [IO.File]::Open($Path,'Open','Read',$(if ($HoldExclusive) { [IO.FileShare]::None } else { [IO.FileShare]::Read }))
    try { [IO.File]::WriteAllText($Ready,'ready',$encoding); Start-Sleep -Milliseconds 800 }
    finally { $stream.Dispose() }
    exit 0
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('Akbura-AtomicPublication-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$target = Join-Path $root 'response.json'
$readyPath = Join-Path $root 'reader.ready'
[IO.File]::WriteAllText($target,'{"Sequence":1}'+"`r`n",$encoding)
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $Publisher),[ref]$null,[ref]$null)
$functionName = if ($RequestReader) { 'Read-Request' } else { 'Save' }
$definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName },$true)
# Define the extracted function in this test scope without executing the session runner.
. ([scriptblock]::Create($definition.Extent.Text))
$script:retries = 0
function Log([string]$message) { if ($message.StartsWith('PUBLICATION_RETRY') -or $message.StartsWith('REQUEST_READ_RETRY')) { $script:retries++ } }
$arguments = @('-NoProfile','-File',$PSCommandPath,'-HoldRead','-Path',$target,'-Ready',$readyPath)
if ($RequestReader) { $arguments += '-HoldExclusive' }
$reader = Start-Process (Get-Command pwsh).Source -WindowStyle Hidden -PassThru -ArgumentList @($arguments | ForEach-Object { '"' + $_ + '"' })
try {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $readyPath)) {
        if ($reader.HasExited -or $wait.Elapsed.TotalSeconds -gt 10) { throw 'The deliberate sharing lock was not acquired.' }
        Start-Sleep -Milliseconds 10
    }
    if ($RequestReader) {
        if ((Read-Request $target).Sequence -ne 1 -or $script:retries -lt 1) { throw 'The request reader did not recover after an exclusive lock.' }
        if ($null -ne (Read-Request (Join-Path $root 'missing.json'))) { throw 'A missing request was not treated as pending.' }
        if ($null -ne (Read-Request (Join-Path $root 'not-created-yet/request.json'))) { throw 'The first poll before checkpoint-directory creation was not treated as pending.' }
        [IO.File]::WriteAllText($target,'{',$encoding)
        $rejected = $false
        try { $null = Read-Request $target } catch { $rejected = $true }
        if (-not $rejected) { throw 'The request reader silently accepted malformed JSON.' }
        Write-Output 'PASS: request reader recovers from an exclusive sharing lock, treats a missing request or not-yet-created parent directory as pending, and rejects malformed JSON.'
        return
    }
    $failed = $false
    try { Save $target @{ Sequence = 2 } } catch { $failed = $true; if (-not $ExpectFailure) { throw } }
    if ($ExpectFailure) {
        if (-not $failed) { throw 'The original publication did not reproduce the sharing failure.' }
        if ((Get-Content -LiteralPath $target -Raw | ConvertFrom-Json).Sequence -ne 1) { throw 'Failed publication modified the original destination.' }
        Write-Output 'REPRODUCED: original atomic publication fails while a reader holds the destination without delete sharing.'
    }
    else {
        if ($failed -or $script:retries -lt 1 -or (Get-Content -LiteralPath $target -Raw | ConvertFrom-Json).Sequence -ne 2 -or (Test-Path -LiteralPath ($target+'.tmp'))) {
            throw 'Atomic publication did not safely recover after the reader released its handle.'
        }
        # Non-transient failures must not be swallowed or converted into success.
        $permanent = Join-Path $root 'destination-directory'
        $null = New-Item -ItemType Directory -Path $permanent
        $rejected = $false
        try { Save $permanent @{ Sequence = 3 } } catch { $rejected = $true }
        if (-not $rejected) { throw 'An invalid destination was silently accepted.' }
        Write-Output 'PASS: Windows read-sharing race recovered atomically; old payload preserved until replacement; invalid destination rejected.'
    }
}
finally { if (-not $reader.HasExited) { $reader.WaitForExit() }; $reader.Dispose() }
