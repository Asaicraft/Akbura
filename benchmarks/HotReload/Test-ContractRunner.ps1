#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('../../bin/ContractRunnerTests/' + [Guid]::NewGuid().ToString('N'))))
$null = New-Item -ItemType Directory -Path $OutputDirectory
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Start-ContractBenchmarks.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) {
    throw 'Controller syntax errors.'
}
# Load only the actual functions under test, never the launching/worker body.
foreach ($definition in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -in @('Complete', 'Run', 'Observe', 'Status', 'Log', 'Foreign-Builds') }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
$checks = 0
function Check([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
    $script:checks++
}
$inputPath = Join-Path $OutputDirectory 'gallery-inputs.json'
[IO.File]::WriteAllText($inputPath, '{}', [Text.UTF8Encoding]::new($false))
$inputHash = (Get-FileHash -LiteralPath $inputPath).Hash
$configuration = @{ ToolHashes = @(@{ Name = 'Akbura.BlackSilence.dll'; Sha256 = 'compiler' }, @{ Name = 'Akbura.Generator.dll'; Sha256 = 'generator' }) }
$job = @{ Command = 'artifacts'; Workload = 'gallery'; File = 'Counter.akbura'; Mode = 'enabled'; Scenario = 'real-referenced-method' }
$path = Join-Path $OutputDirectory 'sample.jsonl'
$rows = [Collections.Generic.List[object]]::new()
$rows.Add(@{ Kind = 'invalidation-provenance'; Optimization = 'enabled'; Profiling = 'stats'; VerifyEveryUpdate = $true
    BlackSilenceSha256 = 'compiler'; GeneratorSha256 = 'generator'; InputsSha256 = $inputHash })
foreach ($index in 0..10) {
    $rows.Add(@{ Kind = 'invalidation'; Index = $index })
    $rows.Add(@{ Kind = 'invalidation-parity'; Index = $index; EqualSources = $true; EqualDiagnostics = $true })
    $rows.Add(@{ Kind = 'artifact-heap'; Index = $index })
}
function Save-Fixture {
    [IO.File]::WriteAllText($path, ((@($rows | ForEach-Object { $_ | ConvertTo-Json -Depth 8 -Compress }) -join "`r`n") + "`r`n"), [Text.UTF8Encoding]::new($false))
}
Save-Fixture
Check (Complete $path $job) 'Complete paired output was not resumable.'
$rows[0].Optimization = 'disabled'
Save-Fixture
Check (-not (Complete $path $job)) 'Wrong optimization mode was accepted.'
$rows[0].Optimization = 'enabled'
$rows[0].InputsSha256 = 'different'
Save-Fixture
Check (-not (Complete $path $job)) 'Changed evaluated inputs were accepted.'
$rows[0].InputsSha256 = $inputHash
$rows[0].BlackSilenceSha256 = 'different'
Save-Fixture
Check (-not (Complete $path $job)) 'Different compiler binary was accepted.'
$rows[0].BlackSilenceSha256 = 'compiler'
$rows[2].EqualDiagnostics = $false
Save-Fixture
Check (-not (Complete $path $job)) 'Failed diagnostic parity was accepted.'
$rows[2].EqualDiagnostics = $true
$rows[4].Index = 0
Save-Fixture
Check (-not (Complete $path $job)) 'Duplicate driver sample was accepted.'
$rows[4].Index = 1
$rows[6].Index = 0
Save-Fixture
Check (-not (Complete $path $job)) 'Duplicate heap observation was accepted.'
$rows[6].Index = 1
$rows[1].Index = 11
Save-Fixture
Check (-not (Complete $path $job)) 'Out-of-range index was accepted.'
[IO.File]::WriteAllText($path, '{"Kind":', [Text.UTF8Encoding]::new($false))
Check (-not (Complete $path $job)) 'Interrupted JSON was accepted.'

$tool = Join-Path $PSScriptRoot 'bin/Debug/net10.0'
Copy-Item -LiteralPath $tool -Destination (Join-Path $OutputDirectory 'tool') -Recurse
$statusPath = Join-Path $OutputDirectory 'status.json'
$journal = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'journal.log'), $false, [Text.UTF8Encoding]::new($false))
$journal.AutoFlush = $true
$jobs = @($job)
$completed = 0
$current = 'Deliberate native failure'
$currentIndex = -1
$currentPid = $null
$lastObservation = ''
$failed = $false
$job.Scenario = 'deliberately-unsupported-runner-test'
try {
    Run $job (Join-Path $OutputDirectory 'native-failure.jsonl')
}
catch {
    $failed = $true
}
finally {
    $journal.Dispose()
}
Check $failed 'A native failure did not terminate the group.'
Check ((Get-Item -LiteralPath (Join-Path $OutputDirectory 'native-failure.jsonl.stderr.log')).Length -gt 0) 'Native stderr was not retained.'
Check ($null -eq $currentPid) 'Child PID was not cleared after failure.'
Write-Output "$checks contract-runner checks passed. Preserved fixtures: $OutputDirectory"
